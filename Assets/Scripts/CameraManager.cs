using UnityEngine;

public class CameraManager : MonoBehaviour
{
    public SwarmManager swarm;
    public Transform chaseCam;
    public float smooth = 8f;

    bool tactical = false;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C)) tactical = false;
        if (Input.GetKeyDown(KeyCode.V)) tactical = true;
    }

    void LateUpdate()
    {
        if (swarm == null || swarm.leader == null) return;

        if (!tactical)
            ChaseView();
        else
            TacticalView();
    }

    void ChaseView()
    {
        transform.position = Vector3.Lerp(
            transform.position,
            chaseCam.position,
            smooth * Time.deltaTime);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            chaseCam.rotation,
            smooth * Time.deltaTime);
    }

    void TacticalView()
    {
        Vector3 center = Vector3.zero;

        foreach (Transform d in swarm.drones)
            center += d.position;

        center /= swarm.drones.Count;

        float radius = 6f;

        foreach (Transform d in swarm.drones)
            radius = Mathf.Max(radius,
                Vector3.Distance(center, d.position));

        Vector3 desired = center + new Vector3(0, radius + 10f, -(radius + 8f));

        transform.position = Vector3.Lerp(
            transform.position,
            desired,
            5f * Time.deltaTime);

        Quaternion look = Quaternion.LookRotation(center - transform.position, Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            look,
            5f * Time.deltaTime);
    }
}