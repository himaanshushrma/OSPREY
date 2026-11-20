using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// DatasetCapture (v3.0): makes a labelled YOLO dataset from the simulator.
///
/// It places a virtual drone camera at random positions over the world (altitude, pitch and
/// field of view match VisionClient's camera), scatters survivor stand-ins on the ground
/// (copies of the rescue targets: the originals may be static-batched and cannot move, so
/// they are hidden during capture), changes the sun, renders a frame, and writes:
///     Dataset/images/{train,val}/osprey_00001.jpg
///     Dataset/labels/{train,val}/osprey_00001.txt   "class cx cy w h" (normalised, YOLO format)
///     Dataset/data.yaml
/// Boxes come from ground truth: each object's renderer bounds projected into the image,
/// kept only when enough of the object is visible (ray-cast line of sight).
///
/// Start: F9 in Play mode, or the API action capture_dataset {"count": 1500}.
/// Targets go back to their original places when the capture finishes.
/// </summary>
public class DatasetCapture : MonoBehaviour
{
    public static DatasetCapture Instance { get; private set; }
    public static readonly string[] Classes = { "person", "hut", "building", "tree" };

    [Header("Output")]
    public int imageCount = 1500;
    [Range(0, 0.5f)] public float valFraction = 0.15f;
    public int width = 640, height = 384;
    [Tooltip("Relative to the project folder")]
    public string folder = "Dataset";

    [Header("Camera (match VisionClient)")]
    public Vector2 altitude = new Vector2(10f, 38f);
    public Vector2 pitch = new Vector2(30f, 80f);
    public float fieldOfView = 70f;

    [Header("Randomisation")]
    [Range(0, 1)] public float targetFocus = 0.7f;   // share of frames aimed at a survivor
    public bool moveTargets = true;
    [Tooltip("Survivor stand-ins scattered per frame (some near the camera's focus)")]
    public int survivorsPerFrame = 10;
    public bool randomSun = true;
    public float minBoxPixels = 6f;

    public bool Running { get; private set; }
    public int Done { get; private set; }
    public string Status { get; private set; } = "";

    Camera cam;
    RenderTexture rt;
    Texture2D tex;

    class Item { public GameObject go; public int cls; public Renderer[] rs; public Collider[] cols; }
    readonly List<Item> items = new List<Item>();
    readonly List<Item> targets = new List<Item>();

    void Awake() { Instance = this; }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9) && !Running) Begin(imageCount, out _);
    }

    public bool Begin(int count, out string message)
    {
        if (Running) { message = $"Already capturing ({Done}/{imageCount})."; return false; }
        imageCount = Mathf.Clamp(count <= 0 ? imageCount : count, 10, 20000);
        StartCoroutine(Run());
        message = $"Capturing {imageCount} labelled images to {Path.GetFullPath(Path.Combine(Application.dataPath, "..", folder))}.";
        return true;
    }

    void Collect()
    {
        items.Clear(); targets.Clear();
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            string label = ObjectDetector.Classify(t.name);
            int cls = System.Array.IndexOf(Classes, label);
            if (cls < 0) continue;
            if (t.GetComponentInParent<DroneController>() != null) continue;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var it = new Item { go = t.gameObject, cls = cls, rs = rs, cols = t.GetComponentsInChildren<Collider>() };
            items.Add(it);
            if (cls == 0) targets.Add(it);
        }
    }

    IEnumerator Run()
    {
        Running = true; Done = 0;
        Collect();
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", folder));
        foreach (var split in new[] { "train", "val" })
        {
            Directory.CreateDirectory(Path.Combine(root, "images", split));
            Directory.CreateDirectory(Path.Combine(root, "labels", split));
        }
        WriteYaml(root);

        var go = new GameObject("DatasetCamera");
        cam = go.AddComponent<Camera>();
        cam.fieldOfView = fieldOfView;
        cam.nearClipPlane = 0.3f; cam.farClipPlane = 600f;
        rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        tex = new Texture2D(width, height, TextureFormat.RGB24, false);

        // Survivor stand-ins: originals are hidden (they may be static-batched, so moving them
        // would move the collider but not the picture), copies are scattered every frame.
        var originals = new List<Item>(targets);
        var proxies = new List<GameObject>();
        if (moveTargets && originals.Count > 0)
        {
            Vector3 scale = originals[0].go.transform.lossyScale;
            Material mat = originals[0].rs[0].sharedMaterial;
            foreach (var t in originals) foreach (var r in t.rs) r.enabled = false;
            items.RemoveAll(i => i.cls == 0);
            targets.Clear();
            for (int i = 0; i < survivorsPerFrame; i++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                g.name = $"DatasetSurvivor_{i}";
                g.transform.localScale = scale;
                var r = g.GetComponent<Renderer>();
                r.sharedMaterial = mat;
                proxies.Add(g);
                var it = new Item { go = g, cls = 0, rs = new[] { r }, cols = g.GetComponents<Collider>() };
                items.Add(it); targets.Add(it);
            }
        }
        Light sun = RenderSettings.sun != null ? RenderSettings.sun : FindSun();
        Quaternion sunRot = sun != null ? sun.transform.rotation : Quaternion.identity;

        var grid = OccupancyGrid.Instance;
        Rect world = grid != null ? grid.Bounds : new Rect(-200, -200, 400, 400);
        var rnd = new System.Random(1234);
        int boxes = 0, tries = 0;

        while (Done < imageCount && tries < imageCount * 4)
        {
            tries++;
            // pick the focus first, then scatter survivors: about half near the focus
            Vector3 focusSeed = new Vector3(Lerp(rnd, world.xMin + 20, world.xMax - 20), 0, Lerp(rnd, world.yMin + 20, world.yMax - 20));
            if (moveTargets)
                for (int i = 0; i < targets.Count; i++)
                {
                    bool near = i < targets.Count / 2 && rnd.NextDouble() < targetFocus;
                    Rect zone = near ? new Rect(focusSeed.x - 30, focusSeed.z - 30, 60, 60) : world;
                    if (proxies.Count > 0) targets[i].go.transform.localScale = originals[0].go.transform.lossyScale * Lerp(rnd, 0.8f, 1.3f);
                    PlaceRandomly(targets[i], zone, rnd);
                }
            if (randomSun && sun != null)
                sun.transform.rotation = Quaternion.Euler(Lerp(rnd, 25f, 75f), Lerp(rnd, 0f, 360f), 0f);

            // camera pose
            float alt = Lerp(rnd, altitude.x, altitude.y);
            float p = Lerp(rnd, pitch.x, pitch.y);
            Vector3 focus;
            focus = focusSeed;
            float yaw = Lerp(rnd, 0f, 360f);
            float back = alt / Mathf.Tan(p * Mathf.Deg2Rad);
            Vector3 dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            // jitter so the focus object is not always in the centre
            focus += Quaternion.Euler(0, yaw, 0) * new Vector3(Lerp(rnd, -12f, 12f), 0, Lerp(rnd, -8f, 8f));
            float groundY = GroundAt(focus);
            Vector3 camPos = new Vector3(focus.x, groundY + alt, focus.z) - dir * back;
            if (Physics.CheckSphere(camPos, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            cam.transform.SetPositionAndRotation(camPos, Quaternion.Euler(p, yaw, 0));

            // wait for physics (moved colliders) and two rendered frames (HDRP history, exposure)
            yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();

            var labels = Labels();
            // keep a few empty frames (negatives) but mostly frames with objects
            if (labels.Count == 0 && rnd.NextDouble() > 0.1) continue;

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;

            Done++;
            boxes += labels.Count;
            string split = rnd.NextDouble() < valFraction ? "val" : "train";
            string name = $"osprey_{Done:00000}";
            File.WriteAllBytes(Path.Combine(root, "images", split, name + ".jpg"), tex.EncodeToJPG(90));
            File.WriteAllText(Path.Combine(root, "labels", split, name + ".txt"), string.Join("\n", labels));
            Status = $"Dataset: {Done}/{imageCount} images, {boxes} boxes";
        }

        // restore the world
        foreach (var g in proxies) Destroy(g);
        foreach (var t in originals) foreach (var r in t.rs) if (r != null) r.enabled = true;
        if (sun != null) sun.transform.rotation = sunRot;
        cam.targetTexture = null;
        Destroy(go); rt.Release(); Destroy(rt); Destroy(tex);

        Status = $"Dataset done: {Done} images, {boxes} boxes in {root}";
        Debug.Log("[DatasetCapture] " + Status + "  ->  python vision/train_yolo.py --data \"" + Path.Combine(root, "data.yaml") + "\"");
        Running = false;
    }

    /// <summary>YOLO label lines for the current camera pose.</summary>
    List<string> Labels()
    {
        var lines = new List<string>();
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        var sb = new StringBuilder();
        foreach (var it in items)
        {
            if (it.go == null || !it.go.activeInHierarchy) continue;
            Bounds b = it.rs[0].bounds;
            for (int i = 1; i < it.rs.Length; i++) b.Encapsulate(it.rs[i].bounds);
            if (!GeometryUtility.TestPlanesAABB(planes, b)) continue;
            if (Vector3.Distance(cam.transform.position, b.center) > 220f) continue;

            // project the 8 corners
            float x0 = 1, y0 = 1, x1 = 0, y1 = 0; bool behind = false;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                Vector3 v = cam.WorldToViewportPoint(corner);
                if (v.z <= 0) { behind = true; break; }
                x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x);
                y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y);
            }
            if (behind) continue;
            float fullW = x1 - x0, fullH = y1 - y0;
            x0 = Mathf.Clamp01(x0); x1 = Mathf.Clamp01(x1); y0 = Mathf.Clamp01(y0); y1 = Mathf.Clamp01(y1);
            float w = x1 - x0, h = y1 - y0;
            if (w * width < minBoxPixels || h * height < minBoxPixels) continue;
            if (fullW > 0 && fullH > 0 && (w * h) / (fullW * fullH) < 0.4f) continue;   // mostly off-screen
            if (it.cls == 3 && w * h > 0.25f) continue;                                    // tree filling the frame
            if (VisibleFraction(it, b) < 0.34f) continue;

            float cx = (x0 + x1) * 0.5f, cy = 1f - (y0 + y1) * 0.5f;   // image y goes down
            sb.Clear();
            sb.Append(it.cls).Append(' ')
              .Append(cx.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
              .Append(cy.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
              .Append(w.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
              .Append(h.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));
            lines.Add(sb.ToString());
        }
        return lines;
    }

    /// <summary>Share of 9 sample points on the object that the camera can see.</summary>
    float VisibleFraction(Item it, Bounds b)
    {
        Vector3 eye = cam.transform.position;
        int seen = 0, n = 0;
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            {
                Vector3 p = b.center + new Vector3(b.extents.x * 0.6f * i, b.extents.y * 0.6f * j, b.extents.z * 0.6f * (i * j));
                n++;
                Vector3 d = p - eye;
                // stop just short of the sample point: objects without colliders count as visible
                // when nothing else is in the way
                if (!Physics.Raycast(eye, d.normalized, out RaycastHit hit, Mathf.Max(0f, d.magnitude - 0.4f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                { seen++; continue; }
                if (hit.transform == it.go.transform || hit.transform.IsChildOf(it.go.transform)) seen++;
            }
        return (float)seen / n;
    }

    void PlaceRandomly(Item t, Rect world, System.Random rnd)
    {
        for (int k = 0; k < 12; k++)
        {
            float x = Lerp(rnd, world.xMin + 5, world.xMax - 5), z = Lerp(rnd, world.yMin + 5, world.yMax - 5);
            if (!Physics.Raycast(new Vector3(x, 300, z), Vector3.down, out RaycastHit hit, 400f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            if (hit.normal.y < 0.85f || hit.collider.name.Contains("Water") || hit.collider.name.Contains("River")) continue;
            if (hit.transform.IsChildOf(t.go.transform)) continue;
            if (ObjectDetector.Classify(hit.transform.name) == "tree" ||
                (hit.transform.parent != null && ObjectDetector.Classify(hit.transform.parent.name) == "tree")) continue;
            float half = t.rs[0].bounds.extents.y;
            t.go.transform.SetPositionAndRotation(hit.point + Vector3.up * half, Quaternion.Euler(0, Lerp(rnd, 0, 360), 0));
            return;
        }
    }

    static float GroundAt(Vector3 p)
    {
        return Physics.Raycast(new Vector3(p.x, 300, p.z), Vector3.down, out RaycastHit hit, 400f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? hit.point.y : 0f;
    }

    static Light FindSun()
    {
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) return l;
        return null;
    }

    static float Lerp(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    void WriteYaml(string root)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# OSPREY synthetic drone-camera dataset (made by DatasetCapture.cs)");
        sb.AppendLine($"path: {root.Replace('\\', '/')}");
        sb.AppendLine("train: images/train");
        sb.AppendLine("val: images/val");
        sb.AppendLine("names:");
        for (int i = 0; i < Classes.Length; i++) sb.AppendLine($"  {i}: {Classes[i]}");
        File.WriteAllText(Path.Combine(root, "data.yaml"), sb.ToString());
    }

    void OnGUI()
    {
        if (!Running && string.IsNullOrEmpty(Status)) return;
        GUI.Label(new Rect(Screen.width * 0.5f - 200, 8, 400, 24), $"<b><color=#FFD84D>{Status}</color></b>",
                  new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter, fontSize = 14 });
    }
}
