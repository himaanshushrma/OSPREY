using UnityEngine;

public class Propeller : MonoBehaviour
{
    public float speed = 2500f;

    void Update()
    {
        transform.Rotate(0f, speed * Time.deltaTime, 0f, Space.Self);
    }
}