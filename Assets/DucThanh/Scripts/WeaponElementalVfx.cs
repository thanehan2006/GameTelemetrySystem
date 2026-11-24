using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public struct WeaponVfxProfile
{
    public string elementName;
    public Color primaryColor;
    public Color secondaryColor;
    public Color coreColor;
    public int tier;
    public float trailWidth;
    public float arcScaleMult;
    public float baseEmissionStrength;
    public float lightRange;
    public float lightIntensity;
}

// Tạo bảng màu nguyên tố riêng và hiệu ứng hào quang nâng bậc theo phẩm chất cho từng vũ khí
public class WeaponElementalVfx : MonoBehaviour
{
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [SerializeField] private WeaponVfxProfile profile;
    [SerializeField] private int configuredTierOverride = -1;
    [SerializeField] private int configuredShopSlot = -1;

    private bool hasProfile;
    private bool attackSurgeActive;
    private float surgeWeight;
    private float pulsePhase;

    private GameObject auraRoot;
    private Light weaponPointLight;
    private ParticleSystem bladeMotesPs;
    private ParticleSystem bladeSparksPs;
    private ParticleSystem bladeCoreFlarePs;
    private ParticleSystem orbitingRunesPs;
    private ParticleSystem mythicCrownPs;

    private float baseMotesRate;
    private float baseSparksRate;
    private float baseCoreFlareRate;
    private float baseRunesRate;
    private float baseCrownRate;

    private readonly List<Material> runtimeMaterials = new List<Material>(4);
    private readonly List<Material> emissionMaterials = new List<Material>(4);
    private readonly Gradient[] cachedTrailGradients = new Gradient[3];
    private float pulseUpdateTimer;
    private float lastAppliedSurgeWeight = -1f;
    private float qualityRateMult = 1f;

    public WeaponVfxProfile Profile
    {
        get
        {
            if (!hasProfile)
            {
                Configure(configuredTierOverride, configuredShopSlot);
            }
            return profile;
        }
    }

    public int WeaponTier => Profile.tier;

    private void Awake()
    {
        if (!hasProfile)
        {
            Configure(configuredTierOverride, configuredShopSlot);
        }
    }

    private void OnEnable()
    {
        GameSettingsManager.OnSettingsChanged += HandleGraphicsSettingsChanged;
        ApplyQualityTierScaling();
    }

    private void OnDisable()
    {
        GameSettingsManager.OnSettingsChanged -= HandleGraphicsSettingsChanged;
    }

    private void HandleGraphicsSettingsChanged()
    {
        ApplyQualityTierScaling();
    }

    private void ApplyQualityTierScaling()
    {
        qualityRateMult = GameSettingsManager.CurrentGraphicsPreset switch
        {
            GraphicsPreset.Low => 0.50f,
            GraphicsPreset.Medium => 0.78f,
            _ => 1.0f
        };

        if (weaponPointLight != null)
        {
            weaponPointLight.enabled = GameSettingsManager.CurrentGraphicsPreset != GraphicsPreset.Low;
        }

        lastAppliedSurgeWeight = -1f;
    }

    private void Update()
    {
        if (!hasProfile) return;

        float dt = Time.deltaTime;
        float targetSurge = attackSurgeActive ? 1f : 0f;
        surgeWeight = Mathf.MoveTowards(surgeWeight, targetSurge, dt * (attackSurgeActive ? 14f : 5f));
        pulsePhase += dt * (2.6f + profile.tier * 0.45f);

        bool surgeChanged = Mathf.Abs(surgeWeight - lastAppliedSurgeWeight) > 0.005f;
        bool majorSurgeDelta = Mathf.Abs(surgeWeight - lastAppliedSurgeWeight) > 0.01f;

        pulseUpdateTimer -= dt;
        if (pulseUpdateTimer <= 0f || majorSurgeDelta)
        {
            pulseUpdateTimer = 0.05f;

            float pulse01 = 0.5f + 0.5f * Mathf.Sin(pulsePhase);
            float secondaryBlend = profile.tier >= 4
                ? 0.25f + 0.45f * pulse01
                : 0.15f + 0.25f * pulse01;

            Color glowTint = Color.Lerp(profile.primaryColor, profile.secondaryColor, secondaryBlend);
            float glowPower = profile.baseEmissionStrength * (0.84f + 0.28f * pulse01 + 0.72f * surgeWeight);
            Color hdrEmission = new Color(glowTint.r * glowPower, glowTint.g * glowPower, glowTint.b * glowPower, 1f);

            for (int i = 0; i < emissionMaterials.Count; i++)
            {
                Material mat = emissionMaterials[i];
                if (mat != null)
                {
                    mat.SetColor(EmissionColorId, hdrEmission);
                }
            }

            if (weaponPointLight != null && weaponPointLight.enabled)
            {
                weaponPointLight.color = Color.Lerp(profile.primaryColor, profile.coreColor, 0.22f * surgeWeight);
                weaponPointLight.intensity = profile.lightIntensity * (0.86f + 0.24f * pulse01 + 0.95f * surgeWeight);
            }
        }

        if (surgeChanged)
        {
            lastAppliedSurgeWeight = surgeWeight;
            float surgeBoostMotes = (1f + 0.95f * surgeWeight) * qualityRateMult;
            float surgeBoostSparks = (1f + 1.25f * surgeWeight) * qualityRateMult;
            float surgeBoostCore = (1f + 1.10f * surgeWeight) * qualityRateMult;
            float surgeBoostRunes = (1f + 0.85f * surgeWeight) * qualityRateMult;
            float surgeBoostCrown = (1f + 1.15f * surgeWeight) * qualityRateMult;
            UpdateEmissionRate(bladeMotesPs, baseMotesRate, surgeBoostMotes);
            UpdateEmissionRate(bladeSparksPs, baseSparksRate, surgeBoostSparks);
            UpdateEmissionRate(bladeCoreFlarePs, baseCoreFlareRate, surgeBoostCore);
            UpdateEmissionRate(orbitingRunesPs, baseRunesRate, surgeBoostRunes);
            UpdateEmissionRate(mythicCrownPs, baseCrownRate, surgeBoostCrown);
        }
    }

    private void OnDestroy()
    {
        ClearEffectsAndMaterials();
    }

    // Gắn và khởi tạo hiệu ứng nguyên tố riêng cho một instance vũ khí
    public static WeaponElementalVfx ApplyToWeapon(GameObject weaponRoot, int tierOverride = -1, int shopSlotIndex = -1)
    {
        if (weaponRoot == null) return null;

        WeaponElementalVfx vfx = weaponRoot.GetComponent<WeaponElementalVfx>();
        if (vfx == null)
        {
            vfx = weaponRoot.AddComponent<WeaponElementalVfx>();
        }

        vfx.Configure(tierOverride, shopSlotIndex);
        return vfx;
    }

    // Phân giải bảng màu nguyên tố và thông số bậc hiệu ứng theo tên vũ khí và ô cửa hàng
    public static WeaponVfxProfile ResolveProfile(string weaponName, int tierOverride = -1, int shopSlotIndex = -1)
    {
        string lower = string.IsNullOrEmpty(weaponName) ? string.Empty : weaponName.ToLowerInvariant();
        int defaultTier = 1;
        string elementName;
        Color primary;
        Color secondary;
        Color core;

        if (lower.Contains("sword5"))
        {
            bool isShopBlueStorm = shopSlotIndex == 1 || (shopSlotIndex < 0 && tierOverride >= 1 && tierOverride <= 2);
            if (isShopBlueStorm)
            {
                elementName = "Bão Tố Cobalt";
                primary = new Color(0.22f, 0.62f, 1.00f, 1f);
                secondary = new Color(0.32f, 0.94f, 1.00f, 1f);
                core = new Color(0.90f, 0.97f, 1.00f, 1f);
                defaultTier = 2;
            }
            else
            {
                elementName = "Thần Hỏa Phượng Hoàng";
                primary = new Color(1.00f, 0.80f, 0.18f, 1f);
                secondary = new Color(1.00f, 0.34f, 0.12f, 1f);
                core = new Color(1.00f, 0.97f, 0.84f, 1f);
                defaultTier = 4;
            }
        }
        else if (lower.Contains("sword3"))
        {
            elementName = "Lục Bảo Phong";
            primary = new Color(0.22f, 0.96f, 0.56f, 1f);
            secondary = new Color(0.65f, 1.00f, 0.34f, 1f);
            core = new Color(0.90f, 1.00f, 0.93f, 1f);
            defaultTier = 1;
        }
        else if (lower.Contains("sword2"))
        {
            elementName = "Băng Tinh Vĩnh Cửu";
            primary = new Color(0.10f, 0.92f, 1.00f, 1f);
            secondary = new Color(0.52f, 0.72f, 1.00f, 1f);
            core = new Color(0.92f, 0.99f, 1.00f, 1f);
            defaultTier = 3;
        }
        else if (lower.Contains("sword1"))
        {
            elementName = "Thanh Phong";
            primary = new Color(0.28f, 0.80f, 1.00f, 1f);
            secondary = new Color(0.62f, 0.94f, 1.00f, 1f);
            core = new Color(0.93f, 0.99f, 1.00f, 1f);
            defaultTier = 1;
        }
        else if (lower.Contains("hammer"))
        {
            elementName = "Nham Thạch Hổ Phách";
            primary = new Color(1.00f, 0.55f, 0.14f, 1f);
            secondary = new Color(1.00f, 0.82f, 0.22f, 1f);
            core = new Color(1.00f, 0.95f, 0.78f, 1f);
            defaultTier = 2;
        }
        else if (lower.Contains("dagger4"))
        {
            elementName = "Huyết Diễm";
            primary = new Color(1.00f, 0.18f, 0.28f, 1f);
            secondary = new Color(1.00f, 0.44f, 0.14f, 1f);
            core = new Color(1.00f, 0.90f, 0.88f, 1f);
            defaultTier = 2;
        }
        else if (lower.Contains("dagger1") || lower.Contains("dagger"))
        {
            elementName = "Độc Ảnh Hư Vô";
            primary = new Color(0.85f, 0.22f, 1.00f, 1f);
            secondary = new Color(0.28f, 1.00f, 0.78f, 1f);
            core = new Color(0.98f, 0.90f, 1.00f, 1f);
            defaultTier = 3;
        }
        else if (lower.Contains("polearm") || lower.Contains("spear") || lower.Contains("halberd"))
        {
            elementName = "Lôi Quang Hoàng Kim";
            primary = new Color(1.00f, 0.90f, 0.16f, 1f);
            secondary = new Color(0.48f, 0.96f, 1.00f, 1f);
            core = new Color(1.00f, 1.00f, 0.90f, 1f);
            defaultTier = 3;
        }
        else if (lower.Contains("scythe"))
        {
            elementName = "Hắc Ám Huyết Nguyệt";
            primary = new Color(0.72f, 0.16f, 1.00f, 1f);
            secondary = new Color(1.00f, 0.14f, 0.40f, 1f);
            core = new Color(1.00f, 0.86f, 0.98f, 1f);
            defaultTier = 4;
        }
        else
        {
            elementName = "Ánh Kim";
            primary = new Color(0.88f, 0.77f, 0.47f, 1f);
            secondary = new Color(1.00f, 0.65f, 0.22f, 1f);
            core = new Color(1.00f, 0.98f, 0.90f, 1f);
            defaultTier = 1;
        }

        int finalTier = tierOverride >= 1 ? Mathf.Clamp(tierOverride, 1, 4) : defaultTier;

        float trailWidth = 0.26f;
        float arcScale = 0.96f;
        float baseEmission = 0.18f;
        float lightRange = 1.6f;
        float lightIntensity = 0.55f;

        if (finalTier == 2)
        {
            trailWidth = 0.33f;
            arcScale = 1.04f;
            baseEmission = 0.28f;
            lightRange = 2.1f;
            lightIntensity = 0.95f;
        }
        else if (finalTier == 3)
        {
            trailWidth = 0.40f;
            arcScale = 1.12f;
            baseEmission = 0.39f;
            lightRange = 2.7f;
            lightIntensity = 1.40f;
        }
        else if (finalTier >= 4)
        {
            trailWidth = 0.48f;
            arcScale = 1.24f;
            baseEmission = 0.52f;
            lightRange = 3.3f;
            lightIntensity = 1.85f;
        }

        return new WeaponVfxProfile
        {
            elementName = elementName,
            primaryColor = primary,
            secondaryColor = secondary,
            coreColor = core,
            tier = finalTier,
            trailWidth = trailWidth,
            arcScaleMult = arcScale,
            baseEmissionStrength = baseEmission,
            lightRange = lightRange,
            lightIntensity = lightIntensity
        };
    }

    // Thiết lập lại màu vật liệu phát sáng và các tầng hạt hào quang cho vũ khí hiện tại
    public void Configure(int tierOverride = -1, int shopSlotIndex = -1)
    {
        configuredTierOverride = tierOverride;
        configuredShopSlot = shopSlotIndex;
        profile = ResolveProfile(gameObject.name, tierOverride, shopSlotIndex);
        hasProfile = true;
        pulsePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        Array.Clear(cachedTrailGradients, 0, cachedTrailGradients.Length);

        ClearEffectsAndMaterials();
        ApplyBladeMaterialGlow();
        BuildTieredAuraHierarchy();
        ApplyQualityTierScaling();
    }

    // Tăng cường độ hào quang và mật độ tia lửa trong lúc nhân vật đang vung đòn đánh
    public void SetAttackSurge(bool active)
    {
        attackSurgeActive = active;
    }

    // Tạo dải màu vệt kiếm phù hợp với màu nguyên tố và biến thể đòn chém
    public Gradient BuildTrailGradient(int attackVariant)
    {
        int idx = Mathf.Clamp(attackVariant, 0, cachedTrailGradients.Length - 1);
        if (cachedTrailGradients[idx] != null) return cachedTrailGradients[idx];

        WeaponVfxProfile p = Profile;
        Color tailCol = idx == 2
            ? Color.Lerp(p.secondaryColor, p.primaryColor, 0.30f)
            : Color.Lerp(p.primaryColor, p.secondaryColor, idx == 1 ? 0.65f : 0.35f);
        Color midCol = Color.Lerp(p.coreColor, p.primaryColor, 0.62f);

        float alphaHead = Mathf.Clamp(0.84f + p.tier * 0.04f, 0.85f, 0.99f);
        float alphaMid = Mathf.Clamp(0.42f + p.tier * 0.05f, 0.45f, 0.68f);

        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(p.coreColor, 0f),
                new GradientColorKey(midCol, 0.46f),
                new GradientColorKey(tailCol, 1f)
            },
            new[]
            {
                new GradientAlphaKey(alphaHead, 0f),
                new GradientAlphaKey(alphaMid, 0.52f),
                new GradientAlphaKey(0f, 1f)
            });
        cachedTrailGradients[idx] = grad;
        return grad;
    }

    private void ApplyBladeMaterialGlow()
    {
        Dictionary<Material, Material> clonedMap = new Dictionary<Material, Material>(4);
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer rend = renderers[i];
            if (rend == null || rend is ParticleSystemRenderer || rend is TrailRenderer || rend is LineRenderer)
            {
                continue;
            }

            Material[] shared = rend.sharedMaterials;
            bool changed = false;

            for (int m = 0; m < shared.Length; m++)
            {
                Material src = shared[m];
                if (src == null) continue;

                if (!clonedMap.TryGetValue(src, out Material inst))
                {
                    inst = new Material(src);
                    inst.name = src.name + "_ElementVfx";

                    if (inst.HasProperty(EmissionMapId) && inst.GetTexture(EmissionMapId) == null)
                    {
                        Texture baseTex = null;
                        if (inst.HasProperty(BaseMapId)) baseTex = inst.GetTexture(BaseMapId);
                        else if (inst.HasProperty(MainTexId)) baseTex = inst.GetTexture(MainTexId);
                        if (baseTex != null) inst.SetTexture(EmissionMapId, baseTex);
                    }

                    inst.EnableKeyword("_EMISSION");
                    inst.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

                    if (inst.HasProperty(EmissionColorId))
                    {
                        Color initialGlow = profile.primaryColor * profile.baseEmissionStrength;
                        inst.SetColor(EmissionColorId, initialGlow);
                        emissionMaterials.Add(inst);
                    }

                    clonedMap[src] = inst;
                    runtimeMaterials.Add(inst);
                }

                shared[m] = inst;
                changed = true;
            }

            if (changed)
            {
                rend.sharedMaterials = shared;
            }
        }
    }

    private void BuildTieredAuraHierarchy()
    {
        ComputeWeaponBladeGeometry(out Vector3 bladeMidLocal, out Vector3 bladeTipLocal, out Vector3 bladeDirLocal, out float bladeLength);

        auraRoot = new GameObject("VFX_ElementalAura");
        auraRoot.transform.SetParent(transform, false);
        auraRoot.transform.localPosition = Vector3.zero;
        auraRoot.transform.localRotation = Quaternion.identity;

        Quaternion alignToBlade = Quaternion.FromToRotation(Vector3.up, bladeDirLocal);

        // Tầng ánh sáng point light bám theo phần lưỡi vũ khí
        GameObject lightGo = new GameObject("BladeGlowLight");
        lightGo.transform.SetParent(auraRoot.transform, false);
        lightGo.transform.localPosition = Vector3.Lerp(bladeMidLocal, bladeTipLocal, 0.35f);
        weaponPointLight = lightGo.AddComponent<Light>();
        weaponPointLight.type = LightType.Point;
        weaponPointLight.color = profile.primaryColor;
        weaponPointLight.range = profile.lightRange;
        weaponPointLight.intensity = profile.lightIntensity;
        weaponPointLight.shadows = LightShadows.None;

        Material softMat = CombatVfxManager.GetAdditiveSoftMaterial();
        Material sparkMat = CombatVfxManager.GetAdditiveSparkMaterial();
        Material starMat = CombatVfxManager.GetAdditiveStarMaterial();

        // Tầng 1 (mọi bậc): Hạt năng lượng bồng bềnh tỏa ra dọc thân lưỡi
        baseMotesRate = profile.tier == 1 ? 8f : profile.tier == 2 ? 15f : profile.tier == 3 ? 22f : 30f;
        bladeMotesPs = CreateBladeMotesSystem(auraRoot.transform, softMat, bladeMidLocal, alignToBlade, bladeLength);

        // Tầng 2 trở lên: Tia lửa năng lượng bắn dọc lưỡi + lõi sáng tại đầu lưỡi
        if (profile.tier >= 2)
        {
            baseSparksRate = profile.tier == 2 ? 14f : profile.tier == 3 ? 22f : 32f;
            bladeSparksPs = CreateBladeSparksSystem(auraRoot.transform, sparkMat, bladeMidLocal, alignToBlade, bladeLength);

            baseCoreFlareRate = profile.tier == 2 ? 5f : profile.tier == 3 ? 9f : 14f;
            bladeCoreFlarePs = CreateBladeCoreFlareSystem(auraRoot.transform, starMat, bladeTipLocal);
        }

        // Tầng 3 trở lên: Hào quang sao rune xoay quanh thân vũ khí
        if (profile.tier >= 3)
        {
            baseRunesRate = profile.tier == 3 ? 12f : 18f;
            orbitingRunesPs = CreateOrbitingRunesSystem(auraRoot.transform, starMat, bladeMidLocal, alignToBlade, bladeLength);
        }

        // Tầng 4 (Thần Thoại): Vòng tinh tú rực sáng hội tụ tại mũi vũ khí
        if (profile.tier >= 4)
        {
            baseCrownRate = 11f;
            mythicCrownPs = CreateMythicCrownSystem(auraRoot.transform, starMat, bladeTipLocal, alignToBlade);
        }
    }

    private ParticleSystem CreateBladeMotesSystem(Transform parent, Material mat, Vector3 midPos, Quaternion rot, float bladeLen)
    {
        ParticleSystem ps = CreateAuraPs(parent, "BladeAuraMotes", mat, midPos, rot);
        var main = ps.main;
        main.duration = 2f;
        main.loop = true;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.35f + profile.tier * 0.08f);
        float maxSize = 0.065f + profile.tier * 0.015f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.032f, maxSize);
        main.gravityModifier = -0.18f;

        Color hdrPrimary = ScaleColorRgb(profile.primaryColor, 1.35f + profile.tier * 0.18f);
        Color hdrSecondary = ScaleColorRgb(profile.secondaryColor, 1.25f + profile.tier * 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(hdrPrimary, hdrSecondary);

        var emission = ps.emission;
        emission.rateOverTime = baseMotesRate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.08f, Mathf.Max(0.28f, bladeLen * 0.58f), 0.08f);

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        colOver.color = CreateAuraFadeGradient(profile.coreColor, profile.secondaryColor, 0.85f);

        var sizeOver = ps.sizeOverLifetime;
        sizeOver.enabled = true;
        sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.35f, 1f, 0f));

        ps.Play();
        return ps;
    }

    private ParticleSystem CreateBladeSparksSystem(Transform parent, Material mat, Vector3 midPos, Quaternion rot, float bladeLen)
    {
        ParticleSystem ps = CreateAuraPs(parent, "BladeEnergySparks", mat, midPos, rot);
        var main = ps.main;
        main.duration = 1.5f;
        main.loop = true;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.48f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 1.45f + profile.tier * 0.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f + profile.tier * 0.008f);
        main.gravityModifier = -0.25f;

        Color brightMain = ScaleColorRgb(profile.coreColor, 1.65f + profile.tier * 0.2f);
        Color brightAccent = ScaleColorRgb(profile.primaryColor, 1.75f + profile.tier * 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(brightMain, brightAccent);

        var emission = ps.emission;
        emission.rateOverTime = baseSparksRate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.06f, Mathf.Max(0.26f, bladeLen * 0.62f), 0.06f);

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        colOver.color = CreateAuraFadeGradient(profile.coreColor, profile.secondaryColor, 1f);

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.08f;
        rend.lengthScale = 2.2f;

        ps.Play();
        return ps;
    }

    private ParticleSystem CreateBladeCoreFlareSystem(Transform parent, Material mat, Vector3 tipPos)
    {
        ParticleSystem ps = CreateAuraPs(parent, "BladeCoreFlare", mat, tipPos, Quaternion.identity);
        var main = ps.main;
        main.duration = 1.6f;
        main.loop = true;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.56f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.10f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.11f, 0.22f + profile.tier * 0.04f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        Color hdrCore = ScaleColorRgb(profile.coreColor, 1.55f + profile.tier * 0.22f);
        Color hdrEdge = ScaleColorRgb(profile.primaryColor, 1.70f + profile.tier * 0.22f);
        main.startColor = new ParticleSystem.MinMaxGradient(hdrCore, hdrEdge);

        var emission = ps.emission;
        emission.rateOverTime = baseCoreFlareRate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.06f;

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        colOver.color = CreateAuraFadeGradient(profile.coreColor, profile.primaryColor, 0.92f);

        var sizeOver = ps.sizeOverLifetime;
        sizeOver.enabled = true;
        sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.45f, 1f, 0.05f));

        ps.Play();
        return ps;
    }

    private ParticleSystem CreateOrbitingRunesSystem(Transform parent, Material mat, Vector3 midPos, Quaternion rot, float bladeLen)
    {
        ParticleSystem ps = CreateAuraPs(parent, "OrbitingRuneStars", mat, midPos, rot);
        var main = ps.main;
        main.duration = 2.0f;
        main.loop = true;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.05f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.055f, 0.115f + (profile.tier - 3) * 0.03f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        Color hdrA = ScaleColorRgb(profile.primaryColor, 1.85f);
        Color hdrB = ScaleColorRgb(profile.secondaryColor, 1.90f);
        main.startColor = new ParticleSystem.MinMaxGradient(hdrA, hdrB);

        var emission = ps.emission;
        emission.rateOverTime = baseRunesRate;

        var shape = ps.shape;
        float runeLen = Mathf.Max(0.28f, bladeLen * 0.56f);
        shape.shapeType = ParticleSystemShapeType.ConeVolume;
        shape.angle = 0f;
        shape.radius = 0.14f + (profile.tier - 3) * 0.04f;
        shape.radiusThickness = 0.25f;
        shape.length = runeLen;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        shape.position = new Vector3(0f, -runeLen * 0.5f, 0f);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.orbitalY = profile.tier >= 4 ? 4.4f : 3.2f;

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        colOver.color = CreateAuraFadeGradient(profile.coreColor, profile.secondaryColor, 0.96f);

        ps.Play();
        return ps;
    }

    private ParticleSystem CreateMythicCrownSystem(Transform parent, Material mat, Vector3 tipPos, Quaternion rot)
    {
        ParticleSystem ps = CreateAuraPs(parent, "MythicTipCrown", mat, tipPos, rot);
        var main = ps.main;
        main.duration = 1.4f;
        main.loop = true;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.60f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.10f, 0.24f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.28f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        Color hdrTipA = ScaleColorRgb(profile.coreColor, 2.15f);
        Color hdrTipB = ScaleColorRgb(profile.secondaryColor, 2.05f);
        main.startColor = new ParticleSystem.MinMaxGradient(hdrTipA, hdrTipB);

        var emission = ps.emission;
        emission.rateOverTime = baseCrownRate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.11f;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.orbitalY = -3.8f;

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        colOver.color = CreateAuraFadeGradient(profile.coreColor, profile.secondaryColor, 1f);

        var sizeOver = ps.sizeOverLifetime;
        sizeOver.enabled = true;
        sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.35f, 1f, 0.05f));

        ps.Play();
        return ps;
    }

    private static ParticleSystem CreateAuraPs(Transform parent, string name, Material mat, Vector3 localPos, Quaternion localRot)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystemRenderer rend = ps.GetComponent<ParticleSystemRenderer>();
        if (mat != null) rend.sharedMaterial = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        return ps;
    }

    private static void UpdateEmissionRate(ParticleSystem ps, float baseRate, float multiplier)
    {
        if (ps == null || baseRate <= 0f) return;
        var emission = ps.emission;
        emission.rateOverTime = baseRate * multiplier;
    }

    private static Color ScaleColorRgb(Color c, float mult)
    {
        return new Color(c.r * mult, c.g * mult, c.b * mult, c.a);
    }

    private static Gradient CreateAuraFadeGradient(Color startCol, Color endCol, float peakAlpha)
    {
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[]
            {
                new GradientColorKey(startCol, 0f),
                new GradientColorKey(endCol, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(peakAlpha, 0.20f),
                new GradientAlphaKey(peakAlpha * 0.65f, 0.68f),
                new GradientAlphaKey(0f, 1f)
            });
        return grad;
    }

    // Xác định tâm thân lưỡi, mũi vũ khí và trục dọc theo hình dạng thực tế của prefab
    private void ComputeWeaponBladeGeometry(out Vector3 midLocal, out Vector3 tipLocal, out Vector3 dirLocal, out float bladeLength)
    {
        Bounds bounds = ComputeWeaponLocalBounds(transform);
        float minY = bounds.min.y;
        float maxY = bounds.max.y;
        float spanY = Mathf.Max(0.12f, maxY - minY);

        string lower = gameObject.name.ToLowerInvariant();
        bool invertShaft = lower.Contains("dagger") || (maxY > 0.01f && Mathf.Abs(minY) > Mathf.Abs(maxY) * 1.6f);

        float midU = invertShaft ? 0.34f : 0.67f;
        float tipU = invertShaft ? 0.08f : 0.92f;
        if (lower.Contains("hammer"))
        {
            midU = 0.80f;
            tipU = 0.94f;
        }
        else if (lower.Contains("scythe"))
        {
            midU = 0.84f;
            tipU = 0.96f;
        }

        midLocal = new Vector3(bounds.center.x, Mathf.Lerp(minY, maxY, midU), bounds.center.z);
        tipLocal = new Vector3(bounds.center.x, Mathf.Lerp(minY, maxY, tipU), bounds.center.z);
        dirLocal = invertShaft ? Vector3.down : Vector3.up;
        bladeLength = spanY * 0.55f;
    }

    private static Bounds ComputeWeaponLocalBounds(Transform root)
    {
        bool hasBounds = false;
        Bounds result = new Bounds(new Vector3(0f, 0.45f, 0f), new Vector3(0.12f, 0.9f, 0.12f));

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter mf = filters[i];
            if (mf == null || mf.sharedMesh == null) continue;
            EncapsulateLocalBounds(root, mf.transform, mf.sharedMesh.bounds, ref result, ref hasBounds);
        }

        SkinnedMeshRenderer[] skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            SkinnedMeshRenderer smr = skinned[i];
            if (smr == null || smr.sharedMesh == null) continue;
            EncapsulateLocalBounds(root, smr.transform, smr.sharedMesh.bounds, ref result, ref hasBounds);
        }

        return result;
    }

    private static void EncapsulateLocalBounds(Transform root, Transform leaf, Bounds leafBounds, ref Bounds target, ref bool hasBounds)
    {
        Matrix4x4 rel = Matrix4x4.identity;
        Transform cur = leaf;
        while (cur != null && cur != root)
        {
            rel = Matrix4x4.TRS(cur.localPosition, cur.localRotation, cur.localScale) * rel;
            cur = cur.parent;
        }

        Vector3 c = leafBounds.center;
        Vector3 e = leafBounds.extents;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = rel.MultiplyPoint3x4(new Vector3(c.x + sx * e.x, c.y + sy * e.y, c.z + sz * e.z));
                    if (!hasBounds)
                    {
                        target = new Bounds(corner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        target.Encapsulate(corner);
                    }
                }
            }
        }
    }

    private void ClearEffectsAndMaterials()
    {
        if (auraRoot != null)
        {
            Destroy(auraRoot);
            auraRoot = null;
        }

        weaponPointLight = null;
        bladeMotesPs = null;
        bladeSparksPs = null;
        bladeCoreFlarePs = null;
        orbitingRunesPs = null;
        mythicCrownPs = null;

        for (int i = 0; i < runtimeMaterials.Count; i++)
        {
            if (runtimeMaterials[i] != null)
            {
                Destroy(runtimeMaterials[i]);
            }
        }
        runtimeMaterials.Clear();
        emissionMaterials.Clear();
        lastAppliedSurgeWeight = -1f;
        pulseUpdateTimer = 0f;
    }
}
