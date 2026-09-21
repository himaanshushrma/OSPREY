using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class FollowerDrone : MonoBehaviour
{
    public Transform leader;
    public FormationManager formation;
    public int droneID;

    [Header("Flight")]
    public float maxSpeed = 8f;
    public float followGain = 6f;
    public float hoverGain = 8f;
    public float rotationSpeed = 8f;

    Rigidbody rb;
    Rigidbody leaderRb;
    ObstacleAvoidance sensor;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        leaderRb = leader.GetComponent<Rigidbody>();
        sensor = GetComponent<ObstacleAvoidance>();

        rb.useGravity = false;
        rb.linearDamping = 0;
        rb.angularDamping = 4;
    }

    void FixedUpdate()
    {
        if (leader == null || formation == null) return;

        Vector3 target =
            leader.TransformPoint(formation.GetOffset(droneID));

        Fly(target);
    }

    void Fly(Vector3 target)
    {
        // Desired velocity = leader velocity
        Vector3 desired = leaderRb.linearVelocity;

        // Correction toward formation slot
        Vector3 error = target - transform.position;
        desired += error * followGain;

        // Obstacle avoidance
        if (sensor != null)
            desired += sensor.GetAvoidanceForce();

        desired.y = error.y * hoverGain;

        if (desired.magnitude > maxSpeed)
            desired = desired.normalized * maxSpeed;

        // Instant velocity matching
        rb.linearVelocity = Vector3.Lerp(
            rb.linearVelocity,
            desired,
            8f * Time.fixedDeltaTime);

        Vector3 look = rb.linearVelocity;
        look.y = 0;

        if (look.sqrMagnitude > 0.05f)
        {
            Quaternion rot = Quaternion.LookRotation(look);

            rb.MoveRotation(
                Quaternion.Slerp(
                    rb.rotation,
                    rot,
                    rotationSpeed * Time.fixedDeltaTime));
        }
    }
}