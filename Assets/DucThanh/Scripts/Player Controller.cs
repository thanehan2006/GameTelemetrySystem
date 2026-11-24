using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(-20)]
// Điều khiển di chuyển, nhảy, giữ chuột phải đỡ đòn, đẩy lùi và khóa con trỏ chuột của người chơi
public class PlayerController : MonoBehaviour
{
    public static PlayerController Instance { get; private set; }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Movement Settings")]
    public float moveSpeed = 6.5f;
    public float rotationSpeed = 14f;
    public float gravity = -25f;

    [Header("Block / Parry Settings")]
    [Range(0f, 1f)] public float blockDamageReduction = 0.72f;

    public float perfectParryWindow = 0.28f;

    [Range(0f, 1f)] public float perfectParryHealPercent = 0.06f;

    [Header("Knockback & Collision")]
    public float knockbackDamping = 36f;
    public float bodyCollisionPushForce = 4.5f;

    [Header("Camera & Visual")]
    public Transform playerCamera;

    public Transform visualModel;

    [Header("Ground & Foot Alignment")]
    [SerializeField] private float targetSkinWidth = 0.04f;
    [SerializeField] private float footGroundOffset = 0f;
    [SerializeField] private float maxVisualGroundDrop = 0.35f;
    [SerializeField] private float visualGroundSmoothSpeed = 22f;

    public Vector3 CurrentInputDirection { get; private set; }
    public bool HasMovementInput { get; private set; }
    public bool IsBlocking { get; private set; }
    public bool IsPerfectParryWindow => IsBlocking && (Time.time - blockStartTime) <= perfectParryWindow;

    private CharacterController controller;
    private PlayerAttack playerAttack;
    private float verticalVelocity;
    private Vector3 knockbackVelocity;
    private Vector3 lungeVelocity;
    private float blockStartTime;
    private float footstepTimer;
    private int lastHorizontalDir;
    private int lastVerticalDir;
    private GameObject blockAuraObj;
    private Material blockAuraMat;
    private Light blockAuraLight;

    private readonly RaycastHit[] groundHitBuffer = new RaycastHit[16];
    private Transform cachedVisualModel;
    private Animator visualAnimator;
    private Transform leftFootBone;
    private Transform rightFootBone;
    private float calibratedIdleFootOffsetY;
    private bool hasCalibratedIdleFootOffset;
    private bool hasInitializedVisualGround;
    private Vector3 lastGroundSamplePos;
    private float cachedGroundY;
    private bool cachedHasGround;
    private float nextGroundSampleTime;
    private Terrain cachedTerrain;

    // Khởi tạo CharacterController và hiệu chỉnh kích thước collider
    void Awake()
    {
        Instance = this;
        PlayerCharacterLoader.ApplySelectedCharacter(gameObject);

        controller = GetComponent<CharacterController>();
        controller.minMoveDistance = 0f;
        CalibrateCharacterController();
        playerAttack = GetComponent<PlayerAttack>();

        if (visualModel == null)
        {
            Transform body = transform.Find("PlayerBody");
            if (body != null)
            {
                visualModel = body;
            }
        }

        RefreshVisualFootBones();
    }

    // Nạp model nhân vật đã chọn, cập nhật tốc độ và đồng bộ vị trí trên mặt đất
    void Start()
    {
        SnapToGround();

        if (playerCamera == null && Camera.main != null)
        {
            playerCamera = Camera.main.transform;
        }

        if (knockbackDamping < 30f)
        {
            knockbackDamping = 36f;
        }

        if (GetComponent<NavMeshObstacle>() == null)
        {
            var obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.radius = controller.radius;
            obstacle.height = controller.height;
            obstacle.center = controller.center;
            obstacle.carving = false;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Hủy đăng ký trạng thái hoặc sự kiện khi đối tượng bị phá hủy
    void OnDestroy()
    {
        if (blockAuraMat != null) Destroy(blockAuraMat);
        if (Instance == this) Instance = null;
    }

    // Xử lý nhận phím di chuyển, trạng thái đỡ đòn và bụi bước chân
    void Update()
    {
        if (GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen)
        {
            CurrentInputDirection = Vector3.zero;
            HasMovementInput = false;
            IsBlocking = false;
            UpdateBlockAuraVisual();
            knockbackVelocity = Vector3.zero;
            lungeVelocity = Vector3.zero;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
            return;
        }

        HandleCursorLock();
        HandleMovement();
        UpdateBlockAuraVisual();
    }

    // Hiệu chỉnh bám mặt đất mượt mà theo khung hình cho nhân vật
    void LateUpdate()
    {

        if (visualModel == null || visualModel == transform) return;

        if (visualModel != cachedVisualModel)
        {
            RefreshVisualFootBones();
        }

        Vector3 curPos = transform.position;
        float now = Time.time;
        if (!cachedHasGround
            || (!controller.isGrounded && now >= nextGroundSampleTime)
            || (controller.isGrounded && (curPos - lastGroundSamplePos).sqrMagnitude > 0.0225f && now >= nextGroundSampleTime))
        {
            cachedHasGround = TrySampleGroundHeight(curPos, out cachedGroundY, 1.2f, 4.0f);
            lastGroundSamplePos = curPos;
            nextGroundSampleTime = now + 0.10f;
        }

        bool hasGround = cachedHasGround;
        float groundY = cachedGroundY;
        bool nearGround = controller.isGrounded || (hasGround && (curPos.y - groundY) <= maxVisualGroundDrop);
        float slopeDeltaY = (hasGround && nearGround) ? Mathf.Clamp(groundY - curPos.y, -maxVisualGroundDrop, 0.08f) : 0f;

        float footSoleCorrectionY = 0f;
        if (visualAnimator != null && visualAnimator.isHuman && leftFootBone != null && rightFootBone != null)
        {
            float scaleY = Mathf.Max(0.01f, visualModel.lossyScale.y);
            float leftSoleRelY = (leftFootBone.position.y - visualModel.position.y) - visualAnimator.leftFeetBottomHeight * scaleY;
            float rightSoleRelY = (rightFootBone.position.y - visualModel.position.y) - visualAnimator.rightFeetBottomHeight * scaleY;
            float lowestFootRelY = Mathf.Min(leftSoleRelY, rightSoleRelY);

            bool isRunning = HasMovementInput && !IsBlocking && (playerAttack == null || !playerAttack.IsAttacking);
            if (!isRunning)
            {
                calibratedIdleFootOffsetY = Mathf.Clamp(-lowestFootRelY, -0.25f, 0.15f);
                hasCalibratedIdleFootOffset = true;
                footSoleCorrectionY = calibratedIdleFootOffsetY;
            }
            else
            {
                float baseCorrection = hasCalibratedIdleFootOffset
                    ? calibratedIdleFootOffsetY
                    : Mathf.Clamp(-lowestFootRelY, -0.25f, 0.05f);

                float predictedSoleY = lowestFootRelY + baseCorrection;
                if (predictedSoleY < -0.015f)
                {
                    footSoleCorrectionY = -lowestFootRelY;
                }
                else if (predictedSoleY > 0.16f)
                {
                    footSoleCorrectionY = baseCorrection - (predictedSoleY - 0.16f) * 0.7f;
                }
                else
                {
                    footSoleCorrectionY = baseCorrection;
                }
            }
        }

        float parentScaleY = Mathf.Max(0.01f, transform.lossyScale.y);
        float targetLocalY = Mathf.Clamp(
            (slopeDeltaY + footSoleCorrectionY + footGroundOffset) / parentScaleY,
            -(maxVisualGroundDrop + 0.25f),
            0.15f);

        Vector3 localPos = visualModel.localPosition;
        float newY;
        if (!hasInitializedVisualGround)
        {
            newY = targetLocalY;
            hasInitializedVisualGround = true;
        }
        else
        {
            float k = 1f - Mathf.Exp(-visualGroundSmoothSpeed * Time.deltaTime);
            newY = Mathf.Lerp(localPos.y, targetLocalY, k);
        }

        visualModel.localPosition = new Vector3(0f, newY, 0f);
    }

    // Tự động cân chỉnh tâm và chiều cao của CharacterController
    private void CalibrateCharacterController()
    {
        if (controller == null) return;
        controller.skinWidth = Mathf.Clamp(targetSkinWidth, 0.01f, 0.06f);
        controller.center = new Vector3(
            controller.center.x,
            controller.height * 0.5f + controller.skinWidth,
            controller.center.z);
    }

    private void RefreshVisualFootBones()
    {
        cachedVisualModel = visualModel;
        hasCalibratedIdleFootOffset = false;
        hasInitializedVisualGround = false;
        calibratedIdleFootOffsetY = 0f;
        visualAnimator = null;
        leftFootBone = null;
        rightFootBone = null;

        if (visualModel == null) return;

        visualAnimator = visualModel.GetComponentInChildren<Animator>();
        if (visualAnimator != null && visualAnimator.isHuman)
        {
            leftFootBone = visualAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rightFootBone = visualAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
        }
    }

    // Đọc phím di chuyển WASD/mũi tên từ Input System
    private Vector2 ReadDigitalMoveInput()
    {
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) lastHorizontalDir = -1;
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) lastHorizontalDir = 1;
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) lastVerticalDir = -1;
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) lastVerticalDir = 1;

        bool left = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        bool right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
        bool down = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
        bool up = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);

        float x = (left && right) ? lastHorizontalDir
                : left ? -1f
                : right ? 1f
                : 0f;

        float z = (down && up) ? lastVerticalDir
                : down ? -1f
                : up ? 1f
                : 0f;

        return new Vector2(x, z);
    }

    // Quy đổi hướng nhập từ bàn phím sang không gian góc nhìn camera
    private Vector3 GetCameraRelativeDirection(Vector3 inputDir)
    {
        if (playerCamera == null)
        {
            if (Camera.main != null) playerCamera = Camera.main.transform;
            if (playerCamera == null) return inputDir;
        }

        Vector3 camForward = playerCamera.forward;
        Vector3 camRight = playerCamera.right;

        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        return (camForward * inputDir.z + camRight * inputDir.x).normalized;
    }

    // Tính vận tốc chạy, nhảy, trọng lực, đẩy lùi và lực lướt khi đánh
    private void HandleMovement()
    {
        Vector2 rawInput = ReadDigitalMoveInput();
        HasMovementInput = rawInput.sqrMagnitude > 0.01f;

        Vector3 inputDir = HasMovementInput ? new Vector3(rawInput.x, 0f, rawInput.y).normalized : Vector3.zero;
        CurrentInputDirection = HasMovementInput ? GetCameraRelativeDirection(inputDir) : Vector3.zero;

        bool wantsBlock = Cursor.lockState == CursorLockMode.Locked && Input.GetMouseButton(1);
        if (wantsBlock && !IsBlocking)
        {
            IsBlocking = true;
            blockStartTime = Time.time;
            lungeVelocity = Vector3.zero;

            if (playerAttack != null)
            {
                playerAttack.CancelAttackForBlock();
            }
        }
        else if (!wantsBlock && IsBlocking)
        {
            IsBlocking = false;
        }

        bool isAttacking = playerAttack != null && playerAttack.IsAttacking;
        Vector3 moveDir = (isAttacking || IsBlocking) ? Vector3.zero : CurrentInputDirection;

        if (IsBlocking)
        {

            Vector3 guardDir = Vector3.zero;
            if (PlayerAttack.CurrentLockedTarget != null && !PlayerAttack.CurrentLockedTarget.IsDead)
            {
                Vector3 toLocked = PlayerAttack.CurrentLockedTarget.transform.position - transform.position;
                toLocked.y = 0f;
                if (toLocked.sqrMagnitude > 0.01f) guardDir = toLocked.normalized;
            }
            if (guardDir.sqrMagnitude <= 0.001f)
            {
                guardDir = CurrentInputDirection.sqrMagnitude > 0.01f
                    ? CurrentInputDirection
                    : GetCameraForwardFlat();
            }
            if (guardDir.sqrMagnitude > 0.001f)
            {
                RotateVisualTowards(guardDir, rotationSpeed * 1.1f);
            }
        }
        else if (!isAttacking && CurrentInputDirection.sqrMagnitude > 0.001f)
        {

            RotateVisualTowards(CurrentInputDirection, rotationSpeed);
        }

        float dt = Time.deltaTime;
        Vector3 horizontalVelocity = moveDir * moveSpeed + lungeVelocity + knockbackVelocity;
        float horizontalSpeed = horizontalVelocity.magnitude;

        if (controller.isGrounded)
        {
            verticalVelocity = -Mathf.Max(6f, horizontalSpeed * 1.15f);
        }
        else
        {
            verticalVelocity = Mathf.Max(verticalVelocity + gravity * dt, -30f);
        }

        Vector3 finalDisplacement = horizontalVelocity * dt;
        finalDisplacement.y = verticalVelocity * dt;

        controller.Move(finalDisplacement);

        if (controller.isGrounded && !isAttacking && !IsBlocking && HasMovementInput)
        {
            footstepTimer -= dt;
            if (footstepTimer <= 0f)
            {
                footstepTimer = 0.36f;
                GameAudioManager.PlayFootstep();
                bool hasDustGround = TrySampleGroundHeight(transform.position, out float dustGroundY, 1.2f, 4f);
                CombatVfxManager.SpawnFootstepDust(new Vector3(
                    transform.position.x,
                    hasDustGround ? dustGroundY : transform.position.y,
                    transform.position.z));
            }
        }
        else
        {
            footstepTimer = 0.08f;
        }

        if (lungeVelocity.sqrMagnitude > 0.001f)
        {
            lungeVelocity = Vector3.MoveTowards(lungeVelocity, Vector3.zero, 45f * dt);
        }
        else
        {
            lungeVelocity = Vector3.zero;
        }

        if (knockbackVelocity.sqrMagnitude > 0.001f)
        {
            knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDamping * dt);
        }
        else
        {
            knockbackVelocity = Vector3.zero;
        }
    }

    // Xoay model nhân vật về hướng tấn công hoặc di chuyển mục tiêu
    public void RotateVisualTowards(Vector3 worldDirection, float speed, bool instant = false)
    {
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(worldDirection.normalized);
        Transform targetRotTransform = visualModel != null ? visualModel : transform;

        if (instant)
        {
            targetRotTransform.rotation = targetRotation;
        }
        else
        {
            targetRotTransform.rotation = Quaternion.Slerp(
                targetRotTransform.rotation,
                targetRotation,
                speed * Time.deltaTime
            );
        }
    }

    // Lấy hướng mặt hiện tại của model nhân vật
    public Vector3 GetVisualForward()
    {
        Transform t = visualModel != null ? visualModel : transform;
        Vector3 fwd = t.forward;
        fwd.y = 0f;
        return fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
    }

    // Áp dụng xung lực bước tới ngắn khi tung đòn đánh
    public void SetCombatLunge(Vector3 velocity)
    {
        velocity.y = 0f;
        lungeVelocity = velocity;
    }

    // Dừng xung lực lướt khi kết thúc đòn đánh
    public void StopCombatLunge()
    {
        lungeVelocity = Vector3.zero;
    }

    // Áp dụng lực đẩy lùi khi nhân vật trúng đòn
    public void ApplyKnockback(Vector3 attackerPosition, float force)
    {
        if (force <= 0.01f) return;

        Vector3 dir = transform.position - attackerPosition;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = -GetVisualForward();
        }
        else
        {
            dir.Normalize();
        }

        knockbackVelocity = dir * Mathf.Min(force, 6f);
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider == null) return;

        OrcEnemyAI enemy = hit.collider.GetComponentInParent<OrcEnemyAI>();
        if (enemy != null)
        {
            StopCombatLunge();
            enemy.ApplyKnockback(transform.position, bodyCollisionPushForce, false);
        }
    }

    private Vector3 GetCameraForwardFlat()
    {
        if (playerCamera == null && Camera.main != null)
        {
            playerCamera = Camera.main.transform;
        }
        if (playerCamera == null) return GetVisualForward();

        Vector3 fwd = playerCamera.forward;
        fwd.y = 0f;
        return fwd.sqrMagnitude > 0.001f ? fwd.normalized : GetVisualForward();
    }

    private bool TrySampleGroundHeight(Vector3 worldPos, out float groundY, float rayUpOffset, float rayDistance)
    {
        float rayTopY = worldPos.y + rayUpOffset;
        Vector3 rayOrigin = new Vector3(worldPos.x, rayTopY, worldPos.z);
        int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, groundHitBuffer, rayDistance, ~0, QueryTriggerInteraction.Ignore);

        bool foundTerrain = false;
        float terrainY = float.MinValue;
        bool foundWalkable = false;
        float highestWalkableY = float.MinValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundHitBuffer[i];
            Collider col = hit.collider;
            if (col == null || col.transform.IsChildOf(transform)) continue;
            if (col.GetComponentInParent<OrcEnemyAI>() != null || col.GetComponentInParent<EnemyHealth>() != null) continue;

            if (col is TerrainCollider)
            {
                if (!foundTerrain || hit.point.y > terrainY)
                {
                    terrainY = hit.point.y;
                    foundTerrain = true;
                }
                continue;
            }

            if (hit.normal.y >= 0.55f && hit.point.y <= rayTopY - 0.1f)
            {
                if (!foundWalkable || hit.point.y > highestWalkableY)
                {
                    highestWalkableY = hit.point.y;
                    foundWalkable = true;
                }
            }
        }

        if (foundTerrain)
        {
            groundY = terrainY;
            return true;
        }

        if (foundWalkable)
        {
            groundY = highestWalkableY;
            return true;
        }

        if (cachedTerrain == null) cachedTerrain = Terrain.activeTerrain;
        if (cachedTerrain != null)
        {
            groundY = cachedTerrain.SampleHeight(worldPos) + cachedTerrain.transform.position.y;
            return true;
        }

        groundY = worldPos.y;
        return false;
    }

    // Ép nhân vật hạ xuống đúng bề mặt địa hình bằng raycast
    private void SnapToGround()
    {
        if (TrySampleGroundHeight(transform.position, out float groundY, 5f, 30f))
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
            controller.enabled = wasEnabled;
        }
    }

    // Khóa lại con trỏ chuột khi người chơi click vào vùng game (không bấm đè nút UI)
    private void HandleCursorLock()
    {
        if (Input.GetMouseButtonDown(0)
            && Cursor.lockState != CursorLockMode.Locked
            && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // Hiển thị hiệu ứng khiên năng lượng khi giữ chuột phải đỡ đòn
    private void UpdateBlockAuraVisual()
    {
        if (!IsBlocking)
        {
            if (blockAuraObj != null && blockAuraObj.activeSelf)
            {
                blockAuraObj.SetActive(false);
            }
            return;
        }

        if (blockAuraObj == null)
        {
            blockAuraObj = new GameObject("PlayerBlockAuraFX");
            blockAuraObj.transform.SetParent(transform, false);

            GameObject ring = new GameObject("ShieldRing");
            ring.transform.SetParent(blockAuraObj.transform, false);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            MeshFilter mf = ring.AddComponent<MeshFilter>();
            mf.sharedMesh = CombatVfxManager.GetHorizontalQuadMesh();
            MeshRenderer mr = ring.AddComponent<MeshRenderer>();
            blockAuraMat = new Material(CombatVfxManager.GetAdditiveRingMaterial());
            mr.sharedMaterial = blockAuraMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            GameObject lightObj = new GameObject("ShieldLight");
            lightObj.transform.SetParent(blockAuraObj.transform, false);
            blockAuraLight = lightObj.AddComponent<Light>();
            blockAuraLight.type = LightType.Point;
            blockAuraLight.shadows = LightShadows.None;
            blockAuraLight.range = 4.2f;
        }

        if (!blockAuraObj.activeSelf)
        {
            blockAuraObj.SetActive(true);
        }

        Vector3 fwd = GetVisualForward();
        blockAuraObj.transform.position = transform.position + Vector3.up * 1.15f + fwd * 0.62f;
        blockAuraObj.transform.rotation = Quaternion.LookRotation(fwd);

        bool inParry = IsPerfectParryWindow;
        float pulse = 0.94f + 0.08f * Mathf.Sin(Time.time * 12f);
        float size = (inParry ? 1.55f : 1.25f) * pulse;
        blockAuraObj.transform.localScale = Vector3.one * size;

        Color auraColor = inParry
            ? new Color(2.2f, 1.85f, 0.45f, 0.85f)
            : new Color(0.65f, 1.55f, 2.15f, 0.58f);

        if (blockAuraMat != null)
        {
            if (blockAuraMat.HasProperty(BaseColorId)) blockAuraMat.SetColor(BaseColorId, auraColor);
            if (blockAuraMat.HasProperty(ColorId)) blockAuraMat.SetColor(ColorId, auraColor);
        }

        if (blockAuraLight != null)
        {
            bool allowLight = GameSettingsManager.CurrentGraphicsPreset != GraphicsPreset.Low;
            if (blockAuraLight.enabled != allowLight) blockAuraLight.enabled = allowLight;
            if (allowLight)
            {
                blockAuraLight.color = inParry ? new Color(1f, 0.85f, 0.28f) : new Color(0.45f, 0.85f, 1f);
                blockAuraLight.intensity = inParry ? 3.8f : 1.8f;
            }
        }
    }
}
