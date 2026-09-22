using System.Collections.Generic;
using UnityEngine;

public class PathPlanner : MonoBehaviour
{
    [Header("Bezier Curve")]
    public float controlDistance = 6f;

    [Range(20, 120)]
    public int samples = 60;

    /// <summary>
    /// Generates a smooth cubic Bézier path between two waypoints.
    /// Rotation of waypoints is NOT required.
    /// </summary>
    public List<Vector3> GeneratePath(Transform start, Transform end)
    {
        List<Vector3> path = new List<Vector3>();

        if (start == null || end == null)
            return path;

        Vector3 p0 = start.position;
        Vector3 p3 = end.position;

        Vector3 direction = (p3 - p0).normalized;
        float distance = Vector3.Distance(p0, p3);

        float curve = Mathf.Min(controlDistance, distance * 0.35f);

        Vector3 p1 = p0 + direction * curve;
        Vector3 p2 = p3 - direction * curve;

        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            path.Add(CubicBezier(p0, p1, p2, p3, t));
        }

        return path;
    }

    Vector3 CubicBezier(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        float t)
    {
        float u = 1f - t;

        return
            u * u * u * a +
            3 * u * u * t * b +
            3 * u * t * t * c +
            t * t * t * d;
    }

    void OnDrawGizmos()
    {
        WaypointManager wp =
            FindFirstObjectByType<WaypointManager>();

        if (wp == null) return;
        if (wp.waypoints.Length < 2) return;

        Gizmos.color = Color.cyan;

        for (int i = 0; i < wp.waypoints.Length - 1; i++)
        {
            List<Vector3> curve =
                GeneratePath(
                    wp.waypoints[i],
                    wp.waypoints[i + 1]);

            for (int j = 0; j < curve.Count - 1; j++)
            {
                Gizmos.DrawLine(
                    curve[j],
                    curve[j + 1]);
            }
        }
    }
}