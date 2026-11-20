using UnityEngine;

/// <summary>
/// Camera góc nhìn thứ ba phong cách Action AAA (Over-the-Shoulder, Collision Avoidance, Smooth Damping, Dynamic FOV).
/// </summary>
[DefaultExecutionOrder(50)]
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target & Framing (AAA Over-the-Shoulder)")]
    public Transform target;
    public float distance = 4.2f;
    public float minDistance = 2.6f;
    public float maxDistance = 6.5f;
    public float scrollZoomSpeed = 2.0f;
    public float height = 1.6f;
    [Tooltip("Lệch vai phải giúp nhân vật nằm gọn ở 1/3 trái màn hình, thoáng tầm nhìn chiến đấu.")]
    public float shoulderOffset = 0.55f;

    [Header("Orbit & Damping")]
    public float mouseSensitivity = 2.0f;
    public float minPitch = -15f;
    public float maxPitch = 55f;
    public float rotationSmooth = 18f;
    public float followSmooth = 22f;

    [Header("Collision Avoidance")]
    public float collisionRadius = 0.25f;
    public float collisionOffset = 0.15f;
    public float collisionInSmooth = 25f;
    public float collisionOutSmooth = 8f;

    [Header("Dynamic FOV")]
    public float baseFov = 55f;
    public float moveFovBoost = 3f;
    public float fovSmooth = 6f;

    private Camera cam;
    private PlayerController playerController;

    private float targetYaw;
    private float targetPitch = 12f;
    private float currentYaw;
    private float currentPitch = 12f;

    private float desiredDistance;
    private float currentDistance;
    private Vector3 smoothPivot;

    private float shakeIntensity;
    private float shakeTimer;
    private float shakeDuration;

    void Awake()
    {
        cam = GetComponent<Camera>();
        desiredDistance = distance;
        currentDistance = distance;
    }

    void Start()
    {
        ResolveTarget();

        if (target != null)
        {
            targetYaw = target.eulerAngles.y;
            currentYaw = targetYaw;
            smoothPivot = GetTargetPivot();
            SnapToTarget();
        }
        else
        {
            targetYaw = transform.eulerAngles.y;
            currentYaw = targetYaw;
        }

        if (cam != null)
        {
            cam.fieldOfView = baseFov;
        }
    }

    void LateUpdate()
    {
        if (target == null)
        {
            ResolveTarget();
            if (target == null) return;
            smoothPivot = GetTargetPivot();
        }

        if (GameOverManager.IsGameOver || LevelUpChoiceManager.IsPaused) return;

        HandleInput();
        UpdateCameraTransform();
        UpdateDynamicFov();
    }

    /// <summary>
    /// Kích hoạt rung camera nhẹ khi va chạm hoặc tung đòn đánh mạnh.
    /// </summary>
    public void AddCameraShake(float intensity, float duration)
    {
        shakeIntensity = Mathf.Max(shakeIntensity, intensity);
        shakeDuration = Mathf.Max(duration, 0.01f);
        shakeTimer = shakeDuration;
    }

    private void ResolveTarget()
    {
        if (target == null)
        {
            var playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null) target = playerObj.transform;
        }

        if (target != null && playerController == null)
        {
            playerController = target.GetComponent<PlayerController>();
        }
    }

    private void HandleInput()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            targetYaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            targetPitch = Mathf.Clamp(targetPitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity, minPitch, maxPitch);

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                desiredDistance = Mathf.Clamp(desiredDistance - scroll * scrollZoomSpeed, minDistance, maxDistance);
            }
        }
    }

    private void UpdateCameraTransform()
    {
        float rotT = 1f - Mathf.Exp(-rotationSmooth * Time.deltaTime);
        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, rotT);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, rotT);

        Quaternion rot = Quaternion.Euler(currentPitch, currentYaw, 0f);

        Vector3 targetPivot = GetTargetPivot();
        float followT = 1f - Mathf.Exp(-followSmooth * Time.deltaTime);
        smoothPivot = Vector3.Lerp(smoothPivot, targetPivot, followT);

        Vector3 shoulderShift = rot * Vector3.right * shoulderOffset;
        Vector3 pivotWithShoulder = smoothPivot + shoulderShift;

        float safeDistance = ResolveCollisionDistance(pivotWithShoulder, rot, desiredDistance);
        float distSmooth = safeDistance < currentDistance ? collisionInSmooth : collisionOutSmooth;
        currentDistance = Mathf.Lerp(currentDistance, safeDistance, 1f - Mathf.Exp(-distSmooth * Time.deltaTime));

        Vector3 finalPos = pivotWithShoulder - rot * Vector3.forward * currentDistance;
        finalPos.y = Mathf.Max(finalPos.y, target.position.y + 0.35f);

        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;
            float falloff = Mathf.Clamp01(shakeTimer / shakeDuration);
            finalPos += Random.insideUnitSphere * (shakeIntensity * falloff);
            if (shakeTimer <= 0f) shakeIntensity = 0f;
        }

        transform.SetPositionAndRotation(finalPos, rot);
    }

    private void UpdateDynamicFov()
    {
        if (cam == null) return;

        bool isMoving = playerController != null && playerController.HasMovementInput;
        float targetFov = baseFov + (isMoving ? moveFovBoost : 0f);
        float t = 1f - Mathf.Exp(-fovSmooth * Time.deltaTime);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, t);
    }

    private float ResolveCollisionDistance(Vector3 origin, Quaternion rot, float maxDist)
    {
        Vector3 dir = -(rot * Vector3.forward);
        RaycastHit[] hits = Physics.SphereCastAll(origin, collisionRadius, dir, maxDist, ~0, QueryTriggerInteraction.Ignore);

        float closest = maxDist;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null || col.transform.IsChildOf(target)) continue;

            float hitDist = Mathf.Max(hits[i].distance - collisionOffset, minDistance * 0.5f);
            if (hitDist < closest)
            {
                closest = hitDist;
            }
        }

        return closest;
    }

    private Vector3 GetTargetPivot()
    {
        return target.position + Vector3.up * height;
    }

    private void SnapToTarget()
    {
        Quaternion rot = Quaternion.Euler(currentPitch, currentYaw, 0f);
        Vector3 pivotWithShoulder = smoothPivot + rot * Vector3.right * shoulderOffset;
        float safeDistance = ResolveCollisionDistance(pivotWithShoulder, rot, desiredDistance);
        currentDistance = safeDistance;

        Vector3 pos = pivotWithShoulder - rot * Vector3.forward * currentDistance;
        pos.y = Mathf.Max(pos.y, target.position.y + 0.35f);
        transform.SetPositionAndRotation(pos, rot);
    }
}
