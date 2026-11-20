using UnityEngine;

public class ObstacleSensor : MonoBehaviour
{
    [Header("Sensor")]
    public float sensorRange = 8f;
    public LayerMask obstacleLayer;

    [HideInInspector] public bool obstacleDetected;
    [HideInInspector] public float obstacleDistance;
    [HideInInspector] public Vector3 hitPoint;
    [HideInInspector] public Vector3 hitNormal;
    [HideInInspector] public RaycastHit lastHit;

    public bool Scan()
    {
        obstacleDetected = false;

        if (Physics.Raycast(
            transform.position,
            transform.forward,
            out RaycastHit hit,
            sensorRange,
            obstacleLayer))
        {
            lastHit = hit;
            hitPoint = hit.point;
            hitNormal = hit.normal;
            obstacleDistance = hit.distance;
            obstacleDetected = true;
            return true;
        }

        return false;
    }

    void Update()
    {
        Scan();
    }

    void OnDrawGizmos()
    {
        Gizmos.color = obstacleDetected ? Color.red : Color.green;

        Gizmos.DrawLine(
            transform.position,
            transform.position + transform.forward * sensorRange);

        if (obstacleDetected)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(hitPoint, 0.15f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(hitPoint, hitPoint + hitNormal);
        }
    }
}