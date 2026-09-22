using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DroneController : MonoBehaviour
{
    public enum Mode
    {
        Manual,
        Auto
    }

    [Header("Flight Mode")]
    public Mode mode = Mode.Manual;

    [Header("Hover")]
    public float targetHeight = 2f;
    public float hoverSpeed = 6f;

    [Header("Manual Flight")]
    public float moveSpeed = 8f;
    public float yawSpeed = 80f;
    public float acceleration = 8f;

    [Header("Auto Flight")]
    public Transform currentWaypoint;
    public float waypointSpeed = 6f;
    public float reachDistance = 1.5f;

    [Range(1f,10f)]
    public float steeringGain = 4f;

    public float maxTurnRate = 90f;

    [Header("Tilt")]
    public float maxPitch = 20f;
    public float maxRoll = 18f;
    public float tiltSmooth = 8f;

    Rigidbody rb;

    float forwardInput;
    float rightInput;
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
        if (mode != Mode.Manual)
            return;

        forwardInput = 0;
        rightInput = 0;
        yawInput = 0;

        if (Input.GetKey(KeyCode.W)) forwardInput = 1;
        if (Input.GetKey(KeyCode.S)) forwardInput = -1;

        if (Input.GetKey(KeyCode.D)) rightInput = 1;
        if (Input.GetKey(KeyCode.A)) rightInput = -1;

        if (Input.GetKey(KeyCode.E)) yawInput = 1;
        if (Input.GetKey(KeyCode.Q)) yawInput = -1;

        if (Input.GetKey(KeyCode.Space))
            targetHeight += 3f * Time.deltaTime;

        if (Input.GetKey(KeyCode.LeftShift))
            targetHeight -= 3f * Time.deltaTime;
    }

    void FixedUpdate()
    {
        if (mode == Mode.Manual)
            FlyManual();
        else
            FlyAuto();
    }

    void FlyManual()
    {
        Vector3 desiredVelocity =
            transform.forward * forwardInput * moveSpeed +
            transform.right * rightInput * moveSpeed;

        desiredVelocity.y =
            (targetHeight - transform.position.y) * hoverSpeed;

        rb.linearVelocity = Vector3.Lerp(
            rb.linearVelocity,
            desiredVelocity,
            acceleration * Time.fixedDeltaTime);

        float newYaw =
            transform.eulerAngles.y +
            yawInput * yawSpeed * Time.fixedDeltaTime;

        Quaternion targetRotation =
            Quaternion.Euler(
                -forwardInput * maxPitch,
                newYaw,
                -rightInput * maxRoll);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                targetRotation,
                tiltSmooth * Time.fixedDeltaTime));
    }

    void FlyAuto()
    {
        if (currentWaypoint == null)
        {
            rb.linearVelocity = Vector3.zero;
            return;
        }

        Vector3 direction =
            currentWaypoint.position - transform.position;

        Vector3 horizontal =
            new Vector3(direction.x, 0, direction.z);

        // Forward movement
        Vector3 desiredVelocity =
            horizontal.normalized * waypointSpeed;

        // Independent altitude control
        desiredVelocity.y =
            (targetHeight - transform.position.y) * hoverSpeed;

        rb.linearVelocity = Vector3.Lerp(
            rb.linearVelocity,
            desiredVelocity,
            acceleration * Time.fixedDeltaTime);

        // Smooth proportional steering
        if (horizontal.sqrMagnitude > 0.01f)
        {
            float targetYaw =
                Quaternion.LookRotation(horizontal).eulerAngles.y;

            float currentYaw =
                transform.eulerAngles.y;

            float headingError =
                Mathf.DeltaAngle(currentYaw, targetYaw);

            float yawRate =
                Mathf.Clamp(
                    headingError * steeringGain,
                    -maxTurnRate,
                    maxTurnRate);

            Quaternion newRotation =
                Quaternion.Euler(
                    0,
                    currentYaw + yawRate * Time.fixedDeltaTime,
                    0);

            rb.MoveRotation(newRotation);
        }
    }
}