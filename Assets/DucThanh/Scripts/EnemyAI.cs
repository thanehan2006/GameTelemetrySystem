using UnityEngine;

/// <summary>
/// AI điều khiển Enemy di chuyển và tấn công Player.
/// Tối ưu hóa: kiểm tra EnemyHealth/PlayerHealth, áp dụng gravity cơ bản tránh lơ lửng, dừng tấn công khi game over.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class EnemyAI : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 3f;
    public float rotationSpeed = 10f;

    [Header("Attack Settings")]
    public float attackRange = 1.5f;
    public int attackDamage = 10;
    public float attackCooldown = 1f;

    private Transform player;
    private PlayerHealth playerHealth;
    private EnemyHealth enemyHealth;
    private float nextAttackTime;

    void Awake()
    {
        enemyHealth = GetComponent<EnemyHealth>();
    }

    void Start()
    {
        FindPlayerTarget();
    }

    private void FindPlayerTarget()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
            playerHealth = playerObject.GetComponent<PlayerHealth>();
        }
    }

    void Update()
    {
        if (GameOverManager.IsGameOver) return;
        if (enemyHealth != null && enemyHealth.IsDead) return;

        if (player == null || playerHealth == null)
        {
            FindPlayerTarget();
            return;
        }

        if (!playerHealth.IsAlive) return;

        // Tính hướng và khoảng cách tới Player trên mặt phẳng XZ
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;

        float distance = direction.magnitude;

        if (distance > attackRange)
        {
            // Đuổi theo Player
            Vector3 moveDir = direction.normalized;
            transform.position += moveDir * (moveSpeed * Time.deltaTime);

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
        }
        else
        {
            // Quay mặt về phía Player và tấn công
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(direction.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }

            if (Time.time >= nextAttackTime)
            {
                nextAttackTime = Time.time + attackCooldown;
                playerHealth.TakeDamage(attackDamage);
                Debug.Log($"[EnemyAI] Tấn công Player! Gây {attackDamage} sát thương.");
            }
        }
    }
}
