using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
// AI điều khiển kẻ địch truy đuổi bằng NavMesh, né vật cản, vung vũ khí tấn công và chịu choáng khi bị parry
public class OrcEnemyAI : MonoBehaviour
{
    private static readonly List<OrcEnemyAI> ActiveEnemies = new List<OrcEnemyAI>();
    private static readonly HashSet<int> ExtraEnemyColliderIds = new HashSet<int>();
    public static int ActiveEnemyCount => ActiveEnemies.Count;
    private static readonly RaycastHit[] GroundHitBuffer = new RaycastHit[16];
    private static readonly float[] SteeringAngles = { 35f, -35f, 65f, -65f, 95f, -95f };
    private const float DestinationUpdateInterval = 0.22f;
    private const float DestinationMoveThresholdSqr = 0.16f;
    private const float NavMeshSampleRadius = 6f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        ActiveEnemies.Clear();
        ExtraEnemyColliderIds.Clear();
    }

    public static bool IsEnemyCollider(Collider col)
    {
        if (col == null) return false;
        if (EnemyHealth.TryGetFromCollider(col, out _)) return true;
        return ExtraEnemyColliderIds.Count > 0 && ExtraEnemyColliderIds.Contains(col.GetInstanceID());
    }

    public static bool IsPlayerCollider(Collider col)
    {
        if (col == null) return false;
        if (col.CompareTag("Player")) return true;
        if (PlayerController.Instance != null)
        {
            Transform pt = PlayerController.Instance.transform;
            return col.transform == pt || col.transform.IsChildOf(pt);
        }
        return col.GetComponentInParent<PlayerController>() != null;
    }

    public enum IdleBehaviorMode
    {
        AlwaysChase,
        StandGuard,
        Patrol
    }

    [Header("Movement Settings")]
    public float moveSpeed = 3.5f;
    public float stoppingDistance = 1.4f;
    public float rotationSpeed = 10f;

    [Header("Patrol & Detection Settings")]
    public IdleBehaviorMode idleBehavior = IdleBehaviorMode.AlwaysChase;
    public float detectionRange = 14f;
    public float leashRange = 30f;
    public float alertAllyRadius = 7f;
    public float patrolSpeedMultiplier = 0.55f;
    public float patrolWaitDuration = 1.8f;
    public float patrolRadius = 6f;

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

    [Header("Weapon Hitbox Timing")]
    public float hitboxStartDelay = 0.35f;
    public float hitboxDuration = 0.45f;

    [Header("Animation State Names")]
    public string idleStateName = "Idle1";
    public string walkStateName = "Walk";
    public string runStateName = "Run";
    public string attackStateName = "Attack1";

    private Transform cachedTransform;
    private Transform player;
    private PlayerHealth playerHealth;
    private Animator animator;
    private OrcWeaponHitbox weaponHitbox;
    private NavMeshAgent navAgent;
    private Collider[] fallbackOwnColliders;

    private float nextAttackTime;
    private bool isAttacking;
    private int idleStateHash;
    private int walkStateHash;
    private bool hasWalkState;
    private int runStateHash;
    private int attackStateHash;
    private int hitStateHash;
    private int currentStateHash;
    private Coroutine attackRoutine;
    private Vector3 knockbackVelocity;
    private float hitStunTimer;
    private float parryStunTimer;

    private bool isAggroed;
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private bool hasHomeConfigured;
    private Vector3[] patrolPoints;
    private int currentPatrolIndex;
    private float patrolWaitTimer;
    private float patrolStuckTimer;
    private Vector3 lastPatrolProgressPos;

    public bool IsParryStunned => parryStunTimer > 0f;
    private float nextDestinationTime;
    private Vector3 lastDestinationTarget;
    private Vector3 lastProgressPos;
    private float stuckTimer;
    private WaitForSeconds waitHitboxStart;
    private WaitForSeconds waitHitboxDuration;
    private WaitForSeconds waitAttackTail;
    private float cachedStartDelay = -1f;
    private float cachedDuration = -1f;
    private float cachedCooldown = -1f;
    private int navMeshFailCount;
    private Vector3 cachedSteeringDir;
    private Vector3 lastSteerTargetDir;
    private float nextSteerProbeTime;
    private float cachedWalkGroundY;
    private bool hasCachedWalkGroundY;
    private float nextGroundProbeTime;
    private Vector3 lastGroundProbePos;
    private float nextDistantNonAggroTime;
    private float lastSqrDistToPlayer = float.MaxValue;

    private bool AgentReady => navAgent != null && navAgent.isActiveAndEnabled && navAgent.isOnNavMesh;

    // Lấy tham chiếu NavMeshAgent, Animator và thiết lập hitbox vũ khí
    void Awake()
    {
        cachedTransform = transform;
        if (GetComponent<EnemyHealth>() == null)
        {
            fallbackOwnColliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < fallbackOwnColliders.Length; i++)
            {
                if (fallbackOwnColliders[i] != null)
                {
                    ExtraEnemyColliderIds.Add(fallbackOwnColliders[i].GetInstanceID());
                }
            }
        }

        animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }
        idleStateHash = Animator.StringToHash(idleStateName);
        walkStateHash = string.IsNullOrEmpty(walkStateName) ? 0 : Animator.StringToHash(walkStateName);
        hasWalkState = animator != null && walkStateHash != 0 && animator.HasState(0, walkStateHash);
        runStateHash = Animator.StringToHash(runStateName);
        attackStateHash = Animator.StringToHash(attackStateName);
        hitStateHash = string.IsNullOrEmpty(hitStateName) ? 0 : Animator.StringToHash(hitStateName);
        isAggroed = (idleBehavior == IdleBehaviorMode.AlwaysChase);

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

    // Tìm người chơi và đưa kẻ địch lên đúng điểm NavMesh
    void Start()
    {
        SnapToGround();
        EnsureOnNavMesh();
        if (!hasHomeConfigured)
        {
            homePosition = transform.position;
            homeRotation = transform.rotation;
        }
        if (idleBehavior == IdleBehaviorMode.AlwaysChase)
        {
            isAggroed = true;
        }
        lastProgressPos = transform.position;
        lastPatrolProgressPos = transform.position;
        nextDestinationTime = Time.time + Random.Range(0f, DestinationUpdateInterval);
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
        navAgent.stoppingDistance = GetCombatStoppingDistance();
        if (navAgent.radius < 0.25f) navAgent.radius = 0.48f;
        if (navAgent.height < 0.8f) navAgent.height = 1.9f;
        navAgent.baseOffset = 0f;
        navAgent.acceleration = 14f;
        navAgent.angularSpeed = 420f;
        navAgent.autoBraking = true;
        navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        navAgent.avoidancePriority = Random.Range(30, 70);
    }

    private float GetCombatStoppingDistance()
    {
        return Mathf.Min(stoppingDistance, Mathf.Max(0.8f, attackRange * 0.82f));
    }

    // Cấu hình quái đứng gác tại một vị trí và chỉ đuổi đánh khi người chơi tới gần
    public void ConfigureStandGuard(Vector3 guardPos, Vector3 lookDirection, float detectRadius = 14f, float maxLeashDistance = 28f)
    {
        idleBehavior = IdleBehaviorMode.StandGuard;
        isAggroed = false;
        detectionRange = Mathf.Max(2f, detectRadius);
        leashRange = Mathf.Max(detectionRange + 4f, maxLeashDistance);
        homePosition = guardPos;
        hasHomeConfigured = true;
        patrolPoints = null;

        lookDirection.y = 0f;
        homeRotation = lookDirection.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(lookDirection.normalized)
            : transform.rotation;
        transform.rotation = homeRotation;
    }

    // Cấu hình quái đi tuần qua lại giữa các điểm và đuổi đánh khi phát hiện người chơi
    public void ConfigurePatrol(Vector3[] waypoints, float detectRadius = 15f, float maxLeashDistance = 30f, float speedMult = 0.55f, float waitTime = 1.8f)
    {
        idleBehavior = IdleBehaviorMode.Patrol;
        isAggroed = false;
        detectionRange = Mathf.Max(2f, detectRadius);
        leashRange = Mathf.Max(detectionRange + 4f, maxLeashDistance);
        patrolSpeedMultiplier = Mathf.Clamp(speedMult, 0.3f, 0.9f);
        patrolWaitDuration = Mathf.Max(0.4f, waitTime);
        patrolPoints = waypoints;
        currentPatrolIndex = 0;
        patrolWaitTimer = 0f;
        patrolStuckTimer = 0f;
        homePosition = (waypoints != null && waypoints.Length > 0) ? waypoints[0] : transform.position;
        homeRotation = transform.rotation;
        hasHomeConfigured = true;
    }

    // Kích hoạt trạng thái truy đuổi và báo động cho đồng minh xung quanh
    public void AlertAggro(bool alertNearbyAllies = true)
    {
        if (isAggroed) return;

        isAggroed = true;
        patrolWaitTimer = 0f;
        patrolStuckTimer = 0f;
        nextDestinationTime = 0f;
        if (animator != null && parryStunTimer <= 0f)
        {
            animator.speed = 1f;
        }

        if (alertNearbyAllies && alertAllyRadius > 0f)
        {
            float sqrAlert = alertAllyRadius * alertAllyRadius;
            Vector3 myPos = cachedTransform != null ? cachedTransform.position : transform.position;
            for (int i = 0; i < ActiveEnemies.Count; i++)
            {
                OrcEnemyAI other = ActiveEnemies[i];
                if (other == null || other == this || other.isAggroed) continue;

                Vector3 otherPos = other.cachedTransform != null ? other.cachedTransform.position : other.transform.position;
                Vector3 diff = otherPos - myPos;
                diff.y = 0f;
                if (diff.sqrMagnitude <= sqrAlert)
                {
                    other.AlertAggro(false);
                }
            }
        }
    }

    // Tìm và neo kẻ địch lên bề mặt NavMesh hợp lệ gần nhất
    public bool EnsureOnNavMesh(float maxSearchDistance = NavMeshSampleRadius)
    {
        if (navAgent == null || !navAgent.isActiveAndEnabled) return false;
        if (navAgent.isOnNavMesh) return true;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, maxSearchDistance, NavMesh.AllAreas))
        {
            navAgent.Warp(hit.position);
            return navAgent.isOnNavMesh;
        }
        return false;
    }

    // Cập nhật tốc độ, sát thương, tầm đánh và kháng đẩy lùi cho từng loại quái hoặc boss
    public void ApplyCombatStats(float speed, int damage, float range, float kbResistance, float cooldown)
    {
        moveSpeed = speed;
        attackDamage = damage;
        attackRange = range;
        stoppingDistance = Mathf.Min(stoppingDistance, Mathf.Max(0.85f, attackRange * 0.80f));
        knockbackResistance = Mathf.Clamp01(kbResistance);
        attackCooldown = cooldown;

        if (navAgent != null)
        {
            navAgent.speed = moveSpeed;
            navAgent.stoppingDistance = GetCombatStoppingDistance();
        }
        if (weaponHitbox != null)
        {
            weaponHitbox.SetDamage(attackDamage);
        }
    }

    void SnapToGround()
    {
        if (TrySampleWalkableGroundY(transform.position, out float groundY))
        {
            Vector3 pos = transform.position;
            pos.y = groundY;
            transform.position = pos;

            if (AgentReady)
            {
                navAgent.Warp(pos);
            }
        }
    }

    private bool TrySampleWalkableGroundY(Vector3 worldPos, out float groundY)
    {
        groundY = worldPos.y;
        Vector3 origin = new Vector3(worldPos.x, worldPos.y + 18f, worldPos.z);
        int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, GroundHitBuffer, 48f, ~0, QueryTriggerInteraction.Ignore);

        float terrainY = float.MinValue;
        bool hasTerrain = false;
        float walkableY = float.MinValue;
        bool hasWalkable = false;

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = GroundHitBuffer[i].collider;
            if (col == null) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

            if (col is TerrainCollider)
            {
                if (GroundHitBuffer[i].point.y > terrainY)
                {
                    terrainY = GroundHitBuffer[i].point.y;
                    hasTerrain = true;
                }
                continue;
            }

            if (col is CharacterController) continue;
            if (IsPlayerCollider(col) || IsEnemyCollider(col)) continue;

            float ptY = GroundHitBuffer[i].point.y;
            if (ptY <= worldPos.y + 1.35f && ptY > walkableY)
            {
                walkableY = ptY;
                hasWalkable = true;
            }
        }

        if (hasTerrain)
        {
            groundY = hasWalkable ? Mathf.Max(terrainY, walkableY) : terrainY;
            return true;
        }

        if (hasWalkable)
        {
            groundY = walkableY;
            return true;
        }

        return false;
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

    // Điều phối trạng thái đứng chờ, truy đuổi, tách bầy và ra đòn theo khoảng cách
    void Update()
    {
        if (Time.deltaTime <= 0f) return;

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
            if (player == null || playerHealth == null)
            {
                lastSqrDistToPlayer = float.MaxValue;
                Idle();
                return;
            }
        }

        if (playerHealth.currentHealth <= 0)
        {
            lastSqrDistToPlayer = float.MaxValue;
            Idle();
            return;
        }

        Vector3 toPlayer = player.position - transform.position;
        float verticalDiff = Mathf.Abs(toPlayer.y);
        toPlayer.y = 0f;
        float sqrDistance = toPlayer.sqrMagnitude;
        lastSqrDistToPlayer = sqrDistance;

        if (idleBehavior != IdleBehaviorMode.AlwaysChase)
        {
            if (!isAggroed)
            {
                if (sqrDistance <= detectionRange * detectionRange)
                {
                    AlertAggro(true);
                }
                else
                {
                    if (sqrDistance > 36f * 36f)
                    {
                        if (Time.time >= nextDistantNonAggroTime)
                        {
                            nextDistantNonAggroTime = Time.time + 0.22f;
                            UpdateNonAggroBehavior();
                        }
                        return;
                    }
                    UpdateNonAggroBehavior();
                    return;
                }
            }
            else if (leashRange > 0f)
            {
                Vector3 toHome = transform.position - homePosition;
                toHome.y = 0f;
                float disengageRange = detectionRange * 1.15f;
                if (toHome.sqrMagnitude > leashRange * leashRange && sqrDistance > disengageRange * disengageRange)
                {
                    isAggroed = false;
                    patrolWaitTimer = 0f;
                    patrolStuckTimer = 0f;
                    nextDestinationTime = 0f;
                    lastPatrolProgressPos = transform.position;
                    UpdateNonAggroBehavior();
                    return;
                }
            }
        }

        if (animator != null && !Mathf.Approximately(animator.speed, 1f))
        {
            animator.speed = 1f;
        }

        if (sqrDistance <= attackRange * attackRange && verticalDiff <= 2.4f)
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

        Vector3 flatDir = toPlayer.sqrMagnitude > 0.0001f ? toPlayer / Mathf.Sqrt(sqrDistance) : transform.forward;
        if (!AgentReady && Time.time >= nextDestinationTime)
        {
            if (EnsureOnNavMesh(6f))
            {
                navMeshFailCount = 0;
            }
            else
            {
                if (navMeshFailCount < 3) navMeshFailCount++;
                nextDestinationTime = Time.time + Mathf.Min(4.0f, 1.5f * Mathf.Max(1, navMeshFailCount));
            }
        }

        if (AgentReady)
        {
            if (navAgent.isStopped) navAgent.isStopped = false;
            if (!Mathf.Approximately(navAgent.speed, moveSpeed)) navAgent.speed = moveSpeed;
            float combatStop = GetCombatStoppingDistance();
            if (!Mathf.Approximately(navAgent.stoppingDistance, combatStop)) navAgent.stoppingDistance = combatStop;

            Vector3 targetPos = player.position;
            if (Time.time >= nextDestinationTime)
            {
                nextDestinationTime = Time.time + DestinationUpdateInterval;
                if ((targetPos - lastDestinationTarget).sqrMagnitude >= 0.49f || !navAgent.hasPath)
                {
                    lastDestinationTarget = targetPos;

                    Vector3 dest = targetPos;
                    if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                    {
                        dest = hit.position;
                    }
                    navAgent.SetDestination(dest);
                }
            }

            if ((transform.position - lastProgressPos).sqrMagnitude > 0.015f)
            {
                lastProgressPos = transform.position;
                stuckTimer = 0f;
            }
            else if (navAgent.hasPath && !navAgent.pathPending && sqrDistance > attackRange * attackRange)
            {
                stuckTimer += Time.deltaTime;
            }

            if (navAgent.pathStatus == NavMeshPathStatus.PathInvalid || stuckTimer > 1.0f)
            {
                MoveWithObstacleAvoidance(flatDir, sqrDistance);
                if (AgentReady)
                {
                    navAgent.Warp(transform.position);
                }
                stuckTimer = 0f;
            }
            return;
        }

        MoveWithObstacleAvoidance(flatDir, sqrDistance);
    }

    private void UpdateNonAggroBehavior()
    {
        float patrolMoveSpeed = Mathf.Max(1.6f, moveSpeed * patrolSpeedMultiplier);

        if (idleBehavior == IdleBehaviorMode.StandGuard)
        {
            Vector3 toHome = homePosition - transform.position;
            toHome.y = 0f;
            if (toHome.sqrMagnitude > 0.85f * 0.85f)
            {
                MoveTowardNonCombatPoint(homePosition, patrolMoveSpeed);
                return;
            }

            StopAgent();
            if (animator != null && !Mathf.Approximately(animator.speed, 1f))
            {
                animator.speed = 1f;
            }
            FaceDirection(homeRotation * Vector3.forward, rotationSpeed * 0.5f);
            PlayAnimation(idleStateHash);
            return;
        }

        EnsurePatrolWaypoints();
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            Idle();
            return;
        }

        if (patrolWaitTimer > 0f)
        {
            patrolWaitTimer -= Time.deltaTime;
            StopAgent();
            if (animator != null && !Mathf.Approximately(animator.speed, 1f))
            {
                animator.speed = 1f;
            }
            PlayAnimation(idleStateHash);
            return;
        }

        if (currentPatrolIndex < 0 || currentPatrolIndex >= patrolPoints.Length)
        {
            currentPatrolIndex = 0;
        }

        Vector3 waypoint = patrolPoints[currentPatrolIndex];
        Vector3 toWaypoint = waypoint - transform.position;
        toWaypoint.y = 0f;

        if (toWaypoint.sqrMagnitude <= 0.95f * 0.95f)
        {
            currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
            patrolWaitTimer = patrolWaitDuration;
            patrolStuckTimer = 0f;
            StopAgent();
            if (animator != null && !Mathf.Approximately(animator.speed, 1f))
            {
                animator.speed = 1f;
            }
            PlayAnimation(idleStateHash);
            return;
        }

        if ((transform.position - lastPatrolProgressPos).sqrMagnitude > 0.01f)
        {
            lastPatrolProgressPos = transform.position;
            patrolStuckTimer = 0f;
        }
        else
        {
            patrolStuckTimer += Time.deltaTime;
            if (patrolStuckTimer > 2.0f)
            {
                currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
                patrolStuckTimer = 0f;
                nextDestinationTime = 0f;
                return;
            }
        }

        MoveTowardNonCombatPoint(waypoint, patrolMoveSpeed);
    }

    private void EnsurePatrolWaypoints()
    {
        if (patrolPoints != null && patrolPoints.Length >= 2) return;

        float offset = Mathf.Max(2.5f, patrolRadius * 0.65f);
        Vector3 p1 = homePosition + Vector3.right * offset;
        Vector3 p2 = homePosition - Vector3.right * offset;
        if (NavMesh.SamplePosition(p1, out NavMeshHit hit1, 6f, NavMesh.AllAreas)) p1 = hit1.position;
        if (NavMesh.SamplePosition(p2, out NavMeshHit hit2, 6f, NavMesh.AllAreas)) p2 = hit2.position;
        patrolPoints = new[] { p1, p2 };
        currentPatrolIndex = 0;
    }

    private void MoveTowardNonCombatPoint(Vector3 targetPoint, float moveSpd)
    {
        if (hasWalkState)
        {
            if (animator != null && !Mathf.Approximately(animator.speed, 1f)) animator.speed = 1f;
            PlayAnimation(walkStateHash);
        }
        else
        {
            if (animator != null && !Mathf.Approximately(animator.speed, 0.72f)) animator.speed = 0.72f;
            PlayAnimation(runStateHash);
        }

        Vector3 toTarget = targetPoint - transform.position;
        toTarget.y = 0f;
        float sqrDistance = toTarget.sqrMagnitude;
        Vector3 flatDir = sqrDistance > 0.0001f ? toTarget / Mathf.Sqrt(sqrDistance) : transform.forward;

        if (AgentReady)
        {
            if (navAgent.isStopped) navAgent.isStopped = false;
            if (!Mathf.Approximately(navAgent.speed, moveSpd)) navAgent.speed = moveSpd;
            if (!Mathf.Approximately(navAgent.stoppingDistance, 0.25f)) navAgent.stoppingDistance = 0.25f;

            if ((targetPoint - lastDestinationTarget).sqrMagnitude >= DestinationMoveThresholdSqr
                || (!navAgent.pathPending && !navAgent.hasPath && Time.time >= nextDestinationTime))
            {
                nextDestinationTime = Time.time + DestinationUpdateInterval;
                lastDestinationTarget = targetPoint;

                Vector3 dest = targetPoint;
                if (NavMesh.SamplePosition(targetPoint, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                {
                    dest = hit.position;
                }
                navAgent.SetDestination(dest);
            }

            if (navAgent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                MoveWithObstacleAvoidance(flatDir, sqrDistance, moveSpd, 0.35f);
                if (AgentReady)
                {
                    navAgent.Warp(transform.position);
                }
            }
            return;
        }

        MoveWithObstacleAvoidance(flatDir, sqrDistance, moveSpd, 0.35f);
    }

    private void MoveWithObstacleAvoidance(Vector3 flatDirToPlayer, float sqrDistance)
    {
        MoveWithObstacleAvoidance(flatDirToPlayer, sqrDistance, moveSpeed, stoppingDistance);
    }

    private void MoveWithObstacleAvoidance(Vector3 flatDirToTarget, float sqrDistance, float speed, float stopDist)
    {
        float now = Time.time;
        if (lastSteerTargetDir.sqrMagnitude < 0.01f
            || now >= nextSteerProbeTime
            || Vector3.Dot(lastSteerTargetDir, flatDirToTarget) < 0.90f)
        {
            cachedSteeringDir = ChooseClearSteeringDirection(flatDirToTarget);
            lastSteerTargetDir = flatDirToTarget;
            nextSteerProbeTime = now + 0.12f;
        }

        Vector3 moveDir = cachedSteeringDir;
        if (moveDir.sqrMagnitude > 0.001f)
        {
            FaceDirection(moveDir, rotationSpeed);

            if (sqrDistance > stopDist * stopDist)
            {
                ApplyFallbackDisplacement(moveDir * (speed * Time.deltaTime));
            }
        }

        if (!hasCachedWalkGroundY || now >= nextGroundProbeTime)
        {
            nextGroundProbeTime = now + 0.10f;
            if (TrySampleWalkableGroundY(transform.position, out float sampledY))
            {
                cachedWalkGroundY = sampledY;
                hasCachedWalkGroundY = true;
            }
        }

        if (hasCachedWalkGroundY)
        {
            Vector3 pos = transform.position;
            pos.y = cachedWalkGroundY;
            transform.position = pos;
        }
    }

    // Dịch chuyển an toàn khi tắt NavMeshAgent: chặn đâm xuyên vật cản và giữ quái trên mặt đất hợp lệ
    private void ApplyFallbackDisplacement(Vector3 displacement)
    {
        Vector3 flatStep = new Vector3(displacement.x, 0f, displacement.z);
        float stepDist = flatStep.magnitude;
        if (stepDist <= 0.0001f) return;

        Vector3 dir = flatStep / stepDist;
        Vector3 probeOrigin = transform.position + Vector3.up * 0.9f;
        if (HasBlockingPropAhead(probeOrigin, dir, Mathf.Max(0.85f, stepDist + 0.5f)))
        {
            return;
        }

        Vector3 targetPos = transform.position + flatStep;
        float now = Time.time;
        if (hasCachedWalkGroundY
            && now < nextGroundProbeTime
            && (targetPos - lastGroundProbePos).sqrMagnitude < 0.36f)
        {
            targetPos.y = cachedWalkGroundY;
            transform.position = targetPos;
            return;
        }

        if (TrySampleWalkableGroundY(targetPos, out float sampledY))
        {
            targetPos.y = sampledY;
            transform.position = targetPos;
            cachedWalkGroundY = sampledY;
            hasCachedWalkGroundY = true;
            lastGroundProbePos = targetPos;
            nextGroundProbeTime = now + 0.12f;
        }
    }

    // Quét tia các góc lân cận để lái quái vòng qua cây cối hoặc đá tảng
    private Vector3 ChooseClearSteeringDirection(Vector3 desiredDir)
    {
        if (desiredDir.sqrMagnitude < 0.0001f) return transform.forward;
        Vector3 origin = transform.position + Vector3.up * 0.9f;
        const float probeDist = 1.65f;

        if (!HasBlockingPropAhead(origin, desiredDir, probeDist))
        {
            return desiredDir;
        }

        for (int i = 0; i < SteeringAngles.Length; i++)
        {
            Vector3 candidate = (Quaternion.Euler(0f, SteeringAngles[i], 0f) * desiredDir).normalized;
            if (!HasBlockingPropAhead(origin, candidate, probeDist))
            {
                return candidate;
            }
        }

        return Vector3.zero;
    }

    private bool HasBlockingPropAhead(Vector3 origin, Vector3 dir, float distance)
    {
        int count = Physics.RaycastNonAlloc(origin, dir, GroundHitBuffer, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = GroundHitBuffer[i].collider;
            if (col == null) continue;
            if (col is TerrainCollider || col is CharacterController) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;
            if (IsPlayerCollider(col) || IsEnemyCollider(col)) continue;
            return true;
        }
        return false;
    }

    // Hiệu chỉnh bám sát bề mặt địa hình mỗi khung hình
    void LateUpdate()
    {
        if (Time.deltaTime <= 0f) return;

        ProcessKnockback();
        if (!isAggroed && lastSqrDistToPlayer > 32f * 32f && knockbackVelocity.sqrMagnitude <= 0.001f)
        {
            return;
        }
        EnforceSeparation();
    }

    // Đẩy lùi kẻ địch khi trúng đòn dựa theo hệ số kháng đẩy lùi
    public void ApplyKnockback(Vector3 attackerPosition, float force, bool applyHitStun = true)
    {
        AlertAggro(true);
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

    // Đưa quái vào trạng thái choáng và ngắt đòn khi người chơi Perfect Parry
    public void ApplyParryStun(Vector3 attackerPosition, float knockbackForce, float duration = -1f)
    {
        AlertAggro(true);
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
            ApplyFallbackDisplacement(displacement);
        }

        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDamping * Time.deltaTime);
    }

    void EnforceSeparation()
    {
        Vector3 separation = Vector3.zero;
        Vector3 pos = cachedTransform != null ? cachedTransform.position : transform.position;
        float minEnemySpacingSqr = minEnemySpacing * minEnemySpacing;

        for (int i = 0; i < ActiveEnemies.Count; i++)
        {
            OrcEnemyAI other = ActiveEnemies[i];
            if (other == null || other == this) continue;

            Vector3 otherPos = other.cachedTransform != null ? other.cachedTransform.position : other.transform.position;
            float dx = pos.x - otherPos.x;
            if (Mathf.Abs(dx) >= minEnemySpacing) continue;
            float dz = pos.z - otherPos.z;
            if (Mathf.Abs(dz) >= minEnemySpacing) continue;

            float sqrDist = dx * dx + dz * dz;
            if (sqrDist < minEnemySpacingSqr)
            {
                float dist = Mathf.Sqrt(sqrDist);
                Vector3 dir = dist > 0.001f ? new Vector3(dx / dist, 0f, dz / dist) : Quaternion.Euler(0f, i * 67f, 0f) * Vector3.forward;
                separation += dir * (minEnemySpacing - dist);
            }
        }

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
            ApplyFallbackDisplacement(step);
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
        if (animator != null && !Mathf.Approximately(animator.speed, 1f))
        {
            animator.speed = 1f;
        }
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

    // Khởi động chuỗi động tác vung rìu/chùy tấn công người chơi
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

    // Đồng bộ thời điểm bật/tắt hitbox với animation vung đòn
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
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }
        if (weaponHitbox != null)
        {
            weaponHitbox.DisableHitbox();
        }
        isAttacking = false;
        hitStunTimer = 0f;
        parryStunTimer = 0f;
        if (animator != null)
        {
            animator.speed = 1f;
        }
    }

    private void OnDestroy()
    {
        if (fallbackOwnColliders == null) return;
        for (int i = 0; i < fallbackOwnColliders.Length; i++)
        {
            if (fallbackOwnColliders[i] != null)
            {
                ExtraEnemyColliderIds.Remove(fallbackOwnColliders[i].GetInstanceID());
            }
        }
    }
}
