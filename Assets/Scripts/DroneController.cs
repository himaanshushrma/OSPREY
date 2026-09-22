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

    [Header("Tilt")]
    public float maxPitch = 20f;
    public float maxRoll = 18f;
    public float tiltSmooth = 8f;

    Rigidbody rb;

    float forward;
    float right;
    float yaw;

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

        forward = 0;
        right = 0;
        yaw = 0;

        if (Input.GetKey(KeyCode.W)) forward = 1;
        if (Input.GetKey(KeyCode.S)) forward = -1;

        if (Input.GetKey(KeyCode.D)) right = 1;
        if (Input.GetKey(KeyCode.A)) right = -1;

        if (Input.GetKey(KeyCode.E)) yaw = 1;
        if (Input.GetKey(KeyCode.Q)) yaw = -1;

        if (Input.GetKey(KeyCode.Space))
            targetHeight += 3 * Time.deltaTime;

        if (Input.GetKey(KeyCode.LeftShift))
            targetHeight -= 3 * Time.deltaTime;
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
        Vector3 desired =
            transform.forward * forward * moveSpeed +
            transform.right * right * moveSpeed;

        desired.y =
            (targetHeight - transform.position.y) * hoverSpeed;

        rb.linearVelocity = Vector3.Lerp(
            rb.linearVelocity,
            desired,
            acceleration * Time.fixedDeltaTime);

        transform.Rotate(
            0,
            yaw * yawSpeed * Time.fixedDeltaTime,
            0);

        Quaternion rot =
            Quaternion.Euler(
                -forward * maxPitch,
                transform.eulerAngles.y,
                -right * maxRoll);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                rot,
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

        Vector3 desiredVelocity =
            horizontal.normalized * waypointSpeed;

        desiredVelocity.y =
            (targetHeight - transform.position.y) * hoverSpeed;

        rb.linearVelocity = Vector3.Lerp(
            rb.linearVelocity,
            desiredVelocity,
            acceleration * Time.fixedDeltaTime);

        if (horizontal.sqrMagnitude > 0.01f)
        {
            Quaternion rot =
                Quaternion.LookRotation(horizontal);

            rb.MoveRotation(
                Quaternion.Slerp(
                    rb.rotation,
                    rot,
                    tiltSmooth * Time.fixedDeltaTime));
        }
    }
}