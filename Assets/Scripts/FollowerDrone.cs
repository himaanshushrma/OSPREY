using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class FollowerDrone : MonoBehaviour
{
    public Transform leader;
    public FormationManager formation;

    public int droneID;

    public float moveForce = 8f;
    public float hoverKp = 30f;
    public float hoverKd = 10f;
    public float rotationSpeed = 4f;

    Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        rb.mass = 1.8f;
        rb.linearDamping = 2f;
        rb.angularDamping = 4f;
    }

    void FixedUpdate()
    {
        if (leader == null || formation == null)
            return;

        Vector3 target =
            leader.TransformPoint(
                formation.GetOffset(droneID));

        Hover(target.y);
        Move(target);
    }

    void Hover(float targetHeight)
    {
        float error =
            targetHeight - transform.position.y;

        float lift =
            error * hoverKp -
            rb.linearVelocity.y * hoverKd;

        rb.AddForce(
            Vector3.up * lift,
            ForceMode.Acceleration);
    }

    void Move(Vector3 target)
    {
        Vector3 dir =
            target - transform.position;

        dir.y = 0;

        if (dir.magnitude < 0.3f)
            return;

        dir.Normalize();

        rb.AddForce(
            dir * moveForce,
            ForceMode.Acceleration);

        Quaternion rot =
            Quaternion.LookRotation(dir);

        rb.MoveRotation(
            Quaternion.Slerp(
                rb.rotation,
                rot,
                rotationSpeed * Time.fixedDeltaTime));
    }
}