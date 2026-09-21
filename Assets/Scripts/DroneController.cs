using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DroneController : MonoBehaviour
{
    public enum FlightMode
    {
        Manual,
        Leader
    }

    [Header("Mode")]
    public FlightMode mode = FlightMode.Manual;
    public WaypointManager waypointManager;

    [Header("Hover")]
    public float targetHeight = 2f;
    public float hoverForce = 30f;
    public float damping = 8f;

    [Header("Movement")]
    public float moveForce = 8f;
    public float yawSpeed = 80f;

    [Header("Tilt")]
    public float maxPitch = 18f;
    public float maxRoll = 15f;
    public float tiltSpeed = 5f;

    [Header("Mission")]
    public float arriveDistance = 0.5f;

    Rigidbody rb;

    float pitch;
    float roll;
    int waypointIndex = 0;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        rb.useGravity = true;
        rb.linearDamping = 2f;
        rb.angularDamping = 4f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.M))
        {
            mode = FlightMode.Manual;
            Debug.Log("MANUAL MODE");
        }

        if (Input.GetKeyDown(KeyCode.L))
        {
            mode = FlightMode.Leader;
            Debug.Log("LEADER MODE");
        }
    }

    void FixedUpdate()
    {
        Hover();

        if (mode == FlightMode.Manual)
            ManualControl();
        else
            LeaderMission();
    }

    void Hover()
    {
        float error = targetHeight - transform.position.y;
        float lift = error * hoverForce - rb.linearVelocity.y * damping;

        rb.AddForce(Vector3.up * lift, ForceMode.Acceleration);
    }

    void ManualControl()
    {
        Vector3 move = Vector3.zero;

        // Forward / Back
        if (Input.GetKey(KeyCode.W))
        {
            move += transform.forward;
            pitch = -maxPitch;
        }
        else if (Input.GetKey(KeyCode.S))
        {
            move -= transform.forward;
            pitch = maxPitch;
        }
        else
            pitch = 0;

        // Left / Right
        if (Input.GetKey(KeyCode.A))
        {
            move -= transform.right;
            roll = maxRoll;
        }
        else if (Input.GetKey(KeyCode.D))
        {
            move += transform.right;
            roll = -maxRoll;
        }
        else
            roll = 0;

        move.Normalize();
        rb.AddForce(move * moveForce, ForceMode.Acceleration);

        // Altitude
        if (Input.GetKey(KeyCode.Space))
            targetHeight += 0.04f;

        if (Input.GetKey(KeyCode.LeftShift))
            targetHeight -= 0.04f;

        targetHeight = Mathf.Clamp(targetHeight, 1f, 20f);

        // Yaw
        float yaw = 0;

        if (Input.GetKey(KeyCode.Q))
            yaw = -yawSpeed;

        if (Input.GetKey(KeyCode.E))
            yaw = yawSpeed;

        Quaternion targetRot = Quaternion.Euler(
            pitch,
            transform.eulerAngles.y + yaw * Time.fixedDeltaTime,
            roll);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                targetRot,
                tiltSpeed * Time.fixedDeltaTime));
    }

    void LeaderMission()
    {
        if (waypointManager == null || waypointManager.Count == 0)
            return;

        Transform wp = waypointManager.GetWaypoint(waypointIndex);

        Vector3 target = new Vector3(
            wp.position.x,
            transform.position.y,
            wp.position.z);

        Vector3 dir = target - transform.position;

        if (dir.magnitude < arriveDistance)
        {
            waypointIndex = (waypointIndex + 1) % waypointManager.Count;
            return;
        }

        dir.Normalize();

        rb.AddForce(dir * moveForce, ForceMode.Acceleration);

        Quaternion targetRot = Quaternion.LookRotation(dir);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                targetRot,
                2f * Time.fixedDeltaTime));
    }
}