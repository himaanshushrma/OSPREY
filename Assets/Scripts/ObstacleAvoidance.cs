using UnityEngine;

public class ObstacleAvoidance : MonoBehaviour
{
    [Header("Reference")]
    public ObstacleSensor sensor;

    [Header("Avoidance")]
    public float sideOffset = 2f;
    public float forwardOffset = 3f;

    private Transform avoidTarget;

    void Awake()
    {
        GameObject g = new GameObject("AvoidTarget");
        avoidTarget = g.transform;
    }

    public Transform GetTarget(Transform missionTarget)
    {
        if (sensor == null)
            return missionTarget;

        if (!sensor.Scan())
            return missionTarget;

        Collider c = sensor.lastHit.collider;

        BoxCollider box = c.GetComponent<BoxCollider>();

        if (box == null)
            return missionTarget;

        Bounds b = box.bounds;

        Vector3 left =
            new Vector3(
                b.min.x,
                missionTarget.position.y,
                b.center.z);

        Vector3 right =
            new Vector3(
                b.max.x,
                missionTarget.position.y,
                b.center.z);

        Vector3 chosen =
            Vector3.Distance(transform.position, left) <
            Vector3.Distance(transform.position, right)
            ? left
            : right;

        Vector3 dir =
            (missionTarget.position - chosen).normalized;

        Vector3 side =
            Vector3.Cross(Vector3.up, dir).normalized;

        avoidTarget.position =
            chosen +
            dir * forwardOffset +
            side * sideOffset;

        if (Vector3.Distance(
                transform.position,
                avoidTarget.position) < 1f)
        {
            return missionTarget;
        }

        return avoidTarget;
    }
}