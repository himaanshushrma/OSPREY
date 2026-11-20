using UnityEngine;

/// <summary>
/// DroneBattery (v3.0): a simple energy model for one drone.
///
/// Power (W) = hover + k * speed^2 + climb * upward speed. Energy drains from
/// `capacityWh`; Percent is what is left. A drone resting on the ground near home charges.
/// Capacity is scaled down so a full charge lasts about 6-8 minutes of flying, which keeps
/// battery behaviour visible in short simulator missions.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DroneBattery : MonoBehaviour
{
    [Header("Model")]
    public float capacityWh = 25f;
    public float hoverWatts = 180f;
    public float speedWattsPerMs2 = 2f;   // extra W per (m/s)^2
    public float climbWattsPerMs = 30f;   // extra W per m/s climbing
    public float chargePercentPerSecond = 1.5f;

    [Header("State")]
    [Range(0, 100)] public float startPercent = 100f;

    public float Percent { get; private set; }
    public bool Charging { get; private set; }
    public float Watts { get; private set; }
    public bool Depleted => Percent <= 0f;

    Rigidbody rb;
    FollowerDrone follower;
    DroneController controller;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        follower = GetComponent<FollowerDrone>();
        controller = GetComponent<DroneController>();
        Percent = startPercent;
    }

    void FixedUpdate()
    {
        if (follower != null && !follower.Alive) { Watts = 0; Charging = false; return; }

        var bm = BatteryManager.Instance;
        bool landedLeader = controller != null && !controller.IsFollower &&
                            controller.State == DroneController.FlightState.Landed;
        // followers sit on the pad around the leader: they charge while the leader is landed
        bool parkedFollower = follower != null && bm != null && bm.LeaderLanded && rb.linearVelocity.magnitude < 1.5f;
        bool nearHome = bm == null || (transform.position - bm.HomePosition).sqrMagnitude < 90f * 90f;

        if ((landedLeader || parkedFollower) && nearHome)
        {
            Charging = Percent < 100f;
            Watts = 0;
            Percent = Mathf.Min(100f, Percent + chargePercentPerSecond * Time.fixedDeltaTime);
            return;
        }

        Charging = false;
        Vector3 v = rb.linearVelocity;
        float h = new Vector2(v.x, v.z).magnitude;
        Watts = hoverWatts + speedWattsPerMs2 * h * h + climbWattsPerMs * Mathf.Max(0f, v.y);
        Percent = Mathf.Max(0f, Percent - PercentPerSecond(Watts) * Time.fixedDeltaTime);
    }

    /// <summary>Set the charge directly (testing / demos: the API action set_battery).</summary>
    public void SetPercent(float p) => Percent = Mathf.Clamp(p, 0f, 100f);

    /// <summary>Battery percent used per second at a given power.</summary>
    public float PercentPerSecond(float watts) => watts / (capacityWh * 3600f) * 100f;

    /// <summary>Percent needed to fly `metres` at `speed` m/s (level flight).</summary>
    public float PercentFor(float metres, float speed)
    {
        float w = hoverWatts + speedWattsPerMs2 * speed * speed;
        return PercentPerSecond(w) * (metres / Mathf.Max(0.5f, speed));
    }
}
