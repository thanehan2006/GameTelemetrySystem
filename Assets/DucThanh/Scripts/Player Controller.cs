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

    [Header("Map Boundary & Fall Protection")]
    public bool enableMapBoundary = true;
    public float mapEdgeMargin = 2.5f;
    public float fallRecoveryDepth = 15f;
    public float boundaryWarningCooldown = 2.5f;

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
    private bool hasMapBounds;
    private float mapMinX;
    private float mapMaxX;
    private float mapMinZ;
    private float mapMaxZ;
    private float mapMinY;
    private float nextBoundsRefreshTime;
    private float nextBoundaryWarningTime;
    private Vector3 lastSafePosition;
    private bool hasSafePosition;
    private int lastBlockVisualParryState = -1;

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
        RefreshMapBounds();
        SnapToGround();
        lastSafePosition = transform.position;
        hasSafePosition = true;

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
        if (GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen || PlayerHealth.IsPlayerDead)
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
        bool wasInitialized = hasInitializedVisualGround;
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

        if (!wasInitialized || Mathf.Abs(newY - localPos.y) > 0.0005f)
        {
            visualModel.localPosition = new Vector3(0f, newY, 0f);
        }
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
        bool hitBoundary = ClampHorizontalVelocityToMapBounds(ref horizontalVelocity, dt);
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
        EnforceMapBoundsAndFallRecovery(hitBoundary);

        if (controller.isGrounded && !isAttacking && !IsBlocking && HasMovementInput)
        {
            footstepTimer -= dt;
            if (footstepTimer <= 0f)
            {
                footstepTimer = 0.36f;
                GameAudioManager.PlayFootstep();
                CombatVfxManager.SpawnFootstepDust(new Vector3(
                    transform.position.x,
                    cachedHasGround ? cachedGroundY : transform.position.y,
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
        if (hit.collider == null || hit.collider is TerrainCollider) return;

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
            if (col == null) continue;
            if (col is TerrainCollider)
            {
                if (!foundTerrain || hit.point.y > terrainY)
                {
                    terrainY = hit.point.y;
                    foundTerrain = true;
                }
                continue;
            }
            if (col.transform.IsChildOf(transform)) continue;
            if (col.GetComponentInParent<OrcEnemyAI>() != null || col.GetComponentInParent<EnemyHealth>() != null) continue;

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
        if (cachedTerrain != null && cachedTerrain.terrainData != null)
        {
            Vector3 tPos = cachedTerrain.transform.position;
            Vector3 tSize = cachedTerrain.terrainData.size;
            if (worldPos.x >= tPos.x && worldPos.x <= tPos.x + tSize.x
                && worldPos.z >= tPos.z && worldPos.z <= tPos.z + tSize.z)
            {
                groundY = cachedTerrain.SampleHeight(worldPos) + tPos.y;
                return true;
            }
        }

        groundY = worldPos.y;
        return false;
    }

    // Ép nhân vật hạ xuống đúng bề mặt địa hình bằng raycast
    private void SnapToGround()
    {
        if (TrySampleGroundHeight(transform.position, out float groundY, 5f, 30f))
        {
            TeleportControllerPosition(new Vector3(transform.position.x, groundY, transform.position.z));
        }
    }

    // Tính vùng giới hạn bản đồ từ toàn bộ Terrain đang hoạt động và thu hẹp theo lề an toàn
    private void RefreshMapBounds()
    {
        if (!enableMapBoundary)
        {
            hasMapBounds = false;
            return;
        }

        bool found = false;
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        float minY = float.MaxValue;

        Terrain[] terrains = Terrain.activeTerrains;
        if (terrains != null)
        {
            for (int i = 0; i < terrains.Length; i++)
            {
                AccumulateTerrainBounds(terrains[i], ref found, ref minX, ref maxX, ref minZ, ref maxZ, ref minY);
            }
        }

        if (!found)
        {
            AccumulateTerrainBounds(Terrain.activeTerrain, ref found, ref minX, ref maxX, ref minZ, ref maxZ, ref minY);
        }

        if (!found)
        {
            hasMapBounds = false;
            return;
        }

        float margin = Mathf.Max(0f, mapEdgeMargin);
        float halfSpanX = (maxX - minX) * 0.5f;
        float halfSpanZ = (maxZ - minZ) * 0.5f;
        float insetX = Mathf.Min(margin, halfSpanX * 0.9f);
        float insetZ = Mathf.Min(margin, halfSpanZ * 0.9f);

        mapMinX = minX + insetX;
        mapMaxX = maxX - insetX;
        mapMinZ = minZ + insetZ;
        mapMaxZ = maxZ - insetZ;
        mapMinY = minY;
        hasMapBounds = true;
    }

    private static void AccumulateTerrainBounds(
        Terrain terrain,
        ref bool found,
        ref float minX,
        ref float maxX,
        ref float minZ,
        ref float maxZ,
        ref float minY)
    {
        if (terrain == null || !terrain.isActiveAndEnabled || terrain.terrainData == null) return;

        Vector3 tPos = terrain.transform.position;
        Vector3 tSize = terrain.terrainData.size;
        minX = Mathf.Min(minX, tPos.x);
        maxX = Mathf.Max(maxX, tPos.x + tSize.x);
        minZ = Mathf.Min(minZ, tPos.z);
        maxZ = Mathf.Max(maxZ, tPos.z + tSize.z);
        minY = Mathf.Min(minY, tPos.y);
        found = true;
    }

    // Chặn vận tốc hướng ra ngoài ranh giới để nhân vật trượt mượt dọc theo mép bản đồ
    private bool ClampHorizontalVelocityToMapBounds(ref Vector3 horizontalVelocity, float dt)
    {
        if (!enableMapBoundary) return false;

        if (!hasMapBounds)
        {
            if (Time.time >= nextBoundsRefreshTime)
            {
                nextBoundsRefreshTime = Time.time + 1f;
                RefreshMapBounds();
            }
            if (!hasMapBounds) return false;
        }

        if (dt <= 0f) return false;

        Vector3 pos = transform.position;
        bool hitX = ClampAxisToBounds(pos.x, mapMinX, mapMaxX, dt, ref horizontalVelocity.x, ref knockbackVelocity.x, ref lungeVelocity.x);
        bool hitZ = ClampAxisToBounds(pos.z, mapMinZ, mapMaxZ, dt, ref horizontalVelocity.z, ref knockbackVelocity.z, ref lungeVelocity.z);
        return hitX || hitZ;
    }

    private static bool ClampAxisToBounds(float pos, float minBound, float maxBound, float dt, ref float vel, ref float knockback, ref float lunge)
    {
        float next = pos + vel * dt;
        if (next < minBound && vel < 0f)
        {
            vel = pos > minBound ? (minBound - pos) / dt : 0f;
            if (knockback < 0f) knockback = 0f;
            if (lunge < 0f) lunge = 0f;
            return true;
        }
        if (next > maxBound && vel > 0f)
        {
            vel = pos < maxBound ? (maxBound - pos) / dt : 0f;
            if (knockback > 0f) knockback = 0f;
            if (lunge > 0f) lunge = 0f;
            return true;
        }
        return false;
    }

    // Giữ nhân vật trong ranh giới sau khi di chuyển và đưa về vị trí an toàn nếu bị rơi khỏi map
    private void EnforceMapBoundsAndFallRecovery(bool hitBoundary)
    {
        if (!enableMapBoundary) return;

        Vector3 pos = transform.position;
        if (hasMapBounds)
        {
            float clampedX = Mathf.Clamp(pos.x, mapMinX, mapMaxX);
            float clampedZ = Mathf.Clamp(pos.z, mapMinZ, mapMaxZ);
            if (!Mathf.Approximately(pos.x, clampedX) || !Mathf.Approximately(pos.z, clampedZ))
            {
                pos.x = clampedX;
                pos.z = clampedZ;
                TeleportControllerPosition(pos);
                hitBoundary = true;
            }
        }

        if (hitBoundary)
        {
            ShowBoundaryBanner("ĐÃ ĐẾN RANH GIỚI BẢN ĐỒ!", 1.4f);
        }

        if (controller.isGrounded && IsInsideMapBounds(pos))
        {
            lastSafePosition = pos;
            hasSafePosition = true;
        }

        if (!hasSafePosition) return;

        float safeRefY = hasMapBounds ? mapMinY : lastSafePosition.y;
        float fallThresholdY = safeRefY - Mathf.Max(2f, fallRecoveryDepth);
        if (pos.y < fallThresholdY)
        {
            Vector3 recoveryPos = lastSafePosition + Vector3.up * 0.25f;
            if (hasMapBounds)
            {
                recoveryPos.x = Mathf.Clamp(recoveryPos.x, mapMinX, mapMaxX);
                recoveryPos.z = Mathf.Clamp(recoveryPos.z, mapMinZ, mapMaxZ);
            }

            verticalVelocity = 0f;
            knockbackVelocity = Vector3.zero;
            lungeVelocity = Vector3.zero;
            TeleportControllerPosition(recoveryPos);
            SnapToGround();
            lastSafePosition = transform.position;
            ShowBoundaryBanner("ĐÃ QUAY LẠI KHU VỰC CHIẾN ĐẤU!", 1.6f, force: true);
        }
    }

    private void ShowBoundaryBanner(string message, float duration, bool force = false)
    {
        if (!force && Time.time < nextBoundaryWarningTime) return;
        nextBoundaryWarningTime = Time.time + Mathf.Max(0.5f, boundaryWarningCooldown);
        if (StageManager.Instance != null)
        {
            StageManager.Instance.ShowBanner(message, new Color(1f, 0.76f, 0.28f), duration);
        }
    }

    private bool IsInsideMapBounds(Vector3 pos, float inset = 0f)
    {
        if (!hasMapBounds) return true;
        float safeInsetX = Mathf.Min(Mathf.Max(0f, inset), (mapMaxX - mapMinX) * 0.45f);
        float safeInsetZ = Mathf.Min(Mathf.Max(0f, inset), (mapMaxZ - mapMinZ) * 0.45f);
        return pos.x >= mapMinX + safeInsetX && pos.x <= mapMaxX - safeInsetX
            && pos.z >= mapMinZ + safeInsetZ && pos.z <= mapMaxZ - safeInsetZ;
    }

    private void TeleportControllerPosition(Vector3 targetPos)
    {
        if (controller != null)
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.position = targetPos;
            controller.enabled = wasEnabled;
        }
        else
        {
            transform.position = targetPos;
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
            lastBlockVisualParryState = -1;
            if (blockAuraObj != null && blockAuraObj.activeSelf)
            {
                blockAuraObj.SetActive(false);
            }
            return;
        }

        if (blockAuraObj == null)
        {
            lastBlockVisualParryState = -1;
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
        int parryState = inParry ? 1 : 0;
        float pulse = 0.94f + 0.08f * Mathf.Sin(Time.time * 12f);
        float size = (inParry ? 1.55f : 1.25f) * pulse;
        blockAuraObj.transform.localScale = Vector3.one * size;

        bool parryChanged = parryState != lastBlockVisualParryState;
        if (parryChanged && blockAuraMat != null)
        {
            Color auraColor = inParry
                ? new Color(2.2f, 1.85f, 0.45f, 0.85f)
                : new Color(0.65f, 1.55f, 2.15f, 0.58f);
            if (blockAuraMat.HasProperty(BaseColorId)) blockAuraMat.SetColor(BaseColorId, auraColor);
            if (blockAuraMat.HasProperty(ColorId)) blockAuraMat.SetColor(ColorId, auraColor);
        }

        if (blockAuraLight != null)
        {
            bool wasLightEnabled = blockAuraLight.enabled;
            bool allowLight = GameSettingsManager.CurrentGraphicsPreset != GraphicsPreset.Low;
            if (wasLightEnabled != allowLight) blockAuraLight.enabled = allowLight;
            if (allowLight && (parryChanged || !wasLightEnabled))
            {
                blockAuraLight.color = inParry ? new Color(1f, 0.85f, 0.28f) : new Color(0.45f, 0.85f, 1f);
                blockAuraLight.intensity = inParry ? 3.8f : 1.8f;
            }
        }

        lastBlockVisualParryState = parryState;
    }
}
