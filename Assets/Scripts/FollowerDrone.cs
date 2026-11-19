using UnityEngine;

/// <summary>
/// FollowerDrone: holds a formation slot around the leader.
/// Slot = leader position + leader rotation x formation offset (from FormationManager).
/// With CollisionAvoidance (added automatically) the follower is pushed away from
/// nearby obstacles, climbs over low ones, and never sinks into the ground; it
/// rejoins its slot once the way is clear.
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

    Rigidbody rb;
    CollisionAvoidance avoid;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 1.5f;
        rb.angularDamping = 4f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

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
    }

    void FixedUpdate()
    {
        if (leader == null || formation == null)
            return;

        // Formation offset in LEADER LOCAL SPACE -> world slot
        Vector3 localOffset = formation.GetOffset(droneID);
        Vector3 target =
            leader.position +
            leader.right * localOffset.x +
            leader.up * localOffset.y +
            leader.forward * localOffset.z;

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
        Quaternion targetRot = Quaternion.Euler(0, leader.eulerAngles.y, 0);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime));
    }
}
