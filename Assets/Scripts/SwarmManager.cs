using UnityEngine;
using System.Collections.Generic;

public class SwarmManager : MonoBehaviour
{
    public DroneController leader;
    public GameObject dronePrefab;
    public FormationManager formation;
    public int followerCount = 4;

    [HideInInspector]
    public List<Transform> drones = new();

    void Start()
    {
        SpawnSwarm();
    }

    void SpawnSwarm()
    {
        if (leader == null || dronePrefab == null || formation == null)
        {
            Debug.LogError("Assign Leader, DronePrefab and FormationManager in SwarmManager!");
            return;
        }

        drones.Clear();
        drones.Add(leader.transform);

        for (int i = 0; i < followerCount; i++)
        {
            Vector3 pos = leader.transform.position + formation.GetOffset(i);

            GameObject clone = Instantiate(
                dronePrefab,
                pos,
                leader.transform.rotation);

            DroneController dc = clone.GetComponent<DroneController>();
            if (dc != null) Destroy(dc);

            FollowerDrone fd = clone.GetComponent<FollowerDrone>();
            if (fd == null) fd = clone.AddComponent<FollowerDrone>();

            fd.leader = leader.transform;
            fd.formation = formation;
            fd.droneID = i;

            drones.Add(clone.transform);
        }
    }
}