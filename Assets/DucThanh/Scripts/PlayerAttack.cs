using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Xử lý tấn công cận chiến của Player khi nhấn chuột trái:
/// - Kích hoạt animation Atk1 và ưu tiên đánh liên hoàn nếu vừa ấn đánh liên tục vừa ấn di chuyển.
/// - Chỉ thoát đòn đánh sang Run khi người chơi chỉ di chuyển mà không tấn công.
/// - Chỉ gây sát thương khi collider của vũ khí trên tay thực sự chạm vào Enemy trong lúc vung.
/// </summary>
[DefaultExecutionOrder(-10)]
public class PlayerAttack : MonoBehaviour
{
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int CancelAttackHash = Animator.StringToHash("CancelAttack");

    [Header("Attack Settings")]
    public int attackDamage = 35;
    public float attackCooldown = 0.6f;
    public float attackSpamWindow = 0.45f;
    public float hitboxStartDelay = 0.1f;
    public float hitboxDuration = 0.45f;
    [SerializeField] private LayerMask enemyLayers = ~0;

    [Header("References")]
    public Transform visualModel;
    public BoxCollider weaponCollider;

    public bool IsAttacking { get; private set; }

    private Animator animator;
    private float nextAttackTime;
    private float attackStartTime;
    private float lastAttackClickTime = -10f;
    private int clickCountInAttack;
    private bool queuedNextAttack;
    private bool wasCursorLocked;
    private bool wasMovingInput;
    private readonly HashSet<EnemyHealth> hitEnemiesInSwing = new HashSet<EnemyHealth>();

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        if (visualModel == null)
        {
            Transform body = transform.Find("PlayerBody");
            if (body != null) visualModel = body;
        }

        ResolveWeaponCollider();
    }

    void Start()
    {
        wasCursorLocked = Cursor.lockState == CursorLockMode.Locked;
        ResolveWeaponCollider();
    }

    void Update()
    {
        if (GameOverManager.IsGameOver)
        {
            IsAttacking = false;
            queuedNextAttack = false;
            clickCountInAttack = 0;
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

        bool isMovingInput = HasMoveInput();
        bool moveKeyDown = IsMoveKeyDown(isMovingInput);

        if (IsAttacking)
        {
            if (attackDown && Time.time - attackStartTime > 0.08f)
            {
                clickCountInAttack++;
                lastAttackClickTime = Time.time;
                queuedNextAttack = true;
            }
            else if (attackHeld && Time.time - attackStartTime > 0.25f)
            {
                lastAttackClickTime = Time.time;
                queuedNextAttack = true;
            }

            if (IsAttackAnimationFinished())
            {
                if (queuedNextAttack || attackHeld)
                {
                    PerformAttack(true);
                }
                else
                {
                    IsAttacking = false;
                    queuedNextAttack = false;
                    clickCountInAttack = 0;
                    hitEnemiesInSwing.Clear();
                }
            }
            else
            {
                bool isSpammingOrHoldingAttack = attackDown
                    || (attackHeld && Time.time - attackStartTime > 0.2f)
                    || (clickCountInAttack >= 2 && Time.time - lastAttackClickTime < attackSpamWindow);

                if (!isSpammingOrHoldingAttack
                    && (moveKeyDown || (isMovingInput && Time.time - attackStartTime > 0.25f)))
                {
                    CancelAttack();
                }
            }
        }
        else if (attackDown && Time.time >= nextAttackTime)
        {
            PerformAttack(false);
        }

        wasCursorLocked = isCursorLocked;
        wasMovingInput = isMovingInput;
    }

    void LateUpdate()
    {
        if (!IsAttacking || GameOverManager.IsGameOver) return;

        float elapsed = Time.time - attackStartTime;
        if (elapsed < hitboxStartDelay) return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.IsName("Atk1") && !animator.IsInTransition(0) && state.normalizedTime > 0.85f) return;

        if (!state.IsName("Atk1") && elapsed > hitboxStartDelay + hitboxDuration) return;

        CheckWeaponHit();
    }

    private bool IsAttackAnimationFinished()
    {
        if (Time.time - attackStartTime <= 0.15f) return false;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
        bool inAtkState = state.IsName("Atk1") || next.IsName("Atk1");

        return !inAtkState
            || (state.IsName("Atk1") && !animator.IsInTransition(0) && state.normalizedTime >= 0.95f)
            || (Time.time - attackStartTime > 4.5f);
    }

    private void PerformAttack(bool isChained)
    {
        IsAttacking = true;
        queuedNextAttack = false;
        clickCountInAttack = isChained ? 2 : 1;
        attackStartTime = Time.time;
        lastAttackClickTime = Time.time;
        nextAttackTime = Time.time + attackCooldown;
        hitEnemiesInSwing.Clear();

        animator.ResetTrigger(CancelAttackHash);
        animator.ResetTrigger(AttackHash);
        animator.SetTrigger(AttackHash);
        animator.Play("Atk1", 0, 0f);
    }

    private void CancelAttack()
    {
        IsAttacking = false;
        queuedNextAttack = false;
        clickCountInAttack = 0;
        hitEnemiesInSwing.Clear();

        animator.ResetTrigger(AttackHash);
        animator.SetTrigger(CancelAttackHash);
        animator.CrossFadeInFixedTime("Run", 0.08f, 0);
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

        Collider[] hits = Physics.OverlapBox(worldCenter, halfExtents, wt.rotation, enemyLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i];
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

            EnemyHealth enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy != null && !enemy.IsDead && hitEnemiesInSwing.Add(enemy))
            {
                enemy.TakeDamage(attackDamage);
            }
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

    private static bool HasMoveInput()
    {
        return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A)
            || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D)
            || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow)
            || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow)
            || Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f
            || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f;
    }

    private bool IsMoveKeyDown(bool isMovingInput)
    {
        return Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A)
            || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D)
            || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow)
            || Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)
            || (isMovingInput && !wasMovingInput);
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
