using UnityEngine;

/// <summary>
/// CollisionAvoidance: the drone's obstacle "senses" and reflexes.
///
/// Every physics step it sphere-casts a forward fan (long range) and a 360 deg ring
/// (short range) against the Obstacle layer, plus one ray down for ground clearance.
/// From those hits it works out:
///   - Repulsion      a push away from everything close (potential field)
///   - Steer()        a new travel direction that bends around what is ahead,
///                    turning toward whichever side has more free space
///   - SpeedFactor    brake as the gap ahead closes
///   - ClimbTargetY   hop over obstacles that are only a little taller than us
///                    (trees, huts, walls); tall buildings are steered around
///   - Assist()       manual-flight brake assist, like consumer drones
/// DroneController (leader) and FollowerDrone use it automatically.
/// </summary>
public class CollisionAvoidance : MonoBehaviour
{
    public enum Status { Clear, Steering, Braking, Climbing }

    [Header("Sensing")]
    [Tooltip("Layers treated as obstacles. Empty = the 'Obstacle' layer.")]
    public LayerMask obstacleMask = 0;
    public float range = 16f;
    public float forwardFan = 60f;
    public int forwardRays = 9;
    public int ringRays = 12;
    public float probeRadius = 0.6f;

    [Header("Direction search (VFH)")]
    [Tooltip("Directions scanned all around for free space. 0 = off (followers).")]
    public int searchRays = 24;
    [Tooltip("Penalty for changing direction, so the drone does not dither.")]
    public float turnPenalty = 0.35f;

    [Header("Response")]
    public float safeDistance = 5f;
    public float repulsionGain = 1.4f;
    [Tooltip("Climb over an obstacle if its top is at most this far above the drone.")]
    public float maxClimb = 10f;
    public float climbMargin = 3f;

    [Header("Mapping (v2.0)")]
    [Tooltip("Report what the sensors see to the shared OccupancyGrid, over SwarmComms.")]
    public bool reportToMap = true;
    public int mapEverySteps = 5;

    [Header("Debug")]
    public bool drawRays = true;

    public Status CurrentStatus { get; private set; } = Status.Clear;
    public float NearestDistance { get; private set; } = Mathf.Infinity;
    public Collider NearestCollider { get; private set; }
    public Vector3 Repulsion { get; private set; }
    public float AheadDistance { get; private set; } = Mathf.Infinity;
    public float GroundDistance { get; private set; } = Mathf.Infinity;
    public float ClimbTargetY { get; private set; } = float.NegativeInfinity;

    public bool WallFollowing => chosenIndex >= 0 && chosenDeviation > 15f;
    public int WallFollowSide => chosenIndex;
    public Vector3 LastSteer { get; private set; }

    Vector3 probeDir = Vector3.forward;
    float leftFree, rightFree;
    Collider aheadCollider;
    Vector3 aheadNormal;
    float climbUntil;
    float[] freeDist;
    Vector3[] searchDirs;
    Vector3 lastChoice;
    int chosenIndex = -1;
    float chosenFree = Mathf.Infinity, chosenDeviation;
    int mapTick;
    bool recording;
    readonly System.Collections.Generic.List<Vector4> mapHits = new System.Collections.Generic.List<Vector4>();
    readonly System.Collections.Generic.List<Vector3> mapFree = new System.Collections.Generic.List<Vector3>();

    void Awake()
    {
        if (obstacleMask.value == 0)
        {
            int layer = LayerMask.NameToLayer("Obstacle");
            obstacleMask = layer >= 0 ? (1 << layer) : Physics.DefaultRaycastLayers;
        }
        probeDir = Flat(transform.forward, Vector3.forward);
    }

    /// <summary>Which way the drone intends to travel (horizontal).</summary>
    public void SetProbeDirection(Vector3 dir)
    {
        dir.y = 0;
        if (dir.sqrMagnitude > 0.01f) probeDir = dir.normalized;
    }

    void FixedUpdate() => Sense();

    // ===================================================
    // SENSING
    // ===================================================

    void Sense()
    {
        Vector3 origin = transform.position;
        recording = reportToMap && OccupancyGrid.Instance != null && (++mapTick % Mathf.Max(1, mapEverySteps) == 0);
        if (recording) { mapHits.Clear(); mapFree.Clear(); }
        Vector3 rep = Vector3.zero;
        float nearest = Mathf.Infinity;
        Collider nearestCol = null;

        // Short 360 deg ring: keeps us off walls beside and behind
        for (int i = 0; i < ringRays; i++)
        {
            Vector3 d = Quaternion.Euler(0, 360f * i / ringRays, 0) * probeDir;
            Cast(origin, d, range * 0.5f, 1f, ref rep, ref nearest, ref nearestCol, out _, out _);
        }

        // Close contact: a sphere-cast cannot see a wall the drone already touches
        foreach (var col in Physics.OverlapSphere(origin, probeRadius + 1.5f, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 cp = col.ClosestPoint(origin);
            Vector3 away = origin - cp;
            away.y = 0;
            float d = away.magnitude;
            if (d < 1e-3f) continue;
            rep += away / d * (1f - d / (probeRadius + 1.5f)) * 3f;
            if (d < nearest) { nearest = d; nearestCol = col; }
        }

        // Long forward fan: looks where we are going
        float ahead = range;
        Collider aheadCol = null;
        Vector3 aheadN = -probeDir;
        leftFree = rightFree = 0;
        for (int i = 0; i < forwardRays; i++)
        {
            float t = forwardRays == 1 ? 0.5f : (float)i / (forwardRays - 1);
            float a = Mathf.Lerp(-forwardFan, forwardFan, t);
            Vector3 d = Quaternion.Euler(0, a, 0) * probeDir;
            float dist = Cast(origin, d, range, 1.5f, ref rep, ref nearest, ref nearestCol, out Collider hitCol, out Vector3 n);

            if (Mathf.Abs(a) <= forwardFan * 0.3f && dist < ahead) { ahead = dist; aheadCol = hitCol; aheadN = n; }
            if (a < 0) leftFree += dist; else if (a > 0) rightFree += dist;
        }

        // 360 deg direction search: how far is free in each world direction?
        if (searchRays > 0)
        {
            if (freeDist == null || freeDist.Length != searchRays)
            {
                freeDist = new float[searchRays];
                searchDirs = new Vector3[searchRays];
                for (int k = 0; k < searchRays; k++)
                    searchDirs[k] = Quaternion.Euler(0, 360f * k / searchRays, 0) * Vector3.forward;
            }
            for (int k = 0; k < searchRays; k++)
            {
                bool hitK = Physics.SphereCast(origin, probeRadius, searchDirs[k], out RaycastHit h, range, obstacleMask, QueryTriggerInteraction.Ignore);
                freeDist[k] = hitK ? h.distance : range;
                if (recording)
                {
                    if (hitK) mapHits.Add(new Vector4(h.point.x, h.point.y, h.point.z, h.collider.bounds.max.y));
                    else mapFree.Add(origin + searchDirs[k] * range);
                }
            }
        }

        Repulsion = rep;
        NearestDistance = nearest;
        NearestCollider = nearestCol;
        AheadDistance = ahead;
        aheadCollider = aheadCol;
        aheadN.y = 0;
        aheadNormal = aheadN.sqrMagnitude > 1e-4f ? aheadN.normalized : -probeDir;

        bool groundHit = Physics.Raycast(origin, Vector3.down, out RaycastHit g, 300f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        GroundDistance = groundHit ? g.distance : Mathf.Infinity;

        if (recording)
        {
            var comms = SwarmComms.Instance;
            if (comms == null || comms.SendToLeader(gameObject))
            {
                var grid = OccupancyGrid.Instance;
                foreach (var e in mapFree) grid.ReportFree(origin, e);
                foreach (var h in mapHits) { grid.ReportFree(origin, h); grid.ReportHit(h, h.w); }
                if (groundHit) grid.ReportGround(g.point);
            }
        }

        // Hop over low obstacles directly ahead
        if (aheadCollider != null && ahead < safeDistance * 2.5f)
        {
            float top = aheadCollider.bounds.max.y;
            if (top - origin.y < maxClimb)
            {
                ClimbTargetY = Mathf.Max(ClimbTargetY, top + climbMargin);
                climbUntil = Time.time + 1.5f;
            }
        }
        if (Time.time > climbUntil) ClimbTargetY = float.NegativeInfinity;

        bool climbing = ClimbTargetY > origin.y + 0.5f;
        CurrentStatus =
            climbing ? Status.Climbing :
            chosenDeviation > 15f ? Status.Steering :
            ahead < safeDistance ? Status.Braking :
            rep.sqrMagnitude > 0.003f ? Status.Steering :
            Status.Clear;
    }

    float Cast(Vector3 origin, Vector3 dir, float length, float weight,
               ref Vector3 rep, ref float nearest, ref Collider nearestCol, out Collider hitCol, out Vector3 normal)
    {
        hitCol = null;
        normal = Vector3.zero;
        if (Physics.SphereCast(origin, probeRadius, dir, out RaycastHit hit, length, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            float k = 1f - hit.distance / length;
            rep -= dir * k * k * weight;
            if (hit.distance < nearest) { nearest = hit.distance; nearestCol = hit.collider; }
            hitCol = hit.collider;
            normal = hit.normal;
            if (recording) mapHits.Add(new Vector4(hit.point.x, hit.point.y, hit.point.z, hit.collider.bounds.max.y));
            if (drawRays) Debug.DrawRay(origin, dir * hit.distance, Color.red);
            return hit.distance;
        }
        if (drawRays) Debug.DrawRay(origin, dir * length, new Color(0f, 1f, 0f, 0.25f));
        if (recording) mapFree.Add(origin + dir * length);
        return length;
    }

    // ===================================================
    // REFLEXES
    // ===================================================

    /// <summary>
    /// Pick a travel direction (Vector Field Histogram style): of all directions with
    /// enough free space (and free neighbours, so the gap is wide enough to fit), take the
    /// one closest to the goal, with a small penalty for turning away from the last
    /// choice. The repulsion field then keeps the drone centred between obstacles.
    /// Followers (searchRays = 0) just use goal + repulsion.
    /// </summary>
    public Vector3 Steer(Vector3 desired)
    {
        desired.y = 0;
        if (desired.sqrMagnitude < 1e-4f) return desired;
        Vector3 goal = desired.normalized;

        if (freeDist == null || searchRays <= 0 || CurrentStatus == Status.Climbing)
        {
            chosenIndex = -1;
            chosenFree = AheadDistance;
            return Finish(goal + Repulsion * repulsionGain, goal);
        }

        int n = freeDist.Length;
        float need = safeDistance * 2f;
        int best = -1;
        float bestCost = float.MaxValue;

        for (int k = 0; k < n; k++)
        {
            float d = freeDist[k];
            float side = Mathf.Min(freeDist[(k + n - 1) % n], freeDist[(k + 1) % n]);
            if (d < need || side < need * 0.6f) continue;

            float cost = Vector3.Angle(searchDirs[k], goal) / 180f;
            if (lastChoice.sqrMagnitude > 0.01f)
                cost += turnPenalty * Vector3.Angle(searchDirs[k], lastChoice) / 180f;
            if (cost < bestCost) { bestCost = cost; best = k; }
        }

        Vector3 dir;
        if (best < 0)
        {
            // Boxed in: let the repulsion field push us out
            chosenIndex = -1;
            chosenFree = NearestDistance;
            dir = goal * 0.3f + Repulsion * repulsionGain * 2f;
        }
        else
        {
            chosenIndex = best;
            chosenFree = freeDist[best];
            Vector3 c = searchDirs[best];
            if (Vector3.Angle(c, goal) <= 360f / n) c = goal;   // goal itself is open: go straight
            dir = c + Repulsion * repulsionGain * 0.5f;
        }

        return Finish(dir, goal);
    }

    Vector3 Finish(Vector3 dir, Vector3 goal)
    {
        dir.y = 0;
        LastSteer = dir.sqrMagnitude > 1e-4f ? dir.normalized : goal;
        lastChoice = LastSteer;
        chosenDeviation = Vector3.Angle(LastSteer, goal);
        return LastSteer;
    }

    /// <summary>Speed multiplier: slows down as obstacles get close.</summary>
    public float SpeedFactor
    {
        get
        {
            if (CurrentStatus == Status.Climbing)
                return AheadDistance < safeDistance * 1.2f ? 0f : 0.35f;   // rise first, then move on
            float free = Mathf.Min(chosenFree, range);
            float f = Mathf.InverseLerp(safeDistance * 0.4f, safeDistance * 2.5f, free);
            if (NearestDistance < safeDistance * 0.6f) f = Mathf.Min(f, 0.4f);
            return Mathf.Clamp(f, 0.2f, 1f);
        }
    }

    /// <summary>Manual-flight brake assist: scales down motion toward obstacles.</summary>
    public Vector3 Assist(Vector3 velocity)
    {
        Vector3 h = new Vector3(velocity.x, 0, velocity.z);
        if (h.sqrMagnitude > 0.01f)
        {
            float f = Mathf.Clamp01(Mathf.InverseLerp(safeDistance * 0.5f, safeDistance * 2f, AheadDistance));
            h *= f;
        }
        h += Repulsion * repulsionGain * 2f;
        return new Vector3(h.x, velocity.y, h.z);
    }

    static Vector3 Flat(Vector3 v, Vector3 fallback)
    {
        v.y = 0;
        return v.sqrMagnitude > 1e-4f ? v.normalized : fallback;
    }
}
