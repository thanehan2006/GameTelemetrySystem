using UnityEngine;

/// <summary>
/// Camera góc nhìn thứ ba: tự bám theo và xoay quanh Player bằng chuột.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 5f;
    public float height = 1.6f;
    public float mouseSensitivity = 2f;
    public float minPitch = -10f;
    public float maxPitch = 60f;
    public float followSmooth = 15f;

    private float yaw;
    private float pitch = 15f;

    void Start()
    {
        if (target == null)
        {
            var playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) target = playerObj.transform;
        }

        yaw = target != null ? target.eulerAngles.y : transform.eulerAngles.y;

        if (target != null)
        {
            SnapToTarget();
        }
    }

    void LateUpdate()
    {
        if (target == null)
        {
            var playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) target = playerObj.transform;
            else return;
        }

        if (GameOverManager.IsGameOver) return;

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, minPitch, maxPitch);
        }

        GetDesiredPose(out Vector3 desired, out Quaternion rot);
        transform.position = Vector3.Lerp(transform.position, desired, followSmooth * Time.deltaTime);
        transform.rotation = rot;
    }

    private void SnapToTarget()
    {
        GetDesiredPose(out Vector3 desired, out Quaternion rot);
        transform.SetPositionAndRotation(desired, rot);
    }

    private void GetDesiredPose(out Vector3 desired, out Quaternion rot)
    {
        rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + Vector3.up * height;
        desired = pivot - rot * Vector3.forward * distance;
        desired.y = Mathf.Max(desired.y, target.position.y + 0.4f);
    }
}
