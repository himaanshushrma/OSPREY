using UnityEngine;

/// <summary>
/// FollowerDrone: holds a formation slot around the leader.
/// Slot = leader position + leader rotation x formation offset (from FormationManager).
///
/// v2.0: the follower flies from the leader state it RECEIVED over SwarmComms (dead-reckoned
/// with the leader's velocity), not from the leader's true transform. If the link has been
/// silent too long it holds position (failsafe). It carries its own camera (ObjectDetector,
/// no overlay) so the whole swarm searches, and it can fail (Fail(): motors off, it drops).
/// With CollisionAvoidance (added automatically) it is pushed away from obstacles, climbs
/// over low ones, and never sinks into the ground.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FollowerDrone : MonoBehaviour
{
    [Header("References")]
    public Transform leader;
    public FormationManager formation;

    [Header("Identity")]
    public int droneID;

    [Header("Flight")]
    public float maxSpeed = 12f;
    public float followGain = 12f;
    public float hoverGain = 10f;
    public float rotationSpeed = 12f;

    [Header("Avoidance")]
    public bool obstacleAvoidance = true;
    public float avoidanceStrength = 8f;
    public float minGroundClearance = 1.5f;

    [Header("Comms (v2.0)")]
    [Tooltip("Hold position if no leader state has arrived for this long.")]
    public float linkFailsafe = 1.5f;

    public bool Alive { get; private set; } = true;
    public bool LinkOnline { get; set; } = true;
    public int OriginalId { get; private set; }

    Rigidbody rb;
    CollisionAvoidance avoid;

    bool hasState;
    Vector3 lPos, lVel;
    Quaternion lRot = Quaternion.identity;
    float lTime;
    Vector3 holdPos;
    bool holding;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 1.5f;
        rb.angularDamping = 4f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        OriginalId = droneID;

        if (obstacleAvoidance)
        {
            avoid = GetComponent<CollisionAvoidance>();
            if (avoid == null) avoid = gameObject.AddComponent<CollisionAvoidance>();
            avoid.range = 10f;           // shorter senses than the leader
            avoid.forwardRays = 5;
            avoid.ringRays = 8;
            avoid.searchRays = 0;        // followers track their slot; no direction search
            avoid.drawRays = false;
        }

        if (GetComponent<ObjectDetector>() == null)
            gameObject.AddComponent<ObjectDetector>();
    }

    /// <summary>Called by SwarmComms when a leader broadcast gets through.</summary>
    public void ReceiveLeaderState(Vector3 pos, Quaternion rot, Vector3 vel, float time)
    {
        lPos = pos; lRot = rot; lVel = vel; lTime = time;
        hasState = true;
    }

    /// <summary>Simulated motor failure: the drone stops flying and falls.</summary>
    public void Fail()
    {
        if (!Alive) return;
        Alive = false;
        LinkOnline = false;
        rb.useGravity = true;
        rb.linearDamping = 0.2f;
        rb.angularDamping = 0.5f;
        rb.AddTorque(new Vector3(2f, 0f, 3f), ForceMode.VelocityChange);
        if (avoid != null) avoid.enabled = false;
    }

    void FixedUpdate()
    {
        if (!Alive || leader == null || formation == null)
            return;

        // Leader state: from the radio if comms exist, else the true transform
        Vector3 leaderPos;
        Quaternion leaderRot;
        if (SwarmComms.Instance != null)
        {
            if (!hasState || Time.time - lTime > linkFailsafe)
            {
                // Link lost: hover where we are
                if (!holding) { holding = true; holdPos = transform.position; }
                Hold();
                return;
            }
            holding = false;
            leaderPos = lPos + lVel * (Time.time - lTime);
            leaderRot = lRot;
        }
        else
        {
            leaderPos = leader.position;
            leaderRot = leader.rotation;
        }

        // Formation offset in LEADER LOCAL SPACE -> world slot
        Vector3 localOffset = formation.GetOffset(droneID);
        Vector3 target =
            leaderPos +
            leaderRot * Vector3.right * localOffset.x +
            leaderRot * Vector3.up * localOffset.y +
            leaderRot * Vector3.forward * localOffset.z;

        Vector3 error = target - transform.position;

        Vector3 velocity = error * followGain;
        velocity.y = error.y * hoverGain;

        if (avoid != null)
        {
            avoid.SetProbeDirection(new Vector3(velocity.x, 0, velocity.z));

            Vector3 push = avoid.Repulsion * avoid.repulsionGain * avoidanceStrength;
            velocity.x += push.x;
            velocity.z += push.z;

            if (avoid.ClimbTargetY > transform.position.y)
                velocity.y = Mathf.Max(velocity.y, (avoid.ClimbTargetY - transform.position.y) * hoverGain);

            if (avoid.GroundDistance < minGroundClearance && velocity.y < 0)
                velocity.y = (minGroundClearance - avoid.GroundDistance) * hoverGain;
        }

        velocity = Vector3.ClampMagnitude(velocity, maxSpeed);

        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, velocity, 10f * Time.fixedDeltaTime);

        // Match leader heading
        Quaternion targetRot = Quaternion.Euler(0, leaderRot.eulerAngles.y, 0);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime));
    }

    void Hold()
    {
        Vector3 v = (holdPos - transform.position) * followGain;
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.ClampMagnitude(v, maxSpeed), 10f * Time.fixedDeltaTime);
    }
}
