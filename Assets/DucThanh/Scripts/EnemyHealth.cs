using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

// Quản lý lượng máu, thanh máu 3D trên đầu, phản ứng trúng đòn, rơi vàng/bình máu và hoạt ảnh chết của quái
public class EnemyHealth : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int DeathStateHash = Animator.StringToHash("Death");
    private static readonly WaitForSeconds HitFlashWait = new WaitForSeconds(0.1f);
    private const float HpBarVisibleSqrDist = 18f * 18f;

    private static Mesh sharedBarQuadMesh;
    private static Texture2D sharedFrameTex;
    private static Texture2D sharedBgTex;
    private static Texture2D sharedFillTex;
    private static Texture2D sharedPointerTex;
    private static Material sharedFrameMat;
    private static Material sharedBgMat;
    private static Material sharedGhostMat;
    private static Material sharedFillMat;
    private static Material sharedPointerMat;

    [Header("Health Settings")]
    public int maxHealth = 100;
    public int scoreValue = 100;
    public int goldReward = 35;
    public bool isBoss = false;

    [Header("Loot Drop")]
    public GameObject healPickupPrefab;
    [Range(0f, 1f)] public float dropHealChance = 0.22f;

    private int currentHealth;
    private bool isDead = false;
    private Renderer[] cachedRenderers;
    private MaterialPropertyBlock hitFlashBlock;
    private Coroutine hitFlashRoutine;

    private Transform hpBarRoot;
    private Transform borderQuadT;
    private Transform bgQuadT;
    private Transform ghostFillQuadT;
    private Transform fillQuadT;
    private Transform targetPointerT;
    private MeshRenderer borderRenderer;
    private MeshRenderer fillRenderer;
    private MaterialPropertyBlock barPropBlock;
    private float barWidth = 0.78f;
    private float barHeight = 0.060f;
    private float innerWidth = 0.756f;
    private float innerHeight = 0.041f;
    private float pointerBaseY = 0.125f;
    private float hpRatio = 1f;
    private float ghostHpRatio = 1f;
    private float ghostHoldTimer;
    private float hitPulseTimer;
    private float currentBarScaleMult = 1f;
    private bool wasLockedTarget;
    private Transform cachedCamTransform;
    private OrcEnemyAI cachedEnemyAI;

    public int CurrentHealth => currentHealth;
    public bool IsDead => isDead;
    public event Action OnDied;
    public event Action<int, int> OnHealthChanged;

    // Tìm các renderer của kẻ địch, bật render kể cả ngoài vùng bao và khởi tạo thanh máu ban đầu
    private void Awake()
    {
        currentHealth = maxHealth;
        isDead = false;
        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        cachedEnemyAI = GetComponent<OrcEnemyAI>();
        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            if (cachedRenderers[i] is SkinnedMeshRenderer smr)
            {
                smr.updateWhenOffscreen = true;
            }
        }
        hitFlashBlock = new MaterialPropertyBlock();
    }

    // Tạo thanh máu nổi 3D phía trên kẻ địch
    private void Start()
    {
        CreateWorldHealthBar();
        UpdateHealthBarFillImmediate();
    }

    // Cập nhật hướng thanh máu nhìn thẳng về camera và hiệu ứng nháy sát thương
    private void LateUpdate()
    {
        if (hpBarRoot == null) return;
        if (isDead)
        {
            if (hpBarRoot.gameObject.activeSelf) hpBarRoot.gameObject.SetActive(false);
            return;
        }

        if (cachedCamTransform == null && Camera.main != null)
        {
            cachedCamTransform = Camera.main.transform;
        }
        if (cachedCamTransform == null) return;

        bool isLocked = PlayerAttack.CurrentLockedTarget == this;
        Vector3 toCam = cachedCamTransform.position - transform.position;
        toCam.y = 0f;
        bool shouldShow = isLocked || isBoss || currentHealth < maxHealth || toCam.sqrMagnitude <= HpBarVisibleSqrDist;

        if (hpBarRoot.gameObject.activeSelf != shouldShow)
        {
            hpBarRoot.gameObject.SetActive(shouldShow);
        }
        if (!shouldShow) return;

        float dt = Time.deltaTime;

        hpBarRoot.rotation = cachedCamTransform.rotation;

        bool needsColorRefresh = false;
        if (hitPulseTimer > 0f)
        {
            hitPulseTimer = Mathf.Max(0f, hitPulseTimer - dt);
            needsColorRefresh = true;
        }

        float desiredScaleMult = isLocked ? 1.12f : 1.0f;
        currentBarScaleMult = Mathf.MoveTowards(currentBarScaleMult, desiredScaleMult, dt * 3.8f);
        float pulseBoost = hitPulseTimer > 0f ? (hitPulseTimer / 0.12f) * 0.08f : 0f;

        float parentScale = Mathf.Max(0.25f, transform.localScale.y);
        hpBarRoot.localScale = Vector3.one * ((currentBarScaleMult + pulseBoost) / parentScale);

        if (targetPointerT != null)
        {
            if (targetPointerT.gameObject.activeSelf != isLocked)
            {
                targetPointerT.gameObject.SetActive(isLocked);
            }
            if (isLocked)
            {
                float bob = Mathf.Sin(Time.time * 6.5f) * 0.016f;
                targetPointerT.localPosition = new Vector3(0f, pointerBaseY + bob, -0.004f);
            }
        }

        if (isLocked != wasLockedTarget)
        {
            wasLockedTarget = isLocked;
            needsColorRefresh = true;
        }
        else if (hpRatio <= 0.25f)
        {
            needsColorRefresh = true;
        }

        if (needsColorRefresh)
        {
            UpdateBarThemeColors(isLocked);
        }

        if (ghostHpRatio > hpRatio)
        {
            if (ghostHoldTimer > 0f)
            {
                ghostHoldTimer -= dt;
            }
            else
            {
                ghostHpRatio = Mathf.MoveTowards(ghostHpRatio, hpRatio, dt * 1.05f);
                ApplyQuadHorizontalFill(ghostFillQuadT, ghostHpRatio, innerHeight, 0f);
            }
        }
    }

    // Thiết lập máu tối đa và hồi đầy thanh máu khi sinh quái theo màn
    public void SetMaxHealth(int newMaxHealth)
    {
        maxHealth = Mathf.Max(1, newMaxHealth);
        currentHealth = maxHealth;
        hpRatio = 1f;
        ghostHpRatio = 1f;
        UpdateHealthBarFillImmediate();
        UpdateBarThemeColors(PlayerAttack.CurrentLockedTarget == this);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    // Nhận sát thương từ người chơi, bật số sát thương nổi, flash trắng và kiểm tra kết liễu
    public void TakeDamage(int damage)
    {
        if (isDead) return;

        currentHealth = Mathf.Clamp(currentHealth - damage, 0, maxHealth);
        hpRatio = Mathf.Clamp01((float)currentHealth / Mathf.Max(1, maxHealth));
        ghostHoldTimer = 0.16f;
        hitPulseTimer = 0.12f;
        if (fillQuadT != null)
        {
            ApplyQuadHorizontalFill(fillQuadT, hpRatio, innerHeight, -0.002f);
        }
        UpdateBarThemeColors(PlayerAttack.CurrentLockedTarget == this);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0)
        {
            Die();
        }
        else
        {
            if (cachedEnemyAI == null) cachedEnemyAI = GetComponent<OrcEnemyAI>();
            if (cachedEnemyAI != null)
            {
                cachedEnemyAI.AlertAggro(true);
            }
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

    // Cộng điểm, rơi vàng/bình máu, báo telemetry và kích hoạt chuỗi tan rã
    private void Die()
    {
        if (isDead) return;
        isDead = true;
        if (hpBarRoot != null) hpBarRoot.gameObject.SetActive(false);

        if (hitFlashRoutine != null)
        {
            StopCoroutine(hitFlashRoutine);
            hitFlashRoutine = null;
        }
        ClearTintFromRenderers();

        OnHealthChanged?.Invoke(0, maxHealth);
        OnDied?.Invoke();

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

        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.AddScore(scoreValue);
        }

        if (goldReward > 0)
        {
            GoldPickup.SpawnGoldBurst(transform.position, goldReward, isBoss ? 8 : 3, isBoss);
        }

        TrySpawnHealDrop();

        StartCoroutine(DeathSequenceCoroutine());
    }

    // Phát animation ngã hoặc hiệu ứng lún biến mất sau khi chết
    private IEnumerator DeathSequenceCoroutine()
    {

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

        Vector3 vfxCenter = transform.position + Vector3.up * (isBoss ? 1.35f : 0.9f);
        CombatVfxManager.SpawnEnemyDeathEffect(vfxCenter, isBoss);
        GameAudioManager.PlayEnemyDeath(isBoss);

        Animator animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.HasState(0, DeathStateHash))
        {
            animator.speed = 1f;
            animator.Play(DeathStateHash, 0, 0f);
        }

        float fallWait = isBoss ? 1.65f : 1.1f;
        yield return new WaitForSeconds(fallWait);

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

    // Tính tỷ lệ rơi bình hồi máu khi tiêu diệt quái
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
        pickup.healAmount = 30;
    }

    // Dựng thanh máu 3D bằng quad kèm mũi tên chỉ thị mục tiêu đang khóa
    private void CreateWorldHealthBar()
    {
        if (hpBarRoot != null) return;

        EnsureSharedBarResources();
        barPropBlock = new MaterialPropertyBlock();

        barWidth = isBoss ? 1.14f : 0.78f;
        barHeight = isBoss ? 0.080f : 0.060f;
        float framePadX = isBoss ? 0.028f : 0.024f;
        innerWidth = Mathf.Max(0.2f, barWidth - framePadX);
        innerHeight = barHeight * 0.68f;
        pointerBaseY = barHeight * 0.5f + (isBoss ? 0.11f : 0.092f);

        CapsuleCollider col = GetComponent<CapsuleCollider>();
        float localHeadY = col != null ? (col.center.y + col.height * 0.5f + 0.26f) : (isBoss ? 2.40f : 1.98f);
        localHeadY = Mathf.Max(1.25f, localHeadY);

        GameObject rootGo = new GameObject("EnemyOverheadHpBar");
        hpBarRoot = rootGo.transform;
        hpBarRoot.SetParent(transform, false);
        hpBarRoot.localPosition = new Vector3(0f, localHeadY, 0f);
        float parentScale = Mathf.Max(0.25f, transform.localScale.y);
        hpBarRoot.localScale = Vector3.one / parentScale;

        Color defaultBorderCol = isBoss
            ? new Color(0.84f, 0.62f, 0.24f, 0.96f)
            : new Color(0.34f, 0.27f, 0.30f, 0.94f);
        borderQuadT = CreateBarQuad(
            "Border",
            hpBarRoot,
            new Vector3(0f, 0f, 0.004f),
            new Vector3(barWidth + 0.036f, barHeight + 0.034f, 1f),
            sharedFrameMat,
            defaultBorderCol,
            out borderRenderer);

        bgQuadT = CreateBarQuad(
            "Background",
            hpBarRoot,
            new Vector3(0f, 0f, 0.002f),
            new Vector3(innerWidth + 0.008f, innerHeight + 0.008f, 1f),
            sharedBgMat,
            new Color(0.08f, 0.03f, 0.04f, 0.86f),
            out _);

        ghostFillQuadT = CreateBarQuad(
            "GhostDamageFill",
            hpBarRoot,
            new Vector3(0f, 0f, 0f),
            new Vector3(innerWidth, innerHeight, 1f),
            sharedGhostMat,
            new Color(1.0f, 0.86f, 0.48f, 0.92f),
            out _);

        fillQuadT = CreateBarQuad(
            "HpFill",
            hpBarRoot,
            new Vector3(0f, 0f, -0.002f),
            new Vector3(innerWidth, innerHeight, 1f),
            sharedFillMat,
            GetHpFillColor(1f, false, 0f),
            out fillRenderer);

        float pointerSize = isBoss ? 0.145f : 0.115f;
        targetPointerT = CreateBarQuad(
            "TargetPointer",
            hpBarRoot,
            new Vector3(0f, pointerBaseY, -0.004f),
            new Vector3(pointerSize, pointerSize, 1f),
            sharedPointerMat,
            new Color(1.12f, 0.88f, 0.26f, 0.98f),
            out _);
        targetPointerT.gameObject.SetActive(false);
    }

    private void UpdateHealthBarFillImmediate()
    {
        hpRatio = Mathf.Clamp01((float)currentHealth / Mathf.Max(1, maxHealth));
        ghostHpRatio = hpRatio;
        if (fillQuadT != null) ApplyQuadHorizontalFill(fillQuadT, hpRatio, innerHeight, -0.002f);
        if (ghostFillQuadT != null) ApplyQuadHorizontalFill(ghostFillQuadT, ghostHpRatio, innerHeight, 0f);
    }

    private void ApplyQuadHorizontalFill(Transform quadT, float ratio, float height, float zOffset)
    {
        if (quadT == null) return;
        float clamped = Mathf.Clamp01(ratio);
        bool visible = clamped > 0.001f;
        if (quadT.gameObject.activeSelf != visible)
        {
            quadT.gameObject.SetActive(visible);
        }
        if (!visible) return;

        float w = innerWidth * clamped;
        float offsetX = -innerWidth * 0.5f * (1f - clamped);
        quadT.localPosition = new Vector3(offsetX, 0f, zOffset);
        quadT.localScale = new Vector3(Mathf.Max(0.0001f, w), height, 1f);
    }

    private Color GetHpFillColor(float ratio, bool isTargeted, float hitPulseNorm)
    {
        Color highCol = isBoss
            ? new Color(0.95f, 0.22f, 0.18f, 0.98f)
            : new Color(0.88f, 0.17f, 0.22f, 0.98f);
        Color midCol = new Color(0.98f, 0.42f, 0.14f, 0.98f);
        Color lowCol = new Color(1.0f, 0.12f, 0.20f, 0.99f);

        Color baseCol;
        if (ratio >= 0.55f)
        {
            baseCol = highCol;
        }
        else if (ratio >= 0.25f)
        {
            float t = (ratio - 0.25f) / 0.30f;
            baseCol = Color.Lerp(midCol, highCol, t);
        }
        else
        {
            float t = ratio / 0.25f;
            baseCol = Color.Lerp(lowCol, midCol, t);
            float lowPulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 9.5f);
            baseCol = Color.Lerp(baseCol, new Color(1.18f, 0.28f, 0.26f, 1f), lowPulse * 0.28f);
        }

        if (isTargeted)
        {
            baseCol = new Color(
                Mathf.Min(1.25f, baseCol.r * 1.12f),
                Mathf.Min(1.25f, baseCol.g * 1.08f),
                Mathf.Min(1.25f, baseCol.b * 1.04f),
                baseCol.a);
        }

        if (hitPulseNorm > 0f)
        {
            baseCol = Color.Lerp(baseCol, new Color(1.25f, 0.92f, 0.72f, 1f), hitPulseNorm * 0.65f);
        }

        return baseCol;
    }

    private void UpdateBarThemeColors(bool isTargeted)
    {
        if (barPropBlock == null) return;
        float hitPulseNorm = hitPulseTimer > 0f ? Mathf.Clamp01(hitPulseTimer / 0.12f) : 0f;

        if (borderRenderer != null)
        {
            Color borderCol = isTargeted
                ? new Color(1.18f, 0.90f, 0.28f, 0.98f)
                : (isBoss ? new Color(0.84f, 0.62f, 0.24f, 0.96f) : new Color(0.34f, 0.27f, 0.30f, 0.94f));
            if (hitPulseNorm > 0f)
            {
                borderCol = Color.Lerp(borderCol, new Color(1.20f, 0.96f, 0.68f, 1f), hitPulseNorm * 0.5f);
            }
            SetQuadColor(borderRenderer, borderCol);
        }

        if (fillRenderer != null)
        {
            SetQuadColor(fillRenderer, GetHpFillColor(hpRatio, isTargeted, hitPulseNorm));
        }
    }

    private Transform CreateBarQuad(string quadName, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, Color color, out MeshRenderer mr)
    {
        GameObject go = new GameObject(quadName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = sharedBarQuadMesh;

        mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        SetQuadColor(mr, color);
        return go.transform;
    }

    private void SetQuadColor(MeshRenderer mr, Color color)
    {
        if (mr == null || barPropBlock == null) return;
        barPropBlock.SetColor(BaseColorId, color);
        barPropBlock.SetColor(ColorId, color);
        mr.SetPropertyBlock(barPropBlock);
    }

    private static void EnsureSharedBarResources()
    {
        if (sharedBarQuadMesh == null)
        {
            sharedBarQuadMesh = new Mesh { name = "EnemyHpBarQuad" };
            sharedBarQuadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f)
            };
            sharedBarQuadMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            sharedBarQuadMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            sharedBarQuadMesh.RecalculateNormals();
        }

        if (sharedFrameTex == null) sharedFrameTex = BuildFrameTexture();
        if (sharedBgTex == null) sharedBgTex = BuildBackgroundTexture();
        if (sharedFillTex == null) sharedFillTex = BuildFillTexture();
        if (sharedPointerTex == null) sharedPointerTex = BuildPointerTexture();

        if (sharedFrameMat == null) sharedFrameMat = CreateTransparentBarMaterial("EnemyHpBarFrameMat", sharedFrameTex, 3000);
        if (sharedBgMat == null) sharedBgMat = CreateTransparentBarMaterial("EnemyHpBarBgMat", sharedBgTex, 3001);
        if (sharedGhostMat == null) sharedGhostMat = CreateTransparentBarMaterial("EnemyHpBarGhostMat", sharedFillTex, 3002);
        if (sharedFillMat == null) sharedFillMat = CreateTransparentBarMaterial("EnemyHpBarFillMat", sharedFillTex, 3003);
        if (sharedPointerMat == null) sharedPointerMat = CreateTransparentBarMaterial("EnemyHpBarPointerMat", sharedPointerTex, 3004);
    }

    private static Material CreateTransparentBarMaterial(string matName, Texture2D tex, int renderQueue)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        Material mat = new Material(sh) { name = matName };
        if (mat.HasProperty(BaseMapId)) mat.SetTexture(BaseMapId, tex);
        if (mat.HasProperty(MainTexId)) mat.SetTexture(MainTexId, tex);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = renderQueue;
        return mat;
    }

    private static float SdfRoundedBox(float px, float py, float halfW, float halfH, float radius)
    {
        float dx = Mathf.Abs(px) - (halfW - radius);
        float dy = Mathf.Abs(py) - (halfH - radius);
        float outX = Mathf.Max(dx, 0f);
        float outY = Mathf.Max(dy, 0f);
        return Mathf.Sqrt(outX * outX + outY * outY) + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
    }

    private static Texture2D BuildFrameTexture()
    {
        const int w = 128;
        const int h = 32;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
        {
            name = "EnemyHpBarFrameTex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[w * h];
        float halfW = w * 0.5f;
        float halfH = h * 0.5f;

        for (int y = 0; y < h; y++)
        {
            float py = (y + 0.5f) - halfH;
            float vy = (y + 0.5f) / h;
            for (int x = 0; x < w; x++)
            {
                float px = (x + 0.5f) - halfW;
                float dist = SdfRoundedBox(px, py, 59.5f, 11.5f, 6.0f);

                float edgeMask = Mathf.Clamp01(0.5f - dist / 1.3f);
                float shadowAlpha = dist > 0f ? Mathf.Pow(Mathf.Clamp01(1f - dist / 4.0f), 1.6f) * 0.58f : 0f;

                float bevel = Mathf.Lerp(0.54f, 1.0f, Mathf.SmoothStep(0.10f, 0.90f, vy));
                if (vy > 0.78f) bevel = Mathf.Min(1f, bevel + 0.14f);

                float shade = Mathf.Lerp(0.02f, bevel, edgeMask);
                float alpha = Mathf.Max(edgeMask, shadowAlpha);
                pixels[y * w + x] = new Color(shade, shade, shade, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private static Texture2D BuildBackgroundTexture()
    {
        const int w = 128;
        const int h = 32;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
        {
            name = "EnemyHpBarBgTex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[w * h];
        float halfW = w * 0.5f;
        float halfH = h * 0.5f;

        for (int y = 0; y < h; y++)
        {
            float py = (y + 0.5f) - halfH;
            float vy = (y + 0.5f) / h;
            for (int x = 0; x < w; x++)
            {
                float px = (x + 0.5f) - halfW;
                float dist = SdfRoundedBox(px, py, 62.5f, 14.5f, 5.5f);
                float alpha = Mathf.Clamp01(0.5f - dist / 1.2f);
                float recessed = Mathf.Lerp(1.0f, 0.52f, Mathf.Pow(vy, 1.3f));
                pixels[y * w + x] = new Color(recessed, recessed, recessed, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private static Texture2D BuildFillTexture()
    {
        const int w = 128;
        const int h = 32;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
        {
            name = "EnemyHpBarFillTex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[w * h];
        float halfW = w * 0.5f;
        float halfH = h * 0.5f;

        for (int y = 0; y < h; y++)
        {
            float py = (y + 0.5f) - halfH;
            float vy = (y + 0.5f) / h;

            float bodyGrad = Mathf.Lerp(0.62f, 0.98f, Mathf.Pow(vy, 0.65f));
            float topSheen = Mathf.Exp(-Mathf.Pow((vy - 0.78f) / 0.14f, 2f)) * 0.34f;
            float bottomBounce = vy < 0.16f ? (1f - vy / 0.16f) * 0.08f : 0f;

            for (int x = 0; x < w; x++)
            {
                float px = (x + 0.5f) - halfW;
                float vx = (x + 0.5f) / w;
                float dist = SdfRoundedBox(px, py, 63.0f, 14.5f, 4.5f);
                float alpha = Mathf.Clamp01(0.5f - dist / 1.15f);

                float tipLift = vx > 0.88f ? ((vx - 0.88f) / 0.12f) * 0.14f : 0f;
                float lum = Mathf.Clamp01(bodyGrad + bottomBounce + tipLift);
                float r = Mathf.Lerp(lum, 1f, topSheen);
                float g = Mathf.Lerp(lum, 1f, topSheen);
                float b = Mathf.Lerp(lum, 1f, topSheen);
                pixels[y * w + x] = new Color(r, g, b, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private static Texture2D BuildPointerTexture()
    {
        const int size = 48;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            name = "EnemyHpBarPointerTex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            float ny = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float nx = ((x + 0.5f) / size) * 2f - 1f;
                float ax = Mathf.Abs(nx);

                float distBottom = (ax * 0.82f) - (ny - 0.10f);
                float distTop = (ny - 0.86f) + ax * 0.42f;
                float distOuter = Mathf.Max(distBottom, distTop);

                float fillAlpha = Mathf.Clamp01(0.5f - distOuter / 0.055f);
                float outlineAlpha = Mathf.Clamp01(0.5f - (distOuter - 0.085f) / 0.055f);

                if (outlineAlpha <= 0.001f)
                {
                    pixels[y * size + x] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }

                float sheen = Mathf.Lerp(0.76f, 1.0f, Mathf.Clamp01((ny - 0.15f) / 0.65f));
                float core = Mathf.Lerp(0.06f, sheen, fillAlpha);
                pixels[y * size + x] = new Color(core, core, core, outlineAlpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return tex;
    }
}
