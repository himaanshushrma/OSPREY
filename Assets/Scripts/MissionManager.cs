using System.Collections.Generic;
using UnityEngine;

public class MissionManager : MonoBehaviour
{
    public enum MissionState
    {
        Idle,
        Takeoff,
        Patrol,
        Complete
    }

    [Header("References")]
    public DroneController leader;
    public WaypointManager waypointManager;
    public SwarmManager swarm;
    public PathPlanner planner;

    [Header("Mission")]
    public float cruiseHeight = 5f;
    public float waypointRadius = 2f;
    public int lookAheadPoints = 6;

    public KeyCode startKey = KeyCode.P;
    public KeyCode cancelKey = KeyCode.Escape;

    public MissionState state = MissionState.Idle;

    private List<Vector3> currentPath = new List<Vector3>();

    private int currentSegment = 0;
    private int nearestIndex = 0;

    private Transform pathTarget;

    void Start()
    {
        GameObject obj = new GameObject("PathTarget");
        pathTarget = obj.transform;
    }

    void Update()
    {
        if (state == MissionState.Idle &&
            Input.GetKeyDown(startKey))
        {
            StartMission();
        }

        if (Input.GetKeyDown(cancelKey))
        {
            CancelMission();
        }

        if (state == MissionState.Patrol)
        {
            FollowPath();
        }
    }

    void FixedUpdate()
    {
        if (state != MissionState.Takeoff)
            return;

        if (Mathf.Abs(
            leader.transform.position.y -
            cruiseHeight) < 0.25f)
        {
            state = MissionState.Patrol;
            GenerateCurve();
        }
    }

    // ===================================================
    // START
    // ===================================================

    void StartMission()
    {
        currentSegment = 0;
        nearestIndex = 0;

        leader.mode = DroneController.Mode.Auto;
        leader.targetHeight = cruiseHeight;

        if (swarm != null)
            swarm.SetAllFollowersAuto();

        state = MissionState.Takeoff;

        Debug.Log("Mission Started");
    }

    // ===================================================
    // CANCEL
    // ===================================================

    void CancelMission()
    {
        leader.mode = DroneController.Mode.Manual;
        leader.currentWaypoint = null;

        if (swarm != null)
            swarm.SetAllFollowersManual();

        state = MissionState.Idle;

        Debug.Log("Mission Cancelled");
    }

    // ===================================================
    // CREATE BEZIER CURVE
    // ===================================================

    void GenerateCurve()
    {
        if (planner == null ||
            waypointManager == null ||
            waypointManager.waypoints.Length < 2)
        {
            Debug.LogError("Planner / Waypoints missing");
            return;
        }

        if (currentSegment >= waypointManager.waypoints.Length - 1)
        {
            CompleteMission();
            return;
        }

        Transform start =
            waypointManager.waypoints[currentSegment];

        Transform end =
            waypointManager.waypoints[currentSegment + 1];

        currentPath = planner.GeneratePath(start, end);

        nearestIndex = 0;

        pathTarget.position = currentPath[0];
        leader.currentWaypoint = pathTarget;

        Debug.Log($"Curve : WP_{currentSegment + 1} -> WP_{currentSegment + 2}");
    }

    // ===================================================
    // PURE PURSUIT FOLLOWER
    // ===================================================

    void FollowPath()
    {
        if (currentPath == null ||
            currentPath.Count == 0)
            return;

        // ---------- Find closest point ----------
        float best = Mathf.Infinity;

        Vector2 droneXZ = new Vector2(
            leader.transform.position.x,
            leader.transform.position.z);

        for (int i = nearestIndex;
             i < currentPath.Count;
             i++)
        {
            Vector2 p = new Vector2(
                currentPath[i].x,
                currentPath[i].z);

            float d = Vector2.Distance(droneXZ, p);

            if (d < best)
            {
                best = d;
                nearestIndex = i;
            }
        }

        // ---------- Look Ahead ----------
        int targetIndex =
            Mathf.Min(
                nearestIndex + lookAheadPoints,
                currentPath.Count - 1);

        pathTarget.position = currentPath[targetIndex];
        leader.currentWaypoint = pathTarget;

        // ---------- Mission Transition ----------
        Transform destination =
            waypointManager.waypoints[currentSegment + 1];

        Vector2 goal = new Vector2(
            destination.position.x,
            destination.position.z);

        if (Vector2.Distance(droneXZ, goal) <= waypointRadius)
        {
            Debug.Log($"Reached WP_{currentSegment + 1}");

            currentSegment++;

            GenerateCurve();
        }
    }

    // ===================================================
    // COMPLETE
    // ===================================================

    void CompleteMission()
    {
        leader.currentWaypoint = null;
        leader.mode = DroneController.Mode.Manual;

        if (swarm != null)
            swarm.SetAllFollowersManual();

        state = MissionState.Complete;

        Debug.Log("MISSION COMPLETE");
    }
}