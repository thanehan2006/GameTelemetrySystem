
using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    public float moveSpeed = 3f;
    public float attackRange = 1.5f;
    public int attackDamage = 10;
    public float attackCooldown = 1f;

    private Transform player;
    private PlayerHealth playerHealth;
    private float nextAttackTime;

    void Start()
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
        if (player == null || playerHealth == null) return;
        if (playerHealth.currentHealth <= 0) return;

        // Chỉ di chuyển trên mặt phẳng XZ
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;

        float distance = direction.magnitude;

        if (distance > attackRange)
        {
            // Đuổi theo Player
            transform.position += direction.normalized
                                * moveSpeed * Time.deltaTime;

            if (direction.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(direction);
            }
        }
        else
        {
            // Tấn công khi đủ gần
            if (Time.time >= nextAttackTime)
            {
                nextAttackTime = Time.time + attackCooldown;
                playerHealth.TakeDamage(attackDamage);

                Debug.Log("Enemy tan cong Player!");
            }
        }
    }
}
