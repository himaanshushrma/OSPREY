using UnityEngine;
using TMPro;

public class HUDController : MonoBehaviour
{
    public Transform drone;
    public Rigidbody droneRB;

    public TMP_Text altitudeText;
    public TMP_Text speedText;
    public TMP_Text batteryText;
    public TMP_Text headingText;

    float battery = 100f;

    void Update()
    {
        if (drone == null || droneRB == null) return;

        float altitude = drone.position.y;
        float speed = droneRB.linearVelocity.magnitude;
        float heading = drone.eulerAngles.y;

        // Battery drains only while moving
        battery -= speed * 0.02f * Time.deltaTime;
        battery = Mathf.Clamp(battery, 0, 100);

        altitudeText.text = $"ALT {altitude:0.0} m";
        speedText.text = $"SPD {speed:0.0} m/s";
        batteryText.text = $"BAT {battery:0}%";
        headingText.text = $"HDG {heading:000}°";
    }
}