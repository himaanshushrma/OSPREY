using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DroneController : MonoBehaviour
{
    public enum Mode
    {
        Manual,
        Auto
    }

    [Header("Mode")]
    public Mode mode = Mode.Manual;

    [Header("Altitude")]
    public float targetHeight = 5f;
    public float hoverGain = 5f;

    [Header("Manual")]
    public float moveSpeed = 8f;
    public float yawSpeed = 90f;

    [Header("Autonomous")]
    public Transform currentWaypoint;
    public float cruiseSpeed = 7f;
    public float minSpeed = 3f;
    public float reachDistance = 0.5f;

    [Header("PID Steering")]
    public float kp = 3.5f;
    public float kd = 1.0f;
    public float maxYawRate = 120f;

    [Header("Tilt")]
    public float maxPitch = 20f;
    public float maxRoll = 18f;
    public float tiltSmooth = 8f;

    Rigidbody rb;

    float previousError;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        rb.useGravity = false;
        rb.linearDamping = 1.5f;
        rb.angularDamping = 5f;
    }

    void FixedUpdate()
    {
        HoverPID();

        if (mode == Mode.Manual)
            ManualFlight();
        else
            PurePursuitFlight();

        ApplyTilt();
    }

    // -------------------------------------------------
    // ALTITUDE PID
    // -------------------------------------------------

    void HoverPID()
    {
        float error = targetHeight - transform.position.y;

        Vector3 vel = rb.linearVelocity;
        vel.y = error * hoverGain;

        rb.linearVelocity = vel;
    }

    // -------------------------------------------------
    // MANUAL
    // -------------------------------------------------

    void ManualFlight()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 dir =
            transform.forward * v +
            transform.right * h;

        Vector3 vel = rb.linearVelocity;

        vel.x = dir.x * moveSpeed;
        vel.z = dir.z * moveSpeed;

        rb.linearVelocity = vel;

        float yaw = 0;

        if (Input.GetKey(KeyCode.Q)) yaw = -1;
        if (Input.GetKey(KeyCode.E)) yaw = 1;

        transform.Rotate(
            Vector3.up,
            yaw * yawSpeed * Time.fixedDeltaTime);
    }

    // -------------------------------------------------
    // PURE PURSUIT
    // -------------------------------------------------

    void PurePursuitFlight()
    {
        if (currentWaypoint == null)
            return;

        Vector3 target =
            new Vector3(
                currentWaypoint.position.x,
                transform.position.y,
                currentWaypoint.position.z);

        Vector3 toTarget = target - transform.position;

        float distance = toTarget.magnitude;

        // IMPORTANT:
        // DO NOT STOP HERE.
        // MissionManager changes the waypoint.
        if (distance < reachDistance)
            return;

        float desiredYaw =
            Mathf.Atan2(
                toTarget.x,
                toTarget.z) * Mathf.Rad2Deg;

        float currentYaw = transform.eulerAngles.y;

        float error =
            Mathf.DeltaAngle(
                currentYaw,
                desiredYaw);

        float derivative =
            (error - previousError) /
            Time.fixedDeltaTime;

        float yawRate =
            kp * error +
            kd * derivative;

        yawRate =
            Mathf.Clamp(
                yawRate,
                -maxYawRate,
                maxYawRate);

        transform.Rotate(
            Vector3.up,
            yawRate * Time.fixedDeltaTime);

        previousError = error;

        float turnFactor =
            Mathf.Abs(error) / 90f;

        float speed =
            Mathf.Lerp(
                cruiseSpeed,
                minSpeed,
                turnFactor);

        Vector3 forward =
            transform.forward * speed;

        Vector3 vel = rb.linearVelocity;

        vel.x = forward.x;
        vel.z = forward.z;

        rb.linearVelocity = vel;
    }

    // -------------------------------------------------
    // VISUAL TILT
    // -------------------------------------------------

    void ApplyTilt()
    {
        Vector3 local =
            transform.InverseTransformDirection(
                rb.linearVelocity);

        float pitch =
            Mathf.Clamp(
                -local.z * 2.5f,
                -maxPitch,
                maxPitch);

        float roll =
            Mathf.Clamp(
                -local.x * 2.5f,
                -maxRoll,
                maxRoll);

        Quaternion target =
            Quaternion.Euler(
                pitch,
                transform.eulerAngles.y,
                roll);

        transform.rotation =
            Quaternion.Slerp(
                transform.rotation,
                target,
                Time.fixedDeltaTime * tiltSmooth);
    }
}