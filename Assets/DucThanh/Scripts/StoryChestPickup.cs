using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Rương kho báu xuất hiện sau khi đánh thắng một đợt quái, chứa mảnh cốt truyện và phần thưởng vàng
public class StoryChestPickup : MonoBehaviour
{
    public static readonly List<StoryChestPickup> ActiveChests = new List<StoryChestPickup>(8);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

    private static readonly WaitForSeconds WaitFadeDelay = new WaitForSeconds(0.15f);
    private static GameObject cachedChestPrefab;
    private static Gradient cachedSparkleGradient;
    private static Material sharedBeamMat;
    private static Material sharedRingMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        ActiveChests.Clear();
        cachedChestPrefab = null;
        cachedSparkleGradient = null;
        if (sharedBeamMat != null)
        {
            if (Application.isPlaying) Destroy(sharedBeamMat);
            else DestroyImmediate(sharedBeamMat);
            sharedBeamMat = null;
        }
        if (sharedRingMat != null)
        {
            if (Application.isPlaying) Destroy(sharedRingMat);
            else DestroyImmediate(sharedRingMat);
            sharedRingMat = null;
        }
    }

    [Header("Story & Reward Data")]
    public int mapLevel = 1;
    public int chapterIndex;
    public string chapterTitle;
    public string chapterSubtitle;
    [TextArea(3, 6)] public string storyBody;
    public int goldReward = 80;
    public float interactRadius = 4.2f;

    public bool IsOpened { get; private set; }

    private Transform visualPivot;
    private Transform groundRingTransform;
    private GameObject beamObject;
    private ParticleSystem sparklePs;
    private Light glowLight;
    private Material fallbackChestMat;
    private Vector3 baseVisualLocalPos;
    private float baseLightIntensity = 3.2f;
    private Transform playerTransform;
    private float nextFarCheckTime;

    private void OnEnable()
    {
        if (!ActiveChests.Contains(this))
        {
            ActiveChests.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveChests.Remove(this);
    }

    private void OnDestroy()
    {
        if (fallbackChestMat != null) Destroy(fallbackChestMat);
    }

    // Tạo rương cốt truyện hoàn chỉnh trên địa hình kèm cột sáng, vòng hào quang và hạt lấp lánh
    public static StoryChestPickup Spawn(
        Vector3 groundPos,
        int mapLevel,
        int chapterIndex,
        string chapterTitle,
        string chapterSubtitle,
        string storyBody,
        int goldReward)
    {
        EnsureChestPrefab();

        GameObject root = new GameObject($"StoryChest_M{mapLevel}_Ch{chapterIndex + 1}");
        root.transform.position = groundPos;

        StoryChestPickup chest = root.AddComponent<StoryChestPickup>();
        chest.mapLevel = mapLevel;
        chest.chapterIndex = chapterIndex;
        chest.chapterTitle = chapterTitle;
        chest.chapterSubtitle = chapterSubtitle;
        chest.storyBody = storyBody;
        chest.goldReward = Mathf.Max(0, goldReward);

        chest.BuildVisuals();

        CombatVfxManager.SpawnCheckpointActivateEffect(groundPos);
        CombatVfxManager.SpawnFloatingText(
            groundPos + Vector3.up * 2.1f,
            $"✦ Rương Cổ (+{chest.goldReward} Vàng) ✦",
            new Color(1f, 0.88f, 0.34f),
            1.25f,
            2.2f);

        return chest;
    }

    // Tìm rương chưa mở gần nhất trong bán kính tương tác
    public static StoryChestPickup GetNearestInteractable(Vector3 playerPos)
    {
        StoryChestPickup nearest = null;
        float bestSqrDist = float.MaxValue;

        for (int i = ActiveChests.Count - 1; i >= 0; i--)
        {
            StoryChestPickup c = ActiveChests[i];
            if (c == null || c.IsOpened) continue;

            Vector3 diff = playerPos - c.transform.position;
            diff.y = 0f;
            float sqrDist = diff.sqrMagnitude;
            float maxSqr = c.interactRadius * c.interactRadius;

            if (sqrDist <= maxSqr && sqrDist < bestSqrDist)
            {
                bestSqrDist = sqrDist;
                nearest = c;
            }
        }

        return nearest;
    }

    public static bool IsPlayerNearAnyUnopenedChest(Transform player)
    {
        return player != null && GetNearestInteractable(player.position) != null;
    }

    // Tránh đặt hai rương chồng lên nhau nếu hai sự kiện sinh rương ở gần nhau
    public static Vector3 AvoidOverlapWithExistingChests(Vector3 candidate, float minSpacing = 3.0f)
    {
        float minSqr = minSpacing * minSpacing;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            bool tooClose = false;
            for (int i = 0; i < ActiveChests.Count; i++)
            {
                StoryChestPickup existing = ActiveChests[i];
                if (existing == null || existing.IsOpened) continue;

                Vector3 diff = candidate - existing.transform.position;
                diff.y = 0f;
                if (diff.sqrMagnitude < minSqr)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose) return candidate;

            float angle = attempt * 60f * Mathf.Deg2Rad;
            candidate += new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * minSpacing;
        }

        return candidate;
    }

    private static void EnsureChestPrefab()
    {
        if (cachedChestPrefab != null) return;

        cachedChestPrefab = GameAssetPreloader.GetAsset<GameObject>("Rewards/SM_Rewards_ChestGold")
            ?? Resources.Load<GameObject>("Rewards/SM_Rewards_ChestGold");

#if UNITY_EDITOR
        if (cachedChestPrefab == null)
        {
            cachedChestPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PolyOne/Free Rewards/Prefabs/SM_Rewards_ChestGold.prefab");
        }
#endif
    }

    private void BuildVisuals()
    {
        GameObject pivot = new GameObject("VisualPivot");
        pivot.transform.SetParent(transform, false);
        baseVisualLocalPos = new Vector3(0f, 0.12f, 0f);
        pivot.transform.localPosition = baseVisualLocalPos;
        visualPivot = pivot.transform;

        if (cachedChestPrefab != null)
        {
            GameObject model = Instantiate(cachedChestPrefab, pivot.transform);
            model.name = "StoryChestModel";
            foreach (Collider col in model.GetComponentsInChildren<Collider>(true))
            {
                Destroy(col);
            }
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * 1.55f;
        }
        else
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "StoryChestFallback";
            box.transform.SetParent(pivot.transform, false);
            Collider col = box.GetComponent<Collider>();
            if (col != null) Destroy(col);
            box.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            box.transform.localScale = new Vector3(1.1f, 0.85f, 0.78f);

            Renderer r = box.GetComponent<Renderer>();
            if (r != null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                fallbackChestMat = new Material(sh);
                Color goldCol = new Color(0.96f, 0.74f, 0.20f, 1f);
                fallbackChestMat.color = goldCol;
                if (fallbackChestMat.HasProperty(BaseColorId)) fallbackChestMat.SetColor(BaseColorId, goldCol);
                if (fallbackChestMat.HasProperty(MetallicId)) fallbackChestMat.SetFloat(MetallicId, 0.75f);
                if (fallbackChestMat.HasProperty(SmoothnessId)) fallbackChestMat.SetFloat(SmoothnessId, 0.75f);
                r.sharedMaterial = fallbackChestMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        // Cột ánh sáng vàng giúp người chơi dễ nhận ra rương trên đấu trường
        beamObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beamObject.name = "ChestLightBeam";
        beamObject.transform.SetParent(transform, false);
        Collider beamCol = beamObject.GetComponent<Collider>();
        if (beamCol != null) Destroy(beamCol);
        beamObject.transform.localPosition = new Vector3(0f, 7f, 0f);
        beamObject.transform.localScale = new Vector3(0.38f, 7f, 0.38f);

        Renderer br = beamObject.GetComponent<Renderer>();
        if (br != null)
        {
            br.sharedMaterial = GetSharedBeamMaterial();
            br.shadowCastingMode = ShadowCastingMode.Off;
            br.receiveShadows = false;
        }

        // Vòng hào quang xoay dưới chân rương
        GameObject ringObj = new GameObject("ChestGroundRing");
        ringObj.transform.SetParent(transform, false);
        ringObj.transform.localPosition = new Vector3(0f, 0.14f, 0f);
        ringObj.transform.localScale = new Vector3(2.8f, 1f, 2.8f);
        MeshFilter rmf = ringObj.AddComponent<MeshFilter>();
        rmf.sharedMesh = CombatVfxManager.GetHorizontalQuadMesh();
        MeshRenderer rmr = ringObj.AddComponent<MeshRenderer>();
        rmr.sharedMaterial = GetSharedRingMaterial();
        rmr.shadowCastingMode = ShadowCastingMode.Off;
        rmr.receiveShadows = false;
        groundRingTransform = ringObj.transform;

        sparklePs = AttachSparkles();

        GameObject lightObj = new GameObject("ChestGlowLight");
        lightObj.transform.SetParent(transform, false);
        lightObj.transform.localPosition = Vector3.up * 0.9f;
        glowLight = lightObj.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.shadows = LightShadows.None;
        glowLight.color = new Color(1f, 0.80f, 0.25f);
        glowLight.range = 7.5f;
        glowLight.intensity = baseLightIntensity;
    }

    private ParticleSystem AttachSparkles()
    {
        GameObject go = new GameObject("ChestSparkles");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 0.45f;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 2.4f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.11f, 0.24f);
        main.gravityModifier = -0.25f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(2.2f, 1.85f, 0.45f, 0.95f),
            new Color(2.3f, 2.2f, 1.35f, 0.9f));

        var emission = ps.emission;
        emission.rateOverTime = 10f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.65f;

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        if (cachedSparkleGradient == null)
        {
            cachedSparkleGradient = new Gradient();
            cachedSparkleGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.80f, 0.22f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.25f), new GradientAlphaKey(0f, 1f) });
        }
        colOver.color = cachedSparkleGradient;

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = CombatVfxManager.GetAdditiveStarMaterial();
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        ps.Play();
        return ps;
    }

    private void Update()
    {
        if (IsOpened || GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen) return;

        float t = Time.time;
        if (t < nextFarCheckTime) return;

        if (playerTransform == null && PlayerController.Instance != null)
        {
            playerTransform = PlayerController.Instance.transform;
        }

        if (playerTransform != null)
        {
            Vector3 diff = playerTransform.position - transform.position;
            diff.y = 0f;
            const float activeSqrDist = 32f * 32f;
            bool inRange = diff.sqrMagnitude <= activeSqrDist;

            if (glowLight != null)
            {
                bool allowLight = inRange && GameSettingsManager.CurrentGraphicsPreset != GraphicsPreset.Low;
                if (glowLight.enabled != allowLight) glowLight.enabled = allowLight;
            }
            if (sparklePs != null)
            {
                if (inRange && !sparklePs.isPlaying) sparklePs.Play(false);
                else if (!inRange && sparklePs.isPlaying) sparklePs.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }

            if (!inRange)
            {
                nextFarCheckTime = t + 0.22f;
                return;
            }
        }
        nextFarCheckTime = 0f;

        if (visualPivot != null)
        {
            float bob = Mathf.Sin(t * 2.6f) * 0.06f;
            visualPivot.localPosition = baseVisualLocalPos + new Vector3(0f, bob, 0f);
            visualPivot.Rotate(Vector3.up, 28f * Time.deltaTime, Space.World);
        }

        if (groundRingTransform != null)
        {
            groundRingTransform.Rotate(Vector3.up, -42f * Time.deltaTime, Space.World);
        }

        if (glowLight != null && glowLight.enabled)
        {
            glowLight.intensity = baseLightIntensity + Mathf.Sin(t * 3.8f) * 0.55f;
        }
    }

    // Mở rương, cộng vàng, phát hiệu ứng và mở bảng đọc mảnh cốt truyện
    public void OpenChest()
    {
        if (IsOpened) return;
        IsOpened = true;

        if (beamObject != null) beamObject.SetActive(false);
        if (groundRingTransform != null) groundRingTransform.gameObject.SetActive(false);
        if (glowLight != null) glowLight.enabled = false;
        if (sparklePs != null) sparklePs.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        GameAudioManager.PlayPickup();
        GameAudioManager.PlayCheckpoint();

        if (ScoreManager.Instance != null && goldReward > 0)
        {
            ScoreManager.Instance.AddGold(goldReward);
        }

        CombatVfxManager.SpawnGoldCollectEffect(transform.position + Vector3.up * 0.8f, goldReward);
        StoryGuideManager.EnsureInstance().HandleChestOpened(this);

        StartCoroutine(FadeAndDestroyRoutine());
    }

    private static Material GetSharedBeamMaterial()
    {
        if (sharedBeamMat == null)
        {
            sharedBeamMat = new Material(CombatVfxManager.GetAdditiveBeamMaterial());
            Color beamColHdr = new Color(1.9f, 1.48f, 0.35f, 0.52f);
            if (sharedBeamMat.HasProperty(BaseColorId)) sharedBeamMat.SetColor(BaseColorId, beamColHdr);
            if (sharedBeamMat.HasProperty(ColorId)) sharedBeamMat.SetColor(ColorId, beamColHdr);
        }
        return sharedBeamMat;
    }

    private static Material GetSharedRingMaterial()
    {
        if (sharedRingMat == null)
        {
            sharedRingMat = new Material(CombatVfxManager.GetAdditiveRingMaterial());
            Color ringColHdr = new Color(2.0f, 1.52f, 0.32f, 0.76f);
            if (sharedRingMat.HasProperty(BaseColorId)) sharedRingMat.SetColor(BaseColorId, ringColHdr);
            if (sharedRingMat.HasProperty(ColorId)) sharedRingMat.SetColor(ColorId, ringColHdr);
        }
        return sharedRingMat;
    }

    private IEnumerator FadeAndDestroyRoutine()
    {
        yield return WaitFadeDelay;

        Vector3 startScale = transform.localScale;
        const float duration = 0.35f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(elapsed / duration);
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, k * k);
            yield return null;
        }

        Destroy(gameObject);
    }
}
