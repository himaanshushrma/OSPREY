using System.Collections.Generic;
using OSPREY.Planning;
using UnityEngine;

/// <summary>
/// PathPlanner: turns one mission leg (waypoint A -> waypoint B) into dense route points
/// that MissionManager's pure pursuit follows.
///
///   1. RRT* (RRTStarPlanner) finds a collision-free polyline at flight altitude
///      (a clear leg comes back as a straight line instantly).
///   2. BezierSmoother rounds every corner with a Bezier curve and samples the route
///      every couple of metres.
/// Modules are found or added automatically if not assigned.
/// (v2.0: merges the original Bezier planner with the RRT* + Bezier design.)
/// </summary>
public class PathPlanner : MonoBehaviour
{
    [Header("Modules")]
    public RRTStarPlanner rrtPlanner;
    public BezierSmoother bezier;

    [Header("Bezier Curve")]
    public float controlDistance = 6f;

    [Range(20, 120)]
    public int samples = 60;

    [Header("Debug")]
    public bool drawPath = true;

    private List<Vector3> finalPath = new List<Vector3>();

    // ====================================================
    // PUBLIC API
    // ====================================================

    public List<Vector3> GeneratePath(Transform start, Transform goal)
    {
        if (start == null || goal == null)
            return new List<Vector3>();

        EnsureModules();

        // 1. RRT* path (straight if the leg is already clear)
        List<Vector3> raw = rrtPlanner != null
            ? rrtPlanner.GeneratePath(start.position, goal.position)
            : new List<Vector3> { start.position, goal.position };

        // 2. Smooth path
        bezier.cornerRadius = controlDistance;
        bezier.minSamples = samples;
        finalPath = bezier.Smooth(raw);

        return finalPath;
    }

    public List<Vector3> GetCurrentPath()
    {
        return finalPath;
    }

    void EnsureModules()
    {
        if (rrtPlanner == null) rrtPlanner = FindFirstObjectByType<RRTStarPlanner>();
        if (rrtPlanner == null) rrtPlanner = gameObject.AddComponent<RRTStarPlanner>();
        if (bezier == null) bezier = GetComponent<BezierSmoother>();
        if (bezier == null) bezier = gameObject.AddComponent<BezierSmoother>();
    }

    // ====================================================
    // DEBUG DRAW
    // ====================================================

    void OnDrawGizmos()
    {
        if (!drawPath) return;

        if (finalPath != null && finalPath.Count >= 2)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < finalPath.Count - 1; i++)
                Gizmos.DrawLine(finalPath[i], finalPath[i + 1]);
            return;
        }

        // Edit mode: preview the scene's waypoint route
        WaypointManager wp = FindFirstObjectByType<WaypointManager>();
        if (wp == null || wp.waypoints == null || wp.waypoints.Length < 2) return;

        Gizmos.color = Color.cyan;
        for (int i = 0; i < wp.waypoints.Length - 1; i++)
            if (wp.waypoints[i] != null && wp.waypoints[i + 1] != null)
                Gizmos.DrawLine(wp.waypoints[i].position, wp.waypoints[i + 1].position);
    }
}
