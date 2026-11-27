using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Quản lý đồng vàng rơi ra từ quái/boss với hiệu ứng nảy vật lý, tia sáng phân cấp và tự hút vào người chơi
public class GoldPickup : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

    public int goldAmount = 20;
    public float magnetRadius = 2.35f;
    public float pickupRadius = 1.25f;
    public float magnetSpeed = 13.5f;

    private static GameObject cachedCoinCrownPrefab;
    private static GameObject cachedCoinStarPrefab;
    private static GameObject cachedBagPrefab;
    private static GameObject cachedChestGoldPrefab;

    private static readonly Material[] SharedBeamMats = new Material[3];
    private static readonly Material[] SharedRingMats = new Material[2];
    private static Gradient cachedSparkleGradient;

    private static readonly Stack<GoldPickup>[] PickupPools =
    {
        new Stack<GoldPickup>(24),
        new Stack<GoldPickup>(24),
        new Stack<GoldPickup>(8),
        new Stack<GoldPickup>(4)
    };
    private static readonly int[] MaxPoolPerVariant = { 24, 24, 8, 4 };
    private static readonly RaycastHit[] GroundSampleHitBuffer = new RaycastHit[12];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticPoolState()
    {
        for (int i = 0; i < PickupPools.Length; i++) PickupPools[i].Clear();
        cachedCoinCrownPrefab = null;
        cachedCoinStarPrefab = null;
        cachedBagPrefab = null;
        cachedChestGoldPrefab = null;
        for (int i = 0; i < SharedBeamMats.Length; i++) SharedBeamMats[i] = null;
        for (int i = 0; i < SharedRingMats.Length; i++) SharedRingMats[i] = null;
        cachedSparkleGradient = null;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private static void OnSceneUnloaded(Scene _)
    {
        for (int i = 0; i < PickupPools.Length; i++)
        {
            PickupPools[i].Clear();
        }
    }

    private Transform playerTransform;
    private Terrain cachedTerrain;
    private Transform visualPivot;
    private Transform groundRingTransform;
    private Light glowLight;
    private ParticleSystem sparklePs;

    private Vector3 velocity;
    private float settledGroundY;
    private float spinSpeed = 200f;
    private float baseLightIntensity = 2.2f;
    private float groundHoverHeight = 0.35f;
    private int bounceCount;
    private bool landed;
    private bool isMagnetized;
    private bool collected;
    private float spawnTime;
    private int poolVariant = -1;
    private bool isPooled;
    private float nextCullCheckTime;
    private float nextGroundSampleTime;

    // Tung ra nhiều đồng vàng hoặc rương vàng tại vị trí quái vừa bị hạ
    public static void SpawnGoldBurst(Vector3 origin, int totalGold, int coinCount = 3, bool isBossDrop = false)
    {
        if (totalGold <= 0) return;
        EnsureRewardPrefabs();

        coinCount = Mathf.Clamp(coinCount, 2, isBossDrop ? 9 : 7);
        int perCoin = Mathf.Max(1, totalGold / coinCount);
        int remainder = totalGold - perCoin * coinCount;

        float baseAngle = Random.Range(0f, 360f);
        float angleStep = 360f / coinCount;

        for (int i = 0; i < coinCount; i++)
        {
            int amt = perCoin + (i == 0 ? remainder : 0);
            float angleRad = (baseAngle + i * angleStep + Random.Range(-22f, 22f)) * Mathf.Deg2Rad;
            Vector3 horizontalDir = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));

            bool isChest = isBossDrop && i == 0 && cachedChestGoldPrefab != null;
            bool isBag = !isChest && ((isBossDrop && i <= 2) || (!isBossDrop && i == 0 && totalGold >= 60)) && cachedBagPrefab != null;
            int variant = isChest ? 3 : isBag ? 2 : (i % 2 == 0 ? 0 : (cachedCoinStarPrefab != null ? 1 : 0));

            GoldPickup pooled = TryRentFromPool(variant);
            if (pooled != null)
            {
                pooled.ResetForSpawn(origin + Vector3.up * 0.95f, amt, horizontalDir, isChest ? 0.65f : 1.0f);
                continue;
            }

            GameObject root = new GameObject(isChest ? "GoldChest_Drop" : isBag ? "GoldBag_Drop" : "GoldCoin_Drop");
            root.transform.position = origin + Vector3.up * 0.95f;

            GameObject pivot = new GameObject("VisualPivot");
            pivot.transform.SetParent(root.transform, false);

            GameObject selectedPrefab = isChest
                ? cachedChestGoldPrefab
                : isBag
                    ? cachedBagPrefab
                    : (variant == 0 ? cachedCoinCrownPrefab : cachedCoinStarPrefab);

            float hoverHeight = isChest ? 0.08f : isBag ? 0.12f : 0.42f;
            float spin = isChest ? 85f : isBag ? 125f : 215f;

            if (selectedPrefab != null)
            {
                GameObject model = Instantiate(selectedPrefab, pivot.transform);
                model.name = "RewardModel";
                foreach (Collider c in model.GetComponentsInChildren<Collider>(true))
                {
                    Destroy(c);
                }
                foreach (Renderer mr in model.GetComponentsInChildren<Renderer>(true))
                {
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }

                if (isChest)
                {
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.identity;
                    model.transform.localScale = Vector3.one * 1.15f;
                }
                else if (isBag)
                {
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
                    model.transform.localScale = Vector3.one * 1.65f;
                }
                else
                {

                    model.transform.localPosition = new Vector3(0f, 0f, -0.03f);
                    model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    model.transform.localScale = Vector3.one * 2.4f;
                }
            }
            else
            {
                CreateFallbackCoinMesh(pivot.transform);
            }

            GameObject beam = new GameObject("LootBeam");
            beam.transform.SetParent(root.transform, false);
            MeshFilter bmf = beam.AddComponent<MeshFilter>();
            bmf.sharedMesh = CombatVfxManager.GetCylinderMesh();

            float beamHeight = isChest ? 3.2f : isBag ? 2.4f : 1.75f;
            float beamWidth = isChest ? 0.34f : isBag ? 0.22f : 0.15f;
            beam.transform.localPosition = new Vector3(0f, beamHeight * 0.5f, 0f);
            beam.transform.localScale = new Vector3(beamWidth, beamHeight * 0.5f, beamWidth);

            MeshRenderer br = beam.AddComponent<MeshRenderer>();
            br.sharedMaterial = GetSharedBeamMaterial(isChest ? 2 : isBag ? 1 : 0);
            br.shadowCastingMode = ShadowCastingMode.Off;
            br.receiveShadows = false;

            GameObject ringObj = new GameObject("GroundRing");
            ringObj.transform.SetParent(root.transform, false);
            ringObj.transform.localPosition = new Vector3(0f, -hoverHeight + 0.08f, 0f);
            float ringSize = isChest ? 1.55f : isBag ? 1.15f : 0.85f;
            ringObj.transform.localScale = new Vector3(ringSize, 1f, ringSize);

            MeshFilter rmf = ringObj.AddComponent<MeshFilter>();
            rmf.sharedMesh = CombatVfxManager.GetHorizontalQuadMesh();
            MeshRenderer rmr = ringObj.AddComponent<MeshRenderer>();
            rmr.sharedMaterial = GetSharedRingMaterial(isChest ? 1 : 0);
            rmr.shadowCastingMode = ShadowCastingMode.Off;
            rmr.receiveShadows = false;

            ParticleSystem sparkles = AttachGoldSparkles(root.transform, isChest ? 8f : isBag ? 5f : 2.5f, isChest ? 0.52f : 0.32f);

            Light l = null;
            float baseInt = isChest ? 3.0f : 2.2f;
            if (isChest && GameSettingsManager.CurrentGraphicsPreset != GraphicsPreset.Low)
            {
                GameObject lightObj = new GameObject("GoldGlow");
                lightObj.transform.SetParent(root.transform, false);
                lightObj.transform.localPosition = Vector3.up * 0.45f;
                l = lightObj.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.color = isChest ? new Color(1f, 0.76f, 0.2f) : new Color(1f, 0.84f, 0.28f);
                l.range = isChest ? 6.0f : 4.5f;
                l.intensity = baseInt;
            }

            GoldPickup gp = root.AddComponent<GoldPickup>();
            gp.goldAmount = amt;
            gp.visualPivot = pivot.transform;
            gp.groundRingTransform = ringObj.transform;
            gp.glowLight = l;
            gp.sparklePs = sparkles;
            gp.spinSpeed = spin;
            gp.baseLightIntensity = baseInt;
            gp.groundHoverHeight = hoverHeight;
            gp.poolVariant = variant;
            gp.isPooled = true;
            gp.InitScatter(horizontalDir, isChest ? 0.65f : 1.0f);
        }
    }

    private static GoldPickup TryRentFromPool(int variant)
    {
        if (variant < 0 || variant >= PickupPools.Length) return null;
        Stack<GoldPickup> pool = PickupPools[variant];
        while (pool.Count > 0)
        {
            GoldPickup item = pool.Pop();
            if (item != null) return item;
        }
        return null;
    }

    private void ResetForSpawn(Vector3 spawnPos, int amount, Vector3 horizontalDir, float distanceMul)
    {
        transform.position = spawnPos;
        goldAmount = amount;
        collected = false;
        landed = false;
        isMagnetized = false;
        bounceCount = 0;
        velocity = Vector3.zero;
        nextCullCheckTime = 0f;
        nextGroundSampleTime = 0f;
        if (visualPivot != null) visualPivot.localRotation = Quaternion.identity;
        if (groundRingTransform != null) groundRingTransform.localRotation = Quaternion.identity;
        if (glowLight != null)
        {
            glowLight.enabled = GameSettingsManager.CurrentGraphicsPreset != GraphicsPreset.Low;
            glowLight.intensity = baseLightIntensity;
        }
        cachedTerrain = Terrain.activeTerrain;
        ResolvePlayer();
        InitScatter(horizontalDir, distanceMul);
        gameObject.SetActive(true);
        if (sparklePs != null)
        {
            sparklePs.Clear();
            sparklePs.Play();
        }
    }

    private static Material GetSharedBeamMaterial(int tier)
    {
        if (SharedBeamMats[tier] == null)
        {
            Material mat = new Material(CombatVfxManager.GetAdditiveBeamMaterial());
            Color col = tier == 2
                ? new Color(2.2f, 1.55f, 0.32f, 0.55f)
                : tier == 1
                    ? new Color(2.0f, 1.5f, 0.35f, 0.45f)
                    : new Color(1.9f, 1.5f, 0.38f, 0.38f);
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, col);
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, col);
            SharedBeamMats[tier] = mat;
        }
        return SharedBeamMats[tier];
    }

    private static Material GetSharedRingMaterial(int tier)
    {
        if (SharedRingMats[tier] == null)
        {
            Material mat = new Material(CombatVfxManager.GetAdditiveRingMaterial());
            Color col = tier == 1
                ? new Color(2.1f, 1.45f, 0.25f, 0.72f)
                : new Color(1.9f, 1.55f, 0.35f, 0.55f);
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, col);
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, col);
            SharedRingMats[tier] = mat;
        }
        return SharedRingMats[tier];
    }

    private static ParticleSystem AttachGoldSparkles(Transform parent, float rate, float radius)
    {
        GameObject go = new GameObject("GoldSparkles");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * 0.25f;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 2f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.15f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.gravityModifier = -0.18f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(2.2f, 1.9f, 0.45f, 0.95f),
            new Color(2.3f, 2.2f, 1.35f, 0.9f));

        var emission = ps.emission;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        if (cachedSparkleGradient == null)
        {
            cachedSparkleGradient = new Gradient();
            cachedSparkleGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.78f, 0.18f), 1f) },
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

    // Lưu trước prefab vật phẩm vàng đã nạp từ màn hình Loading
    public static void RegisterPreloadedPrefabs(GameObject coinCrown, GameObject coinStar, GameObject bag, GameObject chestGold)
    {
        if (coinCrown != null) cachedCoinCrownPrefab = coinCrown;
        if (coinStar != null) cachedCoinStarPrefab = coinStar;
        if (bag != null) cachedBagPrefab = bag;
        if (chestGold != null) cachedChestGoldPrefab = chestGold;
    }

    private static void EnsureRewardPrefabs()
    {
        if (cachedCoinCrownPrefab == null)
            cachedCoinCrownPrefab = GameAssetPreloader.GetAsset<GameObject>("Rewards/SM_Rewards_CoinCrown") ?? Resources.Load<GameObject>("Rewards/SM_Rewards_CoinCrown");
        if (cachedCoinStarPrefab == null)
            cachedCoinStarPrefab = GameAssetPreloader.GetAsset<GameObject>("Rewards/SM_Rewards_CoinStar") ?? Resources.Load<GameObject>("Rewards/SM_Rewards_CoinStar");
        if (cachedBagPrefab == null)
            cachedBagPrefab = GameAssetPreloader.GetAsset<GameObject>("Rewards/SM_Rewards_Bag") ?? Resources.Load<GameObject>("Rewards/SM_Rewards_Bag");
        if (cachedChestGoldPrefab == null)
            cachedChestGoldPrefab = GameAssetPreloader.GetAsset<GameObject>("Rewards/SM_Rewards_ChestGold") ?? Resources.Load<GameObject>("Rewards/SM_Rewards_ChestGold");

#if UNITY_EDITOR
        if (cachedCoinCrownPrefab == null)
            cachedCoinCrownPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PolyOne/Free Rewards/Prefabs/SM_Rewards_CoinCrown.prefab");
        if (cachedCoinStarPrefab == null)
            cachedCoinStarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PolyOne/Free Rewards/Prefabs/SM_Rewards_CoinStar.prefab");
        if (cachedBagPrefab == null)
            cachedBagPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PolyOne/Free Rewards/Prefabs/SM_Rewards_Bag.prefab");
        if (cachedChestGoldPrefab == null)
            cachedChestGoldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PolyOne/Free Rewards/Prefabs/SM_Rewards_ChestGold.prefab");
#endif
    }

    private static void CreateFallbackCoinMesh(Transform parent)
    {
        GameObject coinMesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        coinMesh.name = "CoinVisualFallback";
        coinMesh.transform.SetParent(parent, false);
        Collider col = coinMesh.GetComponent<Collider>();
        if (col != null) Destroy(col);
        coinMesh.transform.localScale = new Vector3(0.46f, 0.08f, 0.46f);
        coinMesh.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Renderer r = coinMesh.GetComponent<Renderer>();
        if (r != null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = new Material(sh);
            mat.color = new Color(1f, 0.82f, 0.16f, 1f);
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, new Color(1f, 0.84f, 0.18f, 1f));
            if (mat.HasProperty(MetallicId)) mat.SetFloat(MetallicId, 0.90f);
            if (mat.HasProperty(SmoothnessId)) mat.SetFloat(SmoothnessId, 0.85f);
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    // Thiết lập hướng văng và lực nảy ban đầu cho đồng xu
    private void InitScatter(Vector3 horizontalDir, float distanceMul)
    {
        spawnTime = Time.time;
        float outSpeed = Random.Range(4.2f, 7.0f) * distanceMul;
        float upSpeed = Random.Range(5.4f, 7.6f);
        velocity = horizontalDir.normalized * outSpeed + Vector3.up * upSpeed;
        settledGroundY = SampleGroundHeight(transform.position) + groundHoverHeight;
        nextGroundSampleTime = Time.time + 0.15f;
    }

    private void Start()
    {
        ResolvePlayer();
        cachedTerrain = Terrain.activeTerrain;
        isMagnetized = false;
        if (spawnTime == 0f) spawnTime = Time.time;
    }

    // Mô phỏng rơi chạm đất, tự xoay, hút vào người chơi ở tầm gần và nhặt vàng
    private void Update()
    {
        if (collected || GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen) return;

        float dt = Time.deltaTime;
        float now = Time.time;
        float age = now - spawnTime;

        if (playerTransform == null)
        {
            ResolvePlayer();
        }

        Vector3 playerTarget = default;
        bool hasPlayerTarget = false;
        if (landed && playerTransform != null)
        {
            if (now < nextCullCheckTime) return;

            const float activeCullSqrDist = 26f * 26f;
            playerTarget = playerTransform.position + Vector3.up * 0.9f;
            hasPlayerTarget = true;
            float cullSqrDist = (playerTarget - transform.position).sqrMagnitude;
            if (cullSqrDist > activeCullSqrDist)
            {
                nextCullCheckTime = now + 0.20f;
                if (glowLight != null && glowLight.enabled) glowLight.enabled = false;
                if (sparklePs != null && sparklePs.isPlaying) sparklePs.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                return;
            }

            if (glowLight != null && glowLight.enabled != (GameSettingsManager.CurrentGraphicsPreset != GraphicsPreset.Low))
            {
                glowLight.enabled = GameSettingsManager.CurrentGraphicsPreset != GraphicsPreset.Low;
            }
            if (sparklePs != null && !sparklePs.isPlaying) sparklePs.Play(false);
        }

        if (visualPivot != null)
        {
            visualPivot.Rotate(Vector3.up, spinSpeed * dt, Space.World);
        }

        if (groundRingTransform != null)
        {
            groundRingTransform.Rotate(Vector3.up, -45f * dt, Space.World);
        }

        if (glowLight != null && glowLight.enabled)
        {
            glowLight.intensity = baseLightIntensity * (0.85f + 0.25f * Mathf.Sin(age * 4.5f));
        }

        if (!landed)
        {
            velocity.y -= 17.5f * dt;
            transform.position += velocity * dt;

            if (now >= nextGroundSampleTime || transform.position.y <= settledGroundY)
            {
                settledGroundY = SampleGroundHeight(transform.position) + groundHoverHeight;
                nextGroundSampleTime = now + 0.15f;
            }

            if (transform.position.y <= settledGroundY && velocity.y < 0f)
            {
                Vector3 p = transform.position;
                p.y = settledGroundY;
                transform.position = p;

                if (bounceCount < 1)
                {
                    bounceCount++;
                    velocity.x *= 0.45f;
                    velocity.z *= 0.45f;
                    velocity.y = Mathf.Abs(velocity.y) * 0.38f;
                }
                else
                {
                    velocity = Vector3.zero;
                    landed = true;
                }
            }
        }
        else if (!isMagnetized)
        {
            Vector3 p = transform.position;
            p.y = settledGroundY + Mathf.Sin(age * 3.4f) * 0.09f;
            transform.position = p;
        }

        if (playerTransform == null) return;
        if (!landed && age < 0.72f) return;

        Vector3 targetPos = hasPlayerTarget ? playerTarget : playerTransform.position + Vector3.up * 0.9f;
        Vector3 diff = targetPos - transform.position;
        float sqrDist = diff.sqrMagnitude;

        if (sqrDist <= magnetRadius * magnetRadius)
        {
            isMagnetized = true;
            landed = true;
            transform.position = Vector3.MoveTowards(transform.position, targetPos, magnetSpeed * dt);
            sqrDist = (targetPos - transform.position).sqrMagnitude;
        }
        else if (isMagnetized)
        {
            isMagnetized = false;
            landed = false;
            velocity = Vector3.zero;
        }

        if (sqrDist <= pickupRadius * pickupRadius)
        {
            Collect();
        }
    }

    // Cộng vàng vào ScoreManager, phát VFX/âm thanh và gửi sự kiện gold_collected
    private void Collect()
    {
        collected = true;

        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.AddGold(goldAmount);
        }

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackGoldCollected(goldAmount);
        }

        CombatVfxManager.SpawnGoldCollectEffect(transform.position + Vector3.up * 0.2f, goldAmount);
        GameAudioManager.PlayPickup();

        if (isPooled && poolVariant >= 0 && poolVariant < PickupPools.Length && PickupPools[poolVariant].Count < MaxPoolPerVariant[poolVariant])
        {
            if (sparklePs != null) sparklePs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (glowLight != null) glowLight.enabled = false;
            gameObject.SetActive(false);
            PickupPools[poolVariant].Push(this);
            return;
        }

        Destroy(gameObject);
    }

    private float SampleGroundHeight(Vector3 pos)
    {
        bool hasRayHit = false;
        float rayY = float.MinValue;
        Vector3 rayOrigin = new Vector3(pos.x, pos.y + 2.5f, pos.z);
        int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, GroundSampleHitBuffer, 60f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = GroundSampleHitBuffer[i].collider;
            if (col == null) continue;
            if (!(col is TerrainCollider))
            {
                if (col is CharacterController) continue;
                if (col.transform == transform || col.transform.IsChildOf(transform)) continue;
                if (OrcEnemyAI.IsPlayerCollider(col) || OrcEnemyAI.IsEnemyCollider(col)) continue;
            }

            float ptY = GroundSampleHitBuffer[i].point.y;
            if (ptY > rayY)
            {
                rayY = ptY;
                hasRayHit = true;
            }
        }

        bool hasTerrainHit = false;
        float terrainY = float.MinValue;
        if (cachedTerrain == null) cachedTerrain = Terrain.activeTerrain;
        if (cachedTerrain != null && cachedTerrain.terrainData != null)
        {
            Vector3 tPos = cachedTerrain.transform.position;
            Vector3 tSize = cachedTerrain.terrainData.size;
            if (pos.x >= tPos.x && pos.x <= tPos.x + tSize.x
                && pos.z >= tPos.z && pos.z <= tPos.z + tSize.z)
            {
                terrainY = cachedTerrain.SampleHeight(pos) + tPos.y;
                hasTerrainHit = true;
            }
        }

        if (hasRayHit && hasTerrainHit) return Mathf.Max(rayY, terrainY);
        if (hasRayHit) return rayY;
        if (hasTerrainHit) return terrainY;

        return pos.y - groundHoverHeight;
    }

    private void ResolvePlayer()
    {
        if (PlayerController.Instance != null)
        {
            playerTransform = PlayerController.Instance.transform;
            return;
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null) playerTransform = playerObj.transform;
    }
}
