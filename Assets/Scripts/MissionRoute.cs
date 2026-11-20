using System.Collections.Generic;
using OSPREY.Planning;
using UnityEngine;

/// <summary>
/// MissionRoute (v2.0): turns a list of points into the mission's waypoints, running the
/// RRT* planner on every leg first so routes go around the city before take-off.
/// Used by SwarmCommandAPI (agent missions), SearchMission and MissionManager (P key).
/// Planned waypoints are temporary objects under "MissionRoute"; the scene's own waypoint
/// objects are never changed.
/// </summary>
public static class MissionRoute
{
    static Transform container;
    static readonly List<Transform> created = new List<Transform>();
    static readonly HashSet<WaypointManager> planned = new HashSet<WaypointManager>();

    public struct Result
    {
        public int points;
        public int detourPoints;
        public float length;
        public bool allLegsPlanned;
    }

    public static RRTStarPlanner Planner
    {
        get
        {
            var p = Object.FindFirstObjectByType<RRTStarPlanner>();
            if (p == null)
            {
                var host = Object.FindFirstObjectByType<OccupancyGrid>();
                p = (host != null ? host.gameObject : new GameObject("RRTStarPlanner")).AddComponent<RRTStarPlanner>();
            }
            return p;
        }
    }

    /// <summary>Load points (y ignored) at `altitude`, RRT*-planning each leg if `plan`.</summary>
    public static Result Load(WaypointManager wm, List<Vector3> points, float altitude, bool plan)
    {
        var pts = new List<Vector3>();
        foreach (var p in points) pts.Add(new Vector3(p.x, altitude, p.z));

        var result = new Result { allLegsPlanned = true };
        List<Vector3> route = pts;
        if (plan && pts.Count >= 2)
        {
            var planner = Planner;
            route = new List<Vector3> { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                var leg = planner.GeneratePath(pts[i - 1], pts[i]);
                if (!planner.LastSucceeded) result.allLegsPlanned = false;
                for (int k = 1; k < leg.Count; k++) route.Add(leg[k]);
                result.detourPoints += Mathf.Max(0, leg.Count - 2);
            }
        }

        Clear();
        if (container == null) container = new GameObject("MissionRoute").transform;
        var list = new List<Transform>();
        for (int i = 0; i < route.Count; i++)
        {
            var t = new GameObject($"Route_{i}").transform;
            t.SetParent(container, false);
            t.position = route[i];
            created.Add(t);
            list.Add(t);
            if (i > 0) result.length += Vector3.Distance(route[i - 1], route[i]);
        }

        wm.waypoints = list.ToArray();
        wm.ResetMission();
        planned.Add(wm);
        result.points = route.Count;

        OccupancyGrid.PlannedRoute = new List<Vector3>(route);
        Densify(OccupancyGrid.PlannedRoute);
        return result;
    }

    /// <summary>
    /// For missions started with P from the scene's own waypoints: plan around obstacles
    /// once, from the leader's position. Already-planned routes are left alone.
    /// </summary>
    public static void EnsurePlanned(WaypointManager wm, Transform leader, float altitude)
    {
        if (wm == null || wm.waypoints == null || wm.waypoints.Length < 2) return;
        if (planned.Contains(wm)) return;

        var pts = new List<Vector3>();
        if (leader != null) pts.Add(leader.position);
        foreach (var t in wm.waypoints) if (t != null) pts.Add(t.position);
        var r = Load(wm, pts, altitude, true);
        Debug.Log($"[RRT*] Scene route planned: {r.points} waypoints, {r.detourPoints} detour points, {r.length:0} m.");
    }

    public static void Clear()
    {
        foreach (var t in created) if (t != null) Object.Destroy(t.gameObject);
        created.Clear();
    }

    static void Densify(List<Vector3> route)
    {
        var dense = new List<Vector3>();
        for (int i = 1; i < route.Count; i++)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(route[i - 1], route[i]) / 2f));
            for (int k = 0; k < n; k++) dense.Add(Vector3.Lerp(route[i - 1], route[i], k / (float)n));
        }
        if (route.Count > 0) dense.Add(route[route.Count - 1]);
        route.Clear();
        route.AddRange(dense);
    }
}
