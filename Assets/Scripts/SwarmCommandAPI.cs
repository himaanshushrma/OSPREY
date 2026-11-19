using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// SwarmCommandAPI
/// ----------------
/// A tiny local HTTP server that exposes OSPREY's swarm controls so an
/// external program (the Python LLM mission agent in /agent) can command it.
///
/// Endpoints (localhost only):
///   GET  /state     -> current swarm / mission state
///   POST /command   -> { "action": "...", ...params }
///
/// Actions:
///   set_formation   { "formation": "Diamond" }
///   set_spacing     { "spacing": 4 }
///   set_altitude    { "altitude": 8 }
///   set_waypoints   { "waypoints": [{"x":0,"z":20}, ...], "fromCurrentPosition": true }
///   start_mission   {}
///   cancel_mission  {}
///
/// Network requests arrive on a background thread; every command is executed
/// on Unity's main thread (Update), because Unity APIs are not thread-safe.
/// </summary>
public class SwarmCommandAPI : MonoBehaviour
{
    [Header("References")]
    public MissionManager mission;
    public FormationManager formation;
    public SwarmManager swarm;
    public WaypointManager waypointManager;
    public DroneController leader;

    [Header("Server")]
    public int port = 8765;
    public bool logCommands = true;

    [Header("Limits")]
    public int maxWaypoints = 20;
    public float maxCoordinate = 500f;
    public float minAltitude = 2f;
    public float maxAltitude = 40f;
    public float minSpacing = 1.5f;
    public float maxSpacing = 15f;

    // ---------------------------------------------------
    // DTOs (JsonUtility needs [Serializable] classes)
    // ---------------------------------------------------

    [Serializable] public class Vec2Dto { public float x; public float z; }
    [Serializable] public class Vec3Dto { public float x; public float y; public float z; }

    [Serializable]
    public class CommandRequest
    {
        public string action = "";
        public string formation = "";
        public float spacing = -1f;
        public float altitude = -1f;
        public bool fromCurrentPosition = true;
        public Vec2Dto[] waypoints;
    }

    [Serializable]
    public class StateDto
    {
        public string missionState;
        public string leaderMode;
        public string formation;
        public float spacing;
        public float cruiseHeight;
        public Vec3Dto leaderPosition;
        public float headingDeg;
        public float speed;
        public int followerCount;
        public int currentSegment;
        public int segmentCount;
        public Vec3Dto[] waypoints;
        public string[] availableFormations;
    }

    [Serializable]
    public class Response
    {
        public bool ok;
        public string message;
        public StateDto state;
    }

    class Pending
    {
        public string method;
        public string path;
        public string body;
        public string responseJson;
        public int status = 200;
        public readonly ManualResetEventSlim done = new ManualResetEventSlim(false);
    }

    // ---------------------------------------------------

    HttpListener listener;
    Thread serverThread;
    volatile bool running;
    readonly ConcurrentQueue<Pending> queue = new ConcurrentQueue<Pending>();

    Transform agentWaypointRoot;
    readonly List<Transform> agentWaypoints = new List<Transform>();

    // ===================================================
    // LIFECYCLE
    // ===================================================

    void Awake()
    {
        // Keep simulating (and answering the agent) while the terminal has focus
        Application.runInBackground = true;

        // Any reference left empty in the Inspector is found automatically
        if (mission == null) mission = FindFirstObjectByType<MissionManager>();
        if (formation == null) formation = FindFirstObjectByType<FormationManager>();

        // Always drive the SAME objects the mission uses; a scene can contain
        // several WaypointManagers, and writing to the wrong one is silently ignored.
        if (mission != null)
        {
            if (mission.waypointManager != null) waypointManager = mission.waypointManager;
            if (mission.leader != null) leader = mission.leader;
            if (mission.swarm != null) swarm = mission.swarm;
        }

        if (swarm == null) swarm = FindFirstObjectByType<SwarmManager>();
        if (waypointManager == null) waypointManager = FindFirstObjectByType<WaypointManager>();

        var allMissions = FindObjectsByType<MissionManager>(FindObjectsSortMode.None);
        if (allMissions.Length > 1)
            Debug.LogWarning($"[SwarmCommandAPI] {allMissions.Length} MissionManagers in the scene; " +
                             $"commanding the one on '{mission.name}'. Remove the extras to avoid conflicts.");

        if (mission == null || formation == null || waypointManager == null || leader == null)
            Debug.LogWarning("[SwarmCommandAPI] Some references are missing; related commands will fail. " +
                             "Assign them in the Inspector.");
    }

    void OnEnable()
    {
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();

            running = true;
            serverThread = new Thread(ServerLoop) { IsBackground = true, Name = "SwarmCommandAPI" };
            serverThread.Start();

            Debug.Log($"[SwarmCommandAPI] Listening on http://localhost:{port}/");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SwarmCommandAPI] Could not start server on port {port}: {e.Message}");
        }
    }

    void OnDisable()
    {
        running = false;

        try { listener?.Stop(); listener?.Close(); }
        catch { /* shutting down */ }

        listener = null;

        // Release any request still waiting
        while (queue.TryDequeue(out Pending p))
        {
            p.status = 503;
            p.responseJson = JsonUtility.ToJson(new Response { ok = false, message = "Simulator stopped" });
            p.done.Set();
        }
    }

    // ===================================================
    // BACKGROUND THREAD: receive HTTP, hand to main thread
    // ===================================================

    void ServerLoop()
    {
        while (running)
        {
            HttpListenerContext ctx;

            try { ctx = listener.GetContext(); }
            catch { break; } // listener stopped

            try
            {
                var p = new Pending
                {
                    method = ctx.Request.HttpMethod,
                    path = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant()
                };

                using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    p.body = reader.ReadToEnd();

                queue.Enqueue(p);

                if (!p.done.Wait(5000))
                {
                    p.status = 504;
                    p.responseJson = JsonUtility.ToJson(new Response
                    {
                        ok = false,
                        message = "Timed out waiting for Unity main thread (is the game paused?)"
                    });
                }

                byte[] bytes = Encoding.UTF8.GetBytes(p.responseJson ?? "{}");
                ctx.Response.StatusCode = p.status;
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SwarmCommandAPI] Request failed: {e.Message}");
                try { ctx.Response.Abort(); } catch { }
            }
        }
    }

    // ===================================================
    // MAIN THREAD: execute queued requests
    // ===================================================

    void Update()
    {
        while (queue.TryDequeue(out Pending p))
        {
            Response r;

            try
            {
                r = Route(p);
            }
            catch (Exception e)
            {
                r = Fail("Internal error: " + e.Message);
                p.status = 500;
            }

            if (!r.ok && p.status == 200)
                p.status = 400;

            r.state = BuildState();
            p.responseJson = JsonUtility.ToJson(r);
            p.done.Set();
        }
    }

    Response Route(Pending p)
    {
        if (p.path == "/state" && p.method == "GET")
            return Ok("State");

        if (p.path == "/command" && p.method == "POST")
        {
            var req = new CommandRequest();

            if (!string.IsNullOrWhiteSpace(p.body))
                JsonUtility.FromJsonOverwrite(p.body, req);

            if (logCommands)
                Debug.Log($"[SwarmCommandAPI] {req.action} {p.body}");

            return Execute(req);
        }

        p.status = 404;
        return Fail($"Unknown endpoint {p.method} {p.path}. Use GET /state or POST /command.");
    }

    // ===================================================
    // COMMANDS
    // ===================================================

    Response Execute(CommandRequest req)
    {
        switch ((req.action ?? "").Trim().ToLowerInvariant())
        {
            case "set_formation":  return SetFormation(req.formation);
            case "set_spacing":    return SetSpacing(req.spacing);
            case "set_altitude":   return SetAltitude(req.altitude);
            case "set_waypoints":  return SetWaypoints(req.waypoints, req.fromCurrentPosition);
            case "start_mission":  return StartMission();
            case "cancel_mission": return CancelMission();
            default:
                return Fail($"Unknown action '{req.action}'.");
        }
    }

    Response SetFormation(string name)
    {
        if (formation == null) return Fail("FormationManager not assigned.");

        if (!Enum.TryParse(name, true, out FormationManager.FormationType f) ||
            !Enum.IsDefined(typeof(FormationManager.FormationType), f))
        {
            return Fail($"Unknown formation '{name}'. Valid: {string.Join(", ", Enum.GetNames(typeof(FormationManager.FormationType)))}");
        }

        formation.currentFormation = f;
        return Ok($"Formation set to {f}.");
    }

    Response SetSpacing(float spacing)
    {
        if (formation == null) return Fail("FormationManager not assigned.");
        if (spacing < minSpacing || spacing > maxSpacing)
            return Fail($"Spacing must be between {minSpacing} and {maxSpacing} m.");

        formation.spacing = spacing;
        return Ok($"Formation spacing set to {spacing:0.0} m.");
    }

    Response SetAltitude(float altitude)
    {
        if (mission == null) return Fail("MissionManager not assigned.");
        if (altitude < minAltitude || altitude > maxAltitude)
            return Fail($"Altitude must be between {minAltitude} and {maxAltitude} m.");

        mission.cruiseHeight = altitude;

        // Apply immediately if already flying
        if (mission.state == MissionManager.MissionState.Takeoff ||
            mission.state == MissionManager.MissionState.Patrol)
        {
            if (leader != null) leader.targetHeight = altitude;
            ApplyAgentWaypointHeight(altitude);
        }

        return Ok($"Cruise altitude set to {altitude:0.0} m.");
    }

    Response SetWaypoints(Vec2Dto[] points, bool fromCurrentPosition)
    {
        if (waypointManager == null) return Fail("WaypointManager not assigned.");
        if (IsMissionActive())
            return Fail("A mission is running. Call cancel_mission before changing waypoints.");

        if (points == null || points.Length == 0)
            return Fail("Provide at least one waypoint.");
        if (points.Length > maxWaypoints)
            return Fail($"Too many waypoints (max {maxWaypoints}).");

        foreach (var pt in points)
        {
            if (pt == null || float.IsNaN(pt.x) || float.IsNaN(pt.z) ||
                Mathf.Abs(pt.x) > maxCoordinate || Mathf.Abs(pt.z) > maxCoordinate)
                return Fail($"Waypoint coordinates must be within ±{maxCoordinate} m.");
        }

        ClearAgentWaypoints();

        if (agentWaypointRoot == null)
            agentWaypointRoot = new GameObject("AgentWaypoints").transform;

        float y = mission != null ? mission.cruiseHeight : 5f;
        var list = new List<Transform>();

        if (fromCurrentPosition && leader != null)
        {
            Vector3 lp = leader.transform.position;
            list.Add(CreateWaypoint("AgentWP_Start", new Vector3(lp.x, y, lp.z)));
        }

        for (int i = 0; i < points.Length; i++)
            list.Add(CreateWaypoint($"AgentWP_{i + 1}", new Vector3(points[i].x, y, points[i].z)));

        if (list.Count < 2)
            return Fail("A mission needs at least 2 points (or 1 point with fromCurrentPosition=true).");

        waypointManager.waypoints = list.ToArray();
        waypointManager.ResetMission();

        return Ok($"Loaded {list.Count} waypoints ({list.Count - 1} path segments).");
    }

    Response StartMission()
    {
        if (mission == null) return Fail("MissionManager not assigned.");
        if (IsMissionActive()) return Fail($"Mission already running ({mission.state}).");
        if (waypointManager == null || waypointManager.waypoints == null || waypointManager.waypoints.Length < 2)
            return Fail("Need at least 2 waypoints. Call set_waypoints first.");

        mission.StartMission();
        return Ok("Mission started: taking off.");
    }

    Response CancelMission()
    {
        if (mission == null) return Fail("MissionManager not assigned.");

        mission.CancelMission();
        return Ok("Mission cancelled. Swarm switched to manual hover.");
    }

    // ===================================================
    // HELPERS
    // ===================================================

    bool IsMissionActive()
    {
        return mission != null &&
               (mission.state == MissionManager.MissionState.Takeoff ||
                mission.state == MissionManager.MissionState.Patrol);
    }

    Transform CreateWaypoint(string name, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(agentWaypointRoot, false);
        go.transform.position = pos;
        agentWaypoints.Add(go.transform);
        return go.transform;
    }

    void ClearAgentWaypoints()
    {
        foreach (var t in agentWaypoints)
            if (t != null) Destroy(t.gameObject);

        agentWaypoints.Clear();
    }

    void ApplyAgentWaypointHeight(float y)
    {
        foreach (var t in agentWaypoints)
            if (t != null) t.position = new Vector3(t.position.x, y, t.position.z);
    }

    static Vec3Dto V(Vector3 v) => new Vec3Dto { x = Round(v.x), y = Round(v.y), z = Round(v.z) };
    static float Round(float f) => Mathf.Round(f * 100f) / 100f;

    StateDto BuildState()
    {
        var s = new StateDto
        {
            missionState = mission != null ? mission.state.ToString() : "Unknown",
            leaderMode = leader != null ? leader.mode.ToString() : "Unknown",
            formation = formation != null ? formation.currentFormation.ToString() : "Unknown",
            spacing = formation != null ? formation.spacing : 0f,
            cruiseHeight = mission != null ? mission.cruiseHeight : 0f,
            followerCount = swarm != null ? swarm.followers.Count : 0,
            currentSegment = mission != null ? mission.CurrentSegment : 0,
            segmentCount = mission != null ? mission.SegmentCount : 0,
            availableFormations = Enum.GetNames(typeof(FormationManager.FormationType))
        };

        if (leader != null)
        {
            s.leaderPosition = V(leader.transform.position);
            s.headingDeg = Round(leader.transform.eulerAngles.y);

            var rb = leader.GetComponent<Rigidbody>();
            s.speed = rb != null ? Round(rb.linearVelocity.magnitude) : 0f;
        }

        if (waypointManager != null && waypointManager.waypoints != null)
        {
            var wps = new List<Vec3Dto>();
            foreach (var t in waypointManager.waypoints)
                if (t != null) wps.Add(V(t.position));
            s.waypoints = wps.ToArray();
        }

        return s;
    }

    static Response Ok(string msg) => new Response { ok = true, message = msg };
    static Response Fail(string msg) => new Response { ok = false, message = msg };
}
