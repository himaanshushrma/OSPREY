using UnityEngine;

public class Waypoint : MonoBehaviour
{
    public float radius = 1.2f;

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(transform.position, 0.25f);

        Gizmos.color = new Color(0,1,1,0.25f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}