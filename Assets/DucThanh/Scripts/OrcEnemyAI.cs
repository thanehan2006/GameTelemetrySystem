using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class OrcEnemyAI : MonoBehaviour
{
    private static readonly List<OrcEnemyAI> ActiveEnemies = new List<OrcEnemyAI>();
    private static readonly RaycastHit[] GroundHitBuffer = new RaycastHit[16];
    private const float DestinationUpdateInterval = 0.15f;
    private const float DestinationMoveThresholdSqr = 0.16f;

    [Header("Movement Settings")]
    public float moveSpeed = 3.5f;
    public float stoppingDistance = 1.4f;
    public float rotationSpeed = 10f;

    [Header("Collision, Separation & Knockback")]
    public float minEnemySpacing = 1.05f;
    public float minPlayerSpacing = 1.15f;
    public float separationSpeed = 6f;
    public float knockbackResistance = 0f;
    public float knockbackDamping = 16f;
    public float hitStunDuration = 0.16f;

    [Header("Parry Stun Settings")]
    public float parryStunDuration = 1.6f;
    public string hitStateName = "Hit";

    [Header("Attack Settings")]
    public float attackRange = 1.8f;
    public int attackDamage = 15;
    public float attackCooldown = 1.6f;
    public float playerKnockbackForce = 6.5f;

    [Header("Weapon Hitbox Timing (seconds in Attack animation)")]
    public float hitboxStartDelay = 0.35f;
    public float hitboxDuration = 0.45f;

    [Header("Animation State Names (Orc_Warrior_AC)")]
    public string idleStateName = "Idle1";
    public string runStateName = "Run";
    public string attackStateName = "Attack1";

    private Transform player;
    private PlayerHealth playerHealth;
    private Animator animator;
    private OrcWeaponHitbox weaponHitbox;
    private NavMeshAgent navAgent;

    private float nextAttackTime;
    private bool isAttacking;
    private int idleStateHash;
    private int runStateHash;
    private int attackStateHash;
    private int hitStateHash;
    private int currentStateHash;
    private Coroutine attackRoutine;
    private Vector3 knockbackVelocity;
    private float hitStunTimer;
    private float parryStunTimer;

    public bool IsParryStunned => parryStunTimer > 0f;
    private float nextDestinationTime;
    private Vector3 lastDestinationTarget;
    private WaitForSeconds waitHitboxStart;
    private WaitForSeconds waitHitboxDuration;
    private WaitForSeconds waitAttackTail;
    private float cachedStartDelay = -1f;
    private float cachedDuration = -1f;
    private float cachedCooldown = -1f;

    private bool AgentReady => navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        idleStateHash = Animator.StringToHash(idleStateName);
        runStateHash = Animator.StringToHash(runStateName);
        attackStateHash = Animator.StringToHash(attackStateName);
        hitStateHash = string.IsNullOrEmpty(hitStateName) ? 0 : Animator.StringToHash(hitStateName);

        // Đảm bảo collider thân của enemy là collider cứng để Player không đi xuyên qua
        CapsuleCollider bodyCollider = GetComponent<CapsuleCollider>();
        if (bodyCollider != null)
        {
            bodyCollider.isTrigger = false;
        }

        SetupNavMeshAgent();
        SetupWeaponHitbox();
    }

    private void OnEnable()
    {
        if (!ActiveEnemies.Contains(this))
        {
            ActiveEnemies.Add(this);
        }
    }

    void Start()
    {
        SnapToGround();
        FindPlayerTarget();
        PlayAnimation(idleStateHash);
    }

    void SetupNavMeshAgent()
    {
        navAgent = GetComponent<NavMeshAgent>();
        if (navAgent == null)
        {
            navAgent = gameObject.AddComponent<NavMeshAgent>();
        }

        navAgent.speed = moveSpeed;
        navAgent.stoppingDistance = stoppingDistance;
        navAgent.radius = 0.5f;
        navAgent.height = 2.0f;
        navAgent.baseOffset = 0f;
        navAgent.acceleration = 12f;
        navAgent.angularSpeed = 360f;
        navAgent.autoBraking = true;
        navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        navAgent.avoidancePriority = Random.Range(30, 70);
    }

    public void ApplyCombatStats(float speed, int damage, float range, float kbResistance, float cooldown)
    {
        moveSpeed = speed;
        attackDamage = damage;
        attackRange = range;
        knockbackResistance = Mathf.Clamp01(kbResistance);
        attackCooldown = cooldown;

        if (navAgent != null)
        {
            navAgent.speed = moveSpeed;
        }
        if (weaponHitbox != null)
        {
            weaponHitbox.SetDamage(attackDamage);
        }
    }

    void SnapToGround()
    {
        Vector3 origin = transform.position + Vector3.up * 20f;
        int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, GroundHitBuffer, 60f, ~0, QueryTriggerInteraction.Ignore);

        float bestY = float.MinValue;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = GroundHitBuffer[i].collider;
            if (col == null || col.transform.IsChildOf(transform)) continue;
            if (GroundHitBuffer[i].point.y > bestY)
            {
                bestY = GroundHitBuffer[i].point.y;
                found = true;
            }
        }

        if (found)
        {
            Vector3 pos = transform.position;
            pos.y = bestY;
            transform.position = pos;

            if (AgentReady)
            {
                navAgent.Warp(pos);
            }
        }
    }

    void SetupWeaponHitbox()
    {
        BoxCollider[] boxColliders = GetComponentsInChildren<BoxCollider>(true);
        BoxCollider axeCollider = null;

        for (int i = 0; i < boxColliders.Length; i++)
        {
            if (boxColliders[i].gameObject.name.IndexOf("axe", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                axeCollider = boxColliders[i];
                break;
            }
        }

        if (axeCollider == null)
        {
            for (int i = 0; i < boxColliders.Length; i++)
            {
                if (boxColliders[i].gameObject != gameObject)
                {
                    axeCollider = boxColliders[i];
                    break;
                }
            }
        }

        if (axeCollider == null) return;

        axeCollider.isTrigger = true;
        weaponHitbox = axeCollider.GetComponent<OrcWeaponHitbox>();
        if (weaponHitbox == null)
        {
            weaponHitbox = axeCollider.gameObject.AddComponent<OrcWeaponHitbox>();
        }
        weaponHitbox.Initialize(this, attackDamage);
        weaponHitbox.DisableHitbox();
    }

    void Update()
    {
        if (parryStunTimer > 0f)
        {
            parryStunTimer -= Time.deltaTime;
            StopAgent();
            if (parryStunTimer <= 0f)
            {
                parryStunTimer = 0f;
                if (animator != null) animator.speed = 1f;
                currentStateHash = 0;
            }
            return;
        }

        if (hitStunTimer > 0f)
        {
            hitStunTimer -= Time.deltaTime;
            StopAgent();
            return;
        }

        if (isAttacking)
        {
            StopAgent();
            if (player != null)
            {
                FaceDirection(player.position - transform.position, rotationSpeed * 0.5f);
            }
            return;
        }

        if (player == null || playerHealth == null)
        {
            FindPlayerTarget();
            if (player == null)
            {
                Idle();
                return;
            }
        }

        if (playerHealth.currentHealth <= 0)
        {
            Idle();
            return;
        }

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float sqrDistance = toPlayer.sqrMagnitude;

        if (sqrDistance <= attackRange * attackRange)
        {
            StopAgent();
            FaceDirection(toPlayer, rotationSpeed);

            if (Time.time >= nextAttackTime)
            {
                StartAttack();
            }
            else
            {
                PlayAnimation(idleStateHash);
            }
            return;
        }

        PlayAnimation(runStateHash);

        if (AgentReady)
        {
            if (navAgent.isStopped) navAgent.isStopped = false;
            if (!Mathf.Approximately(navAgent.speed, moveSpeed)) navAgent.speed = moveSpeed;

            Vector3 targetPos = player.position;
            if (Time.time >= nextDestinationTime || (targetPos - lastDestinationTarget).sqrMagnitude >= DestinationMoveThresholdSqr)
            {
                nextDestinationTime = Time.time + DestinationUpdateInterval;
                lastDestinationTarget = targetPos;
                navAgent.SetDestination(targetPos);
            }
            return;
        }

        // Fallback khi NavMesh chưa bake kịp
        FaceDirection(toPlayer, rotationSpeed);

        if (sqrDistance > stoppingDistance * stoppingDistance)
        {
            float dist = Mathf.Sqrt(sqrDistance);
            transform.position += (toPlayer / dist) * (moveSpeed * Time.deltaTime);
        }

        int groundHitCount = Physics.RaycastNonAlloc(transform.position + Vector3.up * 5f, Vector3.down, GroundHitBuffer, 14f, ~0, QueryTriggerInteraction.Ignore);
        float highestGroundY = float.MinValue;
        bool hasGround = false;
        for (int i = 0; i < groundHitCount; i++)
        {
            Collider col = GroundHitBuffer[i].collider;
            if (col == null || col.transform.IsChildOf(transform)) continue;
            if (GroundHitBuffer[i].point.y > highestGroundY)
            {
                highestGroundY = GroundHitBuffer[i].point.y;
                hasGround = true;
            }
        }

        if (hasGround)
        {
            Vector3 pos = transform.position;
            pos.y = highestGroundY;
            transform.position = pos;
        }
    }

    void LateUpdate()
    {
        ProcessKnockback();
        EnforceSeparation();
    }

    /// <summary>
    /// Đẩy lùi Enemy khi bị vũ khí Player đánh trúng hoặc va chạm mạnh.
    /// </summary>
    public void ApplyKnockback(Vector3 attackerPosition, float force, bool applyHitStun = true)
    {
        float effectiveForce = Mathf.Max(0f, force * (1f - Mathf.Clamp01(knockbackResistance)));
        if (effectiveForce <= 0.01f) return;

        Vector3 dir = transform.position - attackerPosition;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = -transform.forward;
        }
        else
        {
            dir.Normalize();
        }

        knockbackVelocity = dir * effectiveForce;
        if (applyHitStun)
        {
            hitStunTimer = Mathf.Max(hitStunTimer, hitStunDuration);
        }
    }

    /// <summary>
    /// Làm choáng quái khi Player Parry hoàn hảo: ngắt đòn đang vung, đẩy lùi, phát anim khựng và vòng sao choáng.
    /// </summary>
    public void ApplyParryStun(Vector3 attackerPosition, float knockbackForce, float duration = -1f)
    {
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }
        isAttacking = false;
        weaponHitbox?.DisableHitbox();

        ApplyKnockback(attackerPosition, knockbackForce, applyHitStun: false);

        float baseDur = duration > 0f ? duration : parryStunDuration;
        float finalDur = Mathf.Max(0.6f, Mathf.Lerp(baseDur, baseDur * 0.65f, Mathf.Clamp01(knockbackResistance)));

        parryStunTimer = Mathf.Max(parryStunTimer, finalDur);
        nextAttackTime = Mathf.Max(nextAttackTime, Time.time + finalDur + 0.35f);
        StopAgent();

        if (animator != null)
        {
            if (hitStateHash != 0 && animator.HasState(0, hitStateHash))
            {
                currentStateHash = hitStateHash;
                animator.Play(hitStateHash, 0, 0f);
            }
            else
            {
                PlayAnimation(idleStateHash);
            }
            animator.speed = 0.45f;
        }

        CombatVfxManager.SpawnParryStunEffect(transform, finalDur);
    }

    private void ProcessKnockback()
    {
        if (knockbackVelocity.sqrMagnitude <= 0.001f)
        {
            knockbackVelocity = Vector3.zero;
            return;
        }

        Vector3 displacement = knockbackVelocity * Time.deltaTime;
        if (AgentReady)
        {
            navAgent.Move(displacement);
        }
        else
        {
            transform.position += displacement;
        }

        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDamping * Time.deltaTime);
    }

    void EnforceSeparation()
    {
        Vector3 separation = Vector3.zero;
        Vector3 pos = transform.position;

        // Tránh đi xuyên qua các enemy khác
        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            OrcEnemyAI other = ActiveEnemies[i];
            if (other == null || other == this) continue;

            Vector3 diff = pos - other.transform.position;
            diff.y = 0f;
            float sqrDist = diff.sqrMagnitude;
            if (sqrDist < minEnemySpacing * minEnemySpacing)
            {
                float dist = Mathf.Sqrt(sqrDist);
                Vector3 dir = dist > 0.001f ? diff / dist : Quaternion.Euler(0f, i * 67f, 0f) * Vector3.forward;
                separation += dir * (minEnemySpacing - dist);
            }
        }

        // Tránh đi xuyên vào Player
        Vector3 hardPush = Vector3.zero;
        if (player != null)
        {
            Vector3 diff = pos - player.position;
            diff.y = 0f;
            float sqrDist = diff.sqrMagnitude;
            if (sqrDist < minPlayerSpacing * minPlayerSpacing)
            {
                float dist = Mathf.Sqrt(sqrDist);
                Vector3 dir = dist > 0.001f ? diff / dist : -transform.forward;
                const float hardMinDistance = 1.0f;
                if (dist < hardMinDistance)
                {
                    // Đẩy ngay ra ngoài ngưỡng chạm cứng capsule (0.93m) để không làm kẹt CharacterController
                    hardPush = dir * (hardMinDistance - dist);
                }
                else
                {
                    separation += dir * (minPlayerSpacing - dist);
                }
            }
        }

        Vector3 step = hardPush + Vector3.ClampMagnitude(separation, separationSpeed * Time.deltaTime);
        if (step.sqrMagnitude <= 0.00001f) return;

        if (AgentReady)
        {
            navAgent.Move(step);
        }
        else
        {
            transform.position += step;
        }
    }

    void StopAgent()
    {
        if (AgentReady && !navAgent.isStopped)
        {
            navAgent.isStopped = true;
        }
    }

    void Idle()
    {
        StopAgent();
        PlayAnimation(idleStateHash);
    }

    void FaceDirection(Vector3 direction, float speed)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, speed * Time.deltaTime);
        }
    }

    void FindPlayerTarget()
    {
        if (PlayerController.Instance != null)
        {
            player = PlayerController.Instance.transform;
            playerHealth = PlayerHealth.Instance != null ? PlayerHealth.Instance : PlayerController.Instance.GetComponent<PlayerHealth>();
            return;
        }

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            playerHealth = playerObj.GetComponent<PlayerHealth>();
        }
    }

    void StartAttack()
    {
        nextAttackTime = Time.time + attackCooldown;
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
        }
        attackRoutine = StartCoroutine(AttackSequence());
    }

    private void EnsureAttackWaits()
    {
        if (!Mathf.Approximately(cachedStartDelay, hitboxStartDelay)
            || !Mathf.Approximately(cachedDuration, hitboxDuration)
            || !Mathf.Approximately(cachedCooldown, attackCooldown))
        {
            cachedStartDelay = hitboxStartDelay;
            cachedDuration = hitboxDuration;
            cachedCooldown = attackCooldown;
            waitHitboxStart = new WaitForSeconds(hitboxStartDelay);
            waitHitboxDuration = new WaitForSeconds(hitboxDuration);
            float remainingAnimTime = Mathf.Max(0.1f, attackCooldown - hitboxStartDelay - hitboxDuration);
            waitAttackTail = new WaitForSeconds(remainingAnimTime * 0.5f);
        }
    }

    IEnumerator AttackSequence()
    {
        EnsureAttackWaits();
        isAttacking = true;
        PlayAnimation(attackStateHash);
        GameAudioManager.PlayOrcAttack();

        yield return waitHitboxStart;

        if (weaponHitbox != null)
        {
            weaponHitbox.SetDamage(attackDamage);
            weaponHitbox.EnableHitbox();
        }

        yield return waitHitboxDuration;

        if (weaponHitbox != null)
        {
            weaponHitbox.DisableHitbox();
        }

        yield return waitAttackTail;

        isAttacking = false;
        attackRoutine = null;
    }

    void PlayAnimation(int stateHash)
    {
        if (animator == null || stateHash == 0 || currentStateHash == stateHash) return;

        currentStateHash = stateHash;
        animator.Play(stateHash, 0, 0f);
    }

    private void OnDisable()
    {
        ActiveEnemies.Remove(this);
        if (weaponHitbox != null)
        {
            weaponHitbox.DisableHitbox();
        }
        isAttacking = false;
        parryStunTimer = 0f;
        if (animator != null)
        {
            animator.speed = 1f;
        }
    }
}
