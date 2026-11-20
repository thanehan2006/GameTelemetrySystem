using UnityEngine;

/// <summary>
/// Quản lý máu của Player.
/// Tối ưu hóa: kiểm tra trạng thái isDead chuẩn xác, kích hoạt TelemetryManager và GameOverManager.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("Health Settings")]
    public int maxHealth = 100;
    public int currentHealth;

    private bool isDead = false;

    public bool IsAlive => !isDead && currentHealth > 0;

    private void Awake()
    {
        currentHealth = maxHealth;
        isDead = false;
    }

    public void TakeDamage(int damage)
    {
        if (isDead || currentHealth <= 0)
            return;

        currentHealth = Mathf.Max(0, currentHealth - damage);
        Debug.Log($"Player HP: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0 && !isDead)
        {
            Die();
        }
    }

    private void Die()
    {
        isDead = true;
        Debug.Log("PLAYER DEAD!");

        // Ghi nhận sự kiện telemetry khi player hy sinh
        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackPlayerDied();
        }

        // Kích hoạt panel Game Over
        if (GameOverManager.Instance != null)
        {
            GameOverManager.Instance.ShowGameOver();
        }
    }

    public void Heal(int amount)
    {
        if (isDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }
}
