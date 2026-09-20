using UnityEngine;

public class WaypointManager : MonoBehaviour
{
    public Transform[] waypoints;

    public Transform GetWaypoint(int index)
    {
        if (waypoints == null || waypoints.Length == 0)
            return null;

        return waypoints[index % waypoints.Length];
    }

    public int Count => waypoints.Length;
}