using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using TMPro;

// Điều phối toàn bộ tiến trình 3 màn chơi trong mỗi bản đồ, sinh đợt quái, boss, bẫy gai, checkpoint, dẫn đường và HUD nhiệm vụ
public class StageManager : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly RaycastHit[] TerrainHitBuffer = new RaycastHit[16];

    public static StageManager Instance { get; private set; }

    private enum StagePhase
    {
        Intro,
        Stage1Combat,
        TravelToStage2,
        Stage2Combat,
        TravelToStage3,
        Stage3Boss,
        Completed
    }

    [Header("Enemy Prefabs")]
    public GameObject scoutOrcPrefab;
    public GameObject axeOrcPrefab;
    public GameObject shieldTankPrefab;
    public GameObject bossOrcPrefab;

    [Header("Map Enemy Roster")]
    public GameObject[] stage1ExtraEnemyPrefabs;
    public GameObject[] stage2TankPrefabs;
    public GameObject[] stage2StrikerPrefabs;
    public GameObject[] stage3BossGuardPrefabs;

    [Header("Environment & Interactive Props")]
    public GameObject spikeTrapPrefab;
    public GameObject mushroomPickupPrefab;
    public GameObject runeStonePrefab;
    public GameObject goldMinePrefab;
    public GameObject trolleyGoldPrefab;
    public GameObject stoneWallPrefab;
    public GameObject fireflyFxPrefab;

    [Header("Stage Skyboxes")]
    public Material skyboxStage1Dawn;
    public Material skyboxStage2Sunset;
    public Material skyboxStage3Lunar;

    [Header("Tọa độ 3 Khu vực trên Terrain")]
    public Vector3 stage1Center = new Vector3(132f, 25.5f, 368f);
    public Vector3 stage2Center = new Vector3(298f, 15f, 296f);
    public Vector3 stage3Center = new Vector3(230f, 18f, 240f);
    public float zoneTriggerRadius = 28f;

    [Header("Map Identity & Next Map Progression")]
    public int mapIndex = 1;
    public string mapDisplayName = "Rừng Orc & Mỏ Vàng";
    public string nextMapSceneName = "Lv2";

    [Header("Tùy biến Nội dung & Tên Quái theo Map")]
    public string stage1Title = "MÀN 1/3 • TRẠI RỪNG NGOẠI VI";
    public string stage1Banner = "MÀN 1: TRẠI RỪNG NGOẠI VI\nTiêu diệt quân trinh sát Orc!";
    public string stage1ClearBanner = "HOÀN THÀNH MÀN 1!\nHãy theo cột sáng vàng đến Thung Lũng Bẫy!";
    public string travelStage2Title = "ĐẾN MÀN 2 • THUNG LŨNG BẪY";
    public string stage2Title = "MÀN 2/3 • THUNG LŨNG BẪY & LÍNH KHIÊN";
    public string stage2Banner = "MÀN 2: THUNG LŨNG BẪY & LÍNH KHIÊN\nCẩn thận bẫy gai và Orc Tanker!";
    public string stage2Wave2Banner = "ĐỢT 2 — LÍNH KHIÊN & RÌU TĂNG VIỆN!";
    public string stage2ClearBanner = "HOÀN THÀNH MÀN 2!\nTiến tới Mỏ Vàng Bị Chiếm tiêu diệt BOSS!";
    public string travelStage3Title = "ĐẾN MÀN 3 • MỎ VÀNG BỊ CHIẾM (BOSS)";
    public string stage3Title = "MÀN 3/3 • ĐẤU TRƯỜNG MỎ VÀNG";
    public string bossDisplayName = "ĐẠI TƯỚNG ORC WARLORD";
    public string bossIntroBanner = "MÀN 3: ĐẠI TƯỚNG ORC WARLORD!\nHạ gục Boss để giải phóng Mỏ Vàng!";
    public string bossVictoryBanner = "CHIẾN THẮNG! ĐẠI TƯỚNG ORC ĐÃ BỊ TIÊU DIỆT!";
    public string victorySubtitle = "Đại Tướng Orc Warlord đã bị tiêu diệt. Mỏ Vàng và khu rừng đã được giải phóng!";

    [Header("Cân bằng Độ khó & Hiệu ứng Map")]
    public float enemyHpMultiplier = 1f;
    public float enemyDamageMultiplier = 1f;
    public Color stage2UnitAuraColor = new Color(0.3f, 0.7f, 1f);
    public Color bossAuraColor = new Color(1f, 0.18f, 0.12f);

    public int CurrentStage { get; private set; } = 1;
    public int MapLevel => Mathf.Clamp(mapIndex, 1, 3);

    private StagePhase phase = StagePhase.Intro;
    private Transform playerTransform;
    private PlayerController playerController;

    private readonly List<EnemyHealth> activeWaveEnemies = new List<EnemyHealth>();
    private int currentWaveIndex;
    private int totalWavesInStage;
    private bool spawningWave;

    private EnemyHealth activeBoss;
    private OrcEnemyAI activeBossAI;
    private bool bossEnraged;

    private GameObject beaconObject;
    private Transform beaconRingTransform;
    private Material beaconBeamMat;
    private Material beaconRingMat;
    private ParticleSystem beaconMotesPs;
    private Vector3 currentWaypointTarget;

    private TMP_Text hudStageTitleText;
    private TMP_Text hudObjectiveText;
    private TMP_Text hudDashText;
    private GameObject bossBarRoot;
    private Slider bossHpSlider;
    private TMP_Text bossNameText;
    private TMP_Text bossHpValueText;
    private Image bossFillImage;
    private Image bossGhostFillImage;
    private Image bossFrameImage;
    private float bossHpRatio = 1f;
    private float bossGhostHpRatio = 1f;
    private float bossGhostHoldTimer;
    private GameObject bannerRoot;
    private TMP_Text bannerText;
    private CanvasGroup bannerCanvasGroup;
    private float bannerStartTime;
    private float bannerHideTime;

    private StagePhase lastHudPhase = (StagePhase)(-1);
    private int lastHudWave = -1;
    private int lastHudMetric = int.MinValue;
    private int lastDashBlockingState = -1;

    // Khởi tạo instance, đọc hệ số độ khó theo map và áp dụng tối ưu hiệu năng
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        CurrentStage = 1;
    }

    // Dựng vật thể môi trường, HUD, cột sáng dẫn đường và bắt đầu Màn 1
    private void Start()
    {
        ApplyRuntimePerformanceOptimizations();
        ResolvePlayer();

        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.manageStageTelemetry = false;
        }

        stage1Center = SnapPositionToTerrain(stage1Center);
        stage2Center = SnapPositionToTerrain(stage2Center);
        stage3Center = SnapPositionToTerrain(stage3Center);

        BuildStageEnvironments();
        BuildRuntimeHud();
        CreateWaypointBeacon();

        ApplySkybox(skyboxStage1Dawn);
        StartCoroutine(BeginStage1Routine());
    }

    private void OnDestroy()
    {
        if (beaconBeamMat != null) Destroy(beaconBeamMat);
        if (beaconRingMat != null) Destroy(beaconRingMat);
    }

    // Theo dõi điều kiện hoàn thành từng màn và cập nhật hướng dẫn trên HUD
    private void Update()
    {
        if (beaconRingTransform != null && beaconObject != null && beaconObject.activeSelf)
        {
            beaconRingTransform.Rotate(Vector3.up, 38f * Time.deltaTime, Space.World);
        }

        if (bannerRoot != null && bannerRoot.activeSelf)
        {
            float now = Time.unscaledTime;
            if (now >= bannerHideTime)
            {
                bannerRoot.SetActive(false);
            }
            else if (bannerCanvasGroup != null)
            {
                float fadeIn = Mathf.Clamp01((now - bannerStartTime) / 0.22f);
                float fadeOut = Mathf.Clamp01((bannerHideTime - now) / 0.38f);
                bannerCanvasGroup.alpha = Mathf.Min(fadeIn, fadeOut);
            }
        }

        UpdateBossBarVisuals();

        if (GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen) return;

        if (playerTransform == null)
        {
            ResolvePlayer();
            if (playerTransform == null) return;
        }

        CleanupDeadEnemies();
        UpdateDashHud();
        UpdatePhaseLogic();
    }

    // Hiển thị thông báo sự kiện ở chính giữa màn hình kèm hiệu ứng mờ vào/ra mượt mà
    public void ShowBanner(string message, Color color, float duration = 2.5f)
    {
        if (bannerRoot == null || bannerText == null) return;
        bannerText.text = message;
        bannerText.color = color;
        bannerStartTime = Time.unscaledTime;
        bannerHideTime = bannerStartTime + Mathf.Max(0.8f, duration);
        if (bannerCanvasGroup != null) bannerCanvasGroup.alpha = 0f;
        bannerRoot.SetActive(true);
    }

    // Cập nhật thanh sát thương trễ và nhịp phát sáng khi Boss vào pha cuồng nộ
    private void UpdateBossBarVisuals()
    {
        if (bossBarRoot == null || !bossBarRoot.activeSelf) return;

        float dt = Time.unscaledDeltaTime;
        if (bossGhostFillImage != null)
        {
            if (bossGhostHpRatio > bossHpRatio)
            {
                if (bossGhostHoldTimer > 0f)
                {
                    bossGhostHoldTimer -= dt;
                }
                else
                {
                    bossGhostHpRatio = Mathf.MoveTowards(bossGhostHpRatio, bossHpRatio, dt * 0.45f);
                    CombatHudStyler.SetGhostFillRatio(bossGhostFillImage, bossGhostHpRatio);
                }
            }
            else if (bossGhostHpRatio < bossHpRatio)
            {
                bossGhostHpRatio = bossHpRatio;
                CombatHudStyler.SetGhostFillRatio(bossGhostFillImage, bossGhostHpRatio);
            }
        }

        if (bossFillImage != null)
        {
            if (bossEnraged)
            {
                float pulse = 0.86f + 0.14f * Mathf.Sin(Time.unscaledTime * 6f);
                bossFillImage.color = Color.Lerp(CombatHudStyler.BossFillEnraged * pulse, new Color(1f, 0.56f, 0.16f, 1f), 0.25f);
            }
            else
            {
                bossFillImage.color = CombatHudStyler.BossFillNormal;
            }
        }
    }

    private IEnumerator BeginStage1Routine()
    {
        phase = StagePhase.Intro;
        CurrentStage = 1;

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackLevelStart(MapLevel);
        }

        SetHudStage(stage1Title, "Chuẩn bị chiến đấu...");
        ShowBanner(stage1Banner, new Color(1f, 0.88f, 0.45f), 3.0f);

        yield return new WaitForSeconds(1.8f);

        phase = StagePhase.Stage1Combat;
        currentWaveIndex = 1;
        totalWavesInStage = 2;
        SpawnStage1Wave(1);
    }

    private void UpdatePhaseLogic()
    {
        switch (phase)
        {
            case StagePhase.Stage1Combat:
                int count1 = activeWaveEnemies.Count;
                if (lastHudPhase != phase || lastHudWave != currentWaveIndex || lastHudMetric != count1)
                {
                    lastHudPhase = phase;
                    lastHudWave = currentWaveIndex;
                    lastHudMetric = count1;
                    SetHudStage(stage1Title, $"Đợt {currentWaveIndex}/{totalWavesInStage} — Quái còn lại: {count1}");
                }
                if (!spawningWave && count1 == 0)
                {
                    if (currentWaveIndex < totalWavesInStage)
                    {
                        currentWaveIndex++;
                        StartCoroutine(DelayedWaveSpawn(() => SpawnStage1Wave(currentWaveIndex), 1.6f, "ĐỢT 2 ĐANG TỚI!"));
                    }
                    else
                    {
                        CompleteStage1();
                    }
                }
                break;

            case StagePhase.TravelToStage2:
                float dist2 = HorizontalDistance(playerTransform.position, stage2Center);
                int dist2Int = Mathf.RoundToInt(dist2);
                if (lastHudPhase != phase || lastHudMetric != dist2Int)
                {
                    lastHudPhase = phase;
                    lastHudMetric = dist2Int;
                    SetHudStage(travelStage2Title, $"Theo cột sáng vàng tới Trại 2 ({dist2Int}m)");
                }
                if (dist2 <= zoneTriggerRadius)
                {
                    StartStage2();
                }
                break;

            case StagePhase.Stage2Combat:
                int count2 = activeWaveEnemies.Count;
                if (lastHudPhase != phase || lastHudWave != currentWaveIndex || lastHudMetric != count2)
                {
                    lastHudPhase = phase;
                    lastHudWave = currentWaveIndex;
                    lastHudMetric = count2;
                    SetHudStage(stage2Title, $"Đợt {currentWaveIndex}/{totalWavesInStage} — Quái còn lại: {count2} (Coi chừng bẫy!)");
                }
                if (!spawningWave && count2 == 0)
                {
                    if (currentWaveIndex < totalWavesInStage)
                    {
                        currentWaveIndex++;
                        StartCoroutine(DelayedWaveSpawn(() => SpawnStage2Wave(currentWaveIndex), 1.8f, stage2Wave2Banner));
                    }
                    else
                    {
                        CompleteStage2();
                    }
                }
                break;

            case StagePhase.TravelToStage3:
                float dist3 = HorizontalDistance(playerTransform.position, stage3Center);
                int dist3Int = Mathf.RoundToInt(dist3);
                if (lastHudPhase != phase || lastHudMetric != dist3Int)
                {
                    lastHudPhase = phase;
                    lastHudMetric = dist3Int;
                    SetHudStage(travelStage3Title, $"Theo cột sáng đỏ tới Đấu Trường Boss ({dist3Int}m)");
                }
                if (dist3 <= zoneTriggerRadius)
                {
                    StartStage3Boss();
                }
                break;

            case StagePhase.Stage3Boss:
                int adds = 0;
                for (int i = 0; i < activeWaveEnemies.Count; i++)
                {
                    if (activeWaveEnemies[i] != null && !activeWaveEnemies[i].isBoss && !activeWaveEnemies[i].IsDead)
                        adds++;
                }
                if (lastHudPhase != phase || lastHudMetric != adds)
                {
                    lastHudPhase = phase;
                    lastHudMetric = adds;
                    string subObj = adds > 0
                        ? $"Hạ {bossDisplayName} (Còn {adds} hộ vệ)"
                        : $"Hạ {bossDisplayName} để giành chiến thắng!";
                    SetHudStage(stage3Title, subObj);
                }
                break;
        }
    }

    private IEnumerator DelayedWaveSpawn(System.Action spawnAction, float delay, string bannerMsg)
    {
        spawningWave = true;
        ShowBanner(bannerMsg, new Color(1f, 0.72f, 0.28f), 2.0f);
        yield return new WaitForSeconds(delay);
        spawnAction?.Invoke();
        spawningWave = false;
    }

    private static GameObject PickWavePrefab(GameObject[] pool, int index, GameObject fallback)
    {
        if (pool == null || pool.Length == 0) return fallback;
        for (int attempt = 0; attempt < pool.Length; attempt++)
        {
            GameObject candidate = pool[(index + attempt) % pool.Length];
            if (candidate != null) return candidate;
        }
        return fallback;
    }

    // Sinh các đợt quái tại khu vực 1
    private void SpawnStage1Wave(int wave)
    {
        bool hasVariety = stage1ExtraEnemyPrefabs != null && stage1ExtraEnemyPrefabs.Length > 0;
        if (wave == 1)
        {
            int count = hasVariety ? 4 : 3;
            for (int i = 0; i < count; i++)
            {
                GameObject pref = i == 0
                    ? scoutOrcPrefab
                    : PickWavePrefab(stage1ExtraEnemyPrefabs, i - 1, scoutOrcPrefab);
                float spd = pref == scoutOrcPrefab ? 3.4f : 3.65f;
                string pName = pref != null ? pref.name : "Unit";
                SpawnOrc(pref, stage1Center, 9f, 14f, 85, 12, spd, 1.85f, 0.05f, 1.62f, 1.0f, 100, false, $"S1_W1_{pName}");
            }
        }
        else
        {
            int count = hasVariety ? 5 : 4;
            for (int i = 0; i < count; i++)
            {
                GameObject fallback = i == count - 1 && axeOrcPrefab != null ? axeOrcPrefab : scoutOrcPrefab;
                GameObject pref = i < 2
                    ? (i == 0 ? scoutOrcPrefab : fallback)
                    : PickWavePrefab(stage1ExtraEnemyPrefabs, i, fallback);
                bool isHeavy = i >= count - 2;
                int hp = isHeavy ? 125 : 100;
                int dmg = isHeavy ? 16 : 14;
                float spd = isHeavy ? 3.8f : 3.6f;
                float kb = isHeavy ? 0.15f : 0.08f;
                float sc = isHeavy ? 1.06f : 1.0f;
                string pName = pref != null ? pref.name : "Unit";
                SpawnOrc(pref, stage1Center, 10f, 15f, hp, dmg, spd, 1.85f, kb, 1.56f, sc, 120, false, $"S1_W2_{pName}");
            }
        }
    }

    // Hoàn tất Màn 1, mở cổng checkpoint và chỉ đường sang Màn 2
    private void CompleteStage1()
    {
        phase = StagePhase.TravelToStage2;

        ShowBanner(stage1ClearBanner, new Color(0.25f, 0.95f, 0.55f), 3.6f);
        GameAudioManager.PlayStageClear();
        SetWaypointBeacon(stage2Center, new Color(1f, 0.78f, 0.15f));

        SpawnHealthPickupAt(stage1Center + new Vector3(4f, 0f, -2f), 35);
    }

    // Kích hoạt Màn 2 với các cụm bẫy gai và lính khiên
    private void StartStage2()
    {
        phase = StagePhase.Stage2Combat;
        CurrentStage = 2;
        HideWaypointBeacon();
        ApplySkybox(skyboxStage2Sunset);

        ShowBanner(stage2Banner, new Color(1f, 0.58f, 0.2f), 3.2f);

        currentWaveIndex = 1;
        totalWavesInStage = 2;
        SpawnStage2Wave(1);
    }

    private void SpawnStage2Wave(int wave)
    {
        GameObject tankFallback = shieldTankPrefab != null ? shieldTankPrefab : scoutOrcPrefab;
        GameObject axeFallback = axeOrcPrefab != null ? axeOrcPrefab : scoutOrcPrefab;
        bool hasVariety = (stage2TankPrefabs != null && stage2TankPrefabs.Length > 0)
            || (stage2StrikerPrefabs != null && stage2StrikerPrefabs.Length > 0);

        for (int i = 0; i < 2; i++)
        {
            GameObject tankPref = PickWavePrefab(stage2TankPrefabs, (wave - 1) * 2 + i, tankFallback);
            bool isSecondWaveBrute = hasVariety && wave == 2 && i == 1;
            int tankHp = isSecondWaveBrute ? 220 : 185;
            int tankDmg = isSecondWaveBrute ? 19 : 16;
            float tankScale = isSecondWaveBrute ? 1.24f : 1.16f;
            string tankName = tankPref != null ? tankPref.name : "Tank";
            GameObject tank = SpawnOrc(tankPref, stage2Center, 9f, 15f, tankHp, tankDmg, 3.25f, 2.0f, 0.60f, 1.75f, tankScale, 160, false, $"Stage2_Tank_{tankName}");
            AddUnitAuraLight(tank, stage2UnitAuraColor, 3.8f, 1.7f);
        }

        int axeCount = wave == 1 ? (hasVariety ? 3 : 2) : (hasVariety ? 4 : 3);
        for (int i = 0; i < axeCount; i++)
        {
            GameObject strikerPref = PickWavePrefab(stage2StrikerPrefabs, (wave - 1) * 3 + i, axeFallback);
            string strikerName = strikerPref != null ? strikerPref.name : "Striker";
            GameObject striker = SpawnOrc(strikerPref, stage2Center, 10f, 16f, 120, 18, 4.15f, 1.9f, 0.18f, 1.50f, 1.06f, 140, false, $"Stage2_Striker_{strikerName}");
            if (hasVariety && i % 2 == 1)
            {
                AddUnitAuraLight(striker, stage2UnitAuraColor, 3.0f, 1.2f);
            }
        }
    }

    private void CompleteStage2()
    {
        phase = StagePhase.TravelToStage3;

        ShowBanner(stage2ClearBanner, new Color(0.25f, 0.95f, 0.55f), 3.6f);
        GameAudioManager.PlayStageClear();
        SetWaypointBeacon(stage3Center, new Color(1f, 0.22f, 0.22f));

        SpawnHealthPickupAt(stage2Center + new Vector3(-4f, 0f, -3f), 45);
    }

    // Triệu hồi Boss đấu trường cùng thanh máu Boss và quân hộ vệ
    private void StartStage3Boss()
    {
        phase = StagePhase.Stage3Boss;
        CurrentStage = 3;
        bossEnraged = false;
        HideWaypointBeacon();
        ApplySkybox(skyboxStage3Lunar);

        ShowBanner(bossIntroBanner, new Color(1f, 0.28f, 0.28f), 3.5f);
        GameAudioManager.PlayBgm(GameAudioManager.BgmTrack.Boss, 1.2f);
        GameAudioManager.PlayBossRoar();

        GameObject bPrefab = bossOrcPrefab != null ? bossOrcPrefab : (shieldTankPrefab != null ? shieldTankPrefab : scoutOrcPrefab);
        GameObject bossObj = SpawnOrc(bPrefab, stage3Center, 5f, 9f, 700, 24, 3.9f, 2.4f, 0.78f, 1.50f, 1.55f, 500, true, "BOSS_Unit");

        if (bossObj != null)
        {
            AddUnitAuraLight(bossObj, bossAuraColor, 7f, 3.2f);
            activeBoss = bossObj.GetComponent<EnemyHealth>();
            activeBossAI = bossObj.GetComponent<OrcEnemyAI>();

            if (bossNameText != null)
            {
                bossNameText.text = $"❖ BOSS: {bossDisplayName} ❖";
            }

            if (activeBoss != null)
            {
                activeBoss.OnHealthChanged += HandleBossHealthChanged;
                activeBoss.OnDied += HandleBossDied;
                ShowBossBar(activeBoss.CurrentHealth, activeBoss.maxHealth);
            }
        }
    }

    // Cập nhật thanh máu Boss, thanh sát thương trễ và kích hoạt pha cuồng nộ khi máu Boss dưới 50%
    private void HandleBossHealthChanged(int currentHp, int maxHp)
    {
        float nextRatio = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0f;
        if (nextRatio < bossHpRatio)
        {
            bossGhostHoldTimer = 0.20f;
        }
        else if (nextRatio > bossHpRatio)
        {
            bossGhostHpRatio = nextRatio;
            CombatHudStyler.SetGhostFillRatio(bossGhostFillImage, bossGhostHpRatio);
        }
        bossHpRatio = nextRatio;

        if (bossHpSlider != null)
        {
            bossHpSlider.maxValue = maxHp;
            bossHpSlider.value = currentHp;
        }

        if (bossHpValueText != null)
        {
            bossHpValueText.text = $"{currentHp} / {maxHp}";
        }

        if (!bossEnraged && currentHp > 0 && currentHp <= maxHp * 0.5f)
        {
            bossEnraged = true;
            if (activeBossAI != null)
            {
                int enragedDmg = Mathf.Max(1, Mathf.RoundToInt(30 * Mathf.Max(0.5f, enemyDamageMultiplier)));
                activeBossAI.ApplyCombatStats(4.5f, enragedDmg, 2.5f, 0.85f, 1.22f);
            }

            if (ThirdPersonCamera.Instance != null)
            {
                ThirdPersonCamera.Instance.AddCameraShake(0.25f, 0.35f);
            }

            ShowBanner("BOSS CUỒNG NỘ — GỌI VIỆN BINH!", new Color(1f, 0.2f, 0.2f), 3.0f);
            GameAudioManager.PlayBossRoar();
            if (activeBoss != null)
            {
                CombatVfxManager.SpawnBossEnrageEffect(activeBoss.transform);
            }

            GameObject addFallback = axeOrcPrefab != null ? axeOrcPrefab : scoutOrcPrefab;
            int guardCount = (stage3BossGuardPrefabs != null && stage3BossGuardPrefabs.Length > 0) ? 3 : 2;
            for (int i = 0; i < guardCount; i++)
            {
                GameObject guardPref = PickWavePrefab(stage3BossGuardPrefabs, i, addFallback);
                string gName = guardPref != null ? guardPref.name : "Guard";
                GameObject guard = SpawnOrc(guardPref, stage3Center, 8f, 13f, 105, 15, 4.0f, 1.9f, 0.15f, 1.52f, 1.06f, 110, false, $"BossGuard_{gName}");
                AddUnitAuraLight(guard, bossAuraColor, 3.6f, 1.5f);
            }
        }
    }

    // Xử lý chiến thắng khi tiêu diệt Boss và gọi màn hình kết quả Victory
    private void HandleBossDied()
    {
        if (phase == StagePhase.Completed) return;
        phase = StagePhase.Completed;

        if (bossBarRoot != null) bossBarRoot.SetActive(false);

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackLevelComplete(MapLevel);
            TelemetryManager.Instance.TrackGameEnded("Win");
        }

        ShowBanner(bossVictoryBanner, new Color(0.2f, 1f, 0.45f), 4.0f);
        CombatVfxManager.SpawnVictoryCelebration(playerTransform != null ? playerTransform.position : stage3Center);
        StartCoroutine(ShowVictoryDelayed());
    }

    private IEnumerator ShowVictoryDelayed()
    {
        yield return new WaitForSecondsRealtime(1.5f);
        if (GameOverManager.Instance != null)
        {
            GameOverManager.Instance.ShowVictory();
        }
    }

    // Khởi tạo quái hoặc Boss trên địa hình kèm gắn vầng hào quang và chỉ số cân bằng
    private GameObject SpawnOrc(
        GameObject prefab,
        Vector3 arenaCenter,
        float minRadius,
        float maxRadius,
        int hp,
        int damage,
        float speed,
        float range,
        float kbResist,
        float cooldown,
        float scale,
        int scoreVal,
        bool isBoss,
        string unitName)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[StageManager] Thiếu Enemy Prefab để sinh quái!");
            return null;
        }

        Vector3 spawnPos = arenaCenter;
        bool hasNavMeshPoint = false;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector2 dir2D = Random.insideUnitCircle.normalized;
            if (dir2D.sqrMagnitude < 0.01f) dir2D = Vector2.right;

            Vector3 candidate = arenaCenter + new Vector3(dir2D.x, 0f, dir2D.y) * Random.Range(minRadius, maxRadius);
            candidate = SnapPositionToTerrain(candidate);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 7f, NavMesh.AllAreas))
            {
                candidate = navHit.position;
                hasNavMeshPoint = true;
            }

            if (!Physics.CheckSphere(candidate + Vector3.up * 1.45f, 0.55f, ~0, QueryTriggerInteraction.Ignore))
            {
                spawnPos = candidate;
                break;
            }

            if (attempt == 5)
            {
                spawnPos = candidate;
            }
        }

        GameObject enemy = Instantiate(prefab, spawnPos, Quaternion.identity);
        enemy.name = unitName;
        enemy.transform.localScale = Vector3.one * scale;
        CombatVfxManager.SpawnEnemySpawnEffect(spawnPos, isBoss);
        if (mapIndex >= 2 && (isBoss || kbResist >= 0.45f || unitName.IndexOf("Demon", System.StringComparison.OrdinalIgnoreCase) >= 0))
        {
            CombatVfxManager.AttachEnemyBiomeAura(enemy.transform, mapIndex, isBoss || kbResist >= 0.45f);
        }

        int scaledHp = Mathf.Max(1, Mathf.RoundToInt(hp * Mathf.Max(0.5f, enemyHpMultiplier)));
        int scaledDmg = Mathf.Max(1, Mathf.RoundToInt(damage * Mathf.Max(0.5f, enemyDamageMultiplier)));

        EnemyHealth eh = enemy.GetComponent<EnemyHealth>();
        if (eh != null)
        {
            eh.isBoss = isBoss;
            eh.scoreValue = scoreVal;
            float mapGoldMult = 1f + Mathf.Max(0, mapIndex - 1) * 0.12f;
            eh.goldReward = isBoss ? 220 : Mathf.Max(22, Mathf.RoundToInt(scoreVal * 0.30f * mapGoldMult));
            GameObject potionPrefab = HealthPickup.GetPotionPrefab();
            eh.healPickupPrefab = potionPrefab != null ? potionPrefab : mushroomPickupPrefab;
            eh.dropHealChance = isBoss ? 0f : 0.22f;
            eh.SetMaxHealth(scaledHp);
            activeWaveEnemies.Add(eh);
        }

        OrcEnemyAI ai = enemy.GetComponent<OrcEnemyAI>();
        if (ai != null)
        {
            ai.ApplyCombatStats(speed, scaledDmg, range, kbResist, cooldown);
            if (hasNavMeshPoint)
            {
                ai.EnsureOnNavMesh(7f);
            }
        }

        return enemy;
    }

    // Dựng hoặc liên kết trại lửa checkpoint, cụm bẫy gai và bình hồi máu trên bản đồ
    private void BuildStageEnvironments()
    {

        if (GameObject.Find("StageProps_Generated") != null)
        {
            return;
        }

        GameObject propsRoot = new GameObject("StageProps_Generated");

        CreateCheckpoint("Checkpoint_Stage1", stage1Center + new Vector3(-2.5f, 0f, 2f), 1, "Trại Rừng Ngoại Vi", 30, propsRoot.transform);
        CreateCheckpoint("Checkpoint_Stage2", stage2Center + new Vector3(3f, 0f, 0f), 2, "Thung Lũng Bẫy", 45, propsRoot.transform);
        CreateCheckpoint("Checkpoint_Stage3", stage3Center + new Vector3(0f, 0f, 16f), 3, "Cổng Mỏ Vàng", 55, propsRoot.transform);

        if (runeStonePrefab != null)
        {
            SpawnPropOnTerrain(runeStonePrefab, stage1Center + new Vector3(9f, 0f, 6f), Quaternion.Euler(0f, -35f, 0f), Vector3.one * 1.3f, propsRoot.transform, "Stage1_RuneStone");
        }
        SpawnHealthPickupAt(stage1Center + new Vector3(-6f, 0f, -5f), 35, propsRoot.transform);

        Vector3 mid12A = Vector3.Lerp(stage1Center, stage2Center, 0.35f);
        Vector3 mid12B = Vector3.Lerp(stage1Center, stage2Center, 0.68f);
        SpawnHealthPickupAt(mid12A, 35, propsRoot.transform);
        SpawnHealthPickupAt(mid12B, 35, propsRoot.transform);

        Vector3[] trapOffsets =
        {
            new Vector3(-7f, 0f, 5f),
            new Vector3(6f, 0f, 7f),
            new Vector3(-9f, 0f, -6f),
            new Vector3(8f, 0f, -5f),
            new Vector3(0f, 0f, 9f),
            new Vector3(-2f, 0f, -9f)
        };
        for (int i = 0; i < trapOffsets.Length; i++)
        {
            SpawnTrapAt(stage2Center + trapOffsets[i], propsRoot.transform, $"Stage2_SpikeTrap_{i + 1}");
        }
        SpawnHealthPickupAt(stage2Center + new Vector3(-11f, 0f, 1f), 40, propsRoot.transform);
        SpawnHealthPickupAt(stage2Center + new Vector3(11f, 0f, -2f), 40, propsRoot.transform);

        Vector3 mid23 = Vector3.Lerp(stage2Center, stage3Center, 0.5f);
        SpawnHealthPickupAt(mid23, 40, propsRoot.transform);

        if (goldMinePrefab != null)
        {
            SpawnPropOnTerrain(goldMinePrefab, stage3Center + new Vector3(0f, 0f, -12f), Quaternion.Euler(0f, 180f, 0f), Vector3.one * 1.4f, propsRoot.transform, "Stage3_GoldMine");
        }
        if (trolleyGoldPrefab != null)
        {
            SpawnPropOnTerrain(trolleyGoldPrefab, stage3Center + new Vector3(5.5f, 0f, -8f), Quaternion.Euler(0f, 35f, 0f), Vector3.one * 1.25f, propsRoot.transform, "Stage3_TrolleyGold");
            SpawnPropOnTerrain(trolleyGoldPrefab, stage3Center + new Vector3(-6.5f, 0f, -7f), Quaternion.Euler(0f, -25f, 0f), Vector3.one * 1.2f, propsRoot.transform, "Stage3_TrolleyGold_2");
        }
        if (stoneWallPrefab != null)
        {
            int wallCount = 8;
            float ringRadius = 19f;
            for (int i = 0; i < wallCount; i++)
            {

                if (i == 1 || i == 2) continue;
                float angle = i * (360f / wallCount) * Mathf.Deg2Rad;
                Vector3 wallPos = stage3Center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ringRadius;
                Quaternion wallRot = Quaternion.LookRotation(stage3Center - wallPos);
                SpawnPropOnTerrain(stoneWallPrefab, wallPos, wallRot, Vector3.one * 1.4f, propsRoot.transform, $"Stage3_ArenaWall_{i}");
            }
        }
        if (fireflyFxPrefab != null)
        {
            SpawnPropOnTerrain(fireflyFxPrefab, stage3Center + new Vector3(0f, 1.5f, 0f), Quaternion.identity, Vector3.one, propsRoot.transform, "Stage3_Fireflies");
        }

        SpawnHealthPickupAt(stage3Center + new Vector3(-12f, 0f, 4f), 50, propsRoot.transform);
        SpawnHealthPickupAt(stage3Center + new Vector3(12f, 0f, 4f), 50, propsRoot.transform);
    }

    private void CreateCheckpoint(string objName, Vector3 rawPos, int stageNum, string title, int healBonus, Transform parent)
    {
        Vector3 pos = SnapPositionToTerrain(rawPos);
        GameObject cpObj = new GameObject(objName);
        cpObj.transform.SetParent(parent, false);
        cpObj.transform.position = pos;

        CampfireCheckpoint cp = cpObj.AddComponent<CampfireCheckpoint>();
        cp.stageNumber = stageNum;
        cp.checkpointName = title;
        cp.healBonus = healBonus;
        cp.activationRadius = 6f;
    }

    private void SpawnTrapAt(Vector3 rawPos, Transform parent, string trapName)
    {
        Vector3 pos = SnapPositionToTerrain(rawPos);
        GameObject trapObj;
        if (spikeTrapPrefab != null)
        {
            trapObj = Instantiate(spikeTrapPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), parent);
            trapObj.transform.localScale = Vector3.one * 1.35f;
        }
        else
        {
            trapObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trapObj.transform.SetParent(parent, false);
            trapObj.transform.position = pos;
            trapObj.transform.localScale = new Vector3(2.2f, 0.25f, 2.2f);
            Renderer r = trapObj.GetComponent<Renderer>();
            if (r != null) r.material.color = new Color(0.85f, 0.2f, 0.15f);
        }

        trapObj.name = trapName;
        TrapHazard hazard = trapObj.GetComponent<TrapHazard>();
        if (hazard == null) hazard = trapObj.AddComponent<TrapHazard>();
        hazard.damage = Mathf.Max(1, Mathf.RoundToInt(18 * Mathf.Max(0.5f, enemyDamageMultiplier)));
        hazard.hitCooldown = 1.25f;
        hazard.triggerRadius = 1.75f;
    }

    private static void ApplyRuntimePerformanceOptimizations()
    {
        QualitySettings.vSyncCount = 0;
        if (Application.targetFrameRate <= 0 || Application.targetFrameRate > 120)
        {
            Application.targetFrameRate = 60;
        }

        Camera mainCam = Camera.main;
        if (mainCam != null && mainCam.farClipPlane > 240f)
        {
            mainCam.farClipPlane = 240f;
        }

        QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance, 38f);

        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = Mathf.Max(terrain.heightmapPixelError, 16f);
            terrain.treeDistance = Mathf.Min(terrain.treeDistance, 140f);
            terrain.treeBillboardDistance = Mathf.Min(terrain.treeBillboardDistance, 35f);
            terrain.treeMaximumFullLODCount = Mathf.Min(terrain.treeMaximumFullLODCount, 24);
            terrain.detailObjectDistance = Mathf.Min(terrain.detailObjectDistance, 45f);
            terrain.detailObjectDensity = Mathf.Min(terrain.detailObjectDensity, 0.65f);
            terrain.basemapDistance = Mathf.Min(terrain.basemapDistance, 180f);
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    private void SpawnHealthPickupAt(Vector3 rawPos, int healAmt, Transform parent = null)
    {
        Vector3 pos = SnapPositionToTerrain(rawPos) + Vector3.up * 0.16f;
        GameObject potionPrefab = HealthPickup.GetPotionPrefab();
        GameObject prefabToUse = potionPrefab != null ? potionPrefab : mushroomPickupPrefab;

        GameObject obj;
        if (prefabToUse != null)
        {
            obj = Instantiate(prefabToUse, pos, Quaternion.identity, parent);
            obj.transform.localScale = Vector3.one * (prefabToUse == potionPrefab ? 2.15f : 1.6f);
        }
        else
        {
            obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (parent != null) obj.transform.SetParent(parent, false);
            obj.transform.position = pos;
            obj.transform.localScale = Vector3.one * 0.6f;
            Renderer r = obj.GetComponent<Renderer>();
            if (r != null) r.material.color = new Color(0.2f, 0.95f, 0.45f);
        }

        obj.name = "HealthPickup_Potion";
        HealthPickup pickup = obj.GetComponent<HealthPickup>();
        if (pickup == null) pickup = obj.AddComponent<HealthPickup>();
        pickup.healAmount = healAmt;
    }

    private void SpawnPropOnTerrain(GameObject prefab, Vector3 rawPos, Quaternion rot, Vector3 scale, Transform parent, string objName)
    {
        Vector3 pos = SnapPositionToTerrain(rawPos);
        GameObject obj = Instantiate(prefab, pos, rot, parent);
        obj.name = objName;
        obj.transform.localScale = scale;
    }

    private void AddUnitAuraLight(GameObject target, Color color, float range, float intensity)
    {
        if (target == null) return;
        GameObject lightObj = new GameObject("AuraLight");
        lightObj.transform.SetParent(target.transform, false);
        lightObj.transform.localPosition = Vector3.up * 1.4f;
        Light l = lightObj.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
    }

    private void ApplySkybox(Material skyMat)
    {
        if (skyMat == null) return;
        RenderSettings.skybox = skyMat;
    }

    private void CreateWaypointBeacon()
    {
        beaconObject = new GameObject("StageWaypointBeacon");

        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "BeaconPillar";
        pillar.transform.SetParent(beaconObject.transform, false);
        Collider col = pillar.GetComponent<Collider>();
        if (col != null) Destroy(col);
        pillar.transform.localPosition = Vector3.up * 26f;
        pillar.transform.localScale = new Vector3(1.45f, 26f, 1.45f);

        Renderer pr = pillar.GetComponent<Renderer>();
        if (pr != null)
        {
            beaconBeamMat = new Material(CombatVfxManager.GetAdditiveBeamMaterial());
            Color initCol = new Color(1.9f, 1.45f, 0.3f, 0.55f);
            if (beaconBeamMat.HasProperty("_BaseColor")) beaconBeamMat.SetColor("_BaseColor", initCol);
            if (beaconBeamMat.HasProperty("_Color")) beaconBeamMat.SetColor("_Color", initCol);
            pr.material = beaconBeamMat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
        }

        GameObject ring = new GameObject("BeaconGroundRing");
        ring.transform.SetParent(beaconObject.transform, false);
        ring.transform.localPosition = Vector3.up * 0.16f;
        ring.transform.localScale = new Vector3(5.6f, 1f, 5.6f);
        MeshFilter mf = ring.AddComponent<MeshFilter>();
        mf.sharedMesh = CombatVfxManager.GetHorizontalQuadMesh();
        MeshRenderer rmr = ring.AddComponent<MeshRenderer>();
        beaconRingMat = new Material(CombatVfxManager.GetAdditiveRingMaterial());
        rmr.material = beaconRingMat;
        rmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rmr.receiveShadows = false;
        beaconRingTransform = ring.transform;

        GameObject motesObj = new GameObject("BeaconMotes");
        motesObj.transform.SetParent(beaconObject.transform, false);
        motesObj.transform.localPosition = Vector3.up * 0.5f;
        beaconMotesPs = motesObj.AddComponent<ParticleSystem>();
        beaconMotesPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = beaconMotesPs.main;
        main.duration = 4f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 6.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
        main.gravityModifier = -0.35f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = beaconMotesPs.emission;
        emission.rateOverTime = 14f;

        var shape = beaconMotesPs.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1.6f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        var rend = beaconMotesPs.GetComponent<ParticleSystemRenderer>();
        rend.material = CombatVfxManager.GetAdditiveStarMaterial();

        AddUnitAuraLight(beaconObject, new Color(1f, 0.8f, 0.2f), 16f, 4.5f);
        beaconObject.SetActive(false);
    }

    private void SetWaypointBeacon(Vector3 targetPos, Color color)
    {
        currentWaypointTarget = SnapPositionToTerrain(targetPos);
        if (beaconObject == null) return;

        beaconObject.transform.position = currentWaypointTarget;
        Color hdrBeam = new Color(color.r * 1.9f, color.g * 1.9f, color.b * 1.9f, 0.58f);
        Color hdrRing = new Color(color.r * 2.1f, color.g * 2.1f, color.b * 2.1f, 0.78f);

        if (beaconBeamMat != null)
        {
            if (beaconBeamMat.HasProperty(BaseColorId)) beaconBeamMat.SetColor(BaseColorId, hdrBeam);
            if (beaconBeamMat.HasProperty(ColorId)) beaconBeamMat.SetColor(ColorId, hdrBeam);
        }
        if (beaconRingMat != null)
        {
            if (beaconRingMat.HasProperty(BaseColorId)) beaconRingMat.SetColor(BaseColorId, hdrRing);
            if (beaconRingMat.HasProperty(ColorId)) beaconRingMat.SetColor(ColorId, hdrRing);
        }
        if (beaconMotesPs != null)
        {
            var main = beaconMotesPs.main;
            main.startColor = new Color(color.r * 2.2f, color.g * 2.2f, color.b * 2.2f, 0.95f);
        }

        Light l = beaconObject.GetComponentInChildren<Light>();
        if (l != null) l.color = color;

        beaconObject.SetActive(true);
        if (beaconMotesPs != null) beaconMotesPs.Play();
    }

    private void HideWaypointBeacon()
    {
        if (beaconObject != null) beaconObject.SetActive(false);
    }

    // Khởi tạo hoặc nâng cấp giao diện bảng nhiệm vụ, thanh máu Boss và banner trên Canvas
    private void BuildRuntimeHud()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        Transform existingMission = canvas.transform.Find("StageMissionHUD");
        GameObject missionPanel;
        if (existingMission != null)
        {
            missionPanel = existingMission.gameObject;
            hudStageTitleText = existingMission.Find("StageTitleText")?.GetComponent<TMP_Text>();
            hudObjectiveText = existingMission.Find("ObjectiveText")?.GetComponent<TMP_Text>();
            hudDashText = existingMission.Find("DashStatusText")?.GetComponent<TMP_Text>();
        }
        else
        {
            missionPanel = CreateUiPanel("StageMissionHUD", canvas.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(22f, -20f), new Vector2(452f, 128f),
                CombatHudStyler.ObsidianDeep);

            hudStageTitleText = CreateUiText("StageTitleText", missionPanel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(-28f, 28f),
                "MÀN 1/3 • TRẠI RỪNG NGOẠI VI", 16.5f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);

            hudObjectiveText = CreateUiText("ObjectiveText", missionPanel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -46f), new Vector2(-28f, 40f),
                "Đang khởi tạo nhiệm vụ...", 15, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);

            hudDashText = CreateUiText("DashStatusText", missionPanel.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 3f), new Vector2(-28f, 28f),
                "[LMB] Chém  •  [RMB] Đỡ  •  [Tab/Q] Khóa  •  [B] Shop", 12.6f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);
        }
        CombatHudStyler.StyleMissionHud(missionPanel, hudStageTitleText, hudObjectiveText, hudDashText);

        Transform existingBossBar = canvas.transform.Find("BossHpBarHUD");
        if (existingBossBar != null)
        {
            bossBarRoot = existingBossBar.gameObject;
            bossNameText = existingBossBar.Find("BossNameText")?.GetComponent<TMP_Text>();
            bossHpValueText = existingBossBar.Find("BossHpValueText")?.GetComponent<TMP_Text>();
            bossHpSlider = existingBossBar.Find("BossSlider")?.GetComponent<Slider>();
        }
        else
        {
            bossBarRoot = CreateUiPanel("BossHpBarHUD", canvas.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -20f), new Vector2(568f, 76f),
                CombatHudStyler.CrimsonObsidian);

            bossNameText = CreateUiText("BossNameText", bossBarRoot.transform,
                new Vector2(0f, 1f), new Vector2(0.76f, 1f), new Vector2(0f, 1f),
                new Vector2(34f, -8f), new Vector2(-8f, 26f),
                "❖ BOSS: ĐẠI TƯỚNG ORC WARLORD ❖", 16, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);

            bossHpValueText = CreateUiText("BossHpValueText", bossBarRoot.transform,
                new Vector2(0.76f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-20f, -8f), new Vector2(-4f, 26f),
                "700 / 700", 14.5f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineRight);

            GameObject sliderGo = new GameObject("BossSlider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(bossBarRoot.transform, false);
            RectTransform sRt = sliderGo.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0f, 0f);
            sRt.anchorMax = new Vector2(1f, 0f);
            sRt.pivot = new Vector2(0.5f, 0f);
            sRt.anchoredPosition = new Vector2(0f, 12f);
            sRt.sizeDelta = new Vector2(-40f, 20f);

            GameObject bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(sliderGo.transform, false);
            StretchRect(bgGo.GetComponent<RectTransform>());
            bgGo.GetComponent<Image>().color = new Color(0.14f, 0.035f, 0.055f, 0.96f);

            GameObject fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            StretchRect(fillArea.GetComponent<RectTransform>());

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(fillArea.transform, false);
            StretchRect(fillGo.GetComponent<RectTransform>());
            Image fillImg = fillGo.GetComponent<Image>();
            fillImg.color = CombatHudStyler.BossFillNormal;

            bossHpSlider = sliderGo.GetComponent<Slider>();
            bossHpSlider.interactable = false;
            bossHpSlider.transition = Selectable.Transition.None;
            bossHpSlider.fillRect = fillGo.GetComponent<RectTransform>();
            bossHpSlider.minValue = 0f;
            bossHpSlider.maxValue = 700f;
            bossHpSlider.value = 700f;
        }
        CombatHudStyler.StyleBossHud(
            bossBarRoot,
            bossHpSlider,
            bossNameText,
            bossHpValueText,
            out bossFillImage,
            out bossGhostFillImage,
            out bossFrameImage);
        bossBarRoot.SetActive(false);

        Transform existingBanner = canvas.transform.Find("StageBannerHUD");
        if (existingBanner != null)
        {
            bannerRoot = existingBanner.gameObject;
            bannerText = existingBanner.Find("BannerText")?.GetComponent<TMP_Text>();
        }
        else
        {
            bannerRoot = CreateUiPanel("StageBannerHUD", canvas.transform,
                new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(740f, 98f),
                new Color(0.035f, 0.050f, 0.090f, 0.90f));

            bannerText = CreateUiText("BannerText", bannerRoot.transform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-88f, -24f),
                "", 21, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        }
        CombatHudStyler.StyleStageBanner(bannerRoot, bannerText, out bannerCanvasGroup);
        bannerRoot.SetActive(false);
    }

    private void ShowBossBar(int currentHp, int maxHp)
    {
        if (bossBarRoot == null) return;
        float ratio = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 1f;
        bossHpRatio = ratio;
        bossGhostHpRatio = ratio;
        CombatHudStyler.SetGhostFillRatio(bossGhostFillImage, ratio);
        bossBarRoot.SetActive(true);
        HandleBossHealthChanged(currentHp, maxHp);
    }

    private void SetHudStage(string title, string objective)
    {
        if (hudStageTitleText != null) hudStageTitleText.text = title;
        if (hudObjectiveText != null)
        {
            hudObjectiveText.text = string.IsNullOrEmpty(objective) ? "" : $"<color=#E8C56E>◆</color> {objective}";
        }
    }

    private void UpdateDashHud()
    {
        if (hudDashText == null || playerController == null) return;

        bool isLocked = PlayerAttack.CurrentLockedTarget != null && !PlayerAttack.CurrentLockedTarget.IsDead;
        int state = playerController.IsBlocking ? 1 : (isLocked ? 2 : 0);
        if (state == lastDashBlockingState) return;
        lastDashBlockingState = state;

        hudDashText.color = Color.white;
        if (state == 1)
        {
            hudDashText.text = "<color=#FFD966><b>[RMB]</b> ĐANG ĐỠ ĐÒN (BLOCK / PARRY)</color>";
        }
        else if (state == 2)
        {
            hudDashText.text = "<color=#FFAE4A><b>[KHÓA MỤC TIÊU]</b></color>  <color=#5C6A85>•</color>  <color=#E8C56E><b>[Tab]</b></color> Đổi  <color=#5C6A85>•</color>  <color=#E8C56E><b>[Q]</b></color> Bỏ Khóa";
        }
        else
        {
            hudDashText.text = "<color=#7BE5FF><b>[LMB]</b></color> Chém  <color=#5C6A85>•</color>  <color=#7BE5FF><b>[RMB]</b></color> Đỡ  <color=#5C6A85>•</color>  <color=#7BE5FF><b>[Tab/Q]</b></color> Khóa  <color=#5C6A85>•</color>  <color=#E8C56E><b>[B]</b></color> Shop";
        }
    }

    private GameObject CreateUiPanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color bgColor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = bgColor;
        return go;
    }

    private TMP_Text CreateUiText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, string content, float fontSize, FontStyles style, Color color, TextAlignmentOptions align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }

    private static void StretchRect(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void CleanupDeadEnemies()
    {
        for (int i = activeWaveEnemies.Count - 1; i >= 0; i--)
        {
            if (activeWaveEnemies[i] == null || activeWaveEnemies[i].IsDead)
            {
                activeWaveEnemies.RemoveAt(i);
            }
        }
    }

    private Vector3 SnapPositionToTerrain(Vector3 rawPos)
    {
        Vector3 rayOrigin = new Vector3(rawPos.x, rawPos.y + 120f, rawPos.z);
        int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, TerrainHitBuffer, 280f, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            if (TerrainHitBuffer[i].collider is TerrainCollider)
            {
                return new Vector3(rawPos.x, TerrainHitBuffer[i].point.y, rawPos.z);
            }
        }

        float highestY = float.MinValue;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            if (TerrainHitBuffer[i].collider == null) continue;
            if (TerrainHitBuffer[i].point.y > highestY)
            {
                highestY = TerrainHitBuffer[i].point.y;
                found = true;
            }
        }

        return found ? new Vector3(rawPos.x, highestY, rawPos.z) : rawPos;
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void ResolvePlayer()
    {
        if (PlayerController.Instance != null)
        {
            playerController = PlayerController.Instance;
            playerTransform = playerController.transform;
            return;
        }

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            playerController = playerObj.GetComponent<PlayerController>();
        }
    }
}
