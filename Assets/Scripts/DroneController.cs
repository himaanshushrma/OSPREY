using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DroneController : MonoBehaviour
{
    [Header("Flight")]
    public float liftForce = 35f;
    public float moveForce = 8f;
    public float yawTorque = 3f;

    [Header("Smart Hover")]
    public float hoverPower = 25f;
    public float damping = 8f;
    public LayerMask groundMask = -1;

    [Header("Tilt")]
    public float maxTilt = 18f;
    public float tiltSpeed = 5f;

    Rigidbody rb;

    float h, v, yaw;
    float throttle;

    float targetHeight;
    bool hoverMode = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        rb.mass = 1.8f;
        rb.linearDamping = 0.3f;
        rb.angularDamping = 3f;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        targetHeight = GetHeight();
    }

    void Update()
    {
        h = Input.GetAxis("Horizontal");
        v = Input.GetAxis("Vertical");

        yaw = 0;
        if (Input.GetKey(KeyCode.Q)) yaw = -1;
        if (Input.GetKey(KeyCode.E)) yaw = 1;

        throttle = 0;

        if (Input.GetKey(KeyCode.Space))
        {
            throttle = 1;
            hoverMode = false;
        }
        else if (Input.GetKey(KeyCode.LeftShift))
        {
            throttle = -0.8f;
            hoverMode = false;
        }
        else
        {
            // Lock the altitude only once when keys are released
            if (!hoverMode)
            {
                targetHeight = GetHeight();
                hoverMode = true;
            }
        }
    }

    void FixedUpdate()
    {
        // Manual climb / descend
        if (!hoverMode)
        {
            rb.AddForce(Vector3.up * throttle * liftForce, ForceMode.Force);
        }
        else
        {
            // Hold CURRENT altitude
            float current = GetHeight();
            float error = targetHeight - current;

            float lift = (error * hoverPower) - (rb.linearVelocity.y * damping);

            rb.AddForce(Vector3.up * lift, ForceMode.Acceleration);
        }

        // Movement
        Vector3 move =
            transform.forward * v +
            transform.right * h;

        rb.AddForce(move * moveForce, ForceMode.Force);

        // Rotate
        rb.AddTorque(Vector3.up * yaw * yawTorque, ForceMode.VelocityChange);

        // Visual tilt
        Quaternion target = Quaternion.Euler(
            v * maxTilt,
            transform.eulerAngles.y,
            -h * maxTilt
        );

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                target,
                tiltSpeed * Time.fixedDeltaTime
            )
        );
    }

    float GetHeight()
    {
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 100f, groundMask))
            return hit.distance;

        return transform.position.y;
    }
}