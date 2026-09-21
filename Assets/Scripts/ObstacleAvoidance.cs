using UnityEngine;

public class ObstacleAvoidance : MonoBehaviour
{
    [Header("Sensors")]
    public float rayDistance = 4f;
    public float avoidStrength = 10f;
    public LayerMask obstacleMask;

    Vector3 avoidance;

    public Vector3 GetAvoidanceForce()
    {
        return avoidance;
    }

    void FixedUpdate()
    {
        avoidance = CalculateAvoidance();
    }

    Vector3 CalculateAvoidance()
    {
        Vector3 force = Vector3.zero;

        Vector3 origin = transform.position + Vector3.up * 0.15f;

        // Centre
        if (Physics.Raycast(origin, transform.forward, out RaycastHit hitC, rayDistance, obstacleMask))
        {
            force += hitC.normal * avoidStrength;
            Debug.DrawRay(origin, transform.forward * hitC.distance, Color.red);
        }
        else
        {
            Debug.DrawRay(origin, transform.forward * rayDistance, Color.green);
        }

        // Left 30°
        Vector3 leftDir = Quaternion.Euler(0, -30, 0) * transform.forward;

        if (Physics.Raycast(origin, leftDir, out RaycastHit hitL, rayDistance, obstacleMask))
        {
            force += hitL.normal * avoidStrength * 0.8f;
            Debug.DrawRay(origin, leftDir * hitL.distance, Color.red);
        }
        else
        {
            Debug.DrawRay(origin, leftDir * rayDistance, Color.green);
        }

        // Right 30°
        Vector3 rightDir = Quaternion.Euler(0, 30, 0) * transform.forward;

        if (Physics.Raycast(origin, rightDir, out RaycastHit hitR, rayDistance, obstacleMask))
        {
            force += hitR.normal * avoidStrength * 0.8f;
            Debug.DrawRay(origin, rightDir * hitR.distance, Color.red);
        }
        else
        {
            Debug.DrawRay(origin, rightDir * rayDistance, Color.green);
        }

        force.y = 0;
        return force;
    }
}