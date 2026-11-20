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
///   takeoff / land / return_home  {}
///   search_area     { "x0":-190, "z0":-180, "x1":-60, "z1":40, "altitude":25 }   (v2.0)
///   fail_drone      {}   simulate a follower failure (v2.0)
///   split_search    same fields as search_area: every drone searches its own strip (v3.0)
///   search_area with "split": true does the same.
///   capture_dataset { "count": 1500 }   write a labelled YOLO dataset (v3.0)
///   set_battery     { "percent": 35 }    testing: set every live drone's battery (v3.0)
/// GET /map returns the occupancy grid in ROS nav_msgs/OccupancyGrid layout (v3.0).
/// set_waypoints plans every leg with RRT* around obstacles unless "plan": false.
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
    [Serializable] public class DetectionDto { public string label; public float confidence; public float x; public float y; public float z; public float distance; }

    [Serializable]
    public class CommandRequest
    {
        public string action = "";
        public string formation = "";
        public float spacing = -1f;
        public float altitude = -1f;
        public bool fromCurrentPosition = true;
        public bool plan = true;
        public float x0, z0, x1, z1;
        public bool split = false;
        public int count = 0;
        public float percent = 100f;
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
        public string flightState;
        public float altitudeAboveGround;
        public string avoidance;
        public string avoidanceDebug;
        public float nearestObstacle;
        public DetectionDto[] detections;
        public int survivorsFound;
        public int survivorsTotal;
        public Vec3Dto[] survivorPositions;
        public string[] survivorFinders;
        public int commsOnline;
        public int commsTotal;
        public string commsEvent;
        public float mapExploredPercent;
        public bool searchActive;
        public float searchCoverage;
        public int searchLanes;
        public string searchReport;
        public string lastPlan;
        // v3.0
        public bool searchSplit;
        public int searchTeams;
        public float batteryPercent;
        public float swarmBatteryMin;
        public float batteryNeededToGetHome;
        public bool charging;
        public float[] followerBatteries;
        public string batteryEvent;
        public Vec3Dto[] followerPositions;
        public string[] followerModes;
        public string visionSource;
        public string datasetStatus;
    }

    [Serializable]
    public class MapDto
    {
        public int width, height;
        public float resolution, originX, originZ;
        public float exploredPercent;
        public int[] data;
        public Vec3Dto[] plannedRoute;
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

    /// <summary>The port actually in use (may differ from `port` if that one was busy).</summary>
    public int ActivePort { get; private set; }

    void OnEnable()
    {
        // If the preferred port is busy (e.g. held by a crashed editor), try the next few.
        string lastError = "";
        for (int p = port; p < port + 6; p++)
        {
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{p}/");
                listener.Start();
                ActivePort = p;
                break;
            }
            catch (Exception e)
            {
                lastError = e.Message;
                try { listener?.Close(); } catch { }
                listener = null;
            }
        }

        if (listener == null)
        {
            Debug.LogError($"[SwarmCommandAPI] Could not start server on ports {port}-{port + 5}: {lastError}");
            return;
        }

        running = true;
        serverThread = new Thread(ServerLoop) { IsBackground = true, Name = "SwarmCommandAPI" };
        serverThread.Start();

#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += StopServer;
#endif
        if (ActivePort != port)
            Debug.LogWarning($"[SwarmCommandAPI] Port {port} was busy; using {ActivePort} instead.");
        Debug.Log($"[SwarmCommandAPI] Listening on http://localhost:{ActivePort}/");
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopServer;
#endif
        StopServer();
    }

    // Also called before Unity reloads scripts during Play mode; otherwise the old
    // listener keeps the port open and swallows requests without answering.
    void StopServer()
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
            if (p.path == "/map" && p.method == "GET")
            {
                p.responseJson = MapJson();
                p.done.Set();
                continue;
            }

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
            case "set_waypoints":  return SetWaypoints(req.waypoints, req.fromCurrentPosition, req.plan);
            case "start_mission":  return StartMission();
            case "cancel_mission": return CancelMission();
            case "takeoff":        return FlightCommand("takeoff");
            case "land":           return FlightCommand("land");
            case "return_home":    return FlightCommand("return_home");
            case "search_area":    return SearchArea(req);
            case "capture_dataset":
            {
                var dc = DatasetCapture.Instance;
                if (dc == null) return Fail("DatasetCapture not available (DroneController.enableV3 is off).");
                return dc.Begin(req.count, out string m) ? Ok(m) : Fail(m);
            }
            case "set_battery":
            {
                var bm = BatteryManager.Instance;
                if (bm == null) return Fail("BatteryManager not available.");
                int n = 0;
                foreach (var b in bm.LiveBatteries()) { b.SetPercent(req.percent); n++; }
                return Ok($"Battery of {n} drones set to {req.percent:0}% (testing).");
            }
            case "split_search":   req.split = true; return SearchArea(req);
            case "fail_drone":     return FailDrone();
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
        }

        return Ok($"Cruise altitude set to {altitude:0.0} m.");
    }

    string lastPlan = "";

    Response SetWaypoints(Vec2Dto[] points, bool fromCurrentPosition, bool plan)
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

        float y = mission != null ? mission.cruiseHeight : 5f;
        var pts = new List<Vector3>();
        if (fromCurrentPosition && leader != null) pts.Add(leader.transform.position);
        foreach (var p in points) pts.Add(new Vector3(p.x, y, p.z));
        if (pts.Count < 2)
            return Fail("A mission needs at least 2 points (or 1 point with fromCurrentPosition=true).");

        var r = MissionRoute.Load(waypointManager, pts, y, plan);
        lastPlan = plan
            ? $"RRT*: {r.points} waypoints incl. {r.detourPoints} detour points, {r.length:0} m" + (r.allLegsPlanned ? "" : " (some legs direct)")
            : $"Direct: {r.points} waypoints, {r.length:0} m";
        return Ok($"Loaded {pts.Count} waypoints. {lastPlan}.");
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

    Response SearchArea(CommandRequest req)
    {
        var search = SearchMission.Instance;
        if (search == null) return Fail("SearchMission not available (DroneController.enableV2 is off).");
        var area = Rect.MinMaxRect(Mathf.Min(req.x0, req.x1), Mathf.Min(req.z0, req.z1), Mathf.Max(req.x0, req.x1), Mathf.Max(req.z0, req.z1));
        bool ok = req.split ? search.StartSplitSearch(area, req.altitude, out string msg)
                            : search.StartSearch(area, req.altitude, out msg);
        return ok ? Ok(msg) : Fail(msg);
    }

    Response FailDrone()
    {
        var comms = SwarmComms.Instance;
        if (comms == null) return Fail("SwarmComms not available.");
        return Ok(comms.FailRandomDrone());
    }

    Response FlightCommand(string what)
    {
        if (leader == null) return Fail("Leader DroneController not assigned.");
        switch (what)
        {
            case "takeoff":
                if (leader.State != DroneController.FlightState.Landed && leader.State != DroneController.FlightState.Landing)
                    return Fail($"Already airborne ({leader.State}).");
                leader.TakeOff();
                return Ok("Taking off.");
            case "land":
                leader.Land();
                return Ok("Landing at the current position.");
            default:
                if (leader.State == DroneController.FlightState.Landed) return Fail("Drone is landed.");
                leader.ReturnHome();
                return Ok($"Returning home at {Mathf.Max(leader.targetHeight, leader.returnHomeHeight):0} m, then landing.");
        }
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

        if (leader != null)
        {
            s.flightState = leader.State.ToString();
            s.altitudeAboveGround = Round(leader.transform.position.y - leader.GroundY);
            var av = leader.Avoider;
            s.avoidance = av != null ? av.CurrentStatus.ToString() : "Off";
            if (av != null)
            {
                var rbv = leader.GetComponent<Rigidbody>().linearVelocity;
                s.avoidanceDebug = $"ahead={(float.IsInfinity(av.AheadDistance) ? -1 : av.AheadDistance):0.0} side={av.WallFollowSide} " +
                                   $"steer=({av.LastSteer.x:0.00},{av.LastSteer.z:0.00}) vel=({rbv.x:0.0},{rbv.z:0.0}) " +
                                   $"wp={(leader.currentWaypoint != null ? $"({leader.currentWaypoint.position.x:0},{leader.currentWaypoint.position.z:0})" : "none")}";
            }
            s.nearestObstacle = av != null && !float.IsInfinity(av.NearestDistance) ? Round(av.NearestDistance) : -1f;

            var det = leader.GetComponent<ObjectDetector>();
            if (det != null)
            {
                var list = new List<DetectionDto>();
                foreach (var d in det.detections)
                {
                    if (list.Count >= 12) break;
                    list.Add(new DetectionDto { label = d.label, confidence = Round(d.confidence), x = Round(d.position.x), y = Round(d.position.y), z = Round(d.position.z), distance = Round(d.distance) });
                }
                s.detections = list.ToArray();
                s.survivorsFound = det.SurvivorsFound.Count;
                s.survivorsTotal = det.SurvivorsTotal;
                var sp = new List<Vec3Dto>();
                foreach (var p in det.SurvivorsFound) sp.Add(V(p));
                s.survivorPositions = sp.ToArray();
                s.survivorFinders = det.SurvivorFinders.ToArray();
            }

            var comms = SwarmComms.Instance;
            if (comms != null) { s.commsOnline = comms.Online; s.commsTotal = comms.Total; s.commsEvent = comms.LastEvent; }
            var grid = OccupancyGrid.Instance;
            if (grid != null) s.mapExploredPercent = Round(grid.ExploredPercent);
            var search = SearchMission.Instance;
            if (search != null)
            {
                s.searchActive = search.Active;
                s.searchCoverage = Round(search.Coverage * 100f);
                s.searchLanes = search.Lanes;
                s.searchReport = search.LastReport;
                s.searchSplit = search.Split;
                s.searchTeams = search.Teams;
            }

            var bm = BatteryManager.Instance;
            var lb = leader.GetComponent<DroneBattery>();
            if (lb != null) { s.batteryPercent = Round(lb.Percent); s.charging = lb.Charging; }
            if (bm != null)
            {
                s.swarmBatteryMin = Round(bm.SwarmPercent);
                s.batteryNeededToGetHome = Round(bm.NeededToGetHome);
                s.batteryEvent = bm.LastEvent;
            }
            if (swarm != null)
            {
                var bats = new List<float>(); var poss = new List<Vec3Dto>(); var modes = new List<string>();
                foreach (var f in swarm.followers)
                {
                    if (f == null) continue;
                    var fb = f.GetComponent<DroneBattery>();
                    bats.Add(fb != null ? Round(fb.Percent) : -1);
                    poss.Add(V(f.transform.position));
                    modes.Add(!f.Alive ? "Failed" : f.Mode.ToString());
                }
                s.followerBatteries = bats.ToArray(); s.followerPositions = poss.ToArray(); s.followerModes = modes.ToArray();
            }
            var vc = VisionClient.Instance;
            s.visionSource = vc != null && vc.Connected ? "yolov8" : "simulated";
            if (DatasetCapture.Instance != null) s.datasetStatus = DatasetCapture.Instance.Status;
            s.lastPlan = lastPlan;
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

    string MapJson()
    {
        var g = OccupancyGrid.Instance;
        if (g == null) return "{\"error\":\"OccupancyGrid not available\"}";
        var b = g.Bounds;
        var m = new MapDto
        {
            width = g.Cells, height = g.Cells, resolution = g.cellSize,
            originX = b.xMin, originZ = b.yMin, exploredPercent = Round(g.ExploredPercent),
            data = g.RosData()
        };
        var route = new List<Vec3Dto>();
        foreach (var pt in OccupancyGrid.PlannedRoute) route.Add(V(pt));
        m.plannedRoute = route.ToArray();
        return JsonUtility.ToJson(m);
    }

    static Response Ok(string msg) => new Response { ok = true, message = msg };
    static Response Fail(string msg) => new Response { ok = false, message = msg };
}
