using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DroneController : MonoBehaviour
{
    public enum Mode { Manual, Mission }
    public Mode mode = Mode.Manual;

    [Header("Mission")]
    public WaypointManager waypointManager;

    [Header("Hover")]
    public float targetHeight = 2f;
    public float hoverKp = 30f;
    public float hoverKd = 10f;

    [Header("Movement")]
    public float moveForce = 8f;
    public float yawTorque = 3f;

    [Header("Tilt")]
    public float maxPitch = 18f;
    public float maxRoll = 15f;
    public float rotationSpeed = 3f;

    Rigidbody rb;

    int wp = 0;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        rb.mass = 1.8f;
        rb.linearDamping = 2f;
        rb.angularDamping = 4f;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.M))
            mode = Mode.Manual;

        if (Input.GetKeyDown(KeyCode.L))
            mode = Mode.Mission;
    }

    void FixedUpdate()
    {
        Hover();

        if (mode == Mode.Manual)
            ManualFlight();
        else
            MissionFlight();
    }

    void Hover()
    {
        float error = targetHeight - transform.position.y;

        float lift =
            error * hoverKp
            - rb.linearVelocity.y * hoverKd;

        rb.AddForce(Vector3.up * lift, ForceMode.Acceleration);
    }

    void ManualFlight()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 move =
            transform.forward * v +
            transform.right * h;

        rb.AddForce(move * moveForce, ForceMode.Acceleration);

        if (Input.GetKey(KeyCode.Q))
            rb.AddTorque(Vector3.up * -yawTorque, ForceMode.Acceleration);

        if (Input.GetKey(KeyCode.E))
            rb.AddTorque(Vector3.up * yawTorque, ForceMode.Acceleration);

        if (Input.GetKey(KeyCode.Space))
            targetHeight += 0.03f;

        if (Input.GetKey(KeyCode.LeftShift))
            targetHeight -= 0.03f;

        targetHeight = Mathf.Clamp(targetHeight, 0.5f, 10f);

        Tilt(move.normalized);
    }

    void MissionFlight()
    {
        if (waypointManager == null || waypointManager.Count == 0)
            return;

        Transform target = waypointManager.GetWaypoint(wp);

        Vector3 goal = new Vector3(
            target.position.x,
            transform.position.y,
            target.position.z);

        Vector3 dir = goal - transform.position;

        if (dir.magnitude < 0.4f)
        {
            wp = (wp + 1) % waypointManager.Count;
            return;
        }

        dir.Normalize();

        rb.AddForce(dir * moveForce, ForceMode.Acceleration);

        Quaternion face =
            Quaternion.LookRotation(new Vector3(dir.x,0,dir.z));

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                face,
                rotationSpeed * Time.fixedDeltaTime));

        Tilt(dir);
    }

    void Tilt(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.01f) return;

        float pitch =
            Vector3.Dot(dir, transform.forward) * -maxPitch;

        float roll =
            Vector3.Dot(dir, transform.right) * maxRoll;

        Quaternion bank =
            Quaternion.Euler(
                pitch,
                rb.rotation.eulerAngles.y,
                -roll);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                bank,
                rotationSpeed * Time.fixedDeltaTime));
    }
}