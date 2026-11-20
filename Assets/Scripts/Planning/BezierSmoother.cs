using System.Collections.Generic;
using UnityEngine;

namespace OSPREY.Planning
{
    /// <summary>
    /// BezierSmoother: rounds the corners of a polyline route (e.g. from RRT*) with
    /// quadratic Bezier curves and samples it densely, so the drone flies smooth turns.
    /// Each corner is replaced by a curve that starts and ends `cornerRadius` metres
    /// (at most half of each neighbouring segment) from the corner, so the curve stays
    /// close to the planned, collision-checked lines.
    /// </summary>
    public class BezierSmoother : MonoBehaviour
    {
        [Tooltip("How far before/after a corner the curve begins/ends (metres).")]
        public float cornerRadius = 6f;
        [Tooltip("Distance between output points (metres).")]
        public float spacing = 2f;
        [Tooltip("Never return fewer points than this for a leg.")]
        public int minSamples = 40;

        public List<Vector3> Smooth(List<Vector3> path)
        {
            var output = new List<Vector3>();
            if (path == null || path.Count == 0) return output;
            if (path.Count == 1) { output.Add(path[0]); return output; }

            float length = 0;
            for (int i = 1; i < path.Count; i++) length += Vector3.Distance(path[i - 1], path[i]);
            float step = Mathf.Max(0.25f, Mathf.Min(spacing, length / Mathf.Max(1, minSamples)));

            Vector3 cursor = path[0];
            output.Add(cursor);

            for (int i = 1; i < path.Count; i++)
            {
                Vector3 corner = path[i];
                bool last = i == path.Count - 1;

                if (last)
                {
                    AddLine(output, cursor, corner, step);
                    break;
                }

                Vector3 next = path[i + 1];
                float rIn = Mathf.Min(cornerRadius, Vector3.Distance(cursor, corner) * 0.5f);
                float rOut = Mathf.Min(cornerRadius, Vector3.Distance(corner, next) * 0.5f);
                Vector3 a = corner + (cursor - corner).normalized * rIn;
                Vector3 b = corner + (next - corner).normalized * rOut;

                AddLine(output, cursor, a, step);
                AddCurve(output, a, corner, b, step);
                cursor = b;
            }

            return output;
        }

        static void AddLine(List<Vector3> output, Vector3 from, Vector3 to, float step)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / step));
            for (int k = 1; k <= n; k++) output.Add(Vector3.Lerp(from, to, k / (float)n));
        }

        static void AddCurve(List<Vector3> output, Vector3 a, Vector3 c, Vector3 b, float step)
        {
            float approx = Vector3.Distance(a, c) + Vector3.Distance(c, b);
            int n = Mathf.Max(2, Mathf.CeilToInt(approx / step));
            for (int k = 1; k <= n; k++)
            {
                float t = k / (float)n, u = 1 - t;
                output.Add(u * u * a + 2 * u * t * c + t * t * b);
            }
        }
    }
}
