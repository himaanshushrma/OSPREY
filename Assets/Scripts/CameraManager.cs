using UnityEngine;

public class CameraManager : MonoBehaviour
{
    public Transform chaseCam;
    public Transform tacticalCam;

    public float smooth = 8f;

    private int mode = 1;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
            mode = 1;

        if (Input.GetKeyDown(KeyCode.Alpha2))
            mode = 2;
    }

    void LateUpdate()
    {
        Transform target = (mode == 1) ? chaseCam : tacticalCam;

        if (target == null) return;

        transform.position = Vector3.Lerp(
            transform.position,
            target.position,
            smooth * Time.deltaTime);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            target.rotation,
            smooth * Time.deltaTime);
    }
}