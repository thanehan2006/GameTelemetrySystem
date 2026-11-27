using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-10)]
// Xử lý combo tấn công cận chiến, tự xoay nhắm mục tiêu, khóa mục tiêu và phát hiện trúng đòn
public class PlayerAttack : MonoBehaviour
{
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int CancelAttackHash = Animator.StringToHash("CancelAttack");
    private static readonly int RunHash = Animator.StringToHash("Run");
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static readonly int BlockHash = Animator.StringToHash("Block");
    private static readonly int[] AttackStateHashes =
    {
        Animator.StringToHash("Atk1"),
        Animator.StringToHash("Atk2"),
        Animator.StringToHash("Atk3")
    };
    private static Gradient[] cachedTrailGradients;

    [Header("Attack Settings")]
    public int attackDamage = 30;
    public float knockbackForce = 7.5f;

    public float attackAnimSpeed = 1.3f;
    public float attackCooldown = 0.18f;

    [Header("Auto-Target & Turn Speed")]
    public float autoTargetRadius = 4.0f;

    public float attackTurnSpeed = 7.0f;
    public float lungeSpeed = 2.5f;
    public float lungeDuration = 0.12f;
    public float lungeStopDistance = 1.3f;

    [Header("Target Lock-On Settings")]
    public float lockOnRadius = 18.0f;
    public float maxLockKeepDistance = 22.0f;

    [Header("Hitbox Settings")]
    public float hitboxStartDelay = 0.08f;
    [SerializeField] private LayerMask enemyLayers = ~0;
    [Tooltip("NormalizedTime thresholds in Atk1 (3-hit combo) where hit tracking resets for swings 2 and 3")]
    public float[] atk1SwingResetTimes = { 0.34f, 0.56f };

    [Header("References")]
    public Transform visualModel;
    public BoxCollider weaponCollider;

    public bool IsAttacking { get; private set; }
    public static EnemyHealth CurrentLockedTarget { get; private set; }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private Animator animator;
    private PlayerController playerController;
    private PlayerAnimationController animController;
    private Transform currentTargetEnemy;
    private EnemyHealth currentTargetHealth;
    private Vector3 attackFacingDir = Vector3.forward;

    private readonly List<EnemyHealth> lockCandidates = new List<EnemyHealth>(16);
    private GameObject lockIndicatorRoot;
    private Transform lockGroundRingT;
    private Transform lockReticleRootT;
    private Transform lockOuterDiamondT;
    private Transform lockInnerDiamondT;
    private MaterialPropertyBlock lockPropBlock;
    private Camera cachedMainCamera;
    private EnemyHealth cachedLockTarget;
    private CapsuleCollider cachedLockTargetCollider;
    private Vector3 lockSortLookDir;
    private System.Comparison<EnemyHealth> lockCandidateComparer;

    private readonly List<int> availableAttackIndices = new List<int>(3);
    private int currentAttackStateHash = AttackStateHashes[0];
    private int currentAttackVariant = 0;
    private int lastAttackVariant = -1;
    private TrailRenderer weaponTrail;
    private WeaponElementalVfx activeWeaponVfx;

    private float nextAttackTime;
    private float attackStartTime;
    private int currentSwingPhase;
    private float swingStartTime;
    private float swingLungeStartTime = -999f;
    private bool queuedNextAttack;
    private bool wasCursorLocked;
    private readonly HashSet<EnemyHealth> hitEnemiesInSwing = new HashSet<EnemyHealth>();
    private readonly Collider[] hitBuffer = new Collider[96];

    // Lấy tham chiếu điều khiển nhân vật và lưu trữ hitbox vũ khí
    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        playerController = GetComponent<PlayerController>();
        animController = GetComponent<PlayerAnimationController>();
        lockCandidateComparer = CompareLockCandidates;

        if (visualModel == null)
        {
            Transform body = transform.Find("PlayerBody");
            if (body != null) visualModel = body;
        }

        ResolveWeaponCollider();
        EnsureWeaponTrail();
    }

    // Nạp danh sách animation đánh, khởi tạo vệt chém và vòng ngắm khóa mục tiêu
    void Start()
    {
        attackAnimSpeed = Mathf.Clamp(attackAnimSpeed, 1.0f, 1.5f);
        attackTurnSpeed = Mathf.Clamp(attackTurnSpeed, 4.0f, 12.0f);
        if (attackCooldown > 0.4f) attackCooldown = 0.15f;

        wasCursorLocked = Cursor.lockState == CursorLockMode.Locked;
        CacheAvailableAttackStates();
        ResolveWeaponCollider();
        EnsureWeaponTrail();
    }

    // Xử lý input chuột trái đánh, hủy đòn để đỡ đòn và điều khiển khóa mục tiêu
    void Update()
    {
        if (GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen || PlayerHealth.IsPlayerDead)
        {
            StopAttackState();
            wasCursorLocked = false;
            return;
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null) return;
        }

        bool isCursorLocked = Cursor.lockState == CursorLockMode.Locked;
        bool attackDown = isCursorLocked && wasCursorLocked && Input.GetMouseButtonDown(0);
        bool attackHeld = isCursorLocked && wasCursorLocked && Input.GetMouseButton(0);

        HandleLockOnInput();

        bool isBlocking = playerController != null && playerController.IsBlocking;
        if (isBlocking)
        {
            if (IsAttacking) CancelAttackForBlock();
            wasCursorLocked = isCursorLocked;
            return;
        }

        if (IsAttacking)
        {
            float elapsed = Time.time - attackStartTime;
            float lungeElapsed = Time.time - swingLungeStartTime;
            animator.speed = attackAnimSpeed;

            UpdateSmoothAttackRotation(Time.time - swingStartTime);

            if (lungeElapsed >= 0f && lungeElapsed <= lungeDuration)
            {
                UpdateCombatLunge(lungeElapsed);
            }
            else if (playerController != null)
            {
                playerController.StopCombatLunge();
            }

            if (attackDown && elapsed > 0.20f)
            {
                queuedNextAttack = true;
            }

            if (IsAttackAnimationFinished(elapsed))
            {
                if (queuedNextAttack || attackHeld)
                {
                    PerformAttack();
                }
                else
                {
                    EndAttackNormally();
                }
            }
        }
        else if (attackDown && Time.time >= nextAttackTime)
        {
            PerformAttack();
        }

        wasCursorLocked = isCursorLocked;
    }

    // Cập nhật vị trí và hiệu ứng xoay vòng ngắm trên quái đang khóa
    void LateUpdate()
    {
        UpdateLockOnVisualIndicator();

        if (!IsAttacking || GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen || PlayerHealth.IsPlayerDead) return;

        float elapsed = Time.time - attackStartTime;
        if (elapsed < hitboxStartDelay) return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash == currentAttackStateHash && !animator.IsInTransition(0))
        {
            if (state.normalizedTime > 0.90f) return;
            UpdateMultiHitSwingPhase(state.normalizedTime);
        }

        CheckWeaponHit();
    }

    private void OnDisable()
    {
        ClearLockedTarget();
    }

    private void OnDestroy()
    {
        ClearLockedTarget();
        if (lockIndicatorRoot != null)
        {
            Destroy(lockIndicatorRoot);
        }
    }

    // Kích hoạt đòn đánh tiếp theo trong chuỗi combo và phát VFX vệt chém
    private void PerformAttack()
    {
        if (availableAttackIndices.Count == 0)
        {
            CacheAvailableAttackStates();
        }

        currentAttackVariant = PickRandomAttackVariant();
        currentAttackStateHash = AttackStateHashes[currentAttackVariant];

        IsAttacking = true;
        queuedNextAttack = false;
        attackStartTime = Time.time;
        currentSwingPhase = 0;
        swingStartTime = attackStartTime;
        swingLungeStartTime = attackStartTime;
        hitEnemiesInSwing.Clear();

        ResolveAttackDirectionAndTarget();

        animator.speed = attackAnimSpeed;
        animator.ResetTrigger(CancelAttackHash);
        animator.ResetTrigger(AttackHash);

        animator.CrossFadeInFixedTime(currentAttackStateHash, 0.06f, 0, 0f);
        UpdateWeaponTrailTheme(currentAttackVariant);
        SetWeaponTrailEmitting(true);
        TriggerSwingPresentation(currentAttackVariant);
    }

    // Phát hiệu ứng vệt chém và âm thanh vung vũ khí cho từng nhịp chém
    private void TriggerSwingPresentation(int slashVariant)
    {
        if (activeWeaponVfx != null)
        {
            activeWeaponVfx.SetAttackSurge(true);
            WeaponVfxProfile p = activeWeaponVfx.Profile;
            CombatVfxManager.SpawnSlashArc(
                transform.position,
                attackFacingDir,
                slashVariant,
                p.primaryColor,
                p.coreColor,
                p.secondaryColor,
                p.arcScaleMult);
        }
        else
        {
            CombatVfxManager.SpawnSlashArc(transform.position, attackFacingDir, slashVariant);
        }
        GameAudioManager.PlaySwordSwing();
    }

    // Đặt lại danh sách quái đã trúng khi Atk1 chuyển sang các nhịp chém thứ 2 và thứ 3
    private void UpdateMultiHitSwingPhase(float normalizedTime)
    {
        if (currentAttackVariant != 0 || atk1SwingResetTimes == null || atk1SwingResetTimes.Length == 0)
        {
            return;
        }

        int targetPhase = 0;
        for (int i = 0; i < atk1SwingResetTimes.Length; i++)
        {
            if (normalizedTime >= atk1SwingResetTimes[i])
            {
                targetPhase++;
            }
        }

        if (targetPhase > currentSwingPhase)
        {
            currentSwingPhase = targetPhase;
            hitEnemiesInSwing.Clear();
            swingStartTime = Time.time;
            swingLungeStartTime = Time.time;

            if (currentTargetEnemy == null || currentTargetHealth == null || currentTargetHealth.IsDead)
            {
                ResolveAttackDirectionAndTarget();
            }

            TriggerSwingPresentation(currentSwingPhase % 2);
        }
    }

    private void CacheAvailableAttackStates()
    {
        availableAttackIndices.Clear();
        if (animator == null) return;

        for (int i = 0; i < AttackStateHashes.Length; i++)
        {
            if (animator.HasState(0, AttackStateHashes[i]))
            {
                availableAttackIndices.Add(i);
            }
        }

        if (availableAttackIndices.Count == 0)
        {
            availableAttackIndices.Add(0);
        }
    }

    private int PickRandomAttackVariant()
    {
        if (availableAttackIndices.Count <= 1)
        {
            lastAttackVariant = availableAttackIndices[0];
            return lastAttackVariant;
        }

        int pick = availableAttackIndices[Random.Range(0, availableAttackIndices.Count)];
        if (pick == lastAttackVariant)
        {
            int idx = availableAttackIndices.IndexOf(pick);
            pick = availableAttackIndices[(idx + Random.Range(1, availableAttackIndices.Count)) % availableAttackIndices.Count];
        }

        lastAttackVariant = pick;
        return pick;
    }

    // Xoay mượt nhân vật về phía quái ở pha đầu của đòn chém
    private void UpdateSmoothAttackRotation(float elapsed)
    {
        if (playerController == null) return;

        if (currentTargetEnemy != null && currentTargetHealth != null && !currentTargetHealth.IsDead)
        {
            Vector3 toEnemy = currentTargetEnemy.position - transform.position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude > 0.01f)
            {
                attackFacingDir = toEnemy.normalized;
            }
        }
        else
        {
            currentTargetEnemy = null;
            currentTargetHealth = null;

            if (elapsed < 0.35f && playerController.HasMovementInput && playerController.CurrentInputDirection.sqrMagnitude > 0.01f)
            {
                attackFacingDir = playerController.CurrentInputDirection;
            }
        }

        playerController.RotateVisualTowards(attackFacingDir, attackTurnSpeed, false);
    }

    // Xác định mục tiêu ưu tiên gần nhất theo hướng nhìn hoặc đang khóa
    private void ResolveAttackDirectionAndTarget()
    {
        if (CurrentLockedTarget != null && !CurrentLockedTarget.IsDead)
        {
            Vector3 toLocked = CurrentLockedTarget.transform.position - transform.position;
            toLocked.y = 0f;
            if (toLocked.sqrMagnitude <= lockOnRadius * lockOnRadius && toLocked.sqrMagnitude > 0.001f)
            {
                currentTargetHealth = CurrentLockedTarget;
                currentTargetEnemy = CurrentLockedTarget.transform;
                attackFacingDir = toLocked.normalized;
                return;
            }
        }

        currentTargetHealth = FindBestTargetEnemy();
        currentTargetEnemy = currentTargetHealth != null ? currentTargetHealth.transform : null;
        if (currentTargetEnemy != null)
        {
            Vector3 toTarget = currentTargetEnemy.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.001f)
            {
                attackFacingDir = toTarget.normalized;
                return;
            }
        }

        if (playerController != null && playerController.HasMovementInput && playerController.CurrentInputDirection.sqrMagnitude > 0.01f)
        {
            attackFacingDir = playerController.CurrentInputDirection;
            return;
        }

        if (playerController != null)
        {
            attackFacingDir = playerController.GetVisualForward();
        }
        else
        {
            attackFacingDir = transform.forward;
        }
    }

    private EnemyHealth FindBestTargetEnemy()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, autoTargetRadius, hitBuffer, enemyLayers, QueryTriggerInteraction.Collide);
        EnemyHealth bestTarget = null;
        float bestScore = float.MaxValue;

        Vector3 preferredDir = (playerController != null && playerController.HasMovementInput)
            ? playerController.CurrentInputDirection
            : (playerController != null ? playerController.GetVisualForward() : transform.forward);

        for (int i = 0; i < count; i++)
        {
            Collider c = hitBuffer[i];
            if (c == null || c is TerrainCollider || c.transform == transform || c.transform.IsChildOf(transform)) continue;

            if (!EnemyHealth.TryGetFromCollider(c, out EnemyHealth eh) || eh.IsDead) continue;

            Vector3 toEnemy = eh.transform.position - transform.position;
            toEnemy.y = 0f;
            float sqrDist = toEnemy.sqrMagnitude;
            if (sqrDist < 0.0025f || sqrDist > autoTargetRadius * autoTargetRadius) continue;

            float dist = Mathf.Sqrt(sqrDist);
            float anglePenalty = 1f - Vector3.Dot(preferredDir, toEnemy / dist);
            float score = dist + anglePenalty * 0.85f;
            if (score < bestScore)
            {
                bestScore = score;
                bestTarget = eh;
            }
        }

        return bestTarget;
    }

    // Tạo bước đệm tiến tới ngắn giúp cảm giác đánh chắc tay
    private void UpdateCombatLunge(float elapsed)
    {
        if (playerController == null || elapsed > lungeDuration)
        {
            if (playerController != null) playerController.StopCombatLunge();
            return;
        }

        if (currentTargetEnemy != null)
        {
            Vector3 toEnemy = currentTargetEnemy.position - transform.position;
            toEnemy.y = 0f;
            float dist = toEnemy.magnitude;

            if (dist <= lungeStopDistance)
            {
                playerController.StopCombatLunge();
                return;
            }

            playerController.SetCombatLunge(attackFacingDir * lungeSpeed);
        }
        else
        {
            playerController.StopCombatLunge();
        }
    }

    private bool IsAttackAnimationFinished(float elapsed)
    {

        if (elapsed <= 0.25f) return false;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (next.shortNameHash == currentAttackStateHash) return false;
        }

        if (state.shortNameHash == currentAttackStateHash)
        {
            return state.normalizedTime >= 0.92f;
        }

        return elapsed > 0.45f;
    }

    private void EndAttackNormally()
    {
        bool wantsMove = playerController != null ? playerController.HasMovementInput : HasMoveInputFallback();
        nextAttackTime = Time.time + attackCooldown;
        StopAttackState();
        animator.ResetTrigger(AttackHash);

        if (animController != null)
        {
            animController.ForceReturnToLocomotion();
        }
        else
        {
            animator.CrossFadeInFixedTime(wantsMove ? RunHash : IdleHash, 0.08f, 0);
        }
    }

    // Ngắt ngay đòn đánh dở để ưu tiên thao tác đỡ đòn của người chơi
    public void CancelAttackForBlock()
    {
        if (!IsAttacking) return;
        StopAttackState();
        if (animator != null)
        {
            animator.ResetTrigger(AttackHash);
            animator.SetTrigger(CancelAttackHash);
            if (animController != null)
            {
                animController.ForceReturnToLocomotion();
            }
            else if (animator.HasState(0, BlockHash))
            {
                animator.CrossFadeInFixedTime(BlockHash, 0.06f, 0);
            }
        }
    }

    private void StopAttackState()
    {
        IsAttacking = false;
        queuedNextAttack = false;
        currentSwingPhase = 0;
        swingLungeStartTime = -999f;
        hitEnemiesInSwing.Clear();
        SetWeaponTrailEmitting(false);
        if (activeWeaponVfx != null) activeWeaponVfx.SetAttackSurge(false);

        if (animator != null)
        {
            animator.speed = 1f;
        }

        if (playerController != null)
        {
            playerController.StopCombatLunge();
        }
    }

    // Quét collider kẻ địch trong vùng chém để tính sát thương và đẩy lùi
    private void CheckWeaponHit()
    {
        if (weaponCollider == null)
        {
            ResolveWeaponCollider();
        }

        if (weaponCollider != null)
        {
            Transform wt = weaponCollider.transform;
            Vector3 worldCenter = wt.TransformPoint(weaponCollider.center);
            Vector3 lossy = wt.lossyScale;
            Vector3 halfExtents = Vector3.Scale(
                weaponCollider.size * 0.5f,
                new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z))
            );

            int hitCount = Physics.OverlapBoxNonAlloc(worldCenter, halfExtents, hitBuffer, wt.rotation, enemyLayers, QueryTriggerInteraction.Collide);
            ProcessHitColliders(hitBuffer, hitCount, worldCenter);
        }

        if (currentAttackVariant == 2)
        {
            if ((Time.time - attackStartTime) >= 0.14f)
            {
                Vector3 spinCenter = transform.position + Vector3.up * 0.85f;
                int spinCount = Physics.OverlapSphereNonAlloc(spinCenter, 2.65f, hitBuffer, enemyLayers, QueryTriggerInteraction.Collide);
                ProcessHitColliders(hitBuffer, spinCount, spinCenter);
            }
        }
        else if ((Time.time - swingStartTime) >= 0.06f)
        {
            Vector3 forward = attackFacingDir;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = playerController != null ? playerController.GetVisualForward() : transform.forward;
                forward.y = 0f;
            }
            if (forward.sqrMagnitude < 0.001f) forward = transform.forward;
            forward.Normalize();

            Vector3 meleeCenter = transform.position + Vector3.up * 0.95f + forward * 0.95f;
            int arcCount = Physics.OverlapSphereNonAlloc(meleeCenter, 1.45f, hitBuffer, enemyLayers, QueryTriggerInteraction.Collide);
            ProcessForwardArcColliders(hitBuffer, arcCount, meleeCenter, forward);
        }
    }

    private void ProcessHitColliders(Collider[] hits, int count, Vector3 originCenter)
    {
        for (int i = 0; i < count; i++)
        {
            Collider col = hits[i];
            if (col == null || col is TerrainCollider || col.transform == transform || col.transform.IsChildOf(transform)) continue;

            if (EnemyHealth.TryGetFromCollider(col, out EnemyHealth enemy) && !enemy.IsDead && hitEnemiesInSwing.Add(enemy))
            {
                ApplyHitToEnemy(enemy, col, originCenter);
            }
        }
    }

    private void ProcessForwardArcColliders(Collider[] hits, int count, Vector3 originCenter, Vector3 forward)
    {
        for (int i = 0; i < count; i++)
        {
            Collider col = hits[i];
            if (col == null || col is TerrainCollider || col.transform == transform || col.transform.IsChildOf(transform)) continue;

            if (!EnemyHealth.TryGetFromCollider(col, out EnemyHealth enemy) || enemy.IsDead) continue;

            Vector3 toEnemy = enemy.transform.position - transform.position;
            if (toEnemy.y < -1.4f || toEnemy.y > 2.4f) continue;

            toEnemy.y = 0f;
            float sqrDist = toEnemy.sqrMagnitude;
            if (sqrDist > 2.45f * 2.45f) continue;

            if (sqrDist > 1.25f * 1.25f)
            {
                float dist = Mathf.Sqrt(sqrDist);
                if (Vector3.Dot(forward, toEnemy / dist) < 0.25f) continue;
            }

            if (hitEnemiesInSwing.Add(enemy))
            {
                ApplyHitToEnemy(enemy, col, originCenter);
            }
        }
    }

    private void ApplyHitToEnemy(EnemyHealth enemy, Collider col, Vector3 originCenter)
    {
        if (playerController != null)
        {
            playerController.StopCombatLunge();
            swingLungeStartTime = -999f;
        }

        OrcEnemyAI orcAI = enemy.CachedEnemyAI;
        if (orcAI != null)
        {
            float kb = currentAttackVariant == 2 ? knockbackForce * 1.25f : knockbackForce;
            orcAI.ApplyKnockback(transform.position, kb, true);
        }

        int dealtDamage = currentAttackVariant == 2 ? Mathf.RoundToInt(attackDamage * 1.15f) : attackDamage;
        enemy.TakeDamage(dealtDamage);

        Vector3 hitPoint = col.ClosestPoint(originCenter);
        if ((hitPoint - enemy.transform.position).sqrMagnitude < 0.01f)
        {
            hitPoint = enemy.transform.position + Vector3.up * 1.1f;
        }
        Vector3 hitDir = enemy.transform.position - transform.position;
        bool heavyHit = enemy.isBoss || dealtDamage >= 55 || currentAttackVariant == 2;
        CombatVfxManager.SpawnBloodHit(hitPoint, hitDir, heavyHit);
        Color dmgColor = heavyHit ? new Color(1f, 0.80f, 0.22f) : Color.white;
        if (activeWeaponVfx != null)
        {
            WeaponVfxProfile wp = activeWeaponVfx.Profile;
            CombatVfxManager.SpawnElementalHitBurst(
                hitPoint,
                hitDir,
                wp.primaryColor,
                wp.coreColor,
                wp.secondaryColor,
                wp.tier,
                heavyHit);
            if (wp.tier >= 3 && heavyHit)
            {
                dmgColor = Color.Lerp(wp.primaryColor, wp.coreColor, 0.42f);
            }
        }
        CombatVfxManager.SpawnFloatingText(
            hitPoint + Vector3.up * 0.55f,
            heavyHit ? $"{dealtDamage}!" : $"{dealtDamage}",
            dmgColor,
            heavyHit ? 1.12f : 0.90f,
            0.82f);
        CombatVfxManager.TriggerHitStop(heavyHit ? 0.036f : 0.016f, heavyHit ? 0.10f : 0.18f);
        GameAudioManager.PlaySwordHit(heavyHit);

        if (ThirdPersonCamera.Instance != null)
        {
            ThirdPersonCamera.Instance.AddCameraShake(enemy.isBoss || currentAttackVariant == 2 ? 0.14f : 0.075f, 0.085f);
            if (heavyHit) ThirdPersonCamera.Instance.AddFovPunch(2.4f);
        }
    }

    // Cập nhật sát thương cộng thêm và vệt vũ khí khi đổi vũ khí mới
    public void RefreshWeapon(int bonusDamage = 0)
    {
        if (bonusDamage != 0)
        {
            attackDamage = Mathf.Max(1, attackDamage + bonusDamage);
        }

        weaponCollider = null;
        weaponTrail = null;
        activeWeaponVfx = null;
        hitEnemiesInSwing.Clear();
        ResolveWeaponCollider();
        EnsureWeaponTrail();
    }

    private void EnsureWeaponTrail()
    {
        if (weaponCollider == null) return;

        float topY = weaponCollider.center.y + weaponCollider.size.y * 0.5f;
        float botY = weaponCollider.center.y - weaponCollider.size.y * 0.5f;
        float tipOffsetY = Mathf.Abs(botY) > Mathf.Abs(topY) * 1.4f
            ? weaponCollider.center.y - weaponCollider.size.y * 0.42f
            : weaponCollider.center.y + weaponCollider.size.y * 0.42f;
        Vector3 tipLocalPos = new Vector3(weaponCollider.center.x, tipOffsetY, weaponCollider.center.z);
        float desiredTrailWidth = activeWeaponVfx != null ? activeWeaponVfx.Profile.trailWidth : 0.30f;

        Transform existing = weaponCollider.transform.Find("WeaponSlashTrail");
        if (existing != null)
        {
            existing.localPosition = tipLocalPos;
            weaponTrail = existing.GetComponent<TrailRenderer>();
            if (weaponTrail != null)
            {
                weaponTrail.startWidth = desiredTrailWidth;
                weaponTrail.emitting = false;
                UpdateWeaponTrailTheme(currentAttackVariant);
            }
            return;
        }

        GameObject trailObj = new GameObject("WeaponSlashTrail");
        trailObj.transform.SetParent(weaponCollider.transform, false);
        trailObj.transform.localPosition = tipLocalPos;

        weaponTrail = trailObj.AddComponent<TrailRenderer>();
        weaponTrail.time = 0.19f;
        weaponTrail.minVertexDistance = 0.04f;
        weaponTrail.startWidth = desiredTrailWidth;
        weaponTrail.endWidth = 0.02f;
        weaponTrail.sharedMaterial = CombatVfxManager.GetWeaponTrailMaterial();
        weaponTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        weaponTrail.receiveShadows = false;

        UpdateWeaponTrailTheme(currentAttackVariant);
        weaponTrail.emitting = false;
    }

    private void UpdateWeaponTrailTheme(int attackVariant)
    {
        if (weaponTrail == null) EnsureWeaponTrail();
        if (weaponTrail == null) return;

        if (activeWeaponVfx != null)
        {
            weaponTrail.colorGradient = activeWeaponVfx.BuildTrailGradient(attackVariant);
            return;
        }

        EnsureTrailGradients();
        int idx = Mathf.Clamp(attackVariant, 0, cachedTrailGradients.Length - 1);
        weaponTrail.colorGradient = cachedTrailGradients[idx];
    }

    private static void EnsureTrailGradients()
    {
        if (cachedTrailGradients != null) return;

        Gradient g0 = new Gradient();
        g0.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.95f, 0.98f, 1f), 0f),
                new GradientColorKey(new Color(1f, 0.86f, 0.45f), 0.55f),
                new GradientColorKey(new Color(0.95f, 0.52f, 0.16f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.90f, 0f),
                new GradientAlphaKey(0.48f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });

        Gradient g1 = new Gradient();
        g1.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.96f, 0.72f), 0f),
                new GradientColorKey(new Color(1f, 0.68f, 0.20f), 0.52f),
                new GradientColorKey(new Color(0.92f, 0.28f, 0.08f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.92f, 0f),
                new GradientAlphaKey(0.50f, 0.52f),
                new GradientAlphaKey(0f, 1f)
            });

        Gradient g2 = new Gradient();
        g2.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.92f, 0.55f), 0f),
                new GradientColorKey(new Color(1f, 0.42f, 0.12f), 0.5f),
                new GradientColorKey(new Color(0.95f, 0.12f, 0.06f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.95f, 0f),
                new GradientAlphaKey(0.55f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });

        cachedTrailGradients = new[] { g0, g1, g2 };
    }

    private void SetWeaponTrailEmitting(bool active)
    {
        if (weaponTrail == null) EnsureWeaponTrail();
        if (weaponTrail != null)
        {
            weaponTrail.emitting = active;
        }
    }

    private void ResolveWeaponCollider()
    {
        if (weaponCollider != null)
        {
            weaponCollider.isTrigger = true;
            return;
        }

        Transform hand = FindHandBone();
        if (hand == null) return;

        for (int i = 0; i < hand.childCount; i++)
        {
            Transform child = hand.GetChild(i);
            if (child.name.StartsWith("mixamorig:")) continue;

            if (child.localPosition.sqrMagnitude < 0.0001f && Quaternion.Angle(child.localRotation, Quaternion.identity) < 1f)
            {
                WeaponGripAligner.AlignWeaponToHand(hand, child, animator);
            }

            MeshFilter mf = child.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                BoxCollider box = mf.GetComponent<BoxCollider>();
                if (box == null)
                {
                    box = mf.gameObject.AddComponent<BoxCollider>();
                    Bounds b = mf.sharedMesh.bounds;
                    box.center = b.center;
                    box.size = b.size;
                }
                BindResolvedWeapon(box, child.gameObject);
                return;
            }

            Renderer rend = child.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                BoxCollider box = rend.GetComponent<BoxCollider>();
                if (box == null)
                {
                    box = rend.gameObject.AddComponent<BoxCollider>();
                    box.size = Vector3.one * 0.3f;
                }
                BindResolvedWeapon(box, child.gameObject);
                return;
            }
        }
    }

    private void BindResolvedWeapon(BoxCollider box, GameObject weaponRoot)
    {
        box.isTrigger = true;
        weaponCollider = box;
        activeWeaponVfx = weaponRoot.GetComponent<WeaponElementalVfx>();
        if (activeWeaponVfx == null)
        {
            activeWeaponVfx = WeaponElementalVfx.ApplyToWeapon(weaponRoot);
        }
    }

    private Transform FindHandBone()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null) return null;

        Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (rightHand != null && HasWeaponChild(rightHand)) return rightHand;

        Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (leftHand != null && HasWeaponChild(leftHand)) return leftHand;

        foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
        {
            if ((t.name == "mixamorig:RightHand" || t.name == "mixamorig:LeftHand") && HasWeaponChild(t))
            {
                return t;
            }
        }

        return rightHand != null ? rightHand : leftHand;
    }

    private static bool HasWeaponChild(Transform hand)
    {
        for (int i = 0; i < hand.childCount; i++)
        {
            Transform child = hand.GetChild(i);
            if (!child.name.StartsWith("mixamorig:") && child.GetComponentInChildren<Renderer>() != null)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasMoveInputFallback()
    {
        return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A)
            || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D)
            || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow)
            || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);
    }

    private void HandleLockOnInput()
    {

        if (CurrentLockedTarget != null)
        {
            if (CurrentLockedTarget.IsDead)
            {
                AcquireLockOnTarget(cycleNext: false);
            }
            else
            {
                Vector3 diff = CurrentLockedTarget.transform.position - transform.position;
                diff.y = 0f;
                if (diff.sqrMagnitude > maxLockKeepDistance * maxLockKeepDistance)
                {
                    ClearLockedTarget();
                }
            }
        }

        if (Input.GetMouseButtonDown(2) || Input.GetKeyDown(KeyCode.Q))
        {
            if (CurrentLockedTarget != null)
            {
                ClearLockedTarget();
            }
            else
            {
                AcquireLockOnTarget(cycleNext: false);
            }
            return;
        }

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (CurrentLockedTarget == null)
            {
                AcquireLockOnTarget(cycleNext: false);
            }
            else
            {
                AcquireLockOnTarget(cycleNext: true);
            }
        }
    }

    // Tìm, khóa hoặc chuyển đổi sang kẻ địch tiếp theo trong tầm nhìn
    public void AcquireLockOnTarget(bool cycleNext)
    {
        lockCandidates.Clear();
        int count = Physics.OverlapSphereNonAlloc(transform.position, lockOnRadius, hitBuffer, enemyLayers, QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            Collider c = hitBuffer[i];
            if (c == null || c is TerrainCollider || c.transform == transform || c.transform.IsChildOf(transform)) continue;

            if (!EnemyHealth.TryGetFromCollider(c, out EnemyHealth eh) || eh.IsDead) continue;

            Vector3 toEnemy = eh.transform.position - transform.position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude > lockOnRadius * lockOnRadius) continue;

            if (!lockCandidates.Contains(eh))
            {
                lockCandidates.Add(eh);
            }
        }

        if (lockCandidates.Count == 0)
        {
            ClearLockedTarget();
            return;
        }

        Camera mainCam = GetMainCamera();
        Vector3 lookDir = mainCam != null ? mainCam.transform.forward : (playerController != null ? playerController.GetVisualForward() : transform.forward);
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude < 0.001f) lookDir = transform.forward;
        lookDir.Normalize();

        lockSortLookDir = lookDir;
        lockCandidates.Sort(lockCandidateComparer ?? CompareLockCandidates);

        if (!cycleNext || CurrentLockedTarget == null)
        {
            CurrentLockedTarget = lockCandidates[0];
        }
        else
        {
            int curIdx = lockCandidates.IndexOf(CurrentLockedTarget);
            if (curIdx >= 0 && lockCandidates.Count > 1)
            {
                CurrentLockedTarget = lockCandidates[(curIdx + 1) % lockCandidates.Count];
            }
            else
            {
                CurrentLockedTarget = lockCandidates[0];
            }
        }
    }

    private Camera GetMainCamera()
    {
        if (cachedMainCamera == null)
        {
            cachedMainCamera = Camera.main;
        }
        return cachedMainCamera;
    }

    private int CompareLockCandidates(EnemyHealth a, EnemyHealth b)
    {
        float scoreA = ComputeLockScore(a, lockSortLookDir);
        float scoreB = ComputeLockScore(b, lockSortLookDir);
        return scoreA.CompareTo(scoreB);
    }

    private float ComputeLockScore(EnemyHealth eh, Vector3 lookDir)
    {
        if (eh == null) return float.MaxValue;
        Vector3 toEnemy = eh.transform.position - transform.position;
        toEnemy.y = 0f;
        float dist = toEnemy.magnitude;
        if (dist < 0.01f) return 0f;
        float anglePenalty = 1f - Vector3.Dot(lookDir, toEnemy / dist);
        float bossBonus = eh.isBoss ? -1.5f : 0f;
        return dist + anglePenalty * 4.5f + bossBonus;
    }

    // Hủy trạng thái khóa mục tiêu hiện tại
    public static void ClearLockedTarget()
    {
        CurrentLockedTarget = null;
    }

    private void EnsureLockOnIndicator()
    {
        if (lockIndicatorRoot != null) return;

        lockPropBlock = new MaterialPropertyBlock();
        lockIndicatorRoot = new GameObject("TargetLockOnIndicator");
        DontDestroyOnLoad(lockIndicatorRoot);

        GameObject ringGo = new GameObject("LockGroundRing");
        ringGo.transform.SetParent(lockIndicatorRoot.transform, false);
        MeshFilter rmf = ringGo.AddComponent<MeshFilter>();
        rmf.sharedMesh = CombatVfxManager.GetHorizontalQuadMesh();
        MeshRenderer rmr = ringGo.AddComponent<MeshRenderer>();
        rmr.sharedMaterial = CombatVfxManager.GetAdditiveRingMaterial();
        rmr.shadowCastingMode = ShadowCastingMode.Off;
        rmr.receiveShadows = false;
        SetIndicatorColor(rmr, new Color(2.3f, 0.72f, 0.18f, 0.92f));
        lockGroundRingT = ringGo.transform;
        lockGroundRingT.localPosition = new Vector3(0f, 0.14f, 0f);

        GameObject reticleRoot = new GameObject("LockBodyReticle");
        reticleRoot.transform.SetParent(lockIndicatorRoot.transform, false);
        lockReticleRootT = reticleRoot.transform;

        lockOuterDiamondT = CreateReticleDiamond("OuterDiamond", lockReticleRootT, 0.56f, new Color(2.4f, 0.65f, 0.18f, 0.95f));
        lockInnerDiamondT = CreateReticleDiamond("InnerDiamond", lockReticleRootT, 0.34f, new Color(2.4f, 2.05f, 0.55f, 0.95f));

        lockIndicatorRoot.SetActive(false);
    }

    private Transform CreateReticleDiamond(string name, Transform parent, float size, Color hdrColor)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(size, 1f, size);
        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = CombatVfxManager.GetHorizontalQuadMesh();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = CombatVfxManager.GetAdditiveRingMaterial();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        SetIndicatorColor(mr, hdrColor);
        return go.transform;
    }

    private void SetIndicatorColor(MeshRenderer mr, Color c)
    {
        if (mr == null || lockPropBlock == null) return;
        lockPropBlock.SetColor(BaseColorId, c);
        lockPropBlock.SetColor(ColorId, c);
        mr.SetPropertyBlock(lockPropBlock);
    }

    private void UpdateLockOnVisualIndicator()
    {
        if (CurrentLockedTarget == null || CurrentLockedTarget.IsDead || GameOverManager.IsGameOver)
        {
            cachedLockTarget = null;
            cachedLockTargetCollider = null;
            if (lockIndicatorRoot != null && lockIndicatorRoot.activeSelf)
            {
                lockIndicatorRoot.SetActive(false);
            }
            return;
        }

        EnsureLockOnIndicator();
        if (!lockIndicatorRoot.activeSelf)
        {
            lockIndicatorRoot.SetActive(true);
        }

        Transform targetT = CurrentLockedTarget.transform;
        if (cachedLockTarget != CurrentLockedTarget)
        {
            cachedLockTarget = CurrentLockedTarget;
            cachedLockTargetCollider = CurrentLockedTarget.GetComponent<CapsuleCollider>();
        }
        CapsuleCollider col = cachedLockTargetCollider;
        float scaleY = Mathf.Max(0.5f, targetT.localScale.y);
        float radius = col != null ? col.radius * scaleY : (CurrentLockedTarget.isBoss ? 1.1f : 0.55f);
        float height = col != null ? col.height * scaleY : (CurrentLockedTarget.isBoss ? 2.6f : 1.85f);

        Vector3 targetPos = targetT.position;
        lockIndicatorRoot.transform.position = targetPos;

        float t = Time.time;
        float dt = Time.deltaTime;

        if (lockGroundRingT != null)
        {
            float ringBase = Mathf.Clamp(radius * 3.8f, 1.8f, 4.4f);
            float pulse = ringBase * (1f + Mathf.Sin(t * 5.5f) * 0.08f);
            lockGroundRingT.localScale = new Vector3(pulse, 1f, pulse);
            lockGroundRingT.Rotate(Vector3.up, 95f * dt, Space.Self);
        }

        Camera mainCam = GetMainCamera();
        if (lockReticleRootT != null && mainCam != null)
        {
            Transform camT = mainCam.transform;
            Vector3 toCam = (camT.position - (targetPos + Vector3.up * (height * 0.56f))).normalized;
            lockReticleRootT.localPosition = new Vector3(0f, height * 0.56f, 0f) + toCam * (radius * 0.65f + 0.25f);

            lockReticleRootT.rotation = Quaternion.LookRotation(camT.up, -camT.forward);

            float reticlePulse = 1f + Mathf.Sin(t * 6.5f) * 0.10f;
            if (lockOuterDiamondT != null)
            {
                lockOuterDiamondT.localRotation = Quaternion.Euler(0f, t * 110f, 0f);
                lockOuterDiamondT.localScale = new Vector3(0.56f * reticlePulse, 1f, 0.56f * reticlePulse);
            }
            if (lockInnerDiamondT != null)
            {
                lockInnerDiamondT.localRotation = Quaternion.Euler(0f, -t * 140f + 45f, 0f);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (weaponCollider == null) ResolveWeaponCollider();
        if (weaponCollider == null) return;

        Transform wt = weaponCollider.transform;
        Vector3 worldCenter = wt.TransformPoint(weaponCollider.center);
        Vector3 lossy = wt.lossyScale;
        Vector3 size = Vector3.Scale(
            weaponCollider.size,
            new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z))
        );

        Gizmos.color = Color.red;
        Gizmos.matrix = Matrix4x4.TRS(worldCenter, wt.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, size);
    }
}
