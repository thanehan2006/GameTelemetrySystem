using UnityEngine;

/// <summary>
/// Bẫy môi trường (Bẫy gai / Bụi gai ở Màn 2).
/// Khi Player giẫm vào bán kính bẫy sẽ chịu sát thương, bị đẩy lùi nhẹ và gửi sự kiện Telemetry "trap_hit".
/// </summary>
public class TrapHazard : MonoBehaviour
{
    [Header("Trap Settings")]
    public int damage = 22;
    public float triggerRadius = 1.65f;
    public float hitCooldown = 1.15f;
    public float knockbackForce = 5.5f;

    private Transform playerTransform;
    private PlayerHealth playerHealth;
    private PlayerController playerController;
    private float nextHitTime;

    void Start()
    {
        // Tắt collider cứng trên model bẫy để Player có thể giẫm lên và dính sát thương mượt mà
        foreach (Collider col in GetComponentsInChildren<Collider>(true))
        {
            col.isTrigger = true;
        }

        ResolvePlayer();
    }

    void Update()
    {
        if (GameOverManager.IsGameOver || LevelUpChoiceManager.IsPaused) return;
        if (Time.time < nextHitTime) return;

        if (playerTransform == null || playerHealth == null)
        {
            ResolvePlayer();
            if (playerTransform == null || playerHealth == null) return;
        }

        if (!playerHealth.IsAlive || playerHealth.IsInvulnerable) return;

        Vector3 diff = playerTransform.position - transform.position;
        float verticalDiff = Mathf.Abs(diff.y);
        diff.y = 0f;

        if (verticalDiff <= 2.2f && diff.sqrMagnitude <= triggerRadius * triggerRadius)
        {
            TriggerTrap();
        }
    }

    private void TriggerTrap()
    {
        nextHitTime = Time.time + hitCooldown;

        playerHealth.TakeDamage(damage);

        Vector3 hitPoint = playerTransform.position + Vector3.up * 0.65f;
        CombatVfxManager.SpawnBloodHit(hitPoint, Vector3.up + (playerTransform.position - transform.position).normalized, true);
        GameAudioManager.PlayTrapHit();

        if (playerController != null)
        {
            playerController.ApplyKnockback(transform.position, knockbackForce);
        }

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackTrapHit(damage);
        }

        if (StageManager.Instance != null)
        {
            StageManager.Instance.ShowBanner($"DÍNH BẪY GAI! (-{damage} HP)", new Color(1f, 0.35f, 0.25f), 1.6f);
        }

        Debug.Log($"[TrapHazard] Player giẫm phải bẫy {gameObject.name}! Gây {damage} sát thương.");
    }

    private void ResolvePlayer()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            playerHealth = playerObj.GetComponent<PlayerHealth>();
            playerController = playerObj.GetComponent<PlayerController>();
        }
    }
}
