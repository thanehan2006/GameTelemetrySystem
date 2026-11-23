using UnityEngine;
using UnityEngine.Rendering;

// Cụm bẫy gai gây sát thương định kỳ khi người chơi bước vào vùng nguy hiểm
public class TrapHazard : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Trap Settings")]
    public int damage = 18;
    public float triggerRadius = 1.65f;
    public float hitCooldown = 1.25f;
    public float knockbackForce = 5.5f;

    private Transform playerTransform;
    private PlayerHealth playerHealth;
    private PlayerController playerController;
    private Light childLight;
    private MeshRenderer ringRenderer;
    private MaterialPropertyBlock ringPropBlock;
    private bool ringHasBaseColor;
    private bool ringHasColor;
    private float nextHitTime;
    private float pulsePhase;

    // Tìm người chơi và dựng vòng cảnh báo phát sáng đỏ quanh bẫy
    void Start()
    {
        pulsePhase = Random.Range(0f, Mathf.PI * 2f);
        childLight = GetComponentInChildren<Light>();

        foreach (Collider col in GetComponentsInChildren<Collider>(true))
        {
            col.isTrigger = true;
        }

        CreateDangerRingVisual();
        ResolvePlayer();
    }

    // Kiểm tra người chơi dẫm vào bán kính bẫy sau mỗi khoảng hồi
    void Update()
    {
        if (GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen) return;

        if (playerTransform == null || playerHealth == null)
        {
            ResolvePlayer();
            if (playerTransform == null || playerHealth == null) return;
        }

        Vector3 diff = playerTransform.position - transform.position;
        float verticalDiff = Mathf.Abs(diff.y);
        diff.y = 0f;
        float horizSqrDist = diff.sqrMagnitude;

        const float activeRadiusSqr = 30f * 30f;
        bool inRange = horizSqrDist <= activeRadiusSqr;
        if (childLight != null && childLight.enabled != inRange)
        {
            childLight.enabled = inRange;
        }
        if (!inRange) return;

        float now = Time.time;

        if (ringRenderer != null && ringPropBlock != null)
        {
            float pulse = 0.28f + 0.22f * Mathf.Sin(now * 4.2f + pulsePhase);
            Color c = new Color(2.0f, 0.24f, 0.14f, pulse);
            if (ringHasBaseColor) ringPropBlock.SetColor(BaseColorId, c);
            if (ringHasColor) ringPropBlock.SetColor(ColorId, c);
            ringRenderer.SetPropertyBlock(ringPropBlock);
        }

        if (now < nextHitTime) return;
        if (!playerHealth.IsAlive || playerHealth.IsInvulnerable) return;

        if (verticalDiff <= 2.2f && horizSqrDist <= triggerRadius * triggerRadius)
        {
            TriggerTrap();
        }
    }

    // Vẽ vòng tròn phát sáng dưới mặt đất để người chơi nhận biết vùng bẫy
    private void CreateDangerRingVisual()
    {
        if (transform.Find("TrapDangerRing") != null) return;

        float invScale = transform.localScale.x > 0.01f ? 1f / transform.localScale.x : 1f;
        GameObject ring = new GameObject("TrapDangerRing");
        ring.transform.SetParent(transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.12f * invScale, 0f);
        float d = triggerRadius * 1.95f * invScale;
        ring.transform.localScale = new Vector3(d, 1f * invScale, d);

        MeshFilter mf = ring.AddComponent<MeshFilter>();
        mf.sharedMesh = CombatVfxManager.GetHorizontalQuadMesh();
        MeshRenderer mr = ring.AddComponent<MeshRenderer>();
        Material sharedRingMat = CombatVfxManager.GetAdditiveRingMaterial();
        mr.sharedMaterial = sharedRingMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;

        ringRenderer = mr;
        ringPropBlock = new MaterialPropertyBlock();
        ringHasBaseColor = sharedRingMat != null && sharedRingMat.HasProperty(BaseColorId);
        ringHasColor = sharedRingMat != null && sharedRingMat.HasProperty(ColorId);

        Color c = new Color(2.0f, 0.24f, 0.14f, 0.4f);
        if (ringHasBaseColor) ringPropBlock.SetColor(BaseColorId, c);
        if (ringHasColor) ringPropBlock.SetColor(ColorId, c);
        ringRenderer.SetPropertyBlock(ringPropBlock);
    }

    // Trừ máu người chơi, phát hiệu ứng va chạm bẫy và gửi sự kiện trap_hit
    private void TriggerTrap()
    {
        nextHitTime = Time.time + hitCooldown;

        playerHealth.TakeDamage(damage);

        Vector3 hitPoint = playerTransform.position + Vector3.up * 0.65f;
        CombatVfxManager.SpawnTrapTriggerEffect(transform.position);
        CombatVfxManager.SpawnBloodHit(hitPoint, Vector3.up + (playerTransform.position - transform.position).normalized, true);
        CombatVfxManager.SpawnFloatingText(playerTransform.position + Vector3.up * 1.9f, $"-{damage} HP", new Color(1f, 0.28f, 0.22f), 1.02f, 0.9f);
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
    }

    private void ResolvePlayer()
    {
        if (PlayerController.Instance != null)
        {
            playerController = PlayerController.Instance;
            playerTransform = playerController.transform;
            playerHealth = PlayerHealth.Instance != null ? PlayerHealth.Instance : playerController.GetComponent<PlayerHealth>();
            return;
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            playerHealth = playerObj.GetComponent<PlayerHealth>();
            playerController = playerObj.GetComponent<PlayerController>();
        }
    }
}
