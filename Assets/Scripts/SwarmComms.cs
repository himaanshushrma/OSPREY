using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SwarmComms (v2.0): a simulated radio network between the leader and its followers.
///
///  - Downlink (10 Hz): the leader broadcasts its position, heading and velocity.
///    Followers fly their formation slot from the LAST STATE THEY RECEIVED (with dead
///    reckoning), not from the leader's true position. No link = hold position.
///  - Uplink heartbeats (10 Hz): followers report in. No heartbeat for `timeout` seconds =
///    that drone is marked lost, and the remaining drones are re-assigned formation slots
///    so the formation closes the gap (formation consensus).
///  - Range and packet loss apply to every message, including sensor map updates and
///    survivor reports, which are relayed to the leader's map and detector.
/// Press K to simulate a drone failure (it drops out of the sky).
/// </summary>
public class SwarmComms : MonoBehaviour
{
    public static SwarmComms Instance { get; private set; }

    [Header("Radio")]
    public float range = 160f;
    [Range(0, 0.5f)] public float packetLoss = 0.05f;
    public float rateHz = 10f;
    public float timeout = 1.0f;

    [Header("Test")]
    public KeyCode failKey = KeyCode.K;
    public bool drawLinks = true;

    public int Online { get; private set; }
    public int Total { get; private set; }
    public int MessagesSent { get; private set; }
    public int MessagesLost { get; private set; }
    public string LastEvent { get; private set; } = "";

    DroneController leader;
    SwarmManager swarm;
    float nextTick;
    readonly Dictionary<FollowerDrone, float> lastHeard = new Dictionary<FollowerDrone, float>();
    readonly HashSet<FollowerDrone> lost = new HashSet<FollowerDrone>();
    System.Random rng = new System.Random(99);

    void Awake()
    {
        Instance = this;
        leader = GetComponent<DroneController>();
    }

    void Start()
    {
        swarm = FindFirstObjectByType<SwarmManager>();
    }

    void Update()
    {
        if (Input.GetKeyDown(failKey)) FailRandomDrone();

        if (Time.time < nextTick || swarm == null || leader == null) return;
        nextTick = Time.time + 1f / rateHz;

        int online = 0, total = 0;
        bool changed = false;
        Vector3 lp = leader.transform.position;
        var rb = leader.GetComponent<Rigidbody>();
        Vector3 lv = rb != null ? rb.linearVelocity : Vector3.zero;

        foreach (var f in swarm.followers)
        {
            if (f == null) continue;
            total++;
            if (!lastHeard.ContainsKey(f)) lastHeard[f] = Time.time;

            // Downlink: leader state
            if (Deliver(lp, f.transform.position))
                f.ReceiveLeaderState(lp, leader.transform.rotation, lv, Time.time);

            // Uplink: heartbeat
            if (f.Alive && Deliver(f.transform.position, lp))
                lastHeard[f] = Time.time;

            bool isOnline = Time.time - lastHeard[f] < timeout;
            f.LinkOnline = isOnline;
            if (isOnline) online++;

            if (!isOnline && !lost.Contains(f))
            {
                lost.Add(f);
                changed = true;
                LastEvent = $"{f.name} lost: no heartbeat for {timeout:0.0} s";
                Debug.LogWarning($"[SwarmComms] {LastEvent}.");
            }
            else if (isOnline && lost.Contains(f))
            {
                lost.Remove(f);
                changed = true;
                LastEvent = $"{f.name} back online";
                Debug.Log($"[SwarmComms] {LastEvent}.");
            }

            if (drawLinks)
                Debug.DrawLine(lp, f.transform.position, isOnline ? new Color(0.2f, 1f, 0.6f, 0.5f) : new Color(1f, 0.3f, 0.3f, 0.7f), 1f / rateHz);
        }

        Online = online;
        Total = total;
        if (changed) Reassign();
    }

    /// <summary>One message: in range and not lost?</summary>
    bool Deliver(Vector3 from, Vector3 to)
    {
        MessagesSent++;
        if ((from - to).sqrMagnitude > range * range || rng.NextDouble() < packetLoss)
        {
            MessagesLost++;
            return false;
        }
        return true;
    }

    /// <summary>A drone (or the leader itself) tries to send data to the leader.</summary>
    public bool SendToLeader(GameObject sender)
    {
        if (leader == null || sender == leader.gameObject) return true;
        var f = sender.GetComponent<FollowerDrone>();
        if (f != null && !f.Alive) return false;
        return Deliver(sender.transform.position, leader.transform.position);
    }

    /// <summary>Formation consensus: online drones take the slots 0..n-1 in id order.</summary>
    void Reassign()
    {
        var active = new List<FollowerDrone>();
        foreach (var f in swarm.followers)
            if (f != null && f.Alive && !lost.Contains(f)) active.Add(f);
        active.Sort((a, b) => a.OriginalId.CompareTo(b.OriginalId));
        for (int i = 0; i < active.Count; i++) active[i].droneID = i;
        Debug.Log($"[SwarmComms] Formation re-assigned: {active.Count} drones hold slots 0-{active.Count - 1}.");
    }

    public string FailRandomDrone()
    {
        if (swarm == null) return "No swarm.";
        var alive = new List<FollowerDrone>();
        foreach (var f in swarm.followers) if (f != null && f.Alive) alive.Add(f);
        if (alive.Count == 0) return "No drones left to fail.";
        var victim = alive[rng.Next(alive.Count)];
        victim.Fail();
        LastEvent = $"{victim.name} failed (simulated motor failure)";
        Debug.LogWarning($"[SwarmComms] {LastEvent}.");
        return LastEvent + ".";
    }

    static GUIStyle style;

    void OnGUI()
    {
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
        float loss = MessagesSent > 0 ? 100f * MessagesLost / MessagesSent : 0f;
        var r = new Rect(Screen.width - 330, Screen.height - 86, 320, 76);
        GUI.Box(r, GUIContent.none);
        string status = Online == Total ? $"<color=#3DDC97>{Online}/{Total} online</color>" : $"<color=#FF6B6B>{Online}/{Total} online</color>";
        GUI.Label(new Rect(r.x + 8, r.y + 4, 310, 70),
            $"<b>Swarm comms</b>   <color=#AAAAAA>[K] fail a drone</color>\n" +
            $"{status} · range {range:0} m · loss {loss:0.0}%\n" +
            $"<color=#CCCCCC>{LastEvent}</color>", style);
    }
}
