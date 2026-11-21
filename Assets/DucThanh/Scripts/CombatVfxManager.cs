using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Hệ thống hiệu ứng hình ảnh (AAA Combat & World VFX) chuẩn URP HDR:
/// - Tự động thiết lập URP Global Volume (Bloom + ACES Tonemapping + Vignette tinh tế) khi scene chưa có.
/// - Tách biệt shader Additive HDR (phát sáng rực rỡ cho tia lửa, chém kiếm, Parry, hồi máu, vàng, trụ sáng)
///   và Alpha-Blend (máu bắn, sương máu, khói linh hồn, bụi đất).
/// - Micro Hit-Stop (khựng đòn chuẩn Action RPG) + Screen Edge Flash đa màu (Đỏ sát thương / Vàng Parry / Xanh hồi máu).
/// - World-Space Floating Combat Numbers (+HP, +Gold, Damage, PARRY!).
/// - Đầy đủ hiệu ứng: Chém 2 lớp theo 3 chiêu, Máu + Star Flare va chạm, Parry hoàn hảo,
///   Hồi máu xoắn ốc, Nhặt vàng lấp lánh, Vòng triệu hồi quái, Boss Cuồng Nộ, Trại Lửa Checkpoint,
///   Rèn/Trang bị vũ khí, Bẫy gai, Bụi bước chân, Pháo hoa chiến thắng.
/// </summary>
public class CombatVfxManager : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int BlendId = Shader.PropertyToID("_Blend");
    private static readonly int BlendOpId = Shader.PropertyToID("_BlendOp");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int SrcBlendAlphaId = Shader.PropertyToID("_SrcBlendAlpha");
    private static readonly int DstBlendAlphaId = Shader.PropertyToID("_DstBlendAlpha");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly int CullId = Shader.PropertyToID("_Cull");

    private static readonly WaitForSeconds WaitSlashNormal = new WaitForSeconds(0.09f);
    private static readonly WaitForSeconds WaitSlashSpin = new WaitForSeconds(0.12f);

    private const float FloatingTextBaseFontSize = 2.35f;
    private const int MaxConcurrentFlashLights = 3;

    private static CombatVfxManager instance;

    private Material additiveSoftMat;
    private Material additiveSparkMat;
    private Material additiveRingMat;
    private Material additiveSlashMat;
    private Material additiveStarMat;
    private Material additiveBeamMat;
    private Material alphaSoftMat;
    private Material alphaDropletMat;
    private Material floatingTextSharedMat;
    private Texture2D softTex;
    private Texture2D sparkTex;
    private Texture2D ringTex;
    private Texture2D slashTex;
    private Texture2D starTex;
    private Texture2D beamTex;
    private Texture2D vignetteTex;
    private Sprite vignetteSprite;
    private VolumeProfile runtimeVolumeProfile;
    private Mesh horizontalQuadMesh;

    private readonly Queue<FloatingTextAnim> floatingTextPool = new Queue<FloatingTextAnim>(24);
    private readonly Queue<Light> flashLightPool = new Queue<Light>(8);
    private int activeFlashLights;
    private ParticleSystem footstepDustPs;
    private Transform cachedMainCamTransform;

    private Image screenFlashImage;
    private Color screenFlashColor = new Color(0.85f, 0.04f, 0.04f, 0f);
    private float screenFlashAlpha;
    private float screenFlashFadeSpeed = 1.6f;

    private Coroutine hitStopRoutine;
    private bool postProcessingChecked;

    public static CombatVfxManager EnsureInstance()
    {
        if (instance != null) return instance;

        instance = FindFirstObjectByType<CombatVfxManager>();
        if (instance == null)
        {
            GameObject go = new GameObject("CombatVfxManager");
            instance = go.AddComponent<CombatVfxManager>();
        }

        instance.EnsureMaterials();
        instance.EnsurePostProcessing();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        EnsureMaterials();
        EnsurePostProcessing();
    }

    private void Update()
    {
        if (screenFlashImage != null && screenFlashAlpha > 0.001f)
        {
            screenFlashAlpha = Mathf.MoveTowards(screenFlashAlpha, 0f, Time.unscaledDeltaTime * screenFlashFadeSpeed);
            Color c = screenFlashColor;
            c.a = screenFlashAlpha;
            screenFlashImage.color = c;

            if (screenFlashAlpha <= 0.001f)
            {
                screenFlashImage.enabled = false;
            }
        }
    }

    private void OnDestroy()
    {
        if (hitStopRoutine != null && !GameOverManager.IsGameOver && !WeaponShopManager.IsShopOpen)
        {
            Time.timeScale = 1f;
        }

        DestroyIfNotNull(additiveSoftMat);
        DestroyIfNotNull(additiveSparkMat);
        DestroyIfNotNull(additiveRingMat);
        DestroyIfNotNull(additiveSlashMat);
        DestroyIfNotNull(additiveStarMat);
        DestroyIfNotNull(additiveBeamMat);
        DestroyIfNotNull(alphaSoftMat);
        DestroyIfNotNull(alphaDropletMat);
        DestroyIfNotNull(floatingTextSharedMat);

        DestroyIfNotNull(softTex);
        DestroyIfNotNull(sparkTex);
        DestroyIfNotNull(ringTex);
        DestroyIfNotNull(slashTex);
        DestroyIfNotNull(starTex);
        DestroyIfNotNull(beamTex);
        DestroyIfNotNull(vignetteSprite);
        DestroyIfNotNull(vignetteTex);
        DestroyIfNotNull(runtimeVolumeProfile);
        DestroyIfNotNull(horizontalQuadMesh);

        floatingTextPool.Clear();
        flashLightPool.Clear();

        if (instance == this)
        {
            instance = null;
        }
    }

    private static void DestroyIfNotNull(UnityEngine.Object obj)
    {
        if (obj != null) Destroy(obj);
    }

    private Transform GetMainCameraTransform()
    {
        if (cachedMainCamTransform == null)
        {
            Camera cam = Camera.main;
            if (cam != null) cachedMainCamTransform = cam.transform;
        }
        return cachedMainCamTransform;
    }

    // ==================== SHARED MATERIAL & MESH GETTERS ====================

    public static Material GetWeaponTrailMaterial() => EnsureInstance().additiveSoftMat;
    public static Material GetAdditiveRingMaterial() => EnsureInstance().additiveRingMat;
    public static Material GetAdditiveStarMaterial() => EnsureInstance().additiveStarMat;
    public static Material GetAdditiveBeamMaterial() => EnsureInstance().additiveBeamMat;
    public static Mesh GetHorizontalQuadMesh() => EnsureInstance().horizontalQuadMesh;

    // ==================== PUBLIC COMBAT & WORLD VFX API ====================

    public static void SpawnBloodHit(Vector3 position, Vector3 hitDirection, bool heavy = false)
    {
        EnsureInstance().SpawnBloodHitInternal(position, hitDirection, heavy);
    }

    public static void SpawnEnemyDeathEffect(Vector3 position, bool isBoss = false)
    {
        EnsureInstance().SpawnEnemyDeathInternal(position, isBoss);
    }

    public static void SpawnDissolvePuff(Vector3 position, bool isBoss = false)
    {
        EnsureInstance().SpawnDissolvePuffInternal(position, isBoss);
    }

    public static void TriggerPlayerDamageFlash(float intensity = 0.38f)
    {
        TriggerScreenFlash(new Color(0.88f, 0.04f, 0.05f, 1f), intensity, 1.55f);
    }

    public static void TriggerScreenFlash(Color color, float intensity = 0.32f, float fadeSpeed = 1.8f)
    {
        EnsureInstance().TriggerScreenFlashInternal(color, intensity, fadeSpeed);
    }

    public static void TriggerHitStop(float duration = 0.045f, float slowScale = 0.08f)
    {
        EnsureInstance().TriggerHitStopInternal(duration, slowScale);
    }

    public static void SpawnSlashArc(Vector3 playerPos, Vector3 forwardDir, int attackVariant)
    {
        CombatVfxManager mgr = EnsureInstance();
        mgr.StartCoroutine(mgr.SpawnSlashArcCoroutine(playerPos, forwardDir, attackVariant));
    }

    public static void SpawnBlockSpark(Vector3 position, Vector3 normalDir, bool perfectParry = false)
    {
        EnsureInstance().SpawnBlockSparkInternal(position, normalDir, perfectParry);
    }

    public static void SpawnParryStunEffect(Transform enemyTransform, float duration)
    {
        EnsureInstance().SpawnParryStunEffectInternal(enemyTransform, duration);
    }

    public static void SpawnHealEffect(Transform target, int healAmount = 0, bool isParryHeal = false)
    {
        if (target == null) return;
        EnsureInstance().SpawnHealEffectInternal(target, healAmount, isParryHeal);
    }

    public static void SpawnGoldCollectEffect(Vector3 position, int goldAmount)
    {
        EnsureInstance().SpawnGoldCollectInternal(position, goldAmount);
    }

    public static void SpawnEnemySpawnEffect(Vector3 groundPos, bool isBoss = false)
    {
        EnsureInstance().SpawnEnemySpawnInternal(groundPos, isBoss);
    }

    public static void SpawnBossEnrageEffect(Transform bossTransform)
    {
        if (bossTransform == null) return;
        EnsureInstance().SpawnBossEnrageInternal(bossTransform);
    }

    public static void SpawnCheckpointActivateEffect(Vector3 pos)
    {
        EnsureInstance().SpawnCheckpointActivateInternal(pos);
    }

    public static void SpawnWeaponEquipEffect(Transform playerTransform, Color rarityColor)
    {
        if (playerTransform == null) return;
        EnsureInstance().SpawnWeaponEquipInternal(playerTransform, rarityColor);
    }

    public static void SpawnTrapTriggerEffect(Vector3 pos)
    {
        EnsureInstance().SpawnTrapTriggerInternal(pos);
    }

    public static void SpawnFootstepDust(Vector3 groundPos)
    {
        EnsureInstance().SpawnFootstepDustInternal(groundPos);
    }

    public static void SpawnVictoryCelebration(Vector3 centerPos)
    {
        CombatVfxManager mgr = EnsureInstance();
        mgr.StartCoroutine(mgr.VictoryCelebrationCoroutine(centerPos));
    }

    public static void SpawnFloatingText(Vector3 worldPos, string text, Color color, float scale = 1f, float duration = 0.95f)
    {
        if (string.IsNullOrEmpty(text)) return;
        EnsureInstance().SpawnFloatingTextInternal(worldPos, text, color, scale, duration);
    }

    // ==================== URP POST-PROCESSING (BLOOM + ACES) ====================

    private void EnsurePostProcessing()
    {
        if (postProcessingChecked) return;
        postProcessingChecked = true;

        // Kích hoạt Post-Processing trên Main Camera nếu chưa bật
        if (Camera.main != null)
        {
            UniversalAdditionalCameraData camData = Camera.main.GetComponent<UniversalAdditionalCameraData>();
            if (camData != null)
            {
                camData.renderPostProcessing = true;
            }
        }

        // Nếu scene đã có Volume với Bloom rồi thì tôn trọng cấu hình sẵn có
        Volume existingVol = FindFirstObjectByType<Volume>();
        if (existingVol != null && existingVol.sharedProfile != null && existingVol.sharedProfile.Has<Bloom>())
        {
            return;
        }

        GameObject volObj = new GameObject("GlobalVfxVolume");
        volObj.transform.SetParent(transform, false);
        Volume vol = volObj.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 5f;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeVolumeProfile = profile;

        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.95f);
        bloom.intensity.Override(0.42f);
        bloom.scatter.Override(0.58f);
        bloom.highQualityFiltering.Override(false);
        bloom.maxIterations.Override(4);

        Vignette vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.14f);
        vignette.smoothness.Override(0.42f);

        Tonemapping tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);

        ColorAdjustments colorAdj = profile.Add<ColorAdjustments>(true);
        colorAdj.contrast.Override(8f);
        colorAdj.saturation.Override(10f);

        vol.profile = profile;
    }

    // ==================== HIT-STOP & SCREEN FLASH ====================

    private void TriggerHitStopInternal(float duration, float slowScale)
    {
        if (GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen) return;
        if (duration <= 0.005f) return;

        if (hitStopRoutine != null)
        {
            StopCoroutine(hitStopRoutine);
        }
        hitStopRoutine = StartCoroutine(HitStopCoroutine(duration, slowScale));
    }

    private IEnumerator HitStopCoroutine(float duration, float slowScale)
    {
        Time.timeScale = Mathf.Clamp(slowScale, 0.02f, 0.5f);
        yield return new WaitForSecondsRealtime(duration);
        if (!GameOverManager.IsGameOver && !WeaponShopManager.IsShopOpen)
        {
            Time.timeScale = 1f;
        }
        hitStopRoutine = null;
    }

    private void TriggerScreenFlashInternal(Color color, float intensity, float fadeSpeed)
    {
        EnsureScreenFlashOverlay();
        if (screenFlashImage == null) return;

        screenFlashColor = color;
        screenFlashFadeSpeed = Mathf.Max(0.8f, fadeSpeed);
        screenFlashAlpha = Mathf.Clamp(Mathf.Max(screenFlashAlpha * 0.5f, intensity), 0f, 0.62f);
        Color c = screenFlashColor;
        c.a = screenFlashAlpha;
        screenFlashImage.color = c;
        screenFlashImage.enabled = true;
    }

    private void EnsureScreenFlashOverlay()
    {
        if (screenFlashImage != null) return;

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        GameObject go = new GameObject("PlayerScreenVignetteFX", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(canvas.transform, false);
        go.transform.SetAsFirstSibling();

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        screenFlashImage = go.GetComponent<Image>();
        screenFlashImage.sprite = GetOrCreateVignetteSprite();
        screenFlashImage.color = new Color(0.85f, 0.04f, 0.04f, 0f);
        screenFlashImage.raycastTarget = false;
        screenFlashImage.enabled = false;
    }

    // ==================== FLOATING WORLD COMBAT NUMBERS ====================

    private void SpawnFloatingTextInternal(Vector3 worldPos, string text, Color color, float scale, float duration)
    {
        FloatingTextAnim anim = RentFloatingText();
        Vector3 jitter = new Vector3(Random.Range(-0.22f, 0.22f), Random.Range(0f, 0.14f), Random.Range(-0.22f, 0.22f));
        anim.transform.position = worldPos + jitter;
        anim.Play(this, text, color, scale, duration);
    }

    private FloatingTextAnim RentFloatingText()
    {
        while (floatingTextPool.Count > 0)
        {
            FloatingTextAnim pooled = floatingTextPool.Dequeue();
            if (pooled != null)
            {
                pooled.gameObject.SetActive(true);
                return pooled;
            }
        }

        GameObject go = new GameObject("VFX_FloatingText");
        go.transform.SetParent(transform, false);

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.fontSize = FloatingTextBaseFontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.sortingOrder = 50;

        // Dùng chung 1 Material có viền đen duy nhất cho tất cả Floating Text để tránh leak/clone Material mỗi lần đánh
        if (floatingTextSharedMat == null)
        {
            tmp.outlineWidth = 0.22f;
            tmp.outlineColor = new Color32(10, 12, 18, 235);
            floatingTextSharedMat = tmp.fontMaterial;
        }
        else
        {
            tmp.fontSharedMaterial = floatingTextSharedMat;
        }

        FloatingTextAnim anim = go.AddComponent<FloatingTextAnim>();
        anim.Bind(tmp);
        return anim;
    }

    private void ReturnFloatingText(FloatingTextAnim anim)
    {
        if (anim == null) return;
        anim.gameObject.SetActive(false);
        floatingTextPool.Enqueue(anim);
    }

    private class FloatingTextAnim : MonoBehaviour
    {
        private CombatVfxManager owner;
        private TextMeshPro tmp;
        private Color baseColor;
        private float baseScale;
        private float duration;
        private float elapsed;
        private float riseSpeed;

        public void Bind(TextMeshPro textComp)
        {
            tmp = textComp;
        }

        public void Play(CombatVfxManager manager, string text, Color color, float scale, float dur)
        {
            owner = manager;
            if (tmp == null) tmp = GetComponent<TextMeshPro>();
            baseColor = color;
            baseScale = Mathf.Clamp(scale, 0.55f, 1.3f);
            duration = Mathf.Max(0.4f, dur);
            elapsed = 0f;
            riseSpeed = 1.15f;

            if (tmp != null)
            {
                tmp.text = text;
                tmp.color = color;
            }

            transform.localScale = Vector3.one * (0.55f * baseScale);
            Transform camT = owner != null ? owner.GetMainCameraTransform() : null;
            if (camT != null) transform.rotation = camT.rotation;
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            elapsed += dt;
            float t = Mathf.Clamp01(elapsed / duration);

            transform.position += Vector3.up * (riseSpeed * (1f - t * 0.55f) * dt);

            // Pop-in gọn sắc ở 16% đầu, giữ kích thước vừa mắt rồi mờ dần
            float pop = t < 0.16f
                ? Mathf.Lerp(0.55f, 1.12f, t / 0.16f)
                : t < 0.30f
                    ? Mathf.Lerp(1.12f, 1.0f, (t - 0.16f) / 0.14f)
                    : 1.0f;
            transform.localScale = Vector3.one * (baseScale * pop);

            Transform camT = owner != null ? owner.GetMainCameraTransform() : null;
            if (camT != null)
            {
                transform.rotation = camT.rotation;
            }

            if (tmp != null)
            {
                Color c = baseColor;
                c.a = t < 0.62f ? 1f : Mathf.Clamp01(1f - (t - 0.62f) / 0.38f);
                tmp.color = c;
            }

            if (elapsed >= duration)
            {
                if (owner != null) owner.ReturnFloatingText(this);
                else Destroy(gameObject);
            }
        }
    }

    // ==================== BLOOD & IMPACT HIT VFX ====================

    private void SpawnBloodHitInternal(Vector3 position, Vector3 hitDirection, bool heavy)
    {
        if (hitDirection.sqrMagnitude < 0.001f)
            hitDirection = Vector3.up;
        else
            hitDirection = (hitDirection.normalized + Vector3.up * 0.32f).normalized;

        GameObject root = new GameObject("VFX_BloodHit");
        root.transform.position = position;
        root.transform.rotation = Quaternion.LookRotation(hitDirection);

        // 1. Chớp sao va chạm HDR (Impact Star Flare) — cảm giác lưỡi kiếm chạm mục tiêu cực sắc
        {
            GameObject go = new GameObject("ImpactStarFlare");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = heavy ? 0.19f : 0.13f;
            main.startSpeed = 0f;
            main.startSize = heavy ? 1.55f : 1.0f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = heavy
                ? new Color(2.2f, 1.45f, 0.45f, 1f)
                : new Color(1.9f, 1.65f, 0.9f, 0.95f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.35f));

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, new Color(1f, 0.52f, 0.15f), 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveStarMat;

            ps.Play();
        }

        // 2. Vòng xung kích va chạm sắc gọn (Impact Shock Ring)
        {
            GameObject go = new GameObject("ImpactShockRing");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.22f;
            main.loop = false;
            main.startLifetime = heavy ? 0.24f : 0.17f;
            main.startSpeed = 0f;
            main.startSize = heavy ? 1.9f : 1.2f;
            main.startColor = heavy
                ? new Color(1.8f, 0.72f, 0.22f, 0.95f)
                : new Color(1.6f, 1.15f, 0.45f, 0.85f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.15f, 1f, 1f));

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, new Color(1f, 0.35f, 0.12f), 0.9f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveRingMat;

            ps.Play();
        }

        // 3. Tia lửa kim loại HDR bắn tóe cực nhanh (Additive Sparks)
        {
            GameObject go = new GameObject("ImpactSparks");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.3f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, heavy ? 0.32f : 0.22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(heavy ? 6.5f : 5.0f, heavy ? 13.5f : 10.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, heavy ? 0.13f : 0.095f);
            main.gravityModifier = 0.75f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(2.0f, 1.8f, 0.95f, 1f),
                new Color(1.9f, 0.75f, 0.22f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, heavy ? 22 : 12) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = heavy ? 52f : 42f;
            shape.radius = 0.09f;

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, new Color(1f, 0.42f, 0.1f), 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveSparkMat;
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.09f;
            rend.lengthScale = 2.4f;

            ps.Play();
        }

        // 4. Giọt máu đậm bắn theo quán tính lưỡi chém (Alpha-Blend)
        {
            GameObject go = new GameObject("BloodDroplets");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.65f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, heavy ? 0.68f : 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(heavy ? 4.4f : 3.2f, heavy ? 9.0f : 6.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, heavy ? 0.24f : 0.17f);
            main.gravityModifier = 1.75f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 0.02f, 0.03f, 1f),
                new Color(0.95f, 0.06f, 0.06f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, heavy ? 32 : 20) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = heavy ? 44f : 34f;
            shape.radius = 0.1f;

            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.18f));

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(new Color(0.88f, 0.05f, 0.05f), new Color(0.28f, 0.01f, 0.02f), 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = alphaDropletMat;
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.065f;
            rend.lengthScale = 1.75f;

            ps.Play();
        }

        // 5. Sương máu đậm bùng tại điểm trúng đòn
        {
            GameObject go = new GameObject("BloodMist");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.45f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, heavy ? 0.85f : 0.6f);
            main.gravityModifier = 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.62f, 0.02f, 0.04f, 0.72f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, heavy ? 10 : 6) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.18f;

            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.5f));

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(new Color(0.68f, 0.03f, 0.04f), new Color(0.22f, 0.01f, 0.02f), 0.75f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = alphaSoftMat;

            ps.Play();
        }

        StartCoroutine(FlashPointLight(position, heavy ? new Color(1f, 0.55f, 0.18f) : new Color(1f, 0.78f, 0.42f), heavy ? 5.2f : 3.2f, heavy ? 5.5f : 3.6f, heavy ? 0.18f : 0.12f));
        Destroy(root, 1.25f);
    }

    // ==================== ENEMY / BOSS DEATH VFX ====================

    private void SpawnEnemyDeathInternal(Vector3 position, bool isBoss)
    {
        float scaleMul = isBoss ? 1.95f : 1.05f;
        GameObject root = new GameObject(isBoss ? "VFX_BossDeath" : "VFX_EnemyDeath");
        root.transform.position = position;

        // 1. Nổ máu đậm
        {
            GameObject go = new GameObject("DeathBloodBurst");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.9f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, isBoss ? 1.0f : 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.0f * scaleMul, 7.8f * scaleMul);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f * scaleMul, 0.28f * scaleMul);
            main.gravityModifier = 1.55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.52f, 0.02f, 0.03f, 1f),
                new Color(0.95f, 0.08f, 0.06f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, isBoss ? 64 : 32) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.38f * scaleMul;

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(new Color(0.88f, 0.05f, 0.05f), new Color(0.25f, 0.01f, 0.02f), 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = alphaDropletMat;
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.055f;
            rend.lengthScale = 1.6f;

            ps.Play();
        }

        // 2. Khói linh hồn tối tan rã
        {
            GameObject go = new GameObject("SoulSmoke");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, isBoss ? 1.8f : 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.2f * scaleMul);
            main.startSize = new ParticleSystem.MinMaxCurve(0.75f * scaleMul, 1.5f * scaleMul);
            main.gravityModifier = -0.24f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = isBoss
                ? new Color(0.28f, 0.03f, 0.05f, 0.76f)
                : new Color(0.12f, 0.06f, 0.09f, 0.7f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, isBoss ? 28 : 16) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.48f * scaleMul;

            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.55f, 1f, 1.85f));

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(new Color(0.24f, 0.05f, 0.07f), new Color(0.05f, 0.03f, 0.06f), 0.76f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = alphaSoftMat;

            ps.Play();
        }

        // 3. Hạt linh hồn vàng HDR bay xoáy lên trời (Additive Glow)
        {
            GameObject go = new GameObject("SoulEmbers");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1.7f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, isBoss ? 2.3f : 1.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 4.4f * scaleMul);
            main.startSize = new ParticleSystem.MinMaxCurve(0.11f * scaleMul, 0.28f * scaleMul);
            main.gravityModifier = -0.62f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = isBoss
                ? new ParticleSystem.MinMaxGradient(new Color(2.1f, 1.65f, 0.45f, 1f), new Color(2.1f, 0.55f, 0.2f, 1f))
                : new ParticleSystem.MinMaxGradient(new Color(1.9f, 1.55f, 0.55f, 1f), new Color(1.8f, 0.85f, 0.22f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.04f, isBoss ? 68 : 28) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 40f;
            shape.radius = 0.52f * scaleMul;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.85f;
            noise.frequency = 1.6f;

            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(new Color(1f, 0.95f, 0.55f), new Color(1f, 0.45f, 0.1f), 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveStarMat;

            ps.Play();
        }

        // 4. Vòng sóng xung kích kép trên mặt đất (HDR Additive)
        SpawnGroundShockwave(root.transform, new Vector3(0f, -0.7f, 0f),
            isBoss ? 9.5f : 3.8f, isBoss ? 0.8f : 0.48f,
            isBoss ? new Color(2.1f, 0.65f, 0.2f, 0.95f) : new Color(1.8f, 0.55f, 0.22f, 0.85f));

        if (isBoss)
        {
            SpawnGroundShockwave(root.transform, new Vector3(0f, -0.65f, 0f),
                13.5f, 1.05f, new Color(2.2f, 1.55f, 0.35f, 0.9f), 0.14f);

            // Chớp sao khổng lồ cho Boss + Micro Hit-Stop điện ảnh
            StartCoroutine(SpawnVerticalEnergyPillar(position - Vector3.up * 0.7f, new Color(2.0f, 1.3f, 0.3f, 0.85f), 2.2f, 16f, 0.95f));
            TriggerHitStop(0.085f, 0.12f);
            TriggerScreenFlash(new Color(1f, 0.78f, 0.28f, 1f), 0.28f, 1.2f);
        }

        StartCoroutine(FlashPointLight(position + Vector3.up * 0.4f,
            isBoss ? new Color(1f, 0.48f, 0.16f) : new Color(1f, 0.68f, 0.25f),
            isBoss ? 18f : 8f, isBoss ? 12f : 5.2f, isBoss ? 0.75f : 0.38f));

        ThirdPersonCamera cam = ThirdPersonCamera.Instance;
        if (cam != null)
        {
            if (isBoss)
            {
                cam.AddCameraShake(0.42f, 0.55f);
                cam.AddFovPunch(5.5f);
            }
            else
            {
                cam.AddCameraShake(0.09f, 0.12f);
            }
        }

        Destroy(root, isBoss ? 3.5f : 2.3f);
    }

    private void SpawnDissolvePuffInternal(Vector3 position, bool isBoss)
    {
        GameObject root = new GameObject("VFX_DissolvePuff");
        root.transform.position = position;

        // Khói tan biến
        {
            GameObject go = new GameObject("DissolveSmoke");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.8f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, isBoss ? 0.65f : 0.42f);
            main.gravityModifier = -0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.16f, 0.11f, 0.14f, 0.65f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, isBoss ? 18 : 9) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = isBoss ? 1.0f : 0.55f;

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(new Color(0.22f, 0.15f, 0.18f), new Color(0.08f, 0.05f, 0.08f), 0.7f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = alphaSoftMat;
            ps.Play();
        }

        // Hạt tàn tro vàng lấp lánh (Additive)
        {
            GameObject go = new GameObject("DissolveGlints");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.9f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 2.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, isBoss ? 0.24f : 0.16f);
            main.gravityModifier = -0.55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(1.9f, 1.45f, 0.45f, 0.95f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, isBoss ? 24 : 12) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = isBoss ? 0.9f : 0.5f;

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, new Color(1f, 0.55f, 0.15f), 0.95f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveStarMat;
            ps.Play();
        }

        Destroy(root, 1.6f);
    }

    // ==================== DUAL-LAYER HDR SLASH ARC & DASH VFX ====================

    private IEnumerator SpawnSlashArcCoroutine(Vector3 playerPos, Vector3 forwardDir, int attackVariant)
    {
        forwardDir.y = 0f;
        if (forwardDir.sqrMagnitude < 0.001f) forwardDir = Vector3.forward;
        forwardDir.Normalize();

        Quaternion baseRot = Quaternion.LookRotation(forwardDir);
        yield return attackVariant == 2 ? WaitSlashSpin : WaitSlashNormal;

        GameObject root = new GameObject($"VFX_SlashArc_{attackVariant}");
        float heightY = attackVariant == 2 ? 0.58f : 1.16f;
        Vector3 centerPos = playerPos + Vector3.up * heightY + (attackVariant == 2 ? Vector3.zero : forwardDir * 0.48f);
        root.transform.position = centerPos;
        root.transform.rotation = baseRot;

        // Lớp ngoài (Outer Glow Aura) + Lớp lõi trắng sáng sắc nét (Hot Core)
        GameObject outerQuad = CreateSlashQuad("SlashOuter", root.transform, attackVariant == 2 ? additiveRingMat : additiveSlashMat, out Material outerMat);
        GameObject coreQuad = CreateSlashQuad("SlashCore", root.transform, attackVariant == 2 ? additiveRingMat : additiveSlashMat, out Material coreMat);
        coreQuad.transform.localPosition = new Vector3(0f, 0.02f, 0f);

        Color outerStart, outerEnd, coreStart, coreEnd, lightColor;
        float startYaw, endYaw, tiltZ, startScale, endScale, duration;

        if (attackVariant == 2)
        {
            // Atk3: Xoay 360° tầm thấp — vòng lửa hoàng kim-đỏ + sóng mặt đất + bụi & tàn lửa
            outerStart = new Color(2.2f, 0.72f, 0.18f, 0.95f);
            outerEnd = new Color(1.2f, 0.15f, 0.06f, 0f);
            coreStart = new Color(2.4f, 2.0f, 1.15f, 0.95f);
            coreEnd = new Color(1.8f, 0.55f, 0.12f, 0f);
            lightColor = new Color(1f, 0.52f, 0.16f);
            startYaw = -95f;
            endYaw = 305f;
            tiltZ = 0f;
            startScale = 2.8f;
            endScale = 5.1f;
            duration = 0.34f;

            SpawnLowSpinDust(playerPos + Vector3.up * 0.12f);
            SpawnGroundShockwave(root.transform, new Vector3(0f, -0.45f, 0f), 5.6f, 0.36f, new Color(2.0f, 0.85f, 0.25f, 0.85f));
        }
        else if (attackVariant == 1)
        {
            // Atk2: Chém ngang rộng lửa hoàng kim
            outerStart = new Color(2.1f, 1.25f, 0.32f, 0.95f);
            outerEnd = new Color(1.4f, 0.38f, 0.1f, 0f);
            coreStart = new Color(2.5f, 2.3f, 1.65f, 0.95f);
            coreEnd = new Color(2.0f, 0.9f, 0.25f, 0f);
            lightColor = new Color(1f, 0.74f, 0.24f);
            startYaw = -80f;
            endYaw = 92f;
            tiltZ = 6f;
            startScale = 2.9f;
            endScale = 4.35f;
            duration = 0.24f;
        }
        else
        {
            // Atk1: Chém chéo ánh bạc-vàng kim sắc lẹm
            outerStart = new Color(1.55f, 1.85f, 2.15f, 0.92f);
            outerEnd = new Color(1.45f, 0.92f, 0.32f, 0f);
            coreStart = new Color(2.4f, 2.35f, 2.1f, 0.96f);
            coreEnd = new Color(1.9f, 1.25f, 0.45f, 0f);
            lightColor = new Color(1f, 0.92f, 0.68f);
            startYaw = -70f;
            endYaw = 82f;
            tiltZ = -32f;
            startScale = 2.55f;
            endScale = 3.75f;
            duration = 0.22f;
        }

        SpawnSlashWindSparks(centerPos, forwardDir, outerStart, attackVariant == 2);
        StartCoroutine(FlashPointLight(centerPos + forwardDir * 0.6f, lightColor, attackVariant == 2 ? 7.5f : 5.2f, attackVariant == 2 ? 5.2f : 3.4f, duration));

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easeOut = 1f - (1f - t) * (1f - t);

            float yaw = Mathf.Lerp(startYaw, endYaw, easeOut);
            float sc = Mathf.Lerp(startScale, endScale, easeOut);

            outerQuad.transform.localRotation = Quaternion.Euler(0f, yaw, tiltZ);
            outerQuad.transform.localScale = new Vector3(sc, 1f, sc);
            SetMaterialColor(outerMat, Color.Lerp(outerStart, outerEnd, t));

            coreQuad.transform.localRotation = Quaternion.Euler(0f, yaw + 6f, tiltZ);
            coreQuad.transform.localScale = new Vector3(sc * 0.92f, 1f, sc * 0.92f);
            SetMaterialColor(coreMat, Color.Lerp(coreStart, coreEnd, Mathf.Clamp01(t * 1.25f)));

            yield return null;
        }

        Destroy(outerMat);
        Destroy(coreMat);
        Destroy(root);
    }

    private GameObject CreateSlashQuad(string objName, Transform parent, Material templateMat, out Material instanceMat)
    {
        GameObject quad = new GameObject(objName);
        quad.transform.SetParent(parent, false);
        MeshFilter mf = quad.AddComponent<MeshFilter>();
        mf.sharedMesh = horizontalQuadMesh;
        MeshRenderer mr = quad.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        instanceMat = new Material(templateMat);
        mr.material = instanceMat;
        return quad;
    }

    private void SpawnSlashWindSparks(Vector3 pos, Vector3 forward, Color sparkColor, bool is360)
    {
        GameObject go = new GameObject("VFX_SlashSparks");
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(forward);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.36f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.36f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4.2f, is360 ? 10.5f : 7.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.055f, is360 ? 0.14f : 0.11f);
        main.gravityModifier = 0.25f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(sparkColor, new Color(2.2f, 2.1f, 1.7f, 1f));

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, is360 ? 32 : 14) });

        var shape = ps.shape;
        shape.shapeType = is360 ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Cone;
        shape.radius = is360 ? 1.25f : 0.45f;
        if (!is360) shape.angle = 58f;
        else shape.rotation = new Vector3(90f, 0f, 0f);

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        colOver.color = CreateFadeOutGradient(Color.white, sparkColor, 1f);

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.material = additiveSparkMat;
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.08f;
        rend.lengthScale = 2.2f;

        ps.Play();
        Destroy(go, 0.95f);
    }

    private void SpawnLowSpinDust(Vector3 groundPos)
    {
        GameObject root = new GameObject("VFX_LowSpinDust");
        root.transform.position = groundPos;

        // Bụi đất quét tròn
        {
            GameObject go = new GameObject("DustRing");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.65f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.38f, 0.72f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.6f, 6.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 1.05f);
            main.gravityModifier = -0.16f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.76f, 0.66f, 0.50f, 0.58f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.9f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(new Color(0.84f, 0.72f, 0.54f), new Color(0.45f, 0.38f, 0.30f), 0.62f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = alphaSoftMat;
            ps.Play();
        }

        // Tàn lửa xoay theo mặt đất
        {
            GameObject go = new GameObject("SpinEmbers");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.55f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 7.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.gravityModifier = -0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(2.1f, 1.15f, 0.25f, 1f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1.0f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, new Color(1f, 0.38f, 0.08f), 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveStarMat;
            ps.Play();
        }

        Destroy(root, 1.3f);
    }

    // ==================== BLOCK & PERFECT PARRY VFX (SEKIRO-STYLE) ====================

    private void SpawnBlockSparkInternal(Vector3 position, Vector3 normalDir, bool perfectParry)
    {
        normalDir.y = 0f;
        if (normalDir.sqrMagnitude < 0.001f) normalDir = Vector3.forward;
        normalDir.Normalize();

        GameObject root = new GameObject(perfectParry ? "VFX_PerfectParry" : "VFX_BlockSpark");
        root.transform.position = position;
        root.transform.rotation = Quaternion.LookRotation(normalDir);

        // 1. Chớp sao trung tâm rực sáng HDR (Starburst Clash)
        {
            GameObject go = new GameObject("ClashStar");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.28f;
            main.loop = false;
            main.startLifetime = perfectParry ? 0.30f : 0.18f;
            main.startSpeed = 0f;
            main.startSize = perfectParry ? 3.1f : 1.5f;
            main.startColor = perfectParry
                ? new Color(2.5f, 2.05f, 0.65f, 1f)
                : new Color(1.1f, 1.85f, 2.3f, 0.95f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.25f, 1f, 1.25f));

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, perfectParry ? new Color(1f, 0.72f, 0.15f) : new Color(0.45f, 0.85f, 1f), 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveStarMat;
            ps.Play();
        }

        // 2. Tia lửa kim loại HDR bắn tung tóe (48 tia cho Perfect Parry)
        {
            GameObject go = new GameObject("MetallicSparks");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.45f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, perfectParry ? 0.50f : 0.30f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5.5f, perfectParry ? 14.5f : 9.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, perfectParry ? 0.16f : 0.11f);
            main.gravityModifier = 0.95f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = perfectParry
                ? new ParticleSystem.MinMaxGradient(new Color(2.4f, 2.15f, 0.85f, 1f), new Color(2.2f, 1.15f, 0.22f, 1f))
                : new ParticleSystem.MinMaxGradient(new Color(1.1f, 1.95f, 2.3f, 1f), new Color(2.0f, 1.85f, 1.15f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, perfectParry ? 48 : 22) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = perfectParry ? 68f : 55f;
            shape.radius = 0.12f;

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, perfectParry ? new Color(1f, 0.68f, 0.12f) : new Color(0.45f, 0.88f, 1f), 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveSparkMat;
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.09f;
            rend.lengthScale = 2.6f;

            ps.Play();
        }

        // 3. Vòng khiên năng lượng bật ra trước mặt
        {
            GameObject go = new GameObject("GuardRing");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.35f;
            main.loop = false;
            main.startLifetime = perfectParry ? 0.36f : 0.24f;
            main.startSpeed = 0f;
            main.startSize = perfectParry ? 3.2f : 1.75f;
            main.startColor = perfectParry
                ? new Color(2.2f, 1.85f, 0.55f, 0.98f)
                : new Color(0.95f, 1.75f, 2.2f, 0.9f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.18f, 1f, 1f));

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, perfectParry ? new Color(1f, 0.72f, 0.16f) : new Color(0.4f, 0.85f, 1f), 0.95f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveRingMat;
            ps.Play();
        }

        // 4. Riêng Perfect Parry: thêm sóng xung kích mặt đất + chớp viền vàng + Hit-Stop + FOV Punch!
        if (perfectParry)
        {
            SpawnGroundShockwave(root.transform, new Vector3(0f, -0.95f, 0f), 6.2f, 0.42f, new Color(2.2f, 1.8f, 0.45f, 0.95f));
            TriggerScreenFlash(new Color(1f, 0.84f, 0.28f, 1f), 0.28f, 2.4f);
            TriggerHitStop(0.065f, 0.08f);

            if (ThirdPersonCamera.Instance != null)
            {
                ThirdPersonCamera.Instance.AddFovPunch(-3.8f);
            }
        }

        StartCoroutine(FlashPointLight(position,
            perfectParry ? new Color(1f, 0.86f, 0.28f) : new Color(0.55f, 0.88f, 1f),
            perfectParry ? 11f : 6f, perfectParry ? 8.5f : 4.2f, perfectParry ? 0.34f : 0.2f));
        Destroy(root, 1.15f);
    }

    // ==================== PARRY STUN HALO VFX ====================

    private void SpawnParryStunEffectInternal(Transform enemyTransform, float duration)
    {
        if (enemyTransform == null || duration <= 0.05f) return;

        Transform existing = enemyTransform.Find("VFX_ParryStun");
        if (existing != null)
        {
            StunHaloAnim existingAnim = existing.GetComponent<StunHaloAnim>();
            if (existingAnim != null)
            {
                existingAnim.Refresh(duration);
                return;
            }
        }

        GameObject root = new GameObject("VFX_ParryStun");
        root.transform.SetParent(enemyTransform, false);
        root.transform.localPosition = new Vector3(0f, 2.25f, 0f);
        root.transform.localRotation = Quaternion.Euler(14f, 0f, -10f);

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        Color goldColor = new Color(2.2f, 1.8f, 0.4f, 0.95f);
        block.SetColor(BaseColorId, goldColor);
        block.SetColor(ColorId, goldColor);

        CreateStunQuad(root.transform, "StunRing", Vector3.zero, Vector3.one * 0.95f, additiveRingMat, block);

        const float orbitRadius = 0.42f;
        for (int i = 0; i < 3; i++)
        {
            float rad = i * (Mathf.PI * 2f / 3f);
            Vector3 offset = new Vector3(Mathf.Cos(rad) * orbitRadius, 0.02f, Mathf.Sin(rad) * orbitRadius);
            CreateStunQuad(root.transform, $"StunStar_{i}", offset, Vector3.one * 0.28f, additiveStarMat, block);
        }

        StunHaloAnim anim = root.AddComponent<StunHaloAnim>();
        anim.Initialize(enemyTransform.GetComponentInParent<EnemyHealth>(), duration);
    }

    private void CreateStunQuad(Transform parent, string name, Vector3 localPos, Vector3 localScale, Material sharedMat, MaterialPropertyBlock block)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = horizontalQuadMesh;

        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = sharedMat;
        mr.SetPropertyBlock(block);
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    private sealed class StunHaloAnim : MonoBehaviour
    {
        private EnemyHealth enemyHealth;
        private float remaining;
        private float baseY;
        private float elapsed;

        public void Initialize(EnemyHealth health, float duration)
        {
            enemyHealth = health;
            remaining = duration;
            baseY = transform.localPosition.y;
        }

        public void Refresh(float duration)
        {
            remaining = Mathf.Max(remaining, duration);
        }

        private void Update()
        {
            if (enemyHealth != null && enemyHealth.IsDead)
            {
                Destroy(gameObject);
                return;
            }

            float dt = Time.deltaTime;
            remaining -= dt;
            if (remaining <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            elapsed += dt;
            transform.Rotate(Vector3.up, 240f * dt, Space.Self);
            Vector3 p = transform.localPosition;
            p.y = baseY + Mathf.Sin(elapsed * 7f) * 0.05f;
            transform.localPosition = p;
        }
    }

    // ==================== HEAL, GOLD, SPAWN, BOSS, CHECKPOINT, EQUIP, TRAP VFX ====================

    private void SpawnHealEffectInternal(Transform target, int healAmount, bool isParryHeal)
    {
        GameObject root = new GameObject("VFX_HealAura");
        root.transform.SetParent(target, false);
        root.transform.localPosition = Vector3.zero;

        // 1. Các đốm sáng xanh ngọc-vàng bay xoắn quanh nhân vật
        {
            GameObject go = new GameObject("HealWisps");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.up * 0.15f;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.75f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.05f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.11f, 0.26f);
            main.gravityModifier = -0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startColor = isParryHeal
                ? new ParticleSystem.MinMaxGradient(new Color(0.75f, 2.2f, 1.1f, 1f), new Color(2.1f, 1.9f, 0.55f, 1f))
                : new ParticleSystem.MinMaxGradient(new Color(0.45f, 2.2f, 0.95f, 1f), new Color(1.3f, 2.2f, 1.45f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, isParryHeal ? 22 : 30) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.75f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.orbitalY = new ParticleSystem.MinMaxCurve(3.5f);

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, new Color(0.25f, 1f, 0.55f), 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveStarMat;
            ps.Play();
        }

        // 2. Vòng sáng hồi phục lan dưới chân Player
        SpawnGroundShockwave(root.transform, new Vector3(0f, 0.12f, 0f), isParryHeal ? 2.8f : 3.6f, 0.55f,
            new Color(0.55f, 2.1f, 1.05f, 0.9f));

        StartCoroutine(FlashPointLight(target.position + Vector3.up * 1.1f, new Color(0.32f, 1f, 0.58f), 6.5f, 4.5f, 0.45f));

        if (!isParryHeal)
        {
            TriggerScreenFlash(new Color(0.18f, 0.92f, 0.48f, 1f), 0.22f, 1.9f);
        }

        if (healAmount > 0)
        {
            SpawnFloatingTextInternal(target.position + Vector3.up * 2.15f, $"+{healAmount} HP", new Color(0.35f, 1f, 0.58f), isParryHeal ? 1.05f : 1.15f, 1.05f);
        }

        Destroy(root, 1.4f);
    }

    private void SpawnGoldCollectInternal(Vector3 position, int goldAmount)
    {
        GameObject root = new GameObject("VFX_GoldCollect");
        root.transform.position = position;

        // Tia sao vàng lấp lánh tung lên
        {
            GameObject go = new GameObject("GoldStars");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.52f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.0f, 5.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
            main.gravityModifier = 0.25f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(2.3f, 1.9f, 0.45f, 1f),
                new Color(2.4f, 2.25f, 1.35f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.22f;

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, new Color(1f, 0.78f, 0.15f), 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveStarMat;
            ps.Play();
        }

        // Vòng sáng vàng nhỏ
        {
            GameObject go = new GameObject("GoldRing");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.28f;
            main.loop = false;
            main.startLifetime = 0.26f;
            main.startSpeed = 0f;
            main.startSize = 1.45f;
            main.startColor = new Color(2.1f, 1.75f, 0.4f, 0.9f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f));

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, new Color(1f, 0.75f, 0.18f), 0.9f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveRingMat;
            ps.Play();
        }

        if (goldAmount > 0)
        {
            SpawnFloatingTextInternal(position + Vector3.up * 0.65f, $"+{goldAmount} G", new Color(1f, 0.88f, 0.32f), goldAmount >= 40 ? 1.15f : 0.95f, 0.88f);
        }

        StartCoroutine(FlashPointLight(position, new Color(1f, 0.84f, 0.24f), 4.5f, 3.2f, 0.2f));
        Destroy(root, 0.85f);
    }

    private void SpawnEnemySpawnInternal(Vector3 groundPos, bool isBoss)
    {
        GameObject root = new GameObject(isBoss ? "VFX_BossSpawn" : "VFX_EnemySpawn");
        root.transform.position = groundPos;

        Color ringCol = isBoss ? new Color(2.2f, 0.28f, 0.16f, 0.95f) : new Color(1.75f, 0.38f, 0.18f, 0.85f);
        SpawnGroundShockwave(root.transform, new Vector3(0f, 0.14f, 0f), isBoss ? 8.5f : 3.4f, isBoss ? 0.95f : 0.55f, ringCol);

        // Khói & tàn lửa triệu hồi bốc lên từ mặt đất
        {
            GameObject go = new GameObject("SummonEmbers");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.9f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, isBoss ? 1.25f : 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, isBoss ? 5.6f : 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, isBoss ? 0.28f : 0.16f);
            main.gravityModifier = -0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = isBoss
                ? new Color(2.2f, 0.42f, 0.18f, 1f)
                : new Color(1.8f, 0.52f, 0.22f, 0.95f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, isBoss ? 42 : 16) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = isBoss ? 1.6f : 0.75f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(new Color(1f, 0.75f, 0.35f), new Color(1f, 0.15f, 0.08f), 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveStarMat;
            ps.Play();
        }

        if (isBoss)
        {
            StartCoroutine(SpawnVerticalEnergyPillar(groundPos, new Color(2.0f, 0.28f, 0.15f, 0.8f), 2.0f, 14f, 0.95f));
            if (ThirdPersonCamera.Instance != null)
            {
                ThirdPersonCamera.Instance.AddCameraShake(0.28f, 0.4f);
                ThirdPersonCamera.Instance.AddFovPunch(4.0f);
            }
        }

        StartCoroutine(FlashPointLight(groundPos + Vector3.up * 1.1f, new Color(1f, 0.32f, 0.14f), isBoss ? 14f : 6f, isBoss ? 8f : 3.5f, isBoss ? 0.7f : 0.35f));
        Destroy(root, 1.8f);
    }

    private void SpawnBossEnrageInternal(Transform bossTransform)
    {
        Vector3 pos = bossTransform.position;
        GameObject root = new GameObject("VFX_BossEnrageBurst");
        root.transform.position = pos;

        SpawnGroundShockwave(root.transform, new Vector3(0f, 0.18f, 0f), 10.5f, 0.75f, new Color(2.3f, 0.28f, 0.14f, 0.98f));
        SpawnGroundShockwave(root.transform, new Vector3(0f, 0.22f, 0f), 7.2f, 0.55f, new Color(2.3f, 1.15f, 0.25f, 0.92f), 0.1f);
        StartCoroutine(SpawnVerticalEnergyPillar(pos, new Color(2.2f, 0.32f, 0.12f, 0.88f), 2.4f, 15f, 0.9f));
        StartCoroutine(FlashPointLight(pos + Vector3.up * 1.8f, new Color(1f, 0.25f, 0.12f), 18f, 12f, 0.8f));
        TriggerScreenFlash(new Color(0.95f, 0.14f, 0.1f, 1f), 0.32f, 1.3f);

        if (ThirdPersonCamera.Instance != null)
        {
            ThirdPersonCamera.Instance.AddCameraShake(0.36f, 0.48f);
            ThirdPersonCamera.Instance.AddFovPunch(5.0f);
        }

        // Gắn hào quang lửa cuồng nộ cháy liên tục lên Boss cho đến khi bị hạ
        if (bossTransform.Find("BossEnrageAuraFX") == null)
        {
            GameObject aura = new GameObject("BossEnrageAuraFX");
            aura.transform.SetParent(bossTransform, false);
            aura.transform.localPosition = Vector3.up * 0.35f;

            ParticleSystem ps = aura.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 2f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.34f);
            main.gravityModifier = -0.45f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(2.2f, 0.35f, 0.12f, 0.95f),
                new Color(2.3f, 1.25f, 0.25f, 0.95f));

            var emission = ps.emission;
            emission.rateOverTime = 24f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.95f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.65f;
            noise.frequency = 1.4f;

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(new Color(1f, 0.72f, 0.22f), new Color(1f, 0.12f, 0.05f), 0.95f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveSoftMat;
            ps.Play();
        }

        Destroy(root, 2.0f);
    }

    private void SpawnCheckpointActivateInternal(Vector3 pos)
    {
        GameObject root = new GameObject("VFX_CheckpointActivate");
        root.transform.position = pos;

        SpawnGroundShockwave(root.transform, new Vector3(0f, 0.15f, 0f), 8.2f, 0.85f, new Color(2.2f, 1.72f, 0.42f, 0.95f));
        SpawnGroundShockwave(root.transform, new Vector3(0f, 0.18f, 0f), 5.0f, 0.6f, new Color(1.0f, 2.1f, 1.15f, 0.85f), 0.12f);
        StartCoroutine(SpawnVerticalEnergyPillar(pos, new Color(2.1f, 1.65f, 0.45f, 0.78f), 1.65f, 13f, 1.1f));

        // Hạt lửa thánh vàng óng bay lên
        {
            GameObject go = new GameObject("SanctuarySparks");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 4.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
            main.gravityModifier = -0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(2.2f, 1.85f, 0.55f, 1f),
                new Color(2.0f, 1.1f, 0.28f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 38) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.orbitalY = new ParticleSystem.MinMaxCurve(2.2f);

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, new Color(1f, 0.65f, 0.15f), 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveStarMat;
            ps.Play();
        }

        StartCoroutine(FlashPointLight(pos + Vector3.up * 1.4f, new Color(1f, 0.82f, 0.32f), 14f, 8f, 0.85f));
        TriggerScreenFlash(new Color(1f, 0.82f, 0.32f, 1f), 0.18f, 1.4f);
        Destroy(root, 2.4f);
    }

    private void SpawnWeaponEquipInternal(Transform playerTransform, Color rarityColor)
    {
        GameObject root = new GameObject("VFX_WeaponEquip");
        root.transform.SetParent(playerTransform, false);
        root.transform.localPosition = Vector3.zero;

        Color hdrCol = new Color(rarityColor.r * 2.0f, rarityColor.g * 2.0f, rarityColor.b * 2.0f, 1f);
        SpawnGroundShockwave(root.transform, new Vector3(0f, 0.15f, 0f), 4.2f, 0.55f, hdrCol);

        {
            GameObject go = new GameObject("ForgeSparkles");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.up * 0.9f;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.8f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 4.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
            main.gravityModifier = -0.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.startColor = new ParticleSystem.MinMaxGradient(hdrCol, new Color(2.3f, 2.2f, 1.7f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 32) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.7f;

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, rarityColor, 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveStarMat;
            ps.Play();
        }

        StartCoroutine(FlashPointLight(playerTransform.position + Vector3.up * 1.2f, rarityColor, 7f, 5.5f, 0.45f));
        Destroy(root, 1.5f);
    }

    private void SpawnTrapTriggerInternal(Vector3 pos)
    {
        GameObject root = new GameObject("VFX_TrapTrigger");
        root.transform.position = pos;

        SpawnGroundShockwave(root.transform, new Vector3(0f, 0.12f, 0f), 3.2f, 0.35f, new Color(2.1f, 0.26f, 0.16f, 0.92f));

        {
            GameObject go = new GameObject("TrapSparks");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.35f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 8.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(2.2f, 0.35f, 0.18f, 1f),
                new Color(2.2f, 1.35f, 0.35f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 45f;
            shape.radius = 0.45f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveSparkMat;
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.08f;
            rend.lengthScale = 2.0f;
            ps.Play();
        }

        Destroy(root, 0.9f);
    }

    private void SpawnFootstepDustInternal(Vector3 groundPos)
    {
        // Tái sử dụng 1 ParticleSystem duy nhất cho bụi bước chân (0 Instantiate/Destroy khi chạy)
        if (footstepDustPs == null)
        {
            GameObject go = new GameObject("VFX_FootDust_Pooled");
            go.transform.SetParent(transform, false);
            footstepDustPs = go.AddComponent<ParticleSystem>();
            footstepDustPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = footstepDustPs.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = 32;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.44f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.46f);
            main.gravityModifier = -0.08f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.72f, 0.64f, 0.50f, 0.34f);

            var emission = footstepDustPs.emission;
            emission.enabled = false;

            var shape = footstepDustPs.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.2f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var sizeOver = footstepDustPs.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.55f, 1f, 1.55f));

            var colOver = footstepDustPs.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(new Color(0.76f, 0.68f, 0.54f), new Color(0.55f, 0.48f, 0.38f), 0.36f);

            var rend = footstepDustPs.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = alphaSoftMat;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }

        footstepDustPs.transform.position = groundPos + Vector3.up * 0.08f;
        footstepDustPs.Emit(3);
    }

    private IEnumerator VictoryCelebrationCoroutine(Vector3 centerPos)
    {
        Color[] fireworkColors =
        {
            new Color(2.3f, 1.85f, 0.45f, 1f),
            new Color(0.55f, 2.2f, 1.15f, 1f),
            new Color(0.65f, 1.85f, 2.4f, 1f),
            new Color(2.3f, 0.95f, 0.35f, 1f)
        };

        for (int i = 0; i < 4; i++)
        {
            Vector3 burstPos = centerPos + new Vector3(Random.Range(-4.5f, 4.5f), Random.Range(3.2f, 6.2f), Random.Range(-4.5f, 4.5f));
            GameObject go = new GameObject("VFX_VictoryBurst");
            go.transform.position = burstPos;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 9.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
            main.gravityModifier = 0.55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.startColor = fireworkColors[i % fireworkColors.Length];

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 36) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            colOver.color = CreateFadeOutGradient(Color.white, fireworkColors[i % fireworkColors.Length], 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = additiveStarMat;
            ps.Play();

            Destroy(go, 2.2f);
            yield return new WaitForSecondsRealtime(0.28f);
        }
    }

    // ==================== HELPER SUB-EFFECTS ====================

    private void SpawnGroundShockwave(Transform parent, Vector3 localOffset, float maxSize, float lifetime, Color hdrColor, float delay = 0f)
    {
        GameObject go = new GameObject("GroundShockwave");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localOffset;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.startDelay = delay;
        main.duration = lifetime + delay + 0.1f;
        main.loop = false;
        main.startLifetime = lifetime;
        main.startSpeed = 0f;
        main.startSize = maxSize;
        main.startColor = hdrColor;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        var sizeOver = ps.sizeOverLifetime;
        sizeOver.enabled = true;
        sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.1f, 1f, 1f));

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        colOver.color = CreateFadeOutGradient(Color.white, new Color(hdrColor.r * 0.6f, hdrColor.g * 0.6f, hdrColor.b * 0.6f), hdrColor.a);

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.material = additiveRingMat;
        rend.renderMode = ParticleSystemRenderMode.Mesh;
        rend.mesh = horizontalQuadMesh;

        ps.Play();
    }

    private IEnumerator SpawnVerticalEnergyPillar(Vector3 basePos, Color hdrColor, float radius, float height, float duration)
    {
        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "VFX_EnergyPillar";
        Collider col = pillar.GetComponent<Collider>();
        if (col != null) Destroy(col);

        pillar.transform.position = basePos + Vector3.up * (height * 0.5f);
        pillar.transform.localScale = new Vector3(radius, height * 0.5f, radius);

        Renderer r = pillar.GetComponent<Renderer>();
        Material mat = new Material(additiveBeamMat);
        SetMaterialColor(mat, hdrColor);
        r.material = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float widthFactor = t < 0.2f ? Mathf.Lerp(0.25f, 1.15f, t / 0.2f) : Mathf.Lerp(1.15f, 0.1f, (t - 0.2f) / 0.8f);
            pillar.transform.localScale = new Vector3(radius * widthFactor, height * 0.5f, radius * widthFactor);

            Color c = hdrColor;
            c.a = hdrColor.a * (1f - t);
            SetMaterialColor(mat, c);
            yield return null;
        }

        Destroy(mat);
        Destroy(pillar);
    }

    private Light RentFlashLight()
    {
        while (flashLightPool.Count > 0)
        {
            Light pooled = flashLightPool.Dequeue();
            if (pooled != null)
            {
                pooled.gameObject.SetActive(true);
                return pooled;
            }
        }

        GameObject lightGo = new GameObject("VFX_FlashLight");
        lightGo.transform.SetParent(transform, false);
        Light lightComp = lightGo.AddComponent<Light>();
        lightComp.type = LightType.Point;
        return lightComp;
    }

    private void ReturnFlashLight(Light lightComp)
    {
        if (lightComp == null) return;
        lightComp.intensity = 0f;
        lightComp.gameObject.SetActive(false);
        flashLightPool.Enqueue(lightComp);
    }

    private IEnumerator FlashPointLight(Vector3 pos, Color color, float range, float startIntensity, float duration)
    {
        // Giới hạn số Point Light động cùng lúc để tránh tụt FPS khi chém AoE nhiều quái
        if (activeFlashLights >= MaxConcurrentFlashLights) yield break;

        activeFlashLights++;
        Light lightComp = RentFlashLight();
        lightComp.transform.position = pos;
        lightComp.color = color;
        lightComp.range = range;
        lightComp.intensity = startIntensity;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (lightComp != null)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                lightComp.intensity = Mathf.Lerp(startIntensity, 0f, t * t);
            }
            yield return null;
        }

        ReturnFlashLight(lightComp);
        activeFlashLights = Mathf.Max(0, activeFlashLights - 1);
    }

    private static Gradient CreateFadeOutGradient(Color startColor, Color endColor, float startAlpha)
    {
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(startColor, 0f), new GradientColorKey(endColor, 1f) },
            new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(startAlpha * 0.7f, 0.55f), new GradientAlphaKey(0f, 1f) });
        return grad;
    }

    private static void SetMaterialColor(Material mat, Color color)
    {
        if (mat == null) return;
        if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, color);
        if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, color);
    }

    // ==================== PROCEDURAL HDR TEXTURES & URP MATERIALS ====================

    private void EnsureMaterials()
    {
        if (additiveSoftMat != null && additiveSparkMat != null && additiveRingMat != null
            && additiveSlashMat != null && additiveStarMat != null && additiveBeamMat != null
            && alphaSoftMat != null && alphaDropletMat != null && horizontalQuadMesh != null)
        {
            return;
        }

        Shader urpParticleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader fallbackShader = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit");

        softTex = CreateSoftCircleTexture(128);
        sparkTex = CreateDropletTexture(64);
        ringTex = CreateRingTexture(128);
        slashTex = CreateSlashArcTexture(128);
        starTex = CreateStarFlareTexture(128);
        beamTex = CreateBeamGradientTexture(64, 128);

        additiveSoftMat = CreateParticleMaterial(urpParticleShader, fallbackShader, softTex, additive: true);
        additiveSparkMat = CreateParticleMaterial(urpParticleShader, fallbackShader, sparkTex, additive: true);
        additiveRingMat = CreateParticleMaterial(urpParticleShader, fallbackShader, ringTex, additive: true);
        additiveSlashMat = CreateParticleMaterial(urpParticleShader, fallbackShader, slashTex, additive: true);
        additiveStarMat = CreateParticleMaterial(urpParticleShader, fallbackShader, starTex, additive: true);
        additiveBeamMat = CreateParticleMaterial(urpParticleShader, fallbackShader, beamTex, additive: true);

        alphaSoftMat = CreateParticleMaterial(urpParticleShader, fallbackShader, softTex, additive: false);
        alphaDropletMat = CreateParticleMaterial(urpParticleShader, fallbackShader, sparkTex, additive: false);
        horizontalQuadMesh = CreateHorizontalQuadMesh();
    }

    private static Material CreateParticleMaterial(Shader urpParticleShader, Shader fallbackShader, Texture2D tex, bool additive)
    {
        Shader sh = urpParticleShader != null ? urpParticleShader : fallbackShader;
        Material mat = new Material(sh);
        mat.mainTexture = tex;
        if (mat.HasProperty(BaseMapId)) mat.SetTexture(BaseMapId, tex);
        if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, Color.white);
        if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, Color.white);

        if (urpParticleShader != null && sh == urpParticleShader)
        {
            mat.SetFloat(SurfaceId, 1f);
            mat.SetFloat(BlendId, additive ? 2f : 0f);
            mat.SetFloat(BlendOpId, (float)BlendOp.Add);
            mat.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
            mat.SetFloat(DstBlendId, (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetFloat(SrcBlendAlphaId, (float)BlendMode.One);
            mat.SetFloat(DstBlendAlphaId, (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetFloat(ZWriteId, 0f);
            mat.SetFloat(CullId, 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_ALPHAMODULATE_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }

        return mat;
    }

    private static Texture2D CreateSlashArcTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;
        float invHalf = 1f / half;

        for (int y = 0; y < size; y++)
        {
            int row = y * size;
            float dy = (y - half + 0.5f) * invHalf;
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) * invHalf;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx);

                float arcAngle = Mathf.Abs(angle - Mathf.PI * 0.5f);
                if (arcAngle > Mathf.PI) arcAngle = 2f * Mathf.PI - arcAngle;
                float arcFade = Mathf.Clamp01(1f - arcAngle / 1.55f);

                // Mép ngoài sắc nét + đuôi mờ mịn vào bên trong kiểu vệt kiếm Anime/AAA
                float outerEdge = 0.86f;
                float bandWidth = 0.14f + 0.24f * arcFade;
                float innerDist = outerEdge - dist;

                float alpha = 0f;
                if (innerDist >= -0.04f && innerDist <= bandWidth)
                {
                    if (innerDist < 0f)
                    {
                        alpha = Mathf.Clamp01(1f - (-innerDist / 0.04f));
                    }
                    else
                    {
                        float k = 1f - (innerDist / bandWidth);
                        alpha = Mathf.Pow(k, 1.45f);
                    }
                }

                // Thêm vệt gợn năng lượng tinh tế bên trong vệt chém
                float streak = 0.85f + 0.15f * Mathf.Sin(dist * 34f + angle * 5f);
                alpha = Mathf.Clamp01(alpha * Mathf.Pow(arcFade, 0.78f) * streak * 1.35f);

                pixels[row + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private static Texture2D CreateSoftCircleTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;
        float invHalf = 1f / half;

        for (int y = 0; y < size; y++)
        {
            int row = y * size;
            float dy = (y - half + 0.5f) * invHalf;
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) * invHalf;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - dist);
                alpha = alpha * alpha * (3f - 2f * alpha);
                pixels[row + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private static Texture2D CreateStarFlareTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;
        float invHalf = 1f / half;

        for (int y = 0; y < size; y++)
        {
            int row = y * size;
            float dy = (y - half + 0.5f) * invHalf;
            float absDy = Mathf.Abs(dy);
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) * invHalf;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float absDx = Mathf.Abs(dx);

                // Nhân tròn sáng ở tâm + 4 tia sao chữ thập sắc nét
                float core = Mathf.Pow(Mathf.Clamp01(1f - dist * 1.35f), 2.2f);
                float spikeH = Mathf.Pow(Mathf.Clamp01(1f - absDy * 7.5f), 2.0f) * Mathf.Clamp01(1f - absDx);
                float spikeV = Mathf.Pow(Mathf.Clamp01(1f - absDx * 7.5f), 2.0f) * Mathf.Clamp01(1f - absDy);
                float alpha = Mathf.Clamp01(core + (spikeH + spikeV) * 0.85f);

                pixels[row + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private static Texture2D CreateBeamGradientTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapModeU = TextureWrapMode.Repeat;
        tex.wrapModeV = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            float v = (float)y / (height - 1); // 0 = đáy, 1 = đỉnh
            float verticalFade = Mathf.SmoothStep(0f, 0.14f, v) * Mathf.Pow(1f - v, 1.55f);

            for (int x = 0; x < width; x++)
            {
                float u = (float)x / width;
                // Sọc ánh sáng dọc liền mạch vòng quanh trụ (tuần hoàn 2*PI để không bao giờ có vết nối)
                float streaks = 0.72f + 0.28f * Mathf.Sin(u * Mathf.PI * 6f);
                float alpha = Mathf.Clamp01(verticalFade * streaks * 1.15f);
                pixels[row + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private static Texture2D CreateDropletTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;
        float invHalf = 1f / half;
        float invHalfX = 1f / (half * 0.55f);

        for (int y = 0; y < size; y++)
        {
            int row = y * size;
            float dy = (y - half + 0.5f) * invHalf;
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) * invHalfX;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - Mathf.SmoothStep(0.28f, 1.0f, dist));
                pixels[row + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private static Texture2D CreateRingTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;
        float invHalf = 1f / half;

        for (int y = 0; y < size; y++)
        {
            int row = y * size;
            float dy = (y - half + 0.5f) * invHalf;
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) * invHalf;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                // Vòng chính sáng rõ + vòng phụ mảnh bên trong tạo chiều sâu ma pháp
                float outerRing = Mathf.Clamp01(1f - Mathf.Abs(dist - 0.84f) / 0.13f);
                float innerRing = Mathf.Clamp01(1f - Mathf.Abs(dist - 0.64f) / 0.055f) * 0.48f;
                float alpha = Mathf.Clamp01(outerRing * outerRing + innerRing * innerRing);
                pixels[row + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private Sprite GetOrCreateVignetteSprite()
    {
        if (vignetteSprite != null) return vignetteSprite;

        const int size = 128;
        vignetteTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        vignetteTex.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;
        float invHalf = 1f / half;

        for (int y = 0; y < size; y++)
        {
            int row = y * size;
            float dy = (y - half + 0.5f) * invHalf;
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) * invHalf;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.SmoothStep(0.55f, 1.25f, dist);
                pixels[row + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        vignetteTex.SetPixels32(pixels);
        vignetteTex.Apply(false, true);
        vignetteSprite = Sprite.Create(vignetteTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return vignetteSprite;
    }

    private static Mesh CreateHorizontalQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, 0.5f),
            new Vector3(-0.5f, 0f, 0.5f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        return mesh;
    }
}
