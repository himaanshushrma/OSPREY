using UnityEngine;

/// <summary>
/// DroneController: flight control for the leader drone.
///
/// Manual flight keys
///   W/S forward/back   A/D strafe   Q/E yaw
///   Space climb        Left Shift descend   (release = hover / altitude hold)
///   T take off   L land   G return to home   X sport mode   F1 show controls
/// Auto mode flies to currentWaypoint (set by MissionManager) with PD heading control.
///
/// Every mode keeps a minimum clearance above the ground or rooftops, and uses
/// CollisionAvoidance (added automatically) to steer, brake and climb around obstacles.
/// On followers (objects with FollowerDrone) this component stays passive: FollowerDrone
/// flies them, so they no longer react to the keyboard.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DroneController : MonoBehaviour
{
    public enum Mode { Manual, Auto }
    public enum FlightState { Landed, TakingOff, Flying, Landing, ReturningHome }

    public Mode mode = Mode.Manual;

    [Header("Mission")]
    public Transform currentWaypoint;
    [Tooltip("Legacy, no longer used: CollisionAvoidance replaces it.")]
    public ObstacleAvoidance avoidance;

    [Header("Altitude")]
    public float targetHeight = 5f;
    public float hoverGain = 2f;
    public float climbRate = 4f;
    public float minHeight = 1.5f;
    public float maxHeight = 80f;
    public float takeoffHeight = 5f;
    public float minGroundClearance = 2f;
    public float returnHomeHeight = 20f;

    [Header("Manual")]
    public float moveSpeed = 8f;
    public float sportSpeed = 15f;
    public float yawSpeed = 80f;
    public float acceleration = 14f;

    [Header("Auto")]
    public float cruiseSpeed = 7f;
    public float minSpeed = 3f;

    [Header("PID")]
    public float kp = 3.5f;
    public float kd = 1f;
    public float maxYawRate = 120f;

    [Header("Assists")]
    public bool obstacleAvoidance = true;
    public bool objectDetection = true;
    public bool startLanded = false;

    public FlightState State { get; private set; } = FlightState.Flying;
    public bool SportMode { get; private set; }
    public bool IsFollower { get; private set; }
    public CollisionAvoidance Avoider { get; private set; }
    public Vector3 HomePosition { get; private set; }
    public float GroundY { get; private set; }

    Rigidbody rb;
    float prevError;
    bool showHelp;
    Transform homeMarker;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.linearDamping = 1.5f;
        rb.angularDamping = 5f;

        IsFollower = GetComponent<FollowerDrone>() != null;

        if (obstacleAvoidance)
        {
            Avoider = GetComponent<CollisionAvoidance>();
            if (Avoider == null) Avoider = gameObject.AddComponent<CollisionAvoidance>();
        }

        if (IsFollower) return;

        if (objectDetection && GetComponent<ObjectDetector>() == null)
            gameObject.AddComponent<ObjectDetector>();

        HomePosition = transform.position;
        homeMarker = new GameObject("HomePoint").transform;
        homeMarker.position = HomePosition;

        if (startLanded) State = FlightState.Landed;
    }

    // ===================================================
    // KEY COMMANDS
    // ===================================================

    void Update()
    {
        if (IsFollower) return;

        if (Input.GetKeyDown(KeyCode.T)) TakeOff();
        if (Input.GetKeyDown(KeyCode.L)) Land();
        if (Input.GetKeyDown(KeyCode.G)) ReturnHome();
        if (Input.GetKeyDown(KeyCode.X)) SportMode = !SportMode;
        if (Input.GetKeyDown(KeyCode.F1)) showHelp = !showHelp;
    }

    public void TakeOff()
    {
        if (State != FlightState.Landed && State != FlightState.Landing) return;
        targetHeight = Mathf.Max(GroundY + takeoffHeight, minHeight);
        State = FlightState.TakingOff;
    }

    public void Land()
    {
        StopMission();
        mode = Mode.Manual;
        currentWaypoint = null;
        if (State != FlightState.Landed) State = FlightState.Landing;
    }

    public void ReturnHome()
    {
        if (State == FlightState.Landed) return;
        StopMission();
        mode = Mode.Manual;
        currentWaypoint = null;
        targetHeight = Mathf.Max(targetHeight, returnHomeHeight);
        State = FlightState.ReturningHome;
    }

    void StopMission()
    {
        var mission = FindFirstObjectByType<MissionManager>();
        if (mission != null && mission.leader == this &&
            (mission.state == MissionManager.MissionState.Takeoff || mission.state == MissionManager.MissionState.Patrol))
            mission.CancelMission();
    }

    // ===================================================
    // FLIGHT LOOP
    // ===================================================

    void FixedUpdate()
    {
        if (IsFollower) return;

        UpdateGround();

        // A newly started autonomous mission overrides landing / return-home
        if (mode == Mode.Auto && State != FlightState.Flying && State != FlightState.TakingOff)
            State = FlightState.TakingOff;

        if (State == FlightState.Landed)
        {
            rb.linearVelocity = Vector3.zero;
            return;
        }

        Vector3 desired =
            State == FlightState.Landing ? Vector3.zero :
            State == FlightState.ReturningHome ? FlyTo(homeMarker, true) :
            mode == Mode.Manual ? Manual() :
            FlyTo(currentWaypoint, false);

        float vy = Altitude();

        Vector3 v = rb.linearVelocity;
        Vector3 h = Vector3.MoveTowards(new Vector3(v.x, 0, v.z), desired, acceleration * Time.fixedDeltaTime);
        rb.linearVelocity = new Vector3(h.x, vy, h.z);

        Tilt();
    }

    void UpdateGround()
    {
        float down = Avoider != null ? Avoider.GroundDistance : Mathf.Infinity;
        if (float.IsInfinity(down) &&
            Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 300f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            down = hit.distance;
        GroundY = float.IsInfinity(down) ? 0f : transform.position.y - down;
    }

    float Altitude()
    {
        float y = transform.position.y;

        if (State == FlightState.Landing)
        {
            if (y - GroundY < 0.45f)
            {
                State = FlightState.Landed;
                return 0f;
            }
            return Mathf.Max(-climbRate, (GroundY + 0.3f - y) * hoverGain);
        }

        if (mode == Mode.Manual && (State == FlightState.Flying || State == FlightState.TakingOff))
        {
            float input = 0;
            if (Input.GetKey(KeyCode.Space)) input += 1;
            if (Input.GetKey(KeyCode.LeftShift)) input -= 1;
            targetHeight += input * climbRate * Time.fixedDeltaTime;
        }

        targetHeight = Mathf.Clamp(targetHeight, minHeight, maxHeight);

        float effective = Mathf.Max(targetHeight, GroundY + minGroundClearance);
        if (Avoider != null && Avoider.ClimbTargetY > effective)
            effective = Avoider.ClimbTargetY;

        if (State == FlightState.TakingOff && Mathf.Abs(y - targetHeight) < 0.3f)
            State = FlightState.Flying;

        return Mathf.Clamp((effective - y) * hoverGain, -climbRate * 1.5f, climbRate * 1.5f);
    }

    // ===================================================
    // MANUAL
    // ===================================================

    Vector3 Manual()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 fwd = Flat(transform.forward), right = Flat(transform.right);
        Vector3 dir = Vector3.ClampMagnitude(fwd * v + right * h, 1f);
        Vector3 desired = dir * (SportMode ? sportSpeed : moveSpeed);

        float yaw = 0;
        if (Input.GetKey(KeyCode.Q)) yaw = -1;
        if (Input.GetKey(KeyCode.E)) yaw = 1;
        transform.Rotate(Vector3.up, yaw * yawSpeed * Time.fixedDeltaTime, Space.World);

        if (Avoider != null)
        {
            Avoider.SetProbeDirection(desired.sqrMagnitude > 0.01f ? desired : fwd);
            desired = Avoider.Assist(desired);
        }
        return desired;
    }

    // ===================================================
    // AUTO (waypoint / home) with PD heading + avoidance
    // ===================================================

    Vector3 FlyTo(Transform target, bool goingHome)
    {
        if (target == null) return Vector3.zero;

        Vector3 to = target.position - transform.position;
        to.y = 0;
        float dist = to.magnitude;

        if (goingHome && dist < 1.5f) { State = FlightState.Landing; return Vector3.zero; }
        if (dist < 0.3f) return Vector3.zero;

        Vector3 travel = to / dist;
        float speedFactor = 1f;
        if (Avoider != null)
        {
            Avoider.SetProbeDirection(travel);
            travel = Avoider.Steer(travel);
            speedFactor = Avoider.SpeedFactor;
        }

        float desiredYaw = Mathf.Atan2(travel.x, travel.z) * Mathf.Rad2Deg;
        float error = Mathf.DeltaAngle(transform.eulerAngles.y, desiredYaw);
        float yawRate = Mathf.Clamp(kp * error + kd * (error - prevError) / Time.fixedDeltaTime, -maxYawRate, maxYawRate);
        prevError = error;
        transform.Rotate(Vector3.up, yawRate * Time.fixedDeltaTime, Space.World);

        float speed = Mathf.Lerp(cruiseSpeed, minSpeed, Mathf.Abs(error) / 90f) * speedFactor;
        if (goingHome) speed = Mathf.Min(speed, Mathf.Max(1.5f, dist));   // slow on final approach
        return travel * speed;
    }

    // ===================================================
    // VISUAL TILT
    // ===================================================

    void Tilt()
    {
        Vector3 local = transform.InverseTransformDirection(rb.linearVelocity);
        Quaternion q = Quaternion.Euler(-local.z * 2.5f, transform.eulerAngles.y, -local.x * 2.5f);
        transform.rotation = Quaternion.Slerp(transform.rotation, q, Time.fixedDeltaTime * 8f);
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0;
        return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
    }

    // ===================================================
    // STATUS OVERLAY
    // ===================================================

    static GUIStyle style;

    void OnGUI()
    {
        if (IsFollower) return;
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };

        float agl = transform.position.y - GroundY;
        string avoid = "off";
        if (Avoider != null)
        {
            avoid = Avoider.CurrentStatus.ToString();
            if (!float.IsInfinity(Avoider.NearestDistance)) avoid += $"  (nearest {Avoider.NearestDistance:0.0} m)";
            if (Avoider.CurrentStatus != CollisionAvoidance.Status.Clear) avoid = $"<color=#FFB347>{avoid}</color>";
        }

        GUI.Box(new Rect(10, Screen.height - 86, 460, 76), GUIContent.none);
        GUI.Label(new Rect(18, Screen.height - 82, 450, 72),
            $"<b>{State}</b>  ·  {mode}{(SportMode ? "  ·  <color=#FF6B6B>SPORT</color>" : "")}\n" +
            $"ALT {transform.position.y:0.0} m   AGL {agl:0.0} m   SPD {rb.linearVelocity.magnitude:0.0} m/s\n" +
            $"Avoidance: {avoid}       <color=#AAAAAA>[F1] controls</color>", style);

        if (!showHelp) return;
        GUI.Box(new Rect(10, Screen.height - 300, 460, 206), GUIContent.none);
        GUI.Label(new Rect(18, Screen.height - 296, 450, 200),
            "<b>Flight</b>   W/S forward/back · A/D strafe · Q/E yaw\n" +
            "              Space climb · Left Shift descend · release to hover\n" +
            "              T take off · L land · G return home · X sport mode\n" +
            "<b>Mission</b>  P start · Esc cancel · 1-0 formations\n" +
            "<b>View</b>      C chase cam · V tactical cam · O detection overlay\n\n" +
            "Avoidance steers around obstacles, brakes when close,\n" +
            "and climbs over trees, huts and walls.", style);
    }
}
