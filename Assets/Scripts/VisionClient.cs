using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// VisionClient (v3.0): real YOLOv8 vision for the leader drone.
///
/// A camera on the leader (pitched down like a real gimbal) renders 640x384 frames. Every
/// `interval` seconds one frame is JPEG-encoded and sent to vision/vision_server.py
/// (Ultralytics YOLOv8 on your NVIDIA GPU). The server answers with boxes; this script:
///   - draws the camera feed with the real boxes as picture-in-picture (top centre, key Y);
///   - for every "person" box, casts a ray through the box centre to find where it is on the
///     ground, and reports the survivor to the swarm (ObjectDetector.Primary);
///   - while connected, turns off the leader's simulated survivor detection, so survivors
///     the leader reports are found only by the neural network.
/// If the server is not running, it retries every few seconds and the simulated detector
/// keeps working, so nothing breaks.
/// </summary>
public class VisionClient : MonoBehaviour
{
    public static VisionClient Instance { get; private set; }

    [Header("Server")]
    public string serverUrl = "http://127.0.0.1:8766/detect";
    public float interval = 0.2f;
    public float retrySeconds = 4f;

    [Header("Camera (match DatasetCapture)")]
    public int width = 640, height = 384;
    public float fieldOfView = 70f;
    public float pitchDown = 50f;

    [Header("Survivors")]
    [Range(0, 1)] public float survivorConfidence = 0.5f;
    public float matchRadius = 6f;

    [Header("Display")]
    public bool showFeed = true;
    public KeyCode toggleKey = KeyCode.Y;

    [Serializable] public class Box { public string label; public float conf; public float x1, y1, x2, y2; }
    [Serializable] class Reply { public Box[] boxes; public float ms; public string model; }

    public bool Connected { get; private set; }
    public float LastLatencyMs { get; private set; }
    public float ServerMs { get; private set; }
    public string ModelName { get; private set; } = "";
    public Box[] Boxes { get; private set; } = new Box[0];
    public int Frames { get; private set; }

    Camera cam;
    RenderTexture rt;
    Texture2D tex;
    bool busy;
    float nextSend, retryAt;
    Pose sentPose;
    ObjectDetector simulated;

    void Awake() { Instance = this; }

    void Start()
    {
        var go = new GameObject("VisionCamera");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0, -0.3f, 0.4f);
        go.transform.localRotation = Quaternion.Euler(pitchDown, 0, 0);
        cam = go.AddComponent<Camera>();
        cam.fieldOfView = fieldOfView;
        cam.nearClipPlane = 0.3f; cam.farClipPlane = 600f;
        cam.depth = -10;
        rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        simulated = GetComponent<ObjectDetector>();
    }

    void OnDestroy()
    {
        if (rt != null) rt.Release();
        if (simulated != null) simulated.survivorsFromSimulation = true;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey)) showFeed = !showFeed;
        if (busy || Time.time < nextSend || Time.time < retryAt) return;
        nextSend = Time.time + interval;
        StartCoroutine(SendFrame());
    }

    IEnumerator SendFrame()
    {
        busy = true;
        yield return new WaitForEndOfFrame();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply(false);
        RenderTexture.active = prev;
        sentPose = new Pose(cam.transform.position, cam.transform.rotation);
        byte[] jpg = tex.EncodeToJPG(80);

        float t0 = Time.realtimeSinceStartup;
        using (var req = new UnityWebRequest(serverUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(jpg) { contentType = "image/jpeg" };
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 3;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                if (Connected) Debug.LogWarning($"[Vision] Lost the YOLOv8 server ({req.error}). Simulated detector takes over.");
                SetConnected(false);
                retryAt = Time.time + retrySeconds;
                busy = false;
                yield break;
            }

            var reply = JsonUtility.FromJson<Reply>(req.downloadHandler.text);
            if (!Connected) Debug.Log($"[Vision] Connected to YOLOv8 server ({reply.model}). Survivors now come from the neural network.");
            SetConnected(true);
            LastLatencyMs = (Time.realtimeSinceStartup - t0) * 1000f;
            ServerMs = reply.ms;
            ModelName = reply.model;
            Boxes = reply.boxes ?? new Box[0];
            Frames++;
            HandleSurvivors();
        }
        busy = false;
    }

    void SetConnected(bool on)
    {
        Connected = on;
        if (simulated != null) simulated.survivorsFromSimulation = !on;
    }

    /// <summary>Turn "person" boxes into survivor positions on the ground.</summary>
    void HandleSurvivors()
    {
        var primary = ObjectDetector.Primary;
        if (primary == null) return;

        // ray through the box centre, from the camera pose the frame was taken at
        Vector3 camPos = cam.transform.position; Quaternion camRot = cam.transform.rotation;
        cam.transform.SetPositionAndRotation(sentPose.position, sentPose.rotation);
        foreach (var b in Boxes)
        {
            if (b.label != "person" || b.conf < survivorConfidence) continue;
            Vector2 vp = new Vector2((b.x1 + b.x2) * 0.5f, 1f - (b.y1 + b.y2) * 0.5f);
            Ray ray = cam.ViewportPointToRay(new Vector3(vp.x, vp.y, 0));
            if (!Physics.Raycast(ray, out RaycastHit hit, 400f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;

            // the simulator needs an object to de-duplicate; find the target nearest the hit
            GameObject target = NearestTarget(hit.point);
            if (target == null) continue;   // a false positive: nothing there
            primary.RegisterSurvivor(target, hit.point, b.conf, "leader (YOLOv8)");
        }
        cam.transform.SetPositionAndRotation(camPos, camRot);
    }

    readonly List<GameObject> targetCache = new List<GameObject>();

    GameObject NearestTarget(Vector3 p)
    {
        if (targetCache.Count == 0)
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("RescueTarget_")) targetCache.Add(t.gameObject);
        GameObject best = null; float bestD = matchRadius;
        foreach (var g in targetCache)
        {
            if (g == null) continue;
            float d = Vector3.Distance(new Vector3(g.transform.position.x, 0, g.transform.position.z), new Vector3(p.x, 0, p.z));
            if (d < bestD) { bestD = d; best = g; }
        }
        return best;
    }

    // ===================================================
    // PICTURE-IN-PICTURE
    // ===================================================

    static Texture2D white;
    static GUIStyle tagStyle;

    void OnGUI()
    {
        if (!showFeed || rt == null) return;
        if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
        if (tagStyle == null) tagStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true };

        float w = Mathf.Min(384, Screen.width * 0.3f), h = w * height / width;
        Rect r = new Rect(Screen.width * 0.5f - w * 0.5f, 36, w, h);
        if (!Connected)
        {
            // offline: just a status line, no feed (keeps the screen clear)
            GUI.Box(new Rect(r.x, 36, w, 18), GUIContent.none);
            GUI.Label(new Rect(r.x + 4, 37, w, 18), "<color=#FFB347>● YOLOv8 offline</color> · simulated detector · [Y] hide", tagStyle);
            return;
        }
        GUI.DrawTexture(r, rt, ScaleMode.StretchToFill, false);

        foreach (var b in Boxes)
            {
                Color c = b.label == "person" ? new Color(1f, 0.25f, 0.25f) : b.label == "hut" ? new Color(1f, 0.75f, 0.2f) :
                          b.label == "building" ? new Color(0.3f, 0.7f, 1f) : new Color(0.4f, 0.9f, 0.4f);
                Rect bx = new Rect(r.x + b.x1 * w, r.y + b.y1 * h, (b.x2 - b.x1) * w, (b.y2 - b.y1) * h);
                Outline(bx, c);
                GUI.color = c;
                GUI.Label(new Rect(bx.x, bx.y - 15, 140, 16), $"<b>{b.label} {b.conf:0.00}</b>", tagStyle);
                GUI.color = Color.white;
            }

        string status = $"<color=#7CFC00>● YOLOv8</color> {ModelName} · {Boxes.Length} boxes · GPU {ServerMs:0} ms · round trip {LastLatencyMs:0} ms";
        GUI.Box(new Rect(r.x, r.yMax, w, 18), GUIContent.none);
        GUI.Label(new Rect(r.x + 4, r.yMax + 1, w, 18), status, tagStyle);
    }

    static void Outline(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2), white);
        GUI.DrawTexture(new Rect(r.x, r.yMax - 2, r.width, 2), white);
        GUI.DrawTexture(new Rect(r.x, r.y, 2, r.height), white);
        GUI.DrawTexture(new Rect(r.xMax - 2, r.y, 2, r.height), white);
        GUI.color = Color.white;
    }
}
