using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Điểm lưu Checkpoint tại các Trại Lửa (SM_Campfire).
/// - Có vòng sáng thánh đường + tàn lửa ấm bay lên xung quanh trại.
/// - Khi Player bước vào vùng trại lần đầu sẽ bùng trụ sáng vàng, hồi máu + vòng xoáy hồi phục,
///   thông báo trên HUD và gửi sự kiện "checkpoint_reached".
/// </summary>
public class CampfireCheckpoint : MonoBehaviour
{
    public static readonly List<CampfireCheckpoint> ActiveCampfires = new List<CampfireCheckpoint>(4);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Checkpoint Settings")]
    public int stageNumber = 1;
    public string checkpointName = "Trại Rừng Ngoại Vi";
    public float activationRadius = 5.5f;
    public int healBonus = 40;

    private Transform playerTransform;
    private PlayerHealth playerHealth;
    private Transform sanctuaryRing;
    private Material ringMatInstance;
    private bool activated;

    private void OnEnable()
    {
        if (!ActiveCampfires.Contains(this))
        {
            ActiveCampfires.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveCampfires.Remove(this);
    }

    void Start()
    {
        ResolvePlayer();
        CreateAmbientCampfireVfx();
    }

    private void OnDestroy()
    {
        if (ringMatInstance != null) Destroy(ringMatInstance);
    }

    void Update()
    {
        if (GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen) return;

        if (playerTransform == null || playerHealth == null)
        {
            ResolvePlayer();
            if (playerTransform == null || playerHealth == null) return;
        }

        Vector3 diff = playerTransform.position - transform.position;
        diff.y = 0f;
        float sqrDist = diff.sqrMagnitude;

        if (sanctuaryRing != null && sqrDist <= 1600f)
        {
            sanctuaryRing.Rotate(Vector3.up, (activated ? 26f : 14f) * Time.deltaTime, Space.World);
        }

        if (activated || !playerHealth.IsAlive) return;

        if (sqrDist <= activationRadius * activationRadius)
        {
            ActivateCheckpoint();
        }
    }

    private void CreateAmbientCampfireVfx()
    {
        // 1. Vòng sáng vàng dịu trên mặt đất quanh Trại Lửa
        if (transform.Find("SanctuaryRing") == null)
        {
            GameObject ring = new GameObject("SanctuaryRing");
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.14f, 0f);
            float diameter = Mathf.Clamp(activationRadius * 1.1f, 3.6f, 6.2f);
            ring.transform.localScale = new Vector3(diameter, 1f, diameter);

            MeshFilter mf = ring.AddComponent<MeshFilter>();
            mf.sharedMesh = CombatVfxManager.GetHorizontalQuadMesh();
            MeshRenderer mr = ring.AddComponent<MeshRenderer>();
            ringMatInstance = new Material(CombatVfxManager.GetAdditiveRingMaterial());
            Color col = new Color(1.55f, 1.05f, 0.28f, 0.36f);
            if (ringMatInstance.HasProperty(BaseColorId)) ringMatInstance.SetColor(BaseColorId, col);
            if (ringMatInstance.HasProperty(ColorId)) ringMatInstance.SetColor(ColorId, col);
            mr.material = ringMatInstance;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            sanctuaryRing = ring.transform;
        }

        // 2. Tàn lửa ấm bay lên quanh khu vực Checkpoint
        if (transform.Find("CampfireEmbers") == null)
        {
            GameObject go = new GameObject("CampfireEmbers");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.45f;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 3f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.19f);
            main.gravityModifier = -0.22f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(2.1f, 1.15f, 0.28f, 0.9f),
                new Color(2.2f, 1.75f, 0.55f, 0.85f));

            var emission = ps.emission;
            emission.rateOverTime = 8f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.45f;
            noise.frequency = 1.1f;

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.42f, 0.12f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.2f), new GradientAlphaKey(0f, 1f) });
            colOver.color = g;

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = CombatVfxManager.GetAdditiveStarMaterial();
            ps.Play();
        }
    }

    private void ActivateCheckpoint()
    {
        activated = true;
        GameAudioManager.PlayCheckpoint();
        CombatVfxManager.SpawnCheckpointActivateEffect(transform.position);

        if (ringMatInstance != null)
        {
            Color activeCol = new Color(0.85f, 2.1f, 1.25f, 0.55f);
            if (ringMatInstance.HasProperty(BaseColorId)) ringMatInstance.SetColor(BaseColorId, activeCol);
            if (ringMatInstance.HasProperty(ColorId)) ringMatInstance.SetColor(ColorId, activeCol);
        }

        if (healBonus > 0 && playerHealth.currentHealth < playerHealth.maxHealth)
        {
            int before = playerHealth.currentHealth;
            playerHealth.Heal(healBonus);
            int actualHealed = playerHealth.currentHealth - before;
            if (actualHealed > 0)
            {
                CombatVfxManager.SpawnHealEffect(playerTransform, actualHealed, false);
            }
        }

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackCheckpointReached(stageNumber);
        }

        if (StageManager.Instance != null)
        {
            StageManager.Instance.ShowBanner($"CHECKPOINT: {checkpointName.ToUpper()} (+{healBonus} HP)", new Color(1f, 0.78f, 0.25f), 2.5f);
        }
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
