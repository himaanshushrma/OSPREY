using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(ObstacleAvoidance))]
public class FollowerDrone : MonoBehaviour
{
    [Header("Leader")]
    public Transform leader;
    public FormationManager formation;
    public int droneID;

    [Header("Flight")]
    public float moveForce = 8f;
    public float hoverKp = 30f;
    public float hoverKd = 10f;
    public float rotationSpeed = 4f;

    Rigidbody rb;
    ObstacleAvoidance avoid;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        avoid = GetComponent<ObstacleAvoidance>();

        rb.mass = 1.8f;
        rb.linearDamping = 2f;
        rb.angularDamping = 4f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void FixedUpdate()
    {
        if (leader == null || formation == null)
            return;

        Vector3 target = leader.TransformPoint(
            formation.GetOffset(droneID));

        Hover(target.y);
        Move(target);
    }

    void Hover(float targetHeight)
    {
        float error = targetHeight - transform.position.y;

        float lift =
            error * hoverKp -
            rb.linearVelocity.y * hoverKd;

        rb.AddForce(
            Vector3.up * lift,
            ForceMode.Acceleration);
    }

    void Move(Vector3 target)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0;

        if (dir.magnitude < 0.3f)
            return;

        // Formation direction
        dir.Normalize();

        // Obstacle avoidance
        Vector3 avoidForce = Vector3.zero;
        if (avoid != null)
            avoidForce = avoid.GetAvoidanceForce() * 0.20f;

        // Final steering
        Vector3 steering = (dir + avoidForce).normalized;

        rb.AddForce(
            steering * moveForce,
            ForceMode.Acceleration);

        Quaternion rot = Quaternion.LookRotation(steering);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                rot,
                rotationSpeed * Time.fixedDeltaTime));
    }
}