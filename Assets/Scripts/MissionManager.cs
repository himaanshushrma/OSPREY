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

    [Header("Mission")]
    public float cruiseHeight = 5f;
    public KeyCode startKey = KeyCode.P;

    public MissionState state = MissionState.Idle;

    void Update()
    {
        if (state == MissionState.Idle && Input.GetKeyDown(startKey))
            StartMission();

        if (state == MissionState.Patrol)
            CheckWaypoint();
    }

    void FixedUpdate()
    {
        if (state != MissionState.Takeoff) return;

        if (Mathf.Abs(leader.transform.position.y - cruiseHeight) < 0.3f)
        {
            state = MissionState.Patrol;
            leader.currentWaypoint = waypointManager.CurrentTarget();
            Debug.Log("Patrol Started");
        }
    }

    void StartMission()
    {
        waypointManager.ResetMission();

        leader.mode = DroneController.Mode.Auto;
        leader.targetHeight = cruiseHeight;

        state = MissionState.Takeoff;

        Debug.Log("Mission Started");
    }

    void CheckWaypoint()
    {
        if (leader.currentWaypoint == null) return;

        Vector2 droneXZ = new Vector2(
            leader.transform.position.x,
            leader.transform.position.z);

        Vector2 targetXZ = new Vector2(
            leader.currentWaypoint.position.x,
            leader.currentWaypoint.position.z);

        float distance = Vector2.Distance(droneXZ, targetXZ);

        if (distance <= leader.reachDistance)
        {
            Transform next = waypointManager.NextWaypoint();

            if (next != null)
            {
                leader.currentWaypoint = next;
                Debug.Log("Flying to " + next.name);
            }
            else
            {
                leader.currentWaypoint = null;
                state = MissionState.Complete;
                Debug.Log("MISSION COMPLETE");
            }
        }
    }
}