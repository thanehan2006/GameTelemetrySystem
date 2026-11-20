using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class OrcEnemyAI : MonoBehaviour
{
    private static readonly List<OrcEnemyAI> ActiveEnemies = new List<OrcEnemyAI>();

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
    private string currentState = "";
    private Coroutine attackRoutine;
    private Vector3 knockbackVelocity;
    private float hitStunTimer;

    private bool AgentReady => navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();

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
        PlayAnimation(idleStateName);
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

    void SnapToGround()
    {
        if (Physics.Raycast(transform.position + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 5f))
        {
            Vector3 pos = transform.position;
            pos.y = hit.point.y;
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

        foreach (var col in boxColliders)
        {
            if (col.gameObject.name.ToLower().Contains("axe"))
            {
                axeCollider = col;
                break;
            }
        }

        if (axeCollider == null)
        {
            foreach (var col in boxColliders)
            {
                if (col.gameObject != gameObject)
                {
                    axeCollider = col;
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
        float distance = toPlayer.magnitude;

        if (distance <= attackRange)
        {
            StopAgent();
            FaceDirection(toPlayer, rotationSpeed);

            if (Time.time >= nextAttackTime)
            {
                StartAttack();
            }
            else
            {
                PlayAnimation(idleStateName);
            }
            return;
        }

        PlayAnimation(runStateName);

        if (AgentReady)
        {
            navAgent.isStopped = false;
            navAgent.speed = moveSpeed;
            navAgent.SetDestination(player.position);
            return;
        }

        // Fallback khi NavMesh chưa bake kịp
        FaceDirection(toPlayer, rotationSpeed);

        if (distance > stoppingDistance)
        {
            transform.position += toPlayer.normalized * (moveSpeed * Time.deltaTime);
        }

        if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out RaycastHit groundHit, 3f))
        {
            Vector3 pos = transform.position;
            pos.y = groundHit.point.y;
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
        if (AgentReady)
        {
            navAgent.isStopped = true;
        }
    }

    void Idle()
    {
        StopAgent();
        PlayAnimation(idleStateName);
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

    IEnumerator AttackSequence()
    {
        isAttacking = true;
        PlayAnimation(attackStateName);

        yield return new WaitForSeconds(hitboxStartDelay);

        if (weaponHitbox != null)
        {
            weaponHitbox.SetDamage(attackDamage);
            weaponHitbox.EnableHitbox();
        }

        yield return new WaitForSeconds(hitboxDuration);

        if (weaponHitbox != null)
        {
            weaponHitbox.DisableHitbox();
        }

        float remainingAnimTime = Mathf.Max(0.1f, attackCooldown - hitboxStartDelay - hitboxDuration);
        yield return new WaitForSeconds(remainingAnimTime * 0.5f);

        isAttacking = false;
        attackRoutine = null;
    }

    void PlayAnimation(string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName) || currentState == stateName) return;

        currentState = stateName;
        animator.Play(stateName, 0, 0f);
    }

    private void OnDisable()
    {
        ActiveEnemies.Remove(this);
        if (weaponHitbox != null)
        {
            weaponHitbox.DisableHitbox();
        }
        isAttacking = false;
    }
}
