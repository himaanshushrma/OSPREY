using System.Collections.Generic;
using UnityEngine;

public class SwarmManager : MonoBehaviour
{
    [Header("References")]
    public DroneController leader;
    public GameObject dronePrefab;
    public FormationManager formation;

    [Header("Spawn")]
    [Range(1,20)]
    public int followerCount = 4;

    [Header("Followers (Runtime)")]
    public List<FollowerDrone> followers = new List<FollowerDrone>();

    void Start()
    {
        SpawnFollowers();
    }

    void SpawnFollowers()
    {
        foreach (FollowerDrone d in followers)
        {
            if (d != null)
                Destroy(d.gameObject);
        }

        followers.Clear();

        for (int i = 0; i < followerCount; i++)
        {
            Vector3 pos =
                leader.transform.position +
                formation.GetOffset(i);

            GameObject clone = Instantiate(
                dronePrefab,
                pos,
                leader.transform.rotation);

            clone.name = "Follower_" + (i + 1);

            DroneController dc =
                clone.GetComponent<DroneController>();

            if (dc != null)
            {
                dc.mode = DroneController.Mode.Manual;
                dc.targetHeight = leader.targetHeight;
            }

            FollowerDrone fd =
                clone.GetComponent<FollowerDrone>();

            fd.droneID = i;
            fd.leader = leader.transform;
            fd.formation = formation;
            formation.leader = leader.transform;

            followers.Add(fd);
        }
    }

    public void SetAllFollowersAuto()
    {
        foreach (FollowerDrone f in followers)
        {
            DroneController dc = f.GetComponent<DroneController>();

            dc.mode = DroneController.Mode.Auto;
            dc.targetHeight = leader.targetHeight;
        }

        Debug.Log("Swarm AUTO");
    }

    public void SetAllFollowersManual()
    {
        foreach (FollowerDrone f in followers)
        {
            DroneController dc = f.GetComponent<DroneController>();

            dc.mode = DroneController.Mode.Manual;
            dc.currentWaypoint = null;
        }

        Debug.Log("Swarm MANUAL");
    }
}