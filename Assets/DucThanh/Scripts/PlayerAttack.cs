using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hệ thống tấn công cận chiến của Player:
/// - Khi nhấn Chuột Trái: thực hiện trọn vẹn animation Atk1 mượt mà từ đầu đến cuối (không bị ngắt sớm ở 0.1s khi đang giữ WASD).
/// - Tự động hướng mặt mượt mà (tốc độ vừa phải, không giật cục) về phía quái gần nhất hoặc hướng di chuyển.
/// - Gây sát thương + đẩy lùi (Knockback) khi collider vũ khí trên tay chạm vào Enemy trong lúc vung đòn.
/// - Nếu giữ hoặc nhấp tiếp Chuột Trái, sẽ nối đòn tiếp theo ngay sau khi hết animation hiện tại.
/// </summary>
[DefaultExecutionOrder(-10)]
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
    public int attackDamage = 35;
    public float knockbackForce = 7.5f;
    [Tooltip("Tốc độ phát animation tấn công (1.2 = nhanh gọn vừa phải, thực hiện đủ toàn bộ động tác)")]
    public float attackAnimSpeed = 1.2f;
    public float attackCooldown = 0.15f;

    [Header("Auto-Target & Turn Speed (Quay mặt mượt khi đánh)")]
    public float autoTargetRadius = 4.0f;
    [Tooltip("Tốc độ quay mặt của Player về phía quái khi ấn đánh (càng nhỏ quay càng đầm và mượt)")]
    public float attackTurnSpeed = 7.0f;
    public float lungeSpeed = 2.5f;
    public float lungeDuration = 0.12f;
    public float lungeStopDistance = 1.3f;

    [Header("Hitbox Settings")]
    public float hitboxStartDelay = 0.08f;
    [SerializeField] private LayerMask enemyLayers = ~0;

    [Header("References")]
    public Transform visualModel;
    public BoxCollider weaponCollider;

    public bool IsAttacking { get; private set; }

    private Animator animator;
    private PlayerController playerController;
    private PlayerAnimationController animController;
    private Transform currentTargetEnemy;
    private EnemyHealth currentTargetHealth;
    private Vector3 attackFacingDir = Vector3.forward;

    private readonly List<int> availableAttackIndices = new List<int>(3);
    private int currentAttackStateHash = AttackStateHashes[0];
    private int currentAttackVariant = 0;
    private int lastAttackVariant = -1;
    private TrailRenderer weaponTrail;

    private float nextAttackTime;
    private float attackStartTime;
    private bool queuedNextAttack;
    private bool wasCursorLocked;
    private readonly HashSet<EnemyHealth> hitEnemiesInSwing = new HashSet<EnemyHealth>();
    private readonly Collider[] hitBuffer = new Collider[32];

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        playerController = GetComponent<PlayerController>();
        animController = GetComponent<PlayerAnimationController>();

        if (visualModel == null)
        {
            Transform body = transform.Find("PlayerBody");
            if (body != null) visualModel = body;
        }

        ResolveWeaponCollider();
        EnsureWeaponTrail();
    }

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

    void Update()
    {
        if (GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen)
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
            animator.speed = attackAnimSpeed;

            // Quay mặt từ từ, mượt mà về phía mục tiêu hoặc hướng điều hướng trong suốt đòn đánh
            UpdateSmoothAttackRotation(elapsed);

            // Bước tiến nhẹ có kiểm soát ở đầu đòn đánh
            if (elapsed <= lungeDuration)
            {
                UpdateCombatLunge(elapsed);
            }
            else if (playerController != null)
            {
                playerController.StopCombatLunge();
            }

            // Ghi nhận lệnh đánh tiếp theo (Input Buffer), nhưng ĐỢI hết animation hiện tại mới phát đòn mới
            if (attackDown && elapsed > 0.20f)
            {
                queuedNextAttack = true;
            }

            // Đợi thực hiện HẾT animation Atk1 rồi mới chuyển trạng thái
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

    void LateUpdate()
    {
        if (!IsAttacking || GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen) return;

        float elapsed = Time.time - attackStartTime;
        if (elapsed < hitboxStartDelay) return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash == currentAttackStateHash && !animator.IsInTransition(0) && state.normalizedTime > 0.90f) return;

        CheckWeaponHit();
    }

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
        hitEnemiesInSwing.Clear();

        // Tìm hướng quái gần nhất (hoặc hướng phím di chuyển), KHÔNG xoay tức thì mà để Update xoay mượt
        ResolveAttackDirectionAndTarget();

        animator.speed = attackAnimSpeed;
        animator.ResetTrigger(CancelAttackHash);
        animator.ResetTrigger(AttackHash);

        // Phát ngẫu nhiên 1 trong 3 chiêu thức (Atk1 chém chéo, Atk2 chém ngang, Atk3 xoay 360° tầm thấp)
        animator.CrossFadeInFixedTime(currentAttackStateHash, 0.06f, 0, 0f);
        UpdateWeaponTrailTheme(currentAttackVariant);
        SetWeaponTrailEmitting(true);
        CombatVfxManager.SpawnSlashArc(transform.position, attackFacingDir, currentAttackVariant);
        GameAudioManager.PlaySwordSwing();
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

        // Xoay mượt (instant = false) với tốc độ vừa phải attackTurnSpeed
        playerController.RotateVisualTowards(attackFacingDir, attackTurnSpeed, false);
    }

    private void ResolveAttackDirectionAndTarget()
    {
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
            if (c == null || c.transform == transform || c.transform.IsChildOf(transform)) continue;

            EnemyHealth eh = c.GetComponentInParent<EnemyHealth>();
            if (eh == null || eh.IsDead) continue;

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
        // Đảm bảo đã qua giai đoạn blend vào đòn đánh hiện tại (tối thiểu 0.25s)
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
        hitEnemiesInSwing.Clear();
        SetWeaponTrailEmitting(false);

        if (animator != null)
        {
            animator.speed = 1f;
        }

        if (playerController != null)
        {
            playerController.StopCombatLunge();
        }
    }

    private void CheckWeaponHit()
    {
        if (weaponCollider == null)
        {
            ResolveWeaponCollider();
            if (weaponCollider == null) return;
        }

        Transform wt = weaponCollider.transform;
        Vector3 worldCenter = wt.TransformPoint(weaponCollider.center);
        Vector3 lossy = wt.lossyScale;
        Vector3 halfExtents = Vector3.Scale(
            weaponCollider.size * 0.5f,
            new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z))
        );

        int hitCount = Physics.OverlapBoxNonAlloc(worldCenter, halfExtents, hitBuffer, wt.rotation, enemyLayers, QueryTriggerInteraction.Collide);
        ProcessHitColliders(hitBuffer, hitCount, worldCenter);

        // Chiêu Atk3 (Xoay 360° tầm thấp) quét thêm vòng AoE xung quanh Player
        if (currentAttackVariant == 2 && (Time.time - attackStartTime) >= 0.14f)
        {
            Vector3 spinCenter = transform.position + Vector3.up * 0.85f;
            int spinCount = Physics.OverlapSphereNonAlloc(spinCenter, 2.65f, hitBuffer, enemyLayers, QueryTriggerInteraction.Collide);
            ProcessHitColliders(hitBuffer, spinCount, spinCenter);
        }
    }

    private void ProcessHitColliders(Collider[] hits, int count, Vector3 originCenter)
    {
        for (int i = 0; i < count; i++)
        {
            Collider col = hits[i];
            if (col == null || col.transform == transform || col.transform.IsChildOf(transform)) continue;

            EnemyHealth enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy != null && !enemy.IsDead && hitEnemiesInSwing.Add(enemy))
            {
                if (playerController != null)
                {
                    playerController.StopCombatLunge();
                }

                OrcEnemyAI orcAI = enemy.GetComponent<OrcEnemyAI>();
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
                CombatVfxManager.SpawnFloatingText(
                    hitPoint + Vector3.up * 0.55f,
                    heavyHit ? $"{dealtDamage}!" : $"{dealtDamage}",
                    heavyHit ? new Color(1f, 0.80f, 0.22f) : Color.white,
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
        }
    }

    public void RefreshWeapon(int bonusDamage = 0)
    {
        if (bonusDamage != 0)
        {
            attackDamage = Mathf.Max(1, attackDamage + bonusDamage);
        }

        weaponCollider = null;
        weaponTrail = null;
        hitEnemiesInSwing.Clear();
        ResolveWeaponCollider();
        EnsureWeaponTrail();
    }

    private void EnsureWeaponTrail()
    {
        if (weaponCollider == null) return;

        Transform existing = weaponCollider.transform.Find("WeaponSlashTrail");
        if (existing != null)
        {
            weaponTrail = existing.GetComponent<TrailRenderer>();
            if (weaponTrail != null) weaponTrail.emitting = false;
            return;
        }

        GameObject trailObj = new GameObject("WeaponSlashTrail");
        trailObj.transform.SetParent(weaponCollider.transform, false);
        // Đặt gần đầu lưỡi vũ khí theo bounds của BoxCollider
        trailObj.transform.localPosition = weaponCollider.center + new Vector3(0f, weaponCollider.size.y * 0.42f, 0f);

        weaponTrail = trailObj.AddComponent<TrailRenderer>();
        weaponTrail.time = 0.19f;
        weaponTrail.minVertexDistance = 0.04f;
        weaponTrail.startWidth = 0.30f;
        weaponTrail.endWidth = 0.02f;
        weaponTrail.material = CombatVfxManager.GetWeaponTrailMaterial();
        weaponTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        weaponTrail.receiveShadows = false;

        UpdateWeaponTrailTheme(0);
        weaponTrail.emitting = false;
    }

    private void UpdateWeaponTrailTheme(int attackVariant)
    {
        if (weaponTrail == null) EnsureWeaponTrail();
        if (weaponTrail == null) return;

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
                box.isTrigger = true;
                weaponCollider = box;
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
                box.isTrigger = true;
                weaponCollider = box;
                return;
            }
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
