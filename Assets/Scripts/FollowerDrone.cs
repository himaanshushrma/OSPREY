using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(ObstacleAvoidance))]
public class FollowerDrone : MonoBehaviour
{
    [Header("References")]
    public Transform leader;
    public FormationManager formation;
    public int droneID;

    [Header("Movement")]
    public float moveForce = 8f;
    public float hoverKp = 30f;
    public float hoverKd = 10f;
    public float rotationSpeed = 4f;

    Rigidbody rb;
    ObstacleAvoidance sensor;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        sensor = GetComponent<ObstacleAvoidance>();

        rb.mass = 1.8f;
        rb.linearDamping = 2f;
        rb.angularDamping = 4f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void FixedUpdate()
    {
        if (leader == null || formation == null)
            return;

        Vector3 target = leader.TransformPoint(formation.GetOffset(droneID));

        Hover(target.y);
        Move(target);
    }

    void Hover(float targetHeight)
    {
        float error = targetHeight - transform.position.y;

        float lift = error * hoverKp -
                     rb.linearVelocity.y * hoverKd;

        rb.AddForce(Vector3.up * lift, ForceMode.Acceleration);
    }

    void Move(Vector3 target)
    {
        // Formation target
        Vector3 desired = target - transform.position;
        desired.y = 0;

        if (desired.magnitude > 0.1f)
            desired.Normalize();

        // Obstacle avoidance
        Vector3 avoid = Vector3.zero;

        if (sensor != null)
            avoid = sensor.GetAvoidanceForce();

        // Blend both behaviours
        Vector3 direction = desired + avoid * 1.5f;
        direction.y = 0;

        if (direction.sqrMagnitude > 0.01f)
            direction.Normalize();

        rb.AddForce(direction * moveForce, ForceMode.Acceleration);

        // Rotate towards movement
        if (direction != Vector3.zero)
        {
            Quaternion rot = Quaternion.LookRotation(direction);

            rb.MoveRotation(
                Quaternion.Slerp(
                    rb.rotation,
                    rot,
                    rotationSpeed * Time.fixedDeltaTime));
        }
    }
}