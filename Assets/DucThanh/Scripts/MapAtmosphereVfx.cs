using System.Collections.Generic;
using UnityEngine;

// Tạo hiệu ứng thời tiết, ánh sáng lập lòe, cực quang, mạch dung nham và cắt giảm theo khoảng cách (culling) cho từng map
public class MapAtmosphereVfx : MonoBehaviour
{
    public enum BiomeTheme
    {
        FrostAurora,
        InfernoHold
    }

    [Header("Biome & Boss Arena")]
    public BiomeTheme biome = BiomeTheme.FrostAurora;
    public Vector3 bossArenaCenter = new Vector3(196f, 9f, 196f);
    public Color bossRingColor = new Color(0.25f, 0.85f, 1f, 0.85f);

    [Header("Scene Anchors")]
    public Vector3[] lavaGeyserPositions;
    public Vector3[] magmaFissurePositions;
    public Vector3[] crystalClusterPositions;
    public Light[] flickeringLights;
    public Transform[] auroraRibbons;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private const float LightCullSqrDist = 42f * 42f;
    private const float ParticleEnableSqrDist = 56f * 56f;
    private const float ParticleDisableSqrDist = 64f * 64f;
    private const float BossRingCullSqrDist = 68f * 68f;
    private const float CullInterval = 0.25f;
    private const float AuroraColorUpdateInterval = 0.066f;

    private Transform followTarget;
    private Transform weatherAnchor;
    private Transform bossRingTransform;
    private Transform bossInnerRingTransform;
    private Material bossRingMat;
    private Material bossInnerRingMat;
    private Renderer[] auroraRenderers;
    private Color[] auroraBaseColors;
    private MaterialPropertyBlock auroraPropBlock;
    private float[] baseLightIntensities;
    private Vector3[] lightPositions;
    private float[] auroraBaseYs;
    private float[] auroraBaseXs;

    private readonly List<ParticleSystem> culledParticles = new List<ParticleSystem>();
    private readonly List<Vector3> culledParticlePositions = new List<Vector3>();
    private readonly List<float> culledParticleBaseRates = new List<float>();
    private readonly List<ParticleSystem> weatherParticles = new List<ParticleSystem>(4);
    private readonly List<float> weatherBaseRates = new List<float>(4);
    private readonly int[] closestLightIndices = new int[6];
    private readonly float[] closestLightSqrDists = new float[6];
    private int maxActiveSceneLights = 4;
    private float cullTimer;
    private float auroraColorTimer;
    private float auroraPosTimer;
    private float lightFlickerTimer;
    private float activeLightCullSqrDist = LightCullSqrDist;

    private void OnEnable()
    {
        GameSettingsManager.OnSettingsChanged += ApplyQualitySettings;
    }

    private void OnDisable()
    {
        GameSettingsManager.OnSettingsChanged -= ApplyQualitySettings;
    }

    // Phân tích chủ đề biome, tìm đèn và particle để khởi tạo hiệu ứng môi trường
    private void Start()
    {
        CombatVfxManager.EnsureInstance();
        ResolveFollowTarget();
        CacheFlickerLights();
        CacheAuroraRibbons();
        BuildWeatherEffects();
        BuildCrystalPlumes();
        BuildLavaGeysers();
        BuildMagmaFissurePlumes();
        BuildBossArenaRing();
        CacheCulledParticles();
        ApplyQualitySettings();
        cullTimer = 0f;
    }

    private void ApplyQualitySettings()
    {
        GraphicsPreset preset = GameSettingsManager.CurrentGraphicsPreset;
        float rateMult = preset == GraphicsPreset.Low ? 0.38f : (preset == GraphicsPreset.Medium ? 0.68f : 1.0f);
        bool enableNoise = preset != GraphicsPreset.Low;
        float lightDist = preset == GraphicsPreset.Low ? 22f : (preset == GraphicsPreset.Medium ? 32f : 42f);
        activeLightCullSqrDist = lightDist * lightDist;
        maxActiveSceneLights = preset == GraphicsPreset.Low ? 2 : (preset == GraphicsPreset.Medium ? 4 : 6);

        for (int i = 0; i < weatherParticles.Count; i++)
        {
            ParticleSystem ps = weatherParticles[i];
            if (ps == null) continue;
            var emission = ps.emission;
            emission.rateOverTime = weatherBaseRates[i] * rateMult;
            var noise = ps.noise;
            noise.enabled = enableNoise;
            if (enableNoise) noise.quality = ParticleSystemNoiseQuality.Low;
        }

        for (int i = 0; i < culledParticles.Count; i++)
        {
            ParticleSystem ps = culledParticles[i];
            if (ps == null) continue;
            var emission = ps.emission;
            emission.rateOverTime = culledParticleBaseRates[i] * rateMult;
        }
    }

    private void OnDestroy()
    {
        if (bossRingMat != null) Destroy(bossRingMat);
        if (bossInnerRingMat != null) Destroy(bossInnerRingMat);
    }

    private void ResolveFollowTarget()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            followTarget = player.transform;
            return;
        }
        if (Camera.main != null)
        {
            followTarget = Camera.main.transform;
        }
    }

    private void CacheFlickerLights()
    {
        if (flickeringLights == null || flickeringLights.Length == 0)
        {
            flickeringLights = GetComponentsInChildren<Light>(true);
        }

        if (flickeringLights == null) return;
        baseLightIntensities = new float[flickeringLights.Length];
        lightPositions = new Vector3[flickeringLights.Length];
        for (int i = 0; i < flickeringLights.Length; i++)
        {
            Light l = flickeringLights[i];
            if (l != null) l.shadows = LightShadows.None;
            baseLightIntensities[i] = l != null ? l.intensity : 1f;
            lightPositions[i] = l != null ? l.transform.position : Vector3.zero;
        }
    }

    private void CacheCulledParticles()
    {
        culledParticles.Clear();
        culledParticlePositions.Clear();
        culledParticleBaseRates.Clear();

        ParticleSystem[] allPs = GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < allPs.Length; i++)
        {
            ParticleSystem ps = allPs[i];
            if (ps == null) continue;
            if (weatherAnchor != null && ps.transform.IsChildOf(weatherAnchor)) continue;

            culledParticles.Add(ps);
            culledParticlePositions.Add(ps.transform.position);
            culledParticleBaseRates.Add(ps.emission.rateOverTime.constant);
        }
    }

    private void CacheAuroraRibbons()
    {
        if (auroraRibbons == null || auroraRibbons.Length == 0) return;
        auroraBaseYs = new float[auroraRibbons.Length];
        auroraBaseXs = new float[auroraRibbons.Length];
        auroraRenderers = new Renderer[auroraRibbons.Length];
        auroraBaseColors = new Color[auroraRibbons.Length];
        auroraPropBlock = new MaterialPropertyBlock();

        for (int i = 0; i < auroraRibbons.Length; i++)
        {
            Transform r = auroraRibbons[i];
            auroraBaseYs[i] = r != null ? r.localPosition.y : 0f;
            auroraBaseXs[i] = r != null ? r.localPosition.x : 0f;
            if (r != null)
            {
                auroraRenderers[i] = r.GetComponent<Renderer>();
                if (auroraRenderers[i] != null)
                {
                    auroraRenderers[i].GetPropertyBlock(auroraPropBlock);
                    Color c = auroraPropBlock.GetColor(BaseColorId);
                    if (c.maxColorComponent <= 0.01f)
                    {
                        c = i % 2 == 0
                            ? new Color(0.20f, 2.15f, 1.45f, 0.34f)
                            : new Color(0.32f, 1.35f, 2.35f, 0.30f);
                    }
                    auroraBaseColors[i] = c;
                }
            }
        }
    }

    // Tạo hạt tuyết bay cho bản đồ băng giá hoặc tro tàn cho bản đồ dung nham
    private void BuildWeatherEffects()
    {
        GameObject anchorGo = new GameObject("CameraWeatherAnchor");
        anchorGo.transform.SetParent(transform, false);
        weatherAnchor = anchorGo.transform;
        if (followTarget != null) weatherAnchor.position = followTarget.position;

        Material addMat = CombatVfxManager.GetAdditiveSoftMaterial();
        Material alphaMat = CombatVfxManager.GetAlphaSoftMaterial();

        if (biome == BiomeTheme.FrostAurora)
        {

            CreateWeatherParticles(
                "FrostBlizzardPS",
                addMat,
                maxParticles: 130,
                rate: 36f,
                emitOffset: new Vector3(0f, 11f, 0f),
                boxSize: new Vector3(32f, 2f, 32f),
                lifetimeMin: 3.0f,
                lifetimeMax: 4.8f,
                sizeMin: 0.08f,
                sizeMax: 0.20f,
                velMin: new Vector3(1.2f, -4.0f, -0.8f),
                velMax: new Vector3(2.4f, -2.5f, 0.8f),
                startColorMin: new Color(0.85f, 0.96f, 1.0f, 0.70f),
                startColorMax: new Color(0.65f, 0.90f, 1.0f, 0.90f),
                noiseStrength: 0.55f);

            CreateWeatherParticles(
                "AuroraFrostMotesPS",
                addMat,
                maxParticles: 48,
                rate: 12f,
                emitOffset: new Vector3(0f, 1.8f, 0f),
                boxSize: new Vector3(24f, 3.2f, 24f),
                lifetimeMin: 2.6f,
                lifetimeMax: 4.5f,
                sizeMin: 0.09f,
                sizeMax: 0.22f,
                velMin: new Vector3(-0.35f, 0.35f, -0.35f),
                velMax: new Vector3(0.35f, 0.95f, 0.35f),
                startColorMin: new Color(0.30f, 1.85f, 1.45f, 0.75f),
                startColorMax: new Color(0.55f, 1.15f, 2.35f, 0.90f),
                noiseStrength: 0.65f);

            CreateWeatherParticles(
                "GroundFrostMistPS",
                alphaMat,
                maxParticles: 22,
                rate: 6f,
                emitOffset: new Vector3(0f, 0.35f, 0f),
                boxSize: new Vector3(22f, 0.55f, 22f),
                lifetimeMin: 3.2f,
                lifetimeMax: 5.2f,
                sizeMin: 1.6f,
                sizeMax: 3.0f,
                velMin: new Vector3(0.4f, 0.05f, -0.25f),
                velMax: new Vector3(1.1f, 0.18f, 0.25f),
                startColorMin: new Color(0.75f, 0.90f, 1.0f, 0.14f),
                startColorMax: new Color(0.60f, 0.85f, 0.98f, 0.20f),
                noiseStrength: 0.35f);
        }
        else
        {

            CreateWeatherParticles(
                "InfernoEmbersPS",
                addMat,
                maxParticles: 110,
                rate: 30f,
                emitOffset: new Vector3(0f, 0.6f, 0f),
                boxSize: new Vector3(28f, 2.2f, 28f),
                lifetimeMin: 2.4f,
                lifetimeMax: 4.4f,
                sizeMin: 0.07f,
                sizeMax: 0.18f,
                velMin: new Vector3(-0.65f, 1.6f, -0.65f),
                velMax: new Vector3(0.75f, 3.6f, 0.75f),
                startColorMin: new Color(2.6f, 0.95f, 0.18f, 0.95f),
                startColorMax: new Color(2.4f, 0.35f, 0.08f, 0.95f),
                noiseStrength: 1.25f);

            CreateWeatherParticles(
                "VolcanicAshPS",
                alphaMat,
                maxParticles: 65,
                rate: 18f,
                emitOffset: new Vector3(0f, 3.8f, 0f),
                boxSize: new Vector3(28f, 6f, 28f),
                lifetimeMin: 3.5f,
                lifetimeMax: 5.5f,
                sizeMin: 0.12f,
                sizeMax: 0.26f,
                velMin: new Vector3(0.4f, -0.35f, -0.4f),
                velMax: new Vector3(1.1f, 0.45f, 0.4f),
                startColorMin: new Color(0.14f, 0.12f, 0.12f, 0.45f),
                startColorMax: new Color(0.28f, 0.20f, 0.18f, 0.35f),
                noiseStrength: 0.70f);

            CreateWeatherParticles(
                "InfernoHeatMistPS",
                alphaMat,
                maxParticles: 20,
                rate: 6f,
                emitOffset: new Vector3(0f, 0.45f, 0f),
                boxSize: new Vector3(22f, 0.75f, 22f),
                lifetimeMin: 2.8f,
                lifetimeMax: 4.6f,
                sizeMin: 1.4f,
                sizeMax: 2.6f,
                velMin: new Vector3(-0.25f, 0.35f, -0.25f),
                velMax: new Vector3(0.35f, 0.75f, 0.25f),
                startColorMin: new Color(0.55f, 0.20f, 0.10f, 0.16f),
                startColorMax: new Color(0.38f, 0.12f, 0.08f, 0.12f),
                noiseStrength: 0.45f);
        }
    }

    private void CreateWeatherParticles(
        string objName,
        Material mat,
        int maxParticles,
        float rate,
        Vector3 emitOffset,
        Vector3 boxSize,
        float lifetimeMin,
        float lifetimeMax,
        float sizeMin,
        float sizeMax,
        Vector3 velMin,
        Vector3 velMax,
        Color startColorMin,
        Color startColorMax,
        float noiseStrength)
    {
        if (weatherAnchor == null || mat == null) return;

        GameObject go = new GameObject(objName);
        go.transform.SetParent(weatherAnchor, false);
        go.transform.localPosition = emitOffset;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeMin, lifetimeMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startSpeed = 0f;
        main.startColor = new ParticleSystem.MinMaxGradient(startColorMin, startColorMax);

        var emission = ps.emission;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = boxSize;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(velMin.x, velMax.x);
        vel.y = new ParticleSystem.MinMaxCurve(velMin.y, velMax.y);
        vel.z = new ParticleSystem.MinMaxCurve(velMin.z, velMax.z);

        var noise = ps.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.Low;
        noise.strength = noiseStrength;
        noise.frequency = 0.45f;
        noise.scrollSpeed = 0.35f;

        var colLife = ps.colorOverLifetime;
        colLife.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.18f), new GradientAlphaKey(1f, 0.78f), new GradientAlphaKey(0f, 1f) });
        colLife.color = new ParticleSystem.MinMaxGradient(g);

        ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
        psr.sharedMaterial = mat;
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        psr.receiveShadows = false;

        weatherParticles.Add(ps);
        weatherBaseRates.Add(rate);
    }

    // Gắn luồng sương lạnh phát sáng quanh các cụm pha lê băng
    private void BuildCrystalPlumes()
    {
        if (biome != BiomeTheme.FrostAurora || crystalClusterPositions == null || crystalClusterPositions.Length == 0) return;

        Material starMat = CombatVfxManager.GetAdditiveStarMaterial();
        Material softMat = CombatVfxManager.GetAdditiveSoftMaterial();
        Material mat = starMat != null ? starMat : softMat;
        if (mat == null) return;

        for (int i = 0; i < crystalClusterPositions.Length; i++)
        {
            GameObject go = new GameObject($"FrostCrystalPlume_{i + 1}");
            go.transform.SetParent(transform, false);
            go.transform.position = crystalClusterPositions[i] + Vector3.up * 0.6f;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = 24;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.gravityModifier = -0.18f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.32f, 1.95f, 2.35f, 0.90f),
                new Color(0.55f, 2.25f, 1.65f, 0.90f));

            var emission = ps.emission;
            emission.rateOverTime = 7f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1.25f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var colLife = ps.colorOverLifetime;
            colLife.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.45f, 0.92f, 1f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.25f), new GradientAlphaKey(0f, 1f) });
            colLife.color = new ParticleSystem.MinMaxGradient(g);

            ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = mat;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;
        }
    }

    // Tạo cột phun tia lửa và khói nóng trên các hồ dung nham
    private void BuildLavaGeysers()
    {
        if (biome != BiomeTheme.InfernoHold || lavaGeyserPositions == null || lavaGeyserPositions.Length == 0) return;

        Material addMat = CombatVfxManager.GetAdditiveSoftMaterial();
        Material starMat = CombatVfxManager.GetAdditiveStarMaterial();
        if (addMat == null) return;

        for (int i = 0; i < lavaGeyserPositions.Length; i++)
        {
            GameObject go = new GameObject($"LavaPoolSparks_{i + 1}");
            go.transform.SetParent(transform, false);
            go.transform.position = lavaGeyserPositions[i] + Vector3.up * 0.4f;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = 58;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 4.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.38f);
            main.gravityModifier = 0.42f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(2.8f, 1.35f, 0.20f, 0.95f),
                new Color(2.4f, 0.32f, 0.06f, 0.95f));

            var emission = ps.emission;
            emission.rateOverTime = 24f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 9.5f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var colLife = ps.colorOverLifetime;
            colLife.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.3f, 0.08f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colLife.color = new ParticleSystem.MinMaxGradient(g);

            ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = starMat != null ? starMat : addMat;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;
        }
    }

    private void BuildMagmaFissurePlumes()
    {
        if (biome != BiomeTheme.InfernoHold || magmaFissurePositions == null || magmaFissurePositions.Length == 0) return;

        Material addMat = CombatVfxManager.GetAdditiveSoftMaterial();
        if (addMat == null) return;

        for (int i = 0; i < magmaFissurePositions.Length; i++)
        {
            GameObject go = new GameObject($"MagmaFissureVent_{i + 1}");
            go.transform.SetParent(transform, false);
            go.transform.position = magmaFissurePositions[i] + Vector3.up * 0.2f;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.maxParticles = 24;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.28f);
            main.gravityModifier = -0.22f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(2.7f, 0.85f, 0.16f, 0.92f),
                new Color(2.4f, 0.30f, 0.08f, 0.90f));

            var emission = ps.emission;
            emission.rateOverTime = 9f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.1f, 0.2f, 3.2f);
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var colLife = ps.colorOverLifetime;
            colLife.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.3f), 0f), new GradientColorKey(new Color(1f, 0.2f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.2f), new GradientAlphaKey(0f, 1f) });
            colLife.color = new ParticleSystem.MinMaxGradient(g);

            ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = addMat;
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            psr.receiveShadows = false;
        }
    }

    // Vẽ vòng ánh sáng bao quanh khu vực đấu trường Boss
    private void BuildBossArenaRing()
    {
        Material srcRing = CombatVfxManager.GetAdditiveRingMaterial();
        Mesh quad = CombatVfxManager.GetHorizontalQuadMesh();
        if (srcRing == null || quad == null) return;

        GameObject ringGo = new GameObject("BossArenaMagicRing");
        ringGo.transform.SetParent(transform, false);
        ringGo.transform.position = bossArenaCenter + Vector3.up * 0.14f;
        ringGo.transform.localScale = new Vector3(32f, 1f, 32f);
        bossRingTransform = ringGo.transform;

        MeshFilter mf = ringGo.AddComponent<MeshFilter>();
        mf.sharedMesh = quad;

        MeshRenderer mr = ringGo.AddComponent<MeshRenderer>();
        bossRingMat = new Material(srcRing);
        Color hdrCol = bossRingColor * 1.55f;
        hdrCol.a = 0.68f;
        if (bossRingMat.HasProperty(BaseColorId)) bossRingMat.SetColor(BaseColorId, hdrCol);
        if (bossRingMat.HasProperty(ColorId)) bossRingMat.SetColor(ColorId, hdrCol);
        mr.sharedMaterial = bossRingMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        GameObject innerGo = new GameObject("BossArenaInnerRune");
        innerGo.transform.SetParent(transform, false);
        innerGo.transform.position = bossArenaCenter + Vector3.up * 0.16f;
        innerGo.transform.localScale = new Vector3(20.5f, 1f, 20.5f);
        bossInnerRingTransform = innerGo.transform;

        MeshFilter imf = innerGo.AddComponent<MeshFilter>();
        imf.sharedMesh = quad;

        MeshRenderer imr = innerGo.AddComponent<MeshRenderer>();
        bossInnerRingMat = new Material(srcRing);
        Color innerCol = biome == BiomeTheme.FrostAurora
            ? new Color(0.55f, 1.25f, 2.35f, 0.56f)
            : new Color(2.35f, 0.92f, 0.22f, 0.56f);
        if (bossInnerRingMat.HasProperty(BaseColorId)) bossInnerRingMat.SetColor(BaseColorId, innerCol);
        if (bossInnerRingMat.HasProperty(ColorId)) bossInnerRingMat.SetColor(ColorId, innerCol);
        imr.sharedMaterial = bossInnerRingMat;
        imr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        imr.receiveShadows = false;
    }

    // Cập nhật lập lòe đèn, dịch chuyển hệ hạt theo camera và culling vật thể ở xa
    private void LateUpdate()
    {
        if (followTarget == null) ResolveFollowTarget();
        Vector3 viewerPos = followTarget != null ? followTarget.position : bossArenaCenter;
        if (weatherAnchor != null && followTarget != null)
        {
            weatherAnchor.position = viewerPos;
        }

        float dt = Time.deltaTime;
        float t = Time.time;

        cullTimer -= dt;
        if (cullTimer <= 0f)
        {
            cullTimer = CullInterval;
            UpdateDistanceCulling(viewerPos);
        }

        if (flickeringLights != null && baseLightIntensities != null)
        {
            lightFlickerTimer += dt;
            if (lightFlickerTimer >= 0.05f)
            {
                lightFlickerTimer = 0f;
                float amp = biome == BiomeTheme.InfernoHold ? 0.26f : 0.14f;
                float speed = biome == BiomeTheme.InfernoHold ? 5.2f : 2.1f;

                for (int i = 0; i < flickeringLights.Length; i++)
                {
                    Light l = flickeringLights[i];
                    if (l == null || !l.enabled) continue;
                    float wave = Mathf.Sin(t * speed + i * 1.7f) * 0.6f + Mathf.Sin(t * (speed * 2.3f) + i * 3.1f) * 0.4f;
                    l.intensity = baseLightIntensities[i] * (1f + wave * amp);
                }
            }
        }

        if (bossRingTransform != null && bossRingTransform.gameObject.activeSelf)
        {
            float rotDir = biome == BiomeTheme.FrostAurora ? 10f : -12f;
            bossRingTransform.Rotate(Vector3.up, rotDir * dt, Space.Self);
            float pulse = 31.5f + Mathf.Sin(t * 2.2f) * 1.1f;
            bossRingTransform.localScale = new Vector3(pulse, 1f, pulse);
        }
        if (bossInnerRingTransform != null && bossInnerRingTransform.gameObject.activeSelf)
        {
            float rotInner = biome == BiomeTheme.FrostAurora ? -18f : 20f;
            bossInnerRingTransform.Rotate(Vector3.up, rotInner * dt, Space.Self);
            float innerPulse = 20.5f + Mathf.Cos(t * 2.8f) * 0.8f;
            bossInnerRingTransform.localScale = new Vector3(innerPulse, 1f, innerPulse);
        }

        if (auroraRibbons != null && auroraBaseYs != null)
        {
            auroraColorTimer += dt;
            bool updateAuroraColor = auroraColorTimer >= AuroraColorUpdateInterval;
            if (updateAuroraColor) auroraColorTimer = 0f;

            auroraPosTimer += dt;
            bool updateAuroraPos = auroraPosTimer >= 0.033f;
            if (updateAuroraPos) auroraPosTimer = 0f;

            if (updateAuroraPos || updateAuroraColor)
            {
                for (int i = 0; i < auroraRibbons.Length; i++)
                {
                    Transform r = auroraRibbons[i];
                    if (r == null) continue;
                    if (updateAuroraPos)
                    {
                        Vector3 lp = r.localPosition;
                        lp.y = auroraBaseYs[i] + Mathf.Sin(t * 0.45f + i * 1.4f) * 2.6f;
                        if (auroraBaseXs != null) lp.x = auroraBaseXs[i] + Mathf.Cos(t * 0.25f + i * 0.9f) * 3.2f;
                        r.localPosition = lp;
                    }

                    if (updateAuroraColor && auroraRenderers != null && auroraPropBlock != null && auroraBaseColors != null && auroraRenderers[i] != null)
                    {
                        float shimmer = 0.85f + 0.28f * Mathf.Sin(t * 0.85f + i * 1.9f);
                        Color c = auroraBaseColors[i];
                        Color dynamicCol = new Color(c.r * shimmer, c.g * shimmer, c.b * (1.15f - 0.15f * Mathf.Cos(t * 0.7f + i)), c.a);
                        auroraPropBlock.SetColor(BaseColorId, dynamicCol);
                        auroraPropBlock.SetColor(ColorId, dynamicCol);
                        auroraRenderers[i].SetPropertyBlock(auroraPropBlock);
                    }
                }
            }
        }
    }

    // Tắt tạm particle và đèn ngoài tầm nhìn nhằm tối ưu FPS
    private void UpdateDistanceCulling(Vector3 viewerPos)
    {
        if (flickeringLights != null && lightPositions != null)
        {
            int maxLights = Mathf.Clamp(maxActiveSceneLights, 0, closestLightIndices.Length);
            int pickedCount = 0;

            for (int i = 0; i < flickeringLights.Length; i++)
            {
                Light l = flickeringLights[i];
                if (l == null) continue;
                float dx = lightPositions[i].x - viewerPos.x;
                float dz = lightPositions[i].z - viewerPos.z;
                float sqr = dx * dx + dz * dz;
                if (sqr > activeLightCullSqrDist || maxLights <= 0) continue;

                if (pickedCount < maxLights)
                {
                    int insert = pickedCount;
                    while (insert > 0 && sqr < closestLightSqrDists[insert - 1])
                    {
                        closestLightSqrDists[insert] = closestLightSqrDists[insert - 1];
                        closestLightIndices[insert] = closestLightIndices[insert - 1];
                        insert--;
                    }
                    closestLightSqrDists[insert] = sqr;
                    closestLightIndices[insert] = i;
                    pickedCount++;
                }
                else if (sqr < closestLightSqrDists[maxLights - 1])
                {
                    int insert = maxLights - 1;
                    while (insert > 0 && sqr < closestLightSqrDists[insert - 1])
                    {
                        closestLightSqrDists[insert] = closestLightSqrDists[insert - 1];
                        closestLightIndices[insert] = closestLightIndices[insert - 1];
                        insert--;
                    }
                    closestLightSqrDists[insert] = sqr;
                    closestLightIndices[insert] = i;
                }
            }

            for (int i = 0; i < flickeringLights.Length; i++)
            {
                Light l = flickeringLights[i];
                if (l == null) continue;

                bool enableLight = false;
                for (int k = 0; k < pickedCount; k++)
                {
                    if (closestLightIndices[k] == i)
                    {
                        enableLight = true;
                        break;
                    }
                }
                if (l.enabled != enableLight) l.enabled = enableLight;
            }
        }

        for (int i = 0; i < culledParticles.Count; i++)
        {
            ParticleSystem ps = culledParticles[i];
            if (ps == null) continue;
            Vector3 p = culledParticlePositions[i];
            float dx = p.x - viewerPos.x;
            float dz = p.z - viewerPos.z;
            float sqr = dx * dx + dz * dz;

            if (sqr <= ParticleEnableSqrDist)
            {
                if (!ps.isPlaying) ps.Play(false);
            }
            else if (sqr > ParticleDisableSqrDist)
            {
                if (ps.isPlaying) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        float bdx = bossArenaCenter.x - viewerPos.x;
        float bdz = bossArenaCenter.z - viewerPos.z;
        bool showBossRings = (bdx * bdx + bdz * bdz) <= BossRingCullSqrDist;
        if (bossRingTransform != null && bossRingTransform.gameObject.activeSelf != showBossRings)
        {
            bossRingTransform.gameObject.SetActive(showBossRings);
        }
        if (bossInnerRingTransform != null && bossInnerRingTransform.gameObject.activeSelf != showBossRings)
        {
            bossInnerRingTransform.gameObject.SetActive(showBossRings);
        }
    }
}
