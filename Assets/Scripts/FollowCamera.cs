using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    public Transform target;

    public Vector3 offset = new Vector3(0f, 3f, -6f);

    public float followSpeed = 6f;
    public float rotateSpeed = 4f;

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition =
            target.position +
            target.rotation * offset;

        transform.position = Vector3.Lerp(
            transform.position,
            desiredPosition,
            followSpeed * Time.deltaTime);

        Quaternion desiredRotation =
            Quaternion.LookRotation(
                target.position - transform.position,
                Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRotation,
            rotateSpeed * Time.deltaTime);
    }
}