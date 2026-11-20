using System.Collections.Generic;
using UnityEngine;

namespace OSPREY.Planning
{
    /// <summary>
    /// RRT* global planner (v2.0). Plans a collision-free route at a fixed flight altitude
    /// (the y of start/goal) before the swarm takes off.
    ///
    ///   1. Sample a random point (goal-biased) inside the map.
    ///   2. Steer a step from the nearest tree node toward it.
    ///   3. Choose parent: of the nearby nodes, connect through the one giving the
    ///      cheapest total path (not just the nearest one).
    ///   4. Rewire: nearby nodes that get cheaper through the new node switch parent.
    ///   5. Keep refining for a while after the first path is found, then shortcut the
    ///      path (drop every waypoint that a straight clear line can skip).
    ///
    /// Collision checks use the OccupancyGrid (fast, includes what the swarm has sensed)
    /// with an inflation radius so the whole formation fits; without a grid they fall
    /// back to physics sphere checks on the obstacle layer.
    /// </summary>
    public class RRTStarPlanner : MonoBehaviour
    {
        [Header("Planning Area (used when no OccupancyGrid)")]
        public Vector2 mapSize = new Vector2(200, 200);

        [Header("RRT* Settings")]
        public int maxIterations = 3500;
        public int refineIterations = 1200;
        public float stepSize = 8f;
        public float rewireRadius = 22f;
        public float goalRadius = 6f;
        [Range(0, 1)] public float goalBias = 0.12f;

        [Header("Safety")]
        [Tooltip("Keep this far (horizontally) from obstacles, so the formation fits.")]
        public float inflation = 7f;
        [Tooltip("Vertical gap needed above an obstacle to fly over it.")]
        public float clearance = 4f;
        public float collisionRadius = 0.6f;

        [Header("Obstacle Layer (fallback)")]
        public LayerMask obstacleLayer;

        public int LastTreeSize { get; private set; }
        public float LastPathLength { get; private set; }
        public bool LastSucceeded { get; private set; }

        readonly List<Node> tree = new List<Node>();
        Vector3 escapeStart, escapeGoal;
        bool startInside, goalInside;
        OccupancyGrid grid;
        System.Random rng = new System.Random(1234);

        void Awake()
        {
            if (obstacleLayer.value == 0)
            {
                int l = LayerMask.NameToLayer("Obstacle");
                obstacleLayer = l >= 0 ? (1 << l) : Physics.DefaultRaycastLayers;
            }
        }

        // ----------------------------------------------------
        // PUBLIC API
        // ----------------------------------------------------

        /// <summary>Route from start to goal at start.y. Returns [start, ..., goal].</summary>
        public List<Vector3> GeneratePath(Vector3 start, Vector3 goal)
        {
            grid = OccupancyGrid.Instance;
            goal.y = start.y;
            tree.Clear();

            // A start/goal inside the safety margin (e.g. hovering by the hangar) may leave it
            startInside = goalInside = false;
            startInside = !PointFree(start); escapeStart = start;
            goalInside = !PointFree(goal); escapeGoal = goal;

            if (SegmentFree(start, goal))
            {
                LastSucceeded = true; LastTreeSize = 1; LastPathLength = Vector3.Distance(start, goal);
                return new List<Vector3> { start, goal };
            }

            Node root = new Node(start);
            tree.Add(root);
            Node bestGoal = null;
            int foundAt = -1;

            for (int i = 0; i < maxIterations; i++)
            {
                if (foundAt >= 0 && i - foundAt > refineIterations) break;

                Vector3 sample = Sample(goal, start.y);
                Node nearest = Nearest(sample);
                Vector3 p = Steer(nearest.position, sample);

                if (!PointFree(p) || !SegmentFree(nearest.position, p)) continue;

                // Choose parent: cheapest total cost among neighbours
                var near = Neighbours(p);
                Node parent = nearest;
                float best = nearest.cost + Vector3.Distance(nearest.position, p);
                foreach (var nb in near)
                {
                    float c = nb.cost + Vector3.Distance(nb.position, p);
                    if (c < best && SegmentFree(nb.position, p)) { best = c; parent = nb; }
                }

                Node node = new Node(p, parent);
                tree.Add(node);

                // Rewire: neighbours that become cheaper through the new node
                foreach (var nb in near)
                {
                    if (nb == parent) continue;
                    float c = node.cost + Vector3.Distance(node.position, nb.position);
                    if (c + 0.01f < nb.cost && SegmentFree(node.position, nb.position))
                        nb.SetParent(node);
                }

                // Reached the goal?
                if (Vector3.Distance(p, goal) < goalRadius + stepSize && SegmentFree(p, goal))
                {
                    float total = node.cost + Vector3.Distance(p, goal);
                    if (bestGoal == null || total < bestGoal.cost + Vector3.Distance(bestGoal.position, goal))
                        bestGoal = node;
                    if (foundAt < 0) foundAt = i;
                }
            }

            LastTreeSize = tree.Count;

            if (bestGoal == null)
            {
                LastSucceeded = false;
                Debug.LogWarning($"[RRT*] No path found from ({start.x:0},{start.z:0}) to ({goal.x:0},{goal.z:0}) " +
                                 $"after {tree.Count} nodes; flying direct and relying on obstacle avoidance.");
                return new List<Vector3> { start, goal };
            }

            var path = Reconstruct(bestGoal);
            path.Add(goal);
            path = Shortcut(path);

            LastSucceeded = true;
            LastPathLength = 0;
            for (int i = 1; i < path.Count; i++) LastPathLength += Vector3.Distance(path[i - 1], path[i]);
            return path;
        }

        /// <summary>Plan through a list of waypoints, keeping every original waypoint.</summary>
        public List<Vector3> PlanThrough(List<Vector3> waypoints, out int addedPoints)
        {
            var route = new List<Vector3>();
            addedPoints = 0;
            if (waypoints.Count == 0) return route;
            route.Add(waypoints[0]);
            for (int i = 1; i < waypoints.Count; i++)
            {
                var leg = GeneratePath(waypoints[i - 1], waypoints[i]);
                for (int k = 1; k < leg.Count; k++) route.Add(leg[k]);
                addedPoints += Mathf.Max(0, leg.Count - 2);
            }
            return route;
        }

        // ----------------------------------------------------
        // SAMPLING / TREE
        // ----------------------------------------------------

        Vector3 Sample(Vector3 goal, float y)
        {
            if (rng.NextDouble() < goalBias) return goal;
            Rect b = grid != null ? grid.Bounds : new Rect(-mapSize.x, -mapSize.y, mapSize.x * 2, mapSize.y * 2);
            return new Vector3(b.xMin + (float)rng.NextDouble() * b.width, y, b.yMin + (float)rng.NextDouble() * b.height);
        }

        Node Nearest(Vector3 p)
        {
            Node best = tree[0];
            float bd = float.MaxValue;
            foreach (var n in tree)
            {
                float d = (n.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }

        List<Node> Neighbours(Vector3 p)
        {
            var list = new List<Node>();
            float r2 = rewireRadius * rewireRadius;
            foreach (var n in tree)
                if ((n.position - p).sqrMagnitude <= r2) list.Add(n);
            return list;
        }

        Vector3 Steer(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            return len <= stepSize ? to : from + d / len * stepSize;
        }

        // ----------------------------------------------------
        // COLLISION CHECKS
        // ----------------------------------------------------

        bool PointFree(Vector3 p)
        {
            float esc = inflation + (grid != null ? grid.cellSize : 1f);
            if (startInside && (p - escapeStart).sqrMagnitude < esc * esc) return true;
            if (goalInside && (p - escapeGoal).sqrMagnitude < esc * esc) return true;
            if (grid != null) return !grid.Blocked(p, clearance, inflation);
            return !Physics.CheckSphere(p, Mathf.Max(collisionRadius, inflation), obstacleLayer, QueryTriggerInteraction.Ignore);
        }

        bool SegmentFree(Vector3 a, Vector3 b)
        {
            float step = grid != null ? grid.cellSize * 0.5f : 1f;
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / step));
            for (int i = 0; i <= n; i++)
                if (!PointFree(Vector3.Lerp(a, b, i / (float)n))) return false;
            return true;
        }

        List<Vector3> Reconstruct(Node end)
        {
            var path = new List<Vector3>();
            for (Node c = end; c != null; c = c.parent) path.Add(c.position);
            path.Reverse();
            return path;
        }

        List<Vector3> Shortcut(List<Vector3> path)
        {
            var result = new List<Vector3> { path[0] };
            int i = 0;
            while (i < path.Count - 1)
            {
                int j = path.Count - 1;
                while (j > i + 1 && !SegmentFree(path[i], path[j])) j--;
                result.Add(path[j]);
                i = j;
            }
            return result;
        }

        // ----------------------------------------------------
        // DEBUG TREE
        // ----------------------------------------------------

        void OnDrawGizmos()
        {
            if (tree == null) return;
            Gizmos.color = new Color(0, 1, 1, 0.35f);
            foreach (Node n in tree)
                if (n.parent != null) Gizmos.DrawLine(n.position, n.parent.position);
        }
    }
}
