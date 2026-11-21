using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Quản lý máu của Enemy (hỗ trợ cả quái thường, Elite và Boss).
/// Ghi nhận Telemetry (enemy_killed / boss_killed), cộng EXP/Score, rơi nấm hồi máu,
/// chớp đỏ khi trúng đòn và phát chuỗi hiệu ứng ngã gục + tan biến linh hồn khi chết.
/// </summary>
public class EnemyHealth : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int DeathStateHash = Animator.StringToHash("Death");
    private static readonly WaitForSeconds HitFlashWait = new WaitForSeconds(0.1f);

    [Header("Health Settings")]
    public int maxHealth = 100;
    public int scoreValue = 100;
    public int goldReward = 35;
    public bool isBoss = false;

    [Header("Loot Drop")]
    public GameObject healPickupPrefab;
    [Range(0f, 1f)] public float dropHealChance = 0.28f;

    private int currentHealth;
    private bool isDead = false;
    private Renderer[] cachedRenderers;
    private MaterialPropertyBlock hitFlashBlock;
    private Coroutine hitFlashRoutine;

    public int CurrentHealth => currentHealth;
    public bool IsDead => isDead;
    public event Action OnDied;
    public event Action<int, int> OnHealthChanged;

    private void Awake()
    {
        currentHealth = maxHealth;
        isDead = false;
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        hitFlashBlock = new MaterialPropertyBlock();
    }

    public void SetMaxHealth(int newMaxHealth)
    {
        maxHealth = Mathf.Max(1, newMaxHealth);
        currentHealth = maxHealth;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void TakeDamage(int damage)
    {
        if (isDead) return;

        currentHealth = Mathf.Clamp(currentHealth - damage, 0, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0)
        {
            Die();
        }
        else
        {
            TriggerHitFlash();
        }
    }

    private void TriggerHitFlash()
    {
        if (cachedRenderers == null || cachedRenderers.Length == 0) return;
        if (hitFlashRoutine != null) StopCoroutine(hitFlashRoutine);
        hitFlashRoutine = StartCoroutine(HitFlashCoroutine());
    }

    private IEnumerator HitFlashCoroutine()
    {
        ApplyTintToRenderers(new Color(1f, 0.32f, 0.28f, 1f));
        yield return HitFlashWait;
        ClearTintFromRenderers();
        hitFlashRoutine = null;
    }

    private void ApplyTintToRenderers(Color tint)
    {
        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            Renderer r = cachedRenderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(hitFlashBlock);
            hitFlashBlock.SetColor(BaseColorId, tint);
            hitFlashBlock.SetColor(ColorId, tint);
            r.SetPropertyBlock(hitFlashBlock);
        }
    }

    private void ClearTintFromRenderers()
    {
        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            Renderer r = cachedRenderers[i];
            if (r == null) continue;
            r.SetPropertyBlock(null);
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        if (hitFlashRoutine != null)
        {
            StopCoroutine(hitFlashRoutine);
            hitFlashRoutine = null;
        }
        ClearTintFromRenderers();

        OnHealthChanged?.Invoke(0, maxHealth);
        OnDied?.Invoke();

        // Ghi nhận sự kiện Telemetry cho Đề 9
        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackEnemyKilled(scoreValue);
            if (isBoss)
            {
                TelemetryManager.Instance.TrackBossKilled(TelemetryManager.Instance.CurrentLevel);
            }
        }
        else
        {
            Debug.LogWarning("[EnemyHealth] Chưa có TelemetryManager trong Scene!");
        }

        // Cộng điểm & Rơi Vàng để Player nhặt mua vũ khí trong Cửa Hàng [B]
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.AddScore(scoreValue);
        }

        if (goldReward > 0)
        {
            GoldPickup.SpawnGoldBurst(transform.position, goldReward, isBoss ? 8 : 3, isBoss);
        }

        // Tỷ lệ rơi nấm hồi máu sau khi hạ quái thường
        TrySpawnHealDrop();

        // Chạy chuỗi hiệu ứng chết (nổ máu + khói linh hồn + animation ngã + tan biến)
        StartCoroutine(DeathSequenceCoroutine());
    }

    private IEnumerator DeathSequenceCoroutine()
    {
        // Ngắt AI, NavMesh, Hitbox và toàn bộ Collider ngay lập tức để không cản đường hay gây sát thương
        OrcEnemyAI ai = GetComponent<OrcEnemyAI>();
        if (ai != null) ai.enabled = false;

        NavMeshAgent agent = GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;

        foreach (OrcWeaponHitbox hitbox in GetComponentsInChildren<OrcWeaponHitbox>(true))
        {
            hitbox.DisableHitbox();
            hitbox.enabled = false;
        }

        foreach (Collider col in GetComponentsInChildren<Collider>(true))
        {
            col.enabled = false;
        }

        // Bùng hiệu ứng chết (máu + khói linh hồn + hạt vàng bay lên + vòng xung kích)
        Vector3 vfxCenter = transform.position + Vector3.up * (isBoss ? 1.35f : 0.9f);
        CombatVfxManager.SpawnEnemyDeathEffect(vfxCenter, isBoss);
        GameAudioManager.PlayEnemyDeath(isBoss);

        // Phát animation ngã gục Death của Orc nếu có
        Animator animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.HasState(0, DeathStateHash))
        {
            animator.speed = 1f;
            animator.Play(DeathStateHash, 0, 0f);
        }

        float fallWait = isBoss ? 1.65f : 1.1f;
        yield return new WaitForSeconds(fallWait);

        // Hạt tan biến cuối cùng rồi chìm mượt xuống đất
        CombatVfxManager.SpawnDissolvePuff(transform.position + Vector3.up * 0.4f, isBoss);

        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + Vector3.down * (isBoss ? 1.6f : 1.1f);
        Vector3 startScale = transform.localScale;
        float sinkDuration = 0.45f;
        float elapsed = 0f;

        while (elapsed < sinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / sinkDuration);
            transform.position = Vector3.Lerp(startPos, endPos, t);
            transform.localScale = Vector3.Lerp(startScale, startScale * 0.55f, t);
            yield return null;
        }

        Destroy(gameObject);
    }

    private void TrySpawnHealDrop()
    {
        if (isBoss || dropHealChance <= 0f) return;
        if (UnityEngine.Random.value > dropHealChance) return;

        Vector3 dropPos = transform.position + Vector3.up * 0.2f;
        GameObject potionPrefab = HealthPickup.GetPotionPrefab();
        GameObject prefabToUse = potionPrefab != null ? potionPrefab : healPickupPrefab;

        GameObject dropObj = prefabToUse != null
            ? Instantiate(prefabToUse, dropPos, Quaternion.identity)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);

        dropObj.name = "Dropped_HealthPotion";
        if (prefabToUse == potionPrefab && potionPrefab != null)
        {
            dropObj.transform.localScale = Vector3.one * 1.95f;
        }
        else if (prefabToUse == null)
        {
            dropObj.transform.position = dropPos;
            dropObj.transform.localScale = Vector3.one * 0.5f;
            Renderer r = dropObj.GetComponent<Renderer>();
            if (r != null) r.material.color = new Color(0.2f, 0.95f, 0.45f);
        }

        HealthPickup pickup = dropObj.GetComponent<HealthPickup>();
        if (pickup == null) pickup = dropObj.AddComponent<HealthPickup>();
        pickup.healAmount = 35;
    }
}
