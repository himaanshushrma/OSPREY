using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ObjectDetector: a simulated onboard camera + object detector (think YOLOv8 on the drone).
///
/// Every `interval` seconds it looks through a virtual camera on the drone (pitched down,
/// with a field of view and range) and reports what it can actually see:
///   - inside the camera's field of view and range
///   - with a clear line of sight (objects behind buildings or trees are not seen)
/// Each detection gets a class label and a confidence that drops with distance and
/// with distance from the image centre, like a real detector.
///
/// Classes: person (rescue targets), building, tree, hut, bridge, wall.
/// The overlay draws YOLO-style labelled boxes in the Game view (toggle with O) and a
/// panel with counts. The first sighting of each survivor is logged and kept in
/// SurvivorsFound; SwarmCommandAPI reports both to the mission agent.
/// </summary>
public class ObjectDetector : MonoBehaviour
{
    [Header("Camera")]
    public float range = 80f;
    public float fieldOfView = 80f;
    [Tooltip("How far the camera looks down from the horizon, in degrees.")]
    public float pitchDown = 30f;
    public float interval = 0.2f;

    [Header("Detector")]
    [Range(0, 1)] public float minConfidence = 0.35f;
    [Range(0, 1)] public float survivorConfidence = 0.55f;

    [Header("Overlay")]
    public bool showOverlay = true;
    public KeyCode toggleKey = KeyCode.O;
    public int maxBoxes = 30;

    public class Detection
    {
        public string label;
        public float confidence;
        public Vector3 position;
        public Bounds bounds;
        public float distance;
        public GameObject obj;
    }

    public readonly List<Detection> detections = new List<Detection>();
    public readonly List<Vector3> SurvivorsFound = new List<Vector3>();
    public int SurvivorsTotal { get; private set; }

    class Candidate
    {
        public GameObject go;
        public string label;
        public Renderer[] renderers;
        public bool found;
    }

    readonly List<Candidate> candidates = new List<Candidate>();
    readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    float nextScan;
    System.Random noise = new System.Random(7);

    static string Classify(string name)
    {
        if (name.StartsWith("RescueTarget")) return "person";
        if (name.StartsWith("Building_") || name == "Hangar" || name == "ControlTower") return "building";
        if (name == "Tree" || name == "Pine") return "tree";
        if (name.StartsWith("Hut_")) return "hut";
        if (name == "Bridge_Deck") return "bridge";
        if (name.StartsWith("Wall_")) return "wall";
        return null;
    }

    void Start()
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            string label = Classify(t.name);
            if (label == null || t.IsChildOf(transform)) continue;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            candidates.Add(new Candidate { go = t.gameObject, label = label, renderers = rs });
            if (label == "person") SurvivorsTotal++;
        }
        Debug.Log($"[ObjectDetector] Camera online: watching {candidates.Count} objects, {SurvivorsTotal} survivors hidden in the world.");
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) showOverlay = !showOverlay;
        if (Time.time < nextScan) return;
        nextScan = Time.time + interval;
        Scan();
    }

    public Vector3 CameraForward => Quaternion.Euler(pitchDown, transform.eulerAngles.y, 0) * Vector3.forward;

    void Scan()
    {
        detections.Clear();
        counts.Clear();

        Vector3 eye = transform.position;
        Vector3 fwd = CameraForward;
        float cosHalf = Mathf.Cos(fieldOfView * 0.5f * Mathf.Deg2Rad);

        foreach (var c in candidates)
        {
            if (c.go == null || !c.go.activeInHierarchy) continue;

            Bounds b = c.renderers[0].bounds;
            for (int i = 1; i < c.renderers.Length; i++) b.Encapsulate(c.renderers[i].bounds);

            Vector3 to = b.center - eye;
            float d = to.magnitude;
            if (d > range || d < 0.5f) continue;

            Vector3 dir = to / d;
            float align = Vector3.Dot(dir, fwd);
            if (align < cosHalf) continue;

            // Line of sight: the first thing hit must be the object itself (or nothing)
            if (Physics.Raycast(eye, dir, out RaycastHit hit, d - 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Transform ht = hit.transform;
                bool self = ht == c.go.transform || ht.IsChildOf(c.go.transform);
                bool own = ht == transform || ht.IsChildOf(transform);
                if (!self && !own) continue;
            }

            float offCentre = (1f - align) / (1f - cosHalf);
            float sizeBoost = Mathf.Clamp01(b.extents.magnitude / 6f) * 0.1f;
            float conf = 0.96f - 0.4f * (d / range) - 0.2f * offCentre + sizeBoost + (float)(noise.NextDouble() - 0.5) * 0.05f;
            conf = Mathf.Clamp(conf, 0f, 0.99f);
            if (conf < minConfidence) continue;

            detections.Add(new Detection { label = c.label, confidence = conf, position = b.center, bounds = b, distance = d, obj = c.go });
            counts[c.label] = counts.TryGetValue(c.label, out int n) ? n + 1 : 1;

            if (c.label == "person" && !c.found && conf >= survivorConfidence)
            {
                c.found = true;
                SurvivorsFound.Add(b.center);
                Debug.Log($"[ObjectDetector] SURVIVOR FOUND: {c.go.name} at ({b.center.x:0}, {b.center.z:0}), " +
                          $"confidence {conf:0.00}. {SurvivorsFound.Count}/{SurvivorsTotal} found.");
            }
        }

        detections.Sort((a, b) => a.distance.CompareTo(b.distance));
    }

    // ===================================================
    // OVERLAY (YOLO-style boxes in the Game view)
    // ===================================================

    static Texture2D pixel;
    static GUIStyle labelStyle, panelStyle;

    static Color ColorFor(string label)
    {
        switch (label)
        {
            case "person": return new Color(1f, 0.25f, 0.2f);
            case "building": return new Color(0.3f, 0.85f, 1f);
            case "tree": return new Color(0.4f, 1f, 0.4f);
            case "hut": return new Color(1f, 0.85f, 0.25f);
            case "bridge": return new Color(1f, 0.45f, 1f);
            default: return Color.white;
        }
    }

    void OnGUI()
    {
        if (pixel == null) { pixel = new Texture2D(1, 1); pixel.SetPixel(0, 0, Color.white); pixel.Apply(); }
        if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
        if (panelStyle == null) panelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };

        // Panel (top right)
        var parts = new List<string>();
        foreach (var kv in counts) parts.Add($"{kv.Key} ×{kv.Value}");
        GUI.Box(new Rect(Screen.width - 330, 10, 320, 66), GUIContent.none);
        GUI.Label(new Rect(Screen.width - 322, 14, 310, 60),
            $"<b>Object detection</b>  (onboard camera)   <color=#AAAAAA>[O] {(showOverlay ? "hide" : "show")}</color>\n" +
            (parts.Count > 0 ? string.Join("  ·  ", parts) : "<color=#AAAAAA>nothing in view</color>") + "\n" +
            $"Survivors found: <b>{SurvivorsFound.Count}/{SurvivorsTotal}</b>", panelStyle);

        if (!showOverlay) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        int shown = 0;
        foreach (var det in detections)
        {
            if (shown >= maxBoxes) break;
            if (!ScreenRect(cam, det.bounds, out Rect r)) continue;
            if (r.width < 4 || r.height < 4) continue;

            Color col = ColorFor(det.label);
            float t = det.label == "person" ? 3f : 1.5f;
            DrawFrame(r, col, t);

            string text = $"{det.label} {det.confidence:0.00}";
            Vector2 size = labelStyle.CalcSize(new GUIContent(text));
            var tag = new Rect(r.x, r.y - size.y - 2, size.x + 6, size.y + 2);
            GUI.color = new Color(col.r, col.g, col.b, 0.85f);
            GUI.DrawTexture(tag, pixel);
            GUI.color = Color.black;
            GUI.Label(new Rect(tag.x + 3, tag.y + 1, size.x + 4, size.y), text, labelStyle);
            GUI.color = Color.white;
            shown++;
        }
    }

    static bool ScreenRect(Camera cam, Bounds b, out Rect rect)
    {
        rect = default;
        Vector3 c = b.center, e = b.extents;
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                c.x + ((i & 1) == 0 ? -e.x : e.x),
                c.y + ((i & 2) == 0 ? -e.y : e.y),
                c.z + ((i & 4) == 0 ? -e.z : e.z));
            Vector3 s = cam.WorldToScreenPoint(corner);
            if (s.z <= 0) return false;
            xMin = Mathf.Min(xMin, s.x); xMax = Mathf.Max(xMax, s.x);
            yMin = Mathf.Min(yMin, s.y); yMax = Mathf.Max(yMax, s.y);
        }
        rect = Rect.MinMaxRect(xMin, Screen.height - yMax, xMax, Screen.height - yMin);
        return rect.xMax > 0 && rect.yMax > 0 && rect.xMin < Screen.width && rect.yMin < Screen.height;
    }

    static void DrawFrame(Rect r, Color col, float t)
    {
        GUI.color = col;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), pixel);
        GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), pixel);
        GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), pixel);
        GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), pixel);
        GUI.color = Color.white;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, CameraForward * range);
    }
}
