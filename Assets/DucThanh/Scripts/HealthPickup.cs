using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Vật phẩm Bình Hồi Máu (PolyOne Free Rewards — SM_Rewards_Bottle):
/// - Tự động nâng cấp cả những Nấm hồi máu cũ đã bake trong Scene thành Bình Máu thủy tinh 3D.
/// - Có vòng sáng ma pháp trên mặt đất, luồng sáng mờ dẫn mắt, hạt sao lấp lánh bay lên và xoay lơ lửng.
/// - Khi Player chạm vào: hồi HP, kích hoạt vòng xoáy hồi máu HDR + chữ nổi (+XX HP) + Telemetry "item_pickup".
/// </summary>
public class HealthPickup : MonoBehaviour
{
    public const string PotionAssetPath = "Assets/PolyOne/Free Rewards/Prefabs/SM_Rewards_Bottle.prefab";

    [Header("Pickup Settings")]
    public int healAmount = 45;
    public float pickupRadius = 1.85f;
    public float rotateSpeed = 75f;
    public float bobAmplitude = 0.13f;
    public float bobFrequency = 2.6f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static GameObject cachedPotionPrefab;
    private static Material sharedRingMat;
    private static Material sharedBeamMat;
    private static Gradient cachedSparkleGradient;

    private Transform playerTransform;
    private PlayerHealth playerHealth;
    private Transform visualTransform;
    private Transform groundRingTransform;
    private Light glowLight;

    private Vector3 basePosition;
    private float baseLightIntensity = 2.4f;
    private float phaseOffset;
    private bool collected;

    public static GameObject GetPotionPrefab()
    {
        if (cachedPotionPrefab == null)
        {
            cachedPotionPrefab = Resources.Load<GameObject>("Rewards/SM_Rewards_Bottle");
        }
#if UNITY_EDITOR
        if (cachedPotionPrefab == null)
        {
            cachedPotionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PotionAssetPath);
        }
#endif
        return cachedPotionPrefab;
    }

    void Start()
    {
        basePosition = transform.position;
        phaseOffset = Random.Range(0f, Mathf.PI * 2f);

        foreach (Collider col in GetComponentsInChildren<Collider>(true))
        {
            col.isTrigger = true;
        }

        EnsurePotionVisual();
        EnsureAuraVfx();
        ResolvePlayer();
    }

    private static Material GetSharedRingMat()
    {
        if (sharedRingMat == null)
        {
            sharedRingMat = new Material(CombatVfxManager.GetAdditiveRingMaterial());
            Color ringCol = new Color(1.9f, 0.45f, 0.72f, 0.68f);
            if (sharedRingMat.HasProperty(BaseColorId)) sharedRingMat.SetColor(BaseColorId, ringCol);
            if (sharedRingMat.HasProperty(ColorId)) sharedRingMat.SetColor(ColorId, ringCol);
        }
        return sharedRingMat;
    }

    private static Material GetSharedBeamMat()
    {
        if (sharedBeamMat == null)
        {
            sharedBeamMat = new Material(CombatVfxManager.GetAdditiveBeamMaterial());
            Color beamCol = new Color(1.75f, 0.42f, 0.68f, 0.36f);
            if (sharedBeamMat.HasProperty(BaseColorId)) sharedBeamMat.SetColor(BaseColorId, beamCol);
            if (sharedBeamMat.HasProperty(ColorId)) sharedBeamMat.SetColor(ColorId, beamCol);
        }
        return sharedBeamMat;
    }

    private void EnsurePotionVisual()
    {
        // Kiểm tra xem object hiện tại đã dùng mesh SM_Rewards_Bottle hay chưa
        bool alreadyBottle = false;
        foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name.IndexOf("Bottle", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                alreadyBottle = true;
                visualTransform = mf.transform;
                break;
            }
        }

        if (!alreadyBottle)
        {
            // Ẩn toàn bộ mesh cũ (ví dụ SM_Mushroom_01 đã bake sẵn trong scene hoặc sphere fallback)
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                r.enabled = false;
            }

            transform.localScale = Vector3.one;

            GameObject potionPrefab = GetPotionPrefab();
            if (potionPrefab != null)
            {
                GameObject visual = Instantiate(potionPrefab, transform);
                visual.name = "PotionVisual";
                visual.transform.localPosition = new Vector3(0f, 0.14f, 0f);
                visual.transform.localRotation = Quaternion.Euler(0f, 0f, 11f);
                visual.transform.localScale = Vector3.one * 2.15f;

                foreach (Collider c in visual.GetComponentsInChildren<Collider>(true))
                {
                    Destroy(c);
                }

                visualTransform = visual.transform;
            }
        }
        else if (visualTransform == transform)
        {
            // Nếu root chính là SM_Rewards_Bottle prefab, chuẩn hóa tỷ lệ hiển thị dễ nhìn trên Terrain
            if (transform.localScale.x < 1.5f)
            {
                transform.localScale = Vector3.one * 2.1f;
            }
        }
    }

    private void EnsureAuraVfx()
    {
        // 1. Đèn điểm hồng-ngọc ấm dịu
        glowLight = GetComponentInChildren<Light>();
        if (glowLight == null)
        {
            GameObject lightObj = new GameObject("PickupGlow");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = Vector3.up * 0.65f;
            glowLight = lightObj.AddComponent<Light>();
            glowLight.type = LightType.Point;
        }
        glowLight.color = new Color(1f, 0.34f, 0.56f);
        glowLight.range = 5.2f;
        glowLight.intensity = baseLightIntensity;

        // 2. Vòng sáng hồi phục trên mặt đất
        if (transform.Find("PotionGroundRing") == null)
        {
            GameObject ringObj = new GameObject("PotionGroundRing");
            ringObj.transform.SetParent(transform, false);
            // Khử scale của parent nếu root bị scale (trường hợp root chính là prefab đã scale 2.1x)
            float invScale = transform.localScale.x > 0.01f ? 1f / transform.localScale.x : 1f;
            ringObj.transform.localPosition = new Vector3(0f, -0.05f * invScale, 0f);
            ringObj.transform.localScale = new Vector3(1.35f * invScale, 1f * invScale, 1.35f * invScale);

            MeshFilter mf = ringObj.AddComponent<MeshFilter>();
            mf.sharedMesh = CombatVfxManager.GetHorizontalQuadMesh();
            MeshRenderer mr = ringObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GetSharedRingMat();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            groundRingTransform = ringObj.transform;
        }

        // 3. Cột sáng mờ thanh mảnh giúp người chơi dễ nhận ra bình máu từ xa
        if (transform.Find("PotionLightBeam") == null)
        {
            float invScale = transform.localScale.x > 0.01f ? 1f / transform.localScale.x : 1f;
            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "PotionLightBeam";
            beam.transform.SetParent(transform, false);
            Collider c = beam.GetComponent<Collider>();
            if (c != null) Destroy(c);

            beam.transform.localPosition = new Vector3(0f, 1.15f * invScale, 0f);
            beam.transform.localScale = new Vector3(0.24f * invScale, 1.15f * invScale, 0.24f * invScale);

            Renderer br = beam.GetComponent<Renderer>();
            if (br != null)
            {
                br.sharedMaterial = GetSharedBeamMat();
                br.shadowCastingMode = ShadowCastingMode.Off;
                br.receiveShadows = false;
            }
        }

        // 4. Hạt sao lấp lánh bay lên quanh bình máu
        if (transform.Find("PotionSparkles") == null)
        {
            float invScale = transform.localScale.x > 0.01f ? 1f / transform.localScale.x : 1f;
            GameObject psObj = new GameObject("PotionSparkles");
            psObj.transform.SetParent(transform, false);
            psObj.transform.localPosition = new Vector3(0f, 0.35f * invScale, 0f);
            psObj.transform.localScale = Vector3.one * invScale;

            ParticleSystem ps = psObj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 2f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.20f);
            main.gravityModifier = -0.22f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(2.1f, 0.48f, 0.78f, 0.95f),
                new Color(0.65f, 2.2f, 1.15f, 0.95f));

            var emission = ps.emission;
            emission.rateOverTime = 7.5f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.48f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            if (cachedSparkleGradient == null)
            {
                cachedSparkleGradient = new Gradient();
                cachedSparkleGradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.45f, 1f, 0.65f), 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.25f), new GradientAlphaKey(0f, 1f) });
            }
            colOver.color = cachedSparkleGradient;

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = CombatVfxManager.GetAdditiveStarMaterial();
            ps.Play();
        }
    }

    void Update()
    {
        if (collected || GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen) return;

        if (playerTransform == null || playerHealth == null)
        {
            ResolvePlayer();
            if (playerTransform == null || playerHealth == null) return;
        }

        Vector3 diff = playerTransform.position - basePosition;
        float verticalDiff = Mathf.Abs(diff.y);
        diff.y = 0f;
        float horizSqrDist = diff.sqrMagnitude;

        // Distance culling: tắt đèn & bỏ qua xoay/nhấp nhô khi Player ở xa > 26m để giảm tải GPU/CPU
        const float activeVisualRadiusSqr = 26f * 26f;
        bool inVisualRange = horizSqrDist <= activeVisualRadiusSqr;
        if (glowLight != null && glowLight.enabled != inVisualRange)
        {
            glowLight.enabled = inVisualRange;
        }
        if (!inVisualRange) return;

        float dt = Time.deltaTime;
        float now = Time.time;

        if (visualTransform != null && visualTransform != transform)
        {
            visualTransform.Rotate(Vector3.up, rotateSpeed * dt, Space.World);
        }
        else
        {
            transform.Rotate(Vector3.up, rotateSpeed * dt, Space.World);
        }

        if (groundRingTransform != null)
        {
            groundRingTransform.Rotate(Vector3.up, -40f * dt, Space.World);
        }

        float wave = Mathf.Sin(now * bobFrequency + phaseOffset);
        transform.position = new Vector3(basePosition.x, basePosition.y + 0.08f + wave * bobAmplitude, basePosition.z);

        if (glowLight != null)
        {
            glowLight.intensity = baseLightIntensity * (0.84f + 0.24f * Mathf.Sin(now * 3.8f + phaseOffset));
        }

        if (!playerHealth.IsAlive) return;

        if (verticalDiff <= 2.5f && horizSqrDist <= pickupRadius * pickupRadius)
        {
            Collect();
        }
    }

    private void Collect()
    {
        collected = true;

        int healthBefore = playerHealth.currentHealth;
        playerHealth.Heal(healAmount);
        int actualHealed = playerHealth.currentHealth - healthBefore;

        CombatVfxManager.SpawnHealEffect(playerTransform, actualHealed > 0 ? actualHealed : healAmount, false);
        GameAudioManager.PlayPickup();

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackItemPickup(healAmount);
        }

        if (StageManager.Instance != null)
        {
            StageManager.Instance.ShowBanner($"+{healAmount} HP (BÌNH HỒI MÁU)", new Color(0.28f, 0.96f, 0.56f), 1.8f);
        }

        Destroy(gameObject);
    }

    private void ResolvePlayer()
    {
        if (PlayerController.Instance != null)
        {
            playerTransform = PlayerController.Instance.transform;
            playerHealth = PlayerHealth.Instance != null ? PlayerHealth.Instance : PlayerController.Instance.GetComponent<PlayerHealth>();
            return;
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            playerHealth = playerObj.GetComponent<PlayerHealth>();
        }
    }
}
