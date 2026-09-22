using UnityEngine;

public class WaypointManager : MonoBehaviour
{
    public Transform[] waypoints;

    private int currentIndex = 0;

    public void ResetMission()
    {
        currentIndex = 0;
    }

    public Transform CurrentTarget()
    {
        if (waypoints == null || waypoints.Length == 0)
            return null;

        if (currentIndex >= waypoints.Length)
            return null;

        return waypoints[currentIndex];
    }

    public Transform NextWaypoint()
    {
        currentIndex++;

        if (currentIndex >= waypoints.Length)
            return null;

        return waypoints[currentIndex];
    }

    public bool IsMissionComplete()
    {
        return currentIndex >= waypoints.Length;
    }

    void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Length < 2)
            return;

        Gizmos.color = Color.yellow;

        for (int i = 0; i < waypoints.Length - 1; i++)
        {
            if (waypoints[i] != null && waypoints[i + 1] != null)
            {
                Gizmos.DrawLine(
                    waypoints[i].position,
                    waypoints[i + 1].position);
            }
        }

        Gizmos.color = Color.cyan;

        foreach (Transform wp in waypoints)
        {
            if (wp != null)
                Gizmos.DrawSphere(wp.position, 0.25f);
        }
    }
}