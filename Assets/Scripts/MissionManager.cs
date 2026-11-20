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
    public KeyCode startKey = KeyCode.P;
    public KeyCode cancelKey = KeyCode.Escape;

    public MissionState state = MissionState.Idle;

    List<Vector3> currentPath = new List<Vector3>();

    int currentSegment = 0;
    int pathIndex = 0;

    Transform pathTarget;

    // Read-only progress info (used by SwarmCommandAPI / the LLM agent)
    public int CurrentSegment => currentSegment;
    public int SegmentCount =>
        waypointManager != null && waypointManager.waypoints != null
            ? Mathf.Max(0, waypointManager.waypoints.Length - 1)
            : 0;

    void Start()
    {
        GameObject obj = new GameObject("PathTarget");
        pathTarget = obj.transform;

        // Always begin idle (the state field is serialized in the scene)
        state = MissionState.Idle;
    }

    void Update()
    {
        if ((state == MissionState.Idle ||
             state == MissionState.Complete) &&
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
            leader.transform.position.y - cruiseHeight) < 0.3f)
        {
            state = MissionState.Patrol;
            GenerateNewPath();
        }
    }

    //==================================================
    // START
    //==================================================

    public void StartMission()
    {
        currentSegment = 0;
        pathIndex = 0;

        // v2.0: plan the scene's route around obstacles (RRT*) before take-off
        MissionRoute.EnsurePlanned(waypointManager, leader.transform, cruiseHeight);

        leader.mode = DroneController.Mode.Auto;
        leader.targetHeight = cruiseHeight;

        if (swarm != null)
            swarm.SetAllFollowersAuto();

        state = MissionState.Takeoff;

        Debug.Log("MISSION STARTED");
    }

    //==================================================
    // CANCEL
    //==================================================

    public void CancelMission()
    {
        leader.mode = DroneController.Mode.Manual;
        leader.currentWaypoint = null;

        if (swarm != null)
            swarm.SetAllFollowersManual();

        state = MissionState.Idle;
    }

    //==================================================
    // RRT + BEZIER
    //==================================================

    void GenerateNewPath()
    {
        if (currentSegment >= waypointManager.waypoints.Length - 1)
        {
            CompleteMission();
            return;
        }

        Transform start =
            waypointManager.waypoints[currentSegment];

        Transform goal =
            waypointManager.waypoints[currentSegment + 1];

        currentPath = planner.GeneratePath(start, goal);

        pathIndex = 0;

        pathTarget.position = currentPath[0];

        leader.currentWaypoint = pathTarget;

        Debug.Log(
            $"Planning : WP{currentSegment + 1} → WP{currentSegment + 2}");
    }

    //==================================================
    // PURE PURSUIT FOLLOWER
    //==================================================

    void FollowPath()
    {
        if (currentPath == null)
            return;

        if (currentPath.Count == 0)
            return;

        Vector3 drone = leader.transform.position;

        float best = Mathf.Infinity;

        int closest = pathIndex;

        int searchLimit =
            Mathf.Min(pathIndex + 15, currentPath.Count - 1);

        for (int i = pathIndex; i <= searchLimit; i++)
        {
            float d = Vector2.Distance(
                new Vector2(drone.x, drone.z),
                new Vector2(currentPath[i].x, currentPath[i].z));

            if (d < best)
            {
                best = d;
                closest = i;
            }
        }

        pathIndex = closest;

        int lookAhead =
            Mathf.Min(pathIndex + 5, currentPath.Count - 1);

        pathTarget.position = currentPath[lookAhead];

        leader.currentWaypoint = pathTarget;

        Vector3 end =
            currentPath[currentPath.Count - 1];

        float endDistance = Vector2.Distance(
            new Vector2(drone.x, drone.z),
            new Vector2(end.x, end.z));

        if (endDistance < 1.5f)
        {
            currentSegment++;
            GenerateNewPath();
        }
    }

    //==================================================
    // COMPLETE
    //==================================================

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