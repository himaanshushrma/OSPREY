using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DroneController : MonoBehaviour
{
    public enum Mode { Manual, LeaderMission }

    [Header("Mode")]
    public Mode mode = Mode.Manual;

    [Header("Hover")]
    public float targetHeight = 2f;
    public float hoverSpeed = 6f;

    [Header("Movement")]
    public float moveSpeed = 8f;
    public float yawSpeed = 90f;
    public float acceleration = 8f;

    [Header("Tilt")]
    public float maxPitch = 18f;
    public float maxRoll = 15f;
    public float tiltSmooth = 8f;

    Rigidbody rb;

    float forward;
    float right;
    float yawInput;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        rb.useGravity = false;
        rb.linearDamping = 0;
        rb.angularDamping = 4;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void Update()
    {
        if (mode != Mode.Manual) return;

        forward = 0;
        right = 0;
        yawInput = 0;

        if (Input.GetKey(KeyCode.W)) forward = 1;
        if (Input.GetKey(KeyCode.S)) forward = -1;

        if (Input.GetKey(KeyCode.D)) right = 1;
        if (Input.GetKey(KeyCode.A)) right = -1;

        if (Input.GetKey(KeyCode.E)) yawInput = 1;
        if (Input.GetKey(KeyCode.Q)) yawInput = -1;

        if (Input.GetKey(KeyCode.Space))
            targetHeight += 3f * Time.deltaTime;

        if (Input.GetKey(KeyCode.LeftShift))
            targetHeight -= 3f * Time.deltaTime;

        targetHeight = Mathf.Clamp(targetHeight, 1f, 20f);
    }

    void FixedUpdate()
    {
        Fly();
    }

    void Fly()
    {
        // Horizontal velocity
        Vector3 desired =
            transform.forward * forward * moveSpeed +
            transform.right * right * moveSpeed;

        // Vertical velocity
        desired.y =
            (targetHeight - transform.position.y) * hoverSpeed;

        rb.linearVelocity = Vector3.Lerp(
            rb.linearVelocity,
            desired,
            acceleration * Time.fixedDeltaTime);

        // Rotation
        transform.Rotate(
            0,
            yawInput * yawSpeed * Time.fixedDeltaTime,
            0);

        float pitch = -forward * maxPitch;
        float roll = -right * maxRoll;

        Quaternion body =
            Quaternion.Euler(
                pitch,
                transform.eulerAngles.y,
                roll);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                body,
                tiltSmooth * Time.fixedDeltaTime));
    }
}