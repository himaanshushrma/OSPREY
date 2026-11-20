using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SearchMission (v2.0): autonomous area search.
///
/// Given an area, the swarm spreads into a wide Wedge (side by side, so each drone's camera
/// sweeps its own strip) and flies a lawnmower pattern. Lane spacing comes from the swarm's
/// real width plus each camera's ground footprint, with 15% overlap. Every leg is planned
/// with RRT* around obstacles. Coverage is tracked on an 8 m grid from every live drone's
/// camera footprint (shown on the minimap), survivors are found by every drone's detector
/// and radioed to the leader, and when the pattern is done the swarm returns home and lands.
/// Start it from the API / mission agent (search_area) or StartSearch().
///
/// v3.0: StartSplitSearch() splits the area into one strip per drone; every drone flies its
/// own lawnmower lanes at the same time (followers in Independent mode, altitudes staggered
/// so they never share a height), maps and survivor reports merge over the radio, and when
/// all are done they rejoin the formation and go home. Both searches check the battery
/// first (BatteryManager) and refuse plans the swarm cannot finish.
/// </summary>
public class SearchMission : MonoBehaviour
{
    public static SearchMission Instance { get; private set; }

    [Header("Pattern")]
    public float defaultAltitude = 25f;
    public float formationSpacing = 14f;
    [Range(0, 0.5f)] public float overlap = 0.15f;
    public bool returnHomeWhenDone = true;

    [Header("Coverage")]
    public float coverageCell = 8f;

    public bool Active { get; private set; }
    public bool Split { get; private set; }
    public int Teams { get; private set; }
    public bool HasArea { get; private set; }
    public Rect Area { get; private set; }
    public int Lanes { get; private set; }
    public float Coverage => total > 0 ? (float)covered / total : 0f;
    public string LastReport { get; private set; } = "";

    bool[] cov;
    int nx, nz, total, covered;
    float alt, nextMark, startTime;
    int survivorsAtStart;

    DroneController leader;
    MissionManager mission;
    FormationManager formation;
    SwarmManager swarm;

    void Awake()
    {
        Instance = this;
        leader = GetComponent<DroneController>();
    }

    void Start()
    {
        mission = FindFirstObjectByType<MissionManager>();
        formation = FindFirstObjectByType<FormationManager>();
        swarm = FindFirstObjectByType<SwarmManager>();
    }

    // ===================================================
    // START / CANCEL
    // ===================================================

    public bool StartSearch(Rect area, float altitude, out string message)
    {
        if (mission == null || formation == null || leader == null)
        { message = "MissionManager, FormationManager or leader missing."; return false; }
        if (mission.state == MissionManager.MissionState.Takeoff || mission.state == MissionManager.MissionState.Patrol)
        { message = "A mission is running. Cancel it first."; return false; }

        var grid = OccupancyGrid.Instance;
        if (grid != null)
        {
            Rect b = grid.Bounds;
            area = Rect.MinMaxRect(Mathf.Max(area.xMin, b.xMin + 5), Mathf.Max(area.yMin, b.yMin + 5),
                                   Mathf.Min(area.xMax, b.xMax - 5), Mathf.Min(area.yMax, b.yMax - 5));
        }
        if (area.width < 10 || area.height < 10) { message = "Search area is too small (min 10 x 10 m)."; return false; }

        altitude = Mathf.Clamp(altitude <= 0 ? defaultAltitude : altitude, 8f, 40f);

        // Swarm width: Wedge puts followers side by side, `row` slots out per side
        int alive = 0;
        if (swarm != null) foreach (var f in swarm.followers) if (f != null && f.Alive) alive++;
        formation.currentFormation = FormationManager.FormationType.Wedge;
        formation.spacing = formationSpacing;
        int rows = alive > 0 ? (alive - 1) / 2 + 1 : 0;
        float halfWidth = rows * formationSpacing;
        float footHalf = altitude * 0.9f;
        float swath = Mathf.Max(20f, 2f * (halfWidth + footHalf) * (1f - overlap));

        // Lawnmower lanes along z, starting from the corner nearest the leader
        Lanes = Mathf.Max(1, Mathf.CeilToInt(area.width / swath));
        float step = area.width / Lanes;
        Vector3 lp = leader.transform.position;
        bool fromRight = Mathf.Abs(lp.x - area.xMax) < Mathf.Abs(lp.x - area.xMin);
        bool fromTop = Mathf.Abs(lp.z - area.yMax) < Mathf.Abs(lp.z - area.yMin);

        var points = new List<Vector3> { lp };
        for (int k = 0; k < Lanes; k++)
        {
            int lane = fromRight ? Lanes - 1 - k : k;
            float x = area.xMin + step * (lane + 0.5f);
            bool northward = (k % 2 == 0) != fromTop;
            float z0 = northward ? area.yMin + 5 : area.yMax - 5;
            float z1 = northward ? area.yMax - 5 : area.yMin + 5;
            points.Add(new Vector3(x, 0, z0));
            points.Add(new Vector3(x, 0, z1));
        }

        // Battery check on the planned route
        var battery = BatteryManager.Instance;
        if (battery != null)
        {
            float est = EstimateLength(points);
            if (!battery.CheckMission(est, points[points.Count - 1], out _, out string bmsg))
            { message = bmsg; return false; }
        }

        mission.cruiseHeight = altitude;
        var r = MissionRoute.Load(mission.waypointManager, points, altitude, true);
        Split = false;
        Teams = 1;

        // Coverage grid
        nx = Mathf.Max(1, Mathf.CeilToInt(area.width / coverageCell));
        nz = Mathf.Max(1, Mathf.CeilToInt(area.height / coverageCell));
        total = nx * nz;
        cov = new bool[total];
        covered = 0;

        Area = area;
        HasArea = true;
        Active = true;
        alt = altitude;
        startTime = Time.time;
        survivorsAtStart = ObjectDetector.Primary != null ? ObjectDetector.Primary.SurvivorsFound.Count : 0;

        mission.StartMission();

        message = $"Search started: {Lanes} lanes {step:0} m apart over {area.width:0} x {area.height:0} m at {altitude:0} m, " +
                  $"{alive + 1} drones in a {2 * halfWidth:0} m wide Wedge. Route: {r.points} waypoints, " +
                  $"{r.detourPoints} RRT* detour points, {r.length:0} m.";
        LastReport = message;
        Debug.Log("[Search] " + message);
        return true;
    }

    public void Cancel()
    {
        if (!Active) return;
        Active = false;
        RejoinAll();
        mission.CancelMission();
    }

    /// <summary>Stop the search early (e.g. low battery); followers rejoin the formation.</summary>
    public void Abort(string reason)
    {
        if (!Active) return;
        Active = false;
        RejoinAll();
        LastReport = $"Search stopped ({reason}) at {Coverage * 100:0}% coverage.";
        Debug.LogWarning("[Search] " + LastReport);
    }

    void RejoinAll()
    {
        if (swarm == null) return;
        foreach (var f in swarm.followers)
            if (f != null && f.Alive && f.Mode == FollowerDrone.FlightMode.Independent) f.Rejoin();
    }

    static float EstimateLength(List<Vector3> pts)
    {
        float len = 0;
        for (int i = 1; i < pts.Count; i++)
            len += Vector2.Distance(new Vector2(pts[i - 1].x, pts[i - 1].z), new Vector2(pts[i].x, pts[i].z));
        return len * 1.1f;
    }

    // ===================================================
    // SPLIT SEARCH (v3.0)
    // ===================================================

    public bool StartSplitSearch(Rect area, float altitude, out string message)
    {
        if (mission == null || formation == null || leader == null)
        { message = "MissionManager, FormationManager or leader missing."; return false; }
        if (Active || mission.state == MissionManager.MissionState.Takeoff || mission.state == MissionManager.MissionState.Patrol)
        { message = "A mission is running. Cancel it first."; return false; }

        var grid = OccupancyGrid.Instance;
        if (grid != null)
        {
            Rect b = grid.Bounds;
            area = Rect.MinMaxRect(Mathf.Max(area.xMin, b.xMin + 5), Mathf.Max(area.yMin, b.yMin + 5),
                                   Mathf.Min(area.xMax, b.xMax - 5), Mathf.Min(area.yMax, b.yMax - 5));
        }
        if (area.width < 20 || area.height < 20) { message = "Search area is too small (min 20 x 20 m)."; return false; }
        altitude = Mathf.Clamp(altitude <= 0 ? defaultAltitude : altitude, 8f, 34f);

        // Drones, left to right by current x so their paths to the strips don't cross
        var drones = new List<Transform> { leader.transform };
        var followers = new List<FollowerDrone>();
        if (swarm != null) foreach (var f in swarm.followers) if (f != null && f.Alive) { followers.Add(f); drones.Add(f.transform); }
        drones.Sort((a, b) => a.position.x.CompareTo(b.position.x));

        int k = drones.Count;
        float strip = area.width / k;
        float swath = Mathf.Max(12f, 2f * altitude * 0.9f * (1f - overlap));
        int lanesPerStrip = Mathf.Max(1, Mathf.CeilToInt(strip / swath));
        bool fromTop = Mathf.Abs(leader.transform.position.z - area.yMax) < Mathf.Abs(leader.transform.position.z - area.yMin);

        var planner = MissionRoute.Planner;
        var battery = BatteryManager.Instance;
        float longest = 0;
        var plans = new Dictionary<Transform, List<Vector3>>();

        for (int i = 0; i < k; i++)
        {
            float laneAlt = altitude + i * 1.5f;   // stagger heights: drones never share an altitude
            var pts = new List<Vector3> { new Vector3(drones[i].position.x, laneAlt, drones[i].position.z) };
            for (int l = 0; l < lanesPerStrip; l++)
            {
                float x = area.xMin + i * strip + strip * (l + 0.5f) / lanesPerStrip;
                bool north = (l % 2 == 0) != fromTop;
                pts.Add(new Vector3(x, laneAlt, north ? area.yMin + 5 : area.yMax - 5));
                pts.Add(new Vector3(x, laneAlt, north ? area.yMax - 5 : area.yMin + 5));
            }
            float len = EstimateLength(pts);
            if (len > longest) longest = len;
            plans[drones[i]] = pts;
        }

        if (battery != null)
        {
            Vector3 end = plans[leader.transform][plans[leader.transform].Count - 1];
            if (!battery.CheckMission(longest, end, out _, out string bmsg)) { message = bmsg; return false; }
        }

        // Leader: through MissionManager; followers: their own RRT*-planned routes
        mission.cruiseHeight = plans[leader.transform][0].y;
        var r = MissionRoute.Load(mission.waypointManager, plans[leader.transform], mission.cruiseHeight, true);
        int detours = r.detourPoints;
        foreach (var f in followers)
        {
            var route = planner.PlanThrough(plans[f.transform], out int added);
            detours += added;
            f.FlyRoute(route);
        }

        nx = Mathf.Max(1, Mathf.CeilToInt(area.width / coverageCell));
        nz = Mathf.Max(1, Mathf.CeilToInt(area.height / coverageCell));
        total = nx * nz;
        cov = new bool[total];
        covered = 0;

        Area = area; HasArea = true; Active = true; Split = true; Teams = k;
        Lanes = lanesPerStrip * k;
        alt = altitude;
        startTime = Time.time;
        survivorsAtStart = ObjectDetector.Primary != null ? ObjectDetector.Primary.SurvivorsFound.Count : 0;

        mission.StartMission();

        message = $"Split search started: {k} drones, one {strip:0} m strip each ({lanesPerStrip} lane{(lanesPerStrip > 1 ? "s" : "")} per strip) " +
                  $"over {area.width:0} x {area.height:0} m at {altitude:0}-{altitude + (k - 1) * 1.5f:0} m. Longest route {longest:0} m, " +
                  $"{detours} RRT* detour points.";
        LastReport = message;
        Debug.Log("[Search] " + message);
        return true;
    }

    // ===================================================
    // COVERAGE + COMPLETION
    // ===================================================

    void Update()
    {
        if (!Active || mission == null) return;

        if (Time.time >= nextMark)
        {
            nextMark = Time.time + 0.25f;
            Mark(leader.transform);
            if (swarm != null)
                foreach (var f in swarm.followers)
                    if (f != null && f.Alive) Mark(f.transform);
        }

        bool followersDone = true;
        if (Split && swarm != null)
            foreach (var f in swarm.followers)
                if (f != null && f.Alive && f.Mode == FollowerDrone.FlightMode.Independent && !f.RouteDone) followersDone = false;

        bool leaderDone = mission.state == MissionManager.MissionState.Complete || mission.state == MissionManager.MissionState.Idle;
        if (leaderDone && (followersDone || mission.state == MissionManager.MissionState.Idle))
        {
            RejoinAll();
            Active = false;
            var det = ObjectDetector.Primary;
            int found = det != null ? det.SurvivorsFound.Count : 0;
            LastReport = $"{(Split ? "Split search" : "Search")} finished: {Coverage * 100:0}% of the area covered in {Time.time - startTime:0} s, " +
                         $"{found - survivorsAtStart} new survivors ({found}/{(det != null ? det.SurvivorsTotal : 0)} found so far).";
            Debug.Log("[Search] " + LastReport);
            if (returnHomeWhenDone && mission.state == MissionManager.MissionState.Complete) leader.ReturnHome();
        }
    }

    /// <summary>Mark the camera's ground footprint (a circle ahead of the drone) as covered.</summary>
    void Mark(Transform t)
    {
        if (alt <= 0) return;
        Vector3 fwd = t.forward;
        fwd.y = 0;
        fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
        float height = Mathf.Max(5f, t.position.y);
        Vector3 c = t.position + fwd * height * 0.8f;
        float r = height * 0.9f;

        int x0 = Mathf.FloorToInt((c.x - r - Area.xMin) / coverageCell), x1 = Mathf.FloorToInt((c.x + r - Area.xMin) / coverageCell);
        int z0 = Mathf.FloorToInt((c.z - r - Area.yMin) / coverageCell), z1 = Mathf.FloorToInt((c.z + r - Area.yMin) / coverageCell);
        for (int iz = Mathf.Max(0, z0); iz <= Mathf.Min(nz - 1, z1); iz++)
        for (int ix = Mathf.Max(0, x0); ix <= Mathf.Min(nx - 1, x1); ix++)
        {
            float cx = Area.xMin + (ix + 0.5f) * coverageCell, cz = Area.yMin + (iz + 0.5f) * coverageCell;
            if ((cx - c.x) * (cx - c.x) + (cz - c.z) * (cz - c.z) > r * r) continue;
            int i = iz * nx + ix;
            if (!cov[i]) { cov[i] = true; covered++; }
        }
    }

    /// <summary>Minimap overlay: covered cells glow cyan, the area outline is drawn.</summary>
    public void Tint(OccupancyGrid grid, Color32[] px, int n)
    {
        if (cov == null) return;
        var cyan = new Color32(64, 200, 255, 255);
        for (int iz = 0; iz < n; iz++)
        for (int ix = 0; ix < n; ix++)
        {
            Vector3 c = grid.CellCenter(ix, iz);
            if (!Area.Contains(new Vector2(c.x, c.z))) continue;
            int cx = Mathf.Min(nx - 1, (int)((c.x - Area.xMin) / coverageCell));
            int cz = Mathf.Min(nz - 1, (int)((c.z - Area.yMin) / coverageCell));
            int i = iz * n + ix;
            bool edge = c.x - Area.xMin < grid.cellSize || Area.xMax - c.x < grid.cellSize ||
                        c.z - Area.yMin < grid.cellSize || Area.yMax - c.z < grid.cellSize;
            if (edge) px[i] = cyan;
            else if (cov[cz * nx + cx]) px[i] = Color32.Lerp(px[i], cyan, 0.45f);
        }
    }

    static GUIStyle style;

    void OnGUI()
    {
        if (!HasArea) return;
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
        var r = new Rect(Screen.width - 330, 436, 320, 52);
        GUI.Box(r, GUIContent.none);
        var det = ObjectDetector.Primary;
        GUI.Label(new Rect(r.x + 8, r.y + 4, 310, 46),
            $"<b>{(Split ? "Split search" : "Search")}</b> {(Active ? "<color=#40C8FF>in progress</color>" : "done")} · {Lanes} lanes · coverage <b>{Coverage * 100:0}%</b>\n" +
            $"Survivors found: <b>{(det != null ? det.SurvivorsFound.Count : 0)}/{(det != null ? det.SurvivorsTotal : 0)}</b>", style);
    }
}
