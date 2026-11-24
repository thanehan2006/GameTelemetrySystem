using UnityEngine;

[DefaultExecutionOrder(50)]
// Điều khiển camera góc nhìn thứ ba xoay quanh nhân vật, tránh xuyên vật cản và rung khi trúng đòn
public class ThirdPersonCamera : MonoBehaviour
{
    public static ThirdPersonCamera Instance { get; private set; }

    [Header("Target & Framing")]
    public Transform target;
    public float distance = 4.2f;
    public float minDistance = 2.6f;
    public float maxDistance = 6.5f;
    public float scrollZoomSpeed = 2.0f;
    public float height = 1.6f;

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
    private float fovPunch;
    private readonly RaycastHit[] collisionHitBuffer = new RaycastHit[16];

    // Tìm mục tiêu Player trong scene
    void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        desiredDistance = distance;
        currentDistance = distance;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Thiết lập góc nhìn ban đầu phía sau nhân vật
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

    // Cập nhật vị trí, xoay theo chuột và né va chạm tường sau bước di chuyển
    void LateUpdate()
    {
        if (target == null)
        {
            ResolveTarget();
            if (target == null) return;
            smoothPivot = GetTargetPivot();
        }

        if (GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen) return;

        HandleInput();
        UpdateCameraTransform();
        UpdateDynamicFov();
    }

    // Tạo hiệu ứng rung camera khi chém trúng, đỡ đòn hoặc nổ
    public void AddCameraShake(float intensity, float duration)
    {
        shakeIntensity = Mathf.Max(shakeIntensity, intensity);
        shakeDuration = Mathf.Max(duration, 0.01f);
        shakeTimer = shakeDuration;
    }

    // Thay đổi nhẹ FOV tức thời để tăng lực va chạm
    public void AddFovPunch(float deltaFov)
    {
        fovPunch = Mathf.Clamp(fovPunch + deltaFov, -8f, 12f);
    }

    private void ResolveTarget()
    {
        if (target == null)
        {
            if (PlayerController.Instance != null)
            {
                playerController = PlayerController.Instance;
                target = playerController.transform;
            }
            else
            {
                var playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null) target = playerObj.transform;
            }
        }

        if (target != null && playerController == null)
        {
            playerController = target.GetComponent<PlayerController>();
        }
    }

    // Đọc chuyển động chuột để xoay góc nhìn ngang và dọc có giới hạn
    private void HandleInput()
    {
        float mouseX = 0f;
        float mouseY = 0f;

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            float sens = mouseSensitivity * GameSettingsManager.MouseSensitivityMultiplier;
            mouseX = Input.GetAxisRaw("Mouse X");
            mouseY = Input.GetAxisRaw("Mouse Y");
            targetYaw += mouseX * sens;
            targetPitch = Mathf.Clamp(targetPitch - mouseY * sens, minPitch, maxPitch);

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                desiredDistance = Mathf.Clamp(desiredDistance - scroll * scrollZoomSpeed, minDistance, maxDistance);
            }
        }

        EnemyHealth lockedEnemy = PlayerAttack.CurrentLockedTarget;
        if (lockedEnemy != null && !lockedEnemy.IsDead && target != null)
        {
            Vector3 toEnemy = lockedEnemy.transform.position - target.position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude > 0.36f)
            {
                float dt = Time.deltaTime;
                if (Mathf.Abs(mouseX) < 0.35f)
                {
                    float desiredLockYaw = Mathf.Atan2(toEnemy.x, toEnemy.z) * Mathf.Rad2Deg;
                    targetYaw = Mathf.LerpAngle(targetYaw, desiredLockYaw, 1f - Mathf.Exp(-6.8f * dt));
                }
                if (Mathf.Abs(mouseY) < 0.35f)
                {
                    targetPitch = Mathf.Lerp(targetPitch, 18f, 1f - Mathf.Exp(-3.5f * dt));
                }
            }
        }
    }

    // Nội suy vị trí và góc quay camera theo điểm tựa nhân vật
    private void UpdateCameraTransform()
    {
        float dt = Time.deltaTime;
        float rotT = 1f - Mathf.Exp(-rotationSmooth * dt);
        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, rotT);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, rotT);

        Quaternion rot = Quaternion.Euler(currentPitch, currentYaw, 0f);

        Vector3 targetPivot = GetTargetPivot();
        float followT = 1f - Mathf.Exp(-followSmooth * dt);
        smoothPivot = Vector3.Lerp(smoothPivot, targetPivot, followT);

        Vector3 shoulderShift = rot * Vector3.right * shoulderOffset;
        Vector3 pivotWithShoulder = smoothPivot + shoulderShift;

        float safeDistance = ResolveCollisionDistance(pivotWithShoulder, rot, desiredDistance);
        float distSmooth = safeDistance < currentDistance ? collisionInSmooth : collisionOutSmooth;
        currentDistance = Mathf.Lerp(currentDistance, safeDistance, 1f - Mathf.Exp(-distSmooth * dt));

        Vector3 finalPos = pivotWithShoulder - rot * Vector3.forward * currentDistance;
        finalPos.y = Mathf.Max(finalPos.y, target.position.y + 0.35f);

        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.unscaledDeltaTime;
            float falloff = Mathf.Clamp01(shakeTimer / shakeDuration);
            finalPos += Random.insideUnitSphere * (shakeIntensity * falloff);
            if (shakeTimer <= 0f) shakeIntensity = 0f;
        }

        transform.SetPositionAndRotation(finalPos, rot);
    }

    private void UpdateDynamicFov()
    {
        if (cam == null) return;

        float unscaledDt = Time.unscaledDeltaTime;
        bool isMoving = playerController != null && playerController.HasMovementInput;
        if (Mathf.Abs(fovPunch) > 0.001f)
        {
            fovPunch = Mathf.Lerp(fovPunch, 0f, 1f - Mathf.Exp(-10f * unscaledDt));
        }
        else
        {
            fovPunch = 0f;
        }

        float targetFov = baseFov + (isMoving ? moveFovBoost : 0f) + fovPunch;
        float delta = Mathf.Abs(cam.fieldOfView - targetFov);
        if (delta <= 0.005f)
        {
            if (delta > 0f) cam.fieldOfView = targetFov;
            return;
        }

        float t = 1f - Mathf.Exp(-fovSmooth * unscaledDt);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, t);
    }

    // Thu gần camera khi tia kiểm tra va chạm với địa hình hoặc vật thể
    private float ResolveCollisionDistance(Vector3 origin, Quaternion rot, float maxDist)
    {
        Vector3 dir = -(rot * Vector3.forward);
        int hitCount = Physics.SphereCastNonAlloc(origin, collisionRadius, dir, collisionHitBuffer, maxDist, ~0, QueryTriggerInteraction.Ignore);

        float closest = maxDist;
        float minAllowed = minDistance * 0.5f;
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = collisionHitBuffer[i].collider;
            if (col == null || col.transform.IsChildOf(target)) continue;

            float hitDist = Mathf.Max(collisionHitBuffer[i].distance - collisionOffset, minAllowed);
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
