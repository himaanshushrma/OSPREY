using UnityEngine;

public class ObstacleAvoidance : MonoBehaviour
{
    [Header("Sensor Settings")]
    public float rayDistance = 4f;
    public float avoidStrength = 10f;
    public LayerMask obstacleMask;

    public Vector3 GetAvoidanceForce()
    {
        Vector3 force = Vector3.zero;

        Vector3[] rays =
        {
            transform.forward,
            Quaternion.Euler(0,-30,0) * transform.forward,
            Quaternion.Euler(0,30,0) * transform.forward
        };

        foreach (Vector3 dir in rays)
        {
            if (Physics.Raycast(transform.position,
                                dir,
                                out RaycastHit hit,
                                rayDistance,
                                obstacleMask))
            {
                force += (transform.position - hit.point).normalized;
            }
        }

        return force.normalized * avoidStrength;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;

        Gizmos.DrawRay(transform.position,
            transform.forward * rayDistance);

        Gizmos.DrawRay(transform.position,
            Quaternion.Euler(0,-30,0) * transform.forward * rayDistance);

        Gizmos.DrawRay(transform.position,
            Quaternion.Euler(0,30,0) * transform.forward * rayDistance);
    }
}