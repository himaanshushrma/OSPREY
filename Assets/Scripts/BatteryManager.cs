using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BatteryManager (v3.0), on the leader: battery-aware decisions for the whole swarm.
///
///  - SwarmPercent: the lowest battery among live drones (the swarm is only as good as
///    its weakest member).
///  - CheckMission(): before a mission or search, estimates the energy for the route plus
///    the flight home and refuses plans the swarm cannot finish with a reserve left.
///  - Smart return home: every second, works out what it takes to get home from here;
///    when the swarm gets down to that plus a margin, it cancels the mission and returns.
/// </summary>
public class BatteryManager : MonoBehaviour
{
    public static BatteryManager Instance { get; private set; }

    [Header("Policy")]
    [Tooltip("Battery that must be left on landing (percent).")]
    public float landingReserve = 10f;
    [Tooltip("Extra margin before smart return home triggers (percent).")]
    public float returnMargin = 5f;
    [Tooltip("Real routes are longer than a straight line home.")]
    public float detourFactor = 1.3f;
    public float cruiseSpeed = 7f;
    public bool smartReturnHome = true;

    public Vector3 HomePosition { get; private set; }
    public float SwarmPercent { get; private set; } = 100f;
    public float NeededToGetHome { get; private set; }
    public string LastEvent { get; private set; } = "";
    public bool LeaderLanded => leader != null && leader.State == DroneController.FlightState.Landed;

    DroneController leader;
    DroneBattery leaderBattery;
    SwarmManager swarm;
    float nextCheck;
    bool returning;

    void Awake()
    {
        Instance = this;
        leader = GetComponent<DroneController>();
        HomePosition = transform.position;
    }

    void Start()
    {
        swarm = FindFirstObjectByType<SwarmManager>();
        leaderBattery = GetComponent<DroneBattery>();
        if (swarm != null)
            foreach (var f in swarm.followers)
                if (f != null && f.GetComponent<DroneBattery>() == null) f.gameObject.AddComponent<DroneBattery>();
    }

    public IEnumerable<DroneBattery> LiveBatteries()
    {
        if (leaderBattery != null) yield return leaderBattery;
        if (swarm == null) yield break;
        foreach (var f in swarm.followers)
        {
            if (f == null || !f.Alive) continue;
            var b = f.GetComponent<DroneBattery>();
            if (b != null) yield return b;
        }
    }

    void Update()
    {
        float min = 100f;
        foreach (var b in LiveBatteries()) min = Mathf.Min(min, b.Percent);
        SwarmPercent = min;

        if (Time.time < nextCheck || leader == null || leaderBattery == null) return;
        nextCheck = Time.time + 1f;

        float home = Vector3.Distance(Flat(leader.transform.position), Flat(HomePosition)) * detourFactor;
        NeededToGetHome = leaderBattery.PercentFor(home, cruiseSpeed) + landingReserve;

        bool flying = leader.State == DroneController.FlightState.Flying || leader.State == DroneController.FlightState.TakingOff;
        if (smartReturnHome && flying && !returning && SwarmPercent <= NeededToGetHome + returnMargin && home > 15f)
        {
            returning = true;
            LastEvent = $"Smart return home: swarm at {SwarmPercent:0}%, needs {NeededToGetHome:0}% to get home";
            Debug.LogWarning($"[Battery] {LastEvent}.");
            var search = SearchMission.Instance;
            if (search != null && search.Active) search.Abort("battery");
            leader.ReturnHome();
        }
        if (leader.State == DroneController.FlightState.Landed) returning = false;
    }

    /// <summary>
    /// Can the swarm fly `routeMetres` and then get home from `endPoint`?
    /// Returns false with an explanation if not.
    /// </summary>
    public bool CheckMission(float routeMetres, Vector3 endPoint, out float neededPercent, out string message)
    {
        float back = Vector3.Distance(Flat(endPoint), Flat(HomePosition)) * detourFactor;
        var probe = leaderBattery != null ? leaderBattery : GetComponent<DroneBattery>();
        neededPercent = (probe != null ? probe.PercentFor(routeMetres + back, cruiseSpeed) : 0f) + landingReserve;
        if (neededPercent <= SwarmPercent)
        {
            message = $"Battery OK: needs ~{neededPercent:0}% (incl. {landingReserve:0}% reserve), swarm has {SwarmPercent:0}%.";
            return true;
        }
        message = $"Not enough battery: the plan needs ~{neededPercent:0}% (route {routeMetres:0} m + {back:0} m home + " +
                  $"{landingReserve:0}% reserve) but the swarm has {SwarmPercent:0}%. Land on the pad to charge, or plan a smaller mission.";
        return false;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);
}
