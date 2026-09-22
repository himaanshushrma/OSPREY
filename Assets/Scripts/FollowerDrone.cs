using UnityEngine;

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

    Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        rb.useGravity = false;
        rb.linearDamping = 1.5f;
        rb.angularDamping = 4f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void FixedUpdate()
    {
        if (leader == null || formation == null)
            return;

        // Formation offset in LEADER LOCAL SPACE
        Vector3 localOffset = formation.GetOffset(droneID);

        // Convert local offset into world position
        Vector3 target =
            leader.position +
            leader.right * localOffset.x +
            leader.up * localOffset.y +
            leader.forward * localOffset.z;

        // Position error
        Vector3 error = target - transform.position;

        // Horizontal movement
        Vector3 velocity = error * followGain;

        // Altitude correction
        velocity.y = error.y * hoverGain;

        // Clamp speed
        velocity = Vector3.ClampMagnitude(velocity, maxSpeed);

        rb.linearVelocity = Vector3.Lerp(
            rb.linearVelocity,
            velocity,
            10f * Time.fixedDeltaTime);

        // Match leader heading
        Quaternion targetRot =
            Quaternion.Euler(
                0,
                leader.eulerAngles.y,
                0);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                targetRot,
                rotationSpeed * Time.fixedDeltaTime));
    }
}