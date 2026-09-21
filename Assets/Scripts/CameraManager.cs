using UnityEngine;

public class CameraManager : MonoBehaviour
{
    public Transform leader;
    public Transform chaseCam;
    public Transform tacticalAnchor;

    public float smooth = 8f;
    bool tactical;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C)) tactical = false;
        if (Input.GetKeyDown(KeyCode.V)) tactical = true;
    }

    void LateUpdate()
    {
        if (leader == null) return;

        if (!tactical)
        {
            Quaternion yaw = Quaternion.Euler(0, leader.eulerAngles.y, 0);

            Vector3 targetPos = leader.position + yaw * chaseCam.localPosition;
            Quaternion targetRot = Quaternion.Euler(15f, leader.eulerAngles.y, 0);

            transform.position = Vector3.Lerp(transform.position, targetPos, smooth * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, smooth * Time.deltaTime);
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, tacticalAnchor.position, smooth * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, tacticalAnchor.rotation, smooth * Time.deltaTime);
        }
    }
}