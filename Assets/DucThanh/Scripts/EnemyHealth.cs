using System;
using UnityEngine;

/// <summary>
/// Quản lý máu của Enemy.
/// Tối ưu hóa: thêm IsDead property, event OnDied, ghi nhận Telemetry và Score an toàn.
/// </summary>
public class EnemyHealth : MonoBehaviour
{
    [Header("Health Settings")]
    public int maxHealth = 100;
    public int scoreValue = 100;

    private int currentHealth;
    private bool isDead = false;

    public bool IsDead => isDead;
    public event Action OnDied;

    private void Awake()
    {
        currentHealth = maxHealth;
        isDead = false;
    }

    public void SetMaxHealth(int newMaxHealth)
    {
        maxHealth = Mathf.Max(1, newMaxHealth);
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (isDead) return;

        currentHealth = Mathf.Clamp(currentHealth - damage, 0, maxHealth);
        Debug.Log($"Enemy HP: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        OnDied?.Invoke();

        // Ghi nhận sự kiện EnemyKilled cho hệ thống Telemetry
        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackEnemyKilled();
        }
        else
        {
            Debug.LogWarning("[EnemyHealth] Chưa có TelemetryManager trong Scene!");
        }

        // Cộng điểm
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.AddScore(scoreValue);
        }

        // Hủy GameObject (có thể có delay nhỏ nếu cần hiệu ứng)
        Destroy(gameObject);
    }
}
