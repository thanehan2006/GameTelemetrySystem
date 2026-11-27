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
    [SerializeField] private float victoryScreenDelay = 3.2f;

    [Header("Cân bằng Số lượng Quái mỗi Đợt")]
    public int stage1Wave1Count = 3;
    public int stage1Wave2Count = 4;
    public int stage2Wave1TankCount = 1;
    public int stage2Wave1StrikerCount = 3;
    public int stage2Wave2TankCount = 2;
    public int stage2Wave2StrikerCount = 3;
    public int bossEnrageGuardCount = 2;

    [Header("Quái Đứng Sẵn & Đi Tuần Trên Đường")]
    public bool spawnRoadEnemies = true;
    public int road1To2EnemyCount = 4;
    public int road2To3EnemyCount = 3;
    public float roadEnemyDetectRadius = 15f;
    public float roadEnemyLeashRadius = 30f;

    public int CurrentStage { get; private set; } = 1;
    public int MapLevel => Mathf.Clamp(mapIndex, 1, 3);

    private StagePhase phase = StagePhase.Intro;
    private Transform playerTransform;
    private PlayerController playerController;

    private readonly List<EnemyHealth> activeWaveEnemies = new List<EnemyHealth>();
    private readonly List<EnemyHealth> roadEnemies = new List<EnemyHealth>();
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
    private Light beaconLight;
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
    private Color lastBossFillColor = Color.clear;
    private float bossPulseTimer;

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

    // Liên kết HUD có sẵn trên scene, dựng cột sáng dẫn đường và bắt đầu Màn 1
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

        SpawnRoadEnemies();
        BuildRuntimeHud();
        StoryGuideManager.EnsureInstance().InitializeForMap(MapLevel, mapDisplayName, stage1Center);
        CreateWaypointBeacon();

        ApplySkybox(skyboxStage1Dawn);
        StartCoroutine(BeginStage1Routine());
    }

    private void OnDestroy()
    {
        UnsubscribeBossEvents();
        if (beaconBeamMat != null) Destroy(beaconBeamMat);
        if (beaconRingMat != null) Destroy(beaconRingMat);
        if (Instance == this) Instance = null;
    }

    private void UnsubscribeBossEvents()
    {
        if (activeBoss != null)
        {
            activeBoss.OnHealthChanged -= HandleBossHealthChanged;
            activeBoss.OnDied -= HandleBossDied;
        }
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
                bossPulseTimer -= dt;
                if (bossPulseTimer <= 0f || lastBossFillColor == CombatHudStyler.BossFillNormal || lastBossFillColor == Color.clear)
                {
                    bossPulseTimer = 0.05f;
                    float pulse = 0.86f + 0.14f * Mathf.Sin(Time.unscaledTime * 6f);
                    Color targetCol = Color.Lerp(CombatHudStyler.BossFillEnraged * pulse, new Color(1f, 0.56f, 0.16f, 1f), 0.25f);
                    if (targetCol != lastBossFillColor)
                    {
                        lastBossFillColor = targetCol;
                        bossFillImage.color = targetCol;
                    }
                }
            }
            else if (lastBossFillColor != CombatHudStyler.BossFillNormal)
            {
                lastBossFillColor = CombatHudStyler.BossFillNormal;
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

        SetHudStage(stage1Title, "Quan sát khu vực phía trước...");
        while (StoryGuideManager.IsWaitingForOpeningOrWarmup)
        {
            yield return null;
        }

        SetHudStage(stage1Title, "Chuẩn bị chiến đấu...");
        ShowBanner(stage1Banner, new Color(1f, 0.88f, 0.45f), 3.0f);

        yield return new WaitForSeconds(1.8f);

        phase = StagePhase.Stage1Combat;
        currentWaveIndex = 1;
        totalWavesInStage = 2;
        SpawnStage1Wave(1);
        StoryGuideManager.EnsureInstance().NotifyFirstWaveSpawned();
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
                        SpawnWaveStoryChest(0, stage1Center, new Vector3(4.5f, 0f, 3.5f));
                        currentWaveIndex++;
                        StartCoroutine(DelayedWaveSpawn(() => SpawnStage1Wave(currentWaveIndex), 3.2f, "ĐỢT 1 ĐÃ BỊ ĐÁNH TAN — RƯƠNG CỔ VỪA LỘ DIỆN!\nVIỆN BINH ĐỊCH ĐANG TỚI!"));
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
                        SpawnWaveStoryChest(2, stage2Center, new Vector3(-4.5f, 0f, 4.5f));
                        currentWaveIndex++;
                        StartCoroutine(DelayedWaveSpawn(() => SpawnStage2Wave(currentWaveIndex), 3.4f, $"ĐỢT 1 ĐÃ BỊ ĐÁNH TAN — RƯƠNG CỔ VỪA LỘ DIỆN!\n{stage2Wave2Banner}"));
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
        if (wave == 1)
        {
            int count = Mathf.Max(1, stage1Wave1Count);
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
            int count = Mathf.Max(1, stage1Wave2Count);
            int heavyCount = Mathf.Clamp(count / 2, 1, 2);
            for (int i = 0; i < count; i++)
            {
                GameObject fallback = i == count - 1 && axeOrcPrefab != null ? axeOrcPrefab : scoutOrcPrefab;
                GameObject pref = i < 2
                    ? (i == 0 ? scoutOrcPrefab : fallback)
                    : PickWavePrefab(stage1ExtraEnemyPrefabs, i, fallback);
                bool isHeavy = i >= count - heavyCount;
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

        SpawnWaveStoryChest(1, stage1Center, new Vector3(-4.5f, 0f, 4.0f));
        StoryGuideManager.EnsureInstance().NotifyTravelToStage2Started();
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

        int tankCount = Mathf.Max(1, wave == 1 ? stage2Wave1TankCount : stage2Wave2TankCount);
        for (int i = 0; i < tankCount; i++)
        {
            GameObject tankPref = PickWavePrefab(stage2TankPrefabs, (wave - 1) * 2 + i, tankFallback);
            bool isSecondWaveBrute = hasVariety && wave == 2 && i == tankCount - 1;
            int tankHp = isSecondWaveBrute ? 220 : 185;
            int tankDmg = isSecondWaveBrute ? 19 : 16;
            float tankScale = isSecondWaveBrute ? 1.24f : 1.16f;
            string tankName = tankPref != null ? tankPref.name : "Tank";
            GameObject tank = SpawnOrc(tankPref, stage2Center, 9f, 15f, tankHp, tankDmg, 3.25f, 2.0f, 0.60f, 1.75f, tankScale, 160, false, $"Stage2_Tank_{tankName}");
            AddUnitAuraLight(tank, stage2UnitAuraColor, 3.8f, 1.7f);
        }

        int strikerCount = Mathf.Max(1, wave == 1 ? stage2Wave1StrikerCount : stage2Wave2StrikerCount);
        for (int i = 0; i < strikerCount; i++)
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

        SpawnWaveStoryChest(3, stage2Center, new Vector3(4.5f, 0f, -4.5f));
        SpawnHealthPickupAt(stage2Center + new Vector3(-4f, 0f, -3f), 45);
    }

    private void SpawnWaveStoryChest(int chapterIndex, Vector3 arenaCenter, Vector3 fallbackOffset)
    {
        Vector3 desiredPos = arenaCenter + fallbackOffset;
        if (playerTransform != null)
        {
            float dist = HorizontalDistance(playerTransform.position, arenaCenter);
            if (dist <= 20f)
            {
                desiredPos = Vector3.Lerp(playerTransform.position, arenaCenter, 0.35f) + fallbackOffset * 0.45f;
            }
        }

        Vector3 chestPos = FindWalkableRoadPoint(desiredPos);
        StoryGuideManager.EnsureInstance().SpawnWaveStoryChest(chapterIndex, chestPos);
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
        GameAudioManager.PlayBossBgmForMap(MapLevel, 1.2f);
        GameAudioManager.PlayBossRoar();

        GameObject bPrefab = bossOrcPrefab != null ? bossOrcPrefab : (shieldTankPrefab != null ? shieldTankPrefab : scoutOrcPrefab);
        GameObject bossObj = SpawnOrc(bPrefab, stage3Center, 5f, 9f, 700, 24, 3.9f, 2.4f, 0.78f, 1.50f, 1.55f, 500, true, "BOSS_Unit");

        if (bossObj != null)
        {
            AddUnitAuraLight(bossObj, bossAuraColor, 7f, 3.2f, essential: true);
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
            GameAudioManager.PlayBossBgmForMap(MapLevel, 0.8f);
            GameAudioManager.PlayBossRoar();
            if (activeBoss != null)
            {
                CombatVfxManager.SpawnBossEnrageEffect(activeBoss.transform);
            }

            GameObject addFallback = axeOrcPrefab != null ? axeOrcPrefab : scoutOrcPrefab;
            int guardCount = Mathf.Max(1, bossEnrageGuardCount);
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
        UnsubscribeBossEvents();

        if (bossBarRoot != null) bossBarRoot.SetActive(false);

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackLevelComplete(MapLevel);
            TelemetryManager.Instance.TrackGameEnded("Win");
        }

        ShowBanner(bossVictoryBanner, new Color(0.2f, 1f, 0.45f), 4.0f);
        CombatVfxManager.SpawnVictoryCelebration(playerTransform != null ? playerTransform.position : stage3Center);

        PlayerHealth ph = PlayerHealth.Instance != null
            ? PlayerHealth.Instance
            : (playerTransform != null ? playerTransform.GetComponent<PlayerHealth>() : null);
        if (ph != null)
        {
            ph.SetInvulnerable(victoryScreenDelay + 1f);
        }

        StartCoroutine(ShowVictoryDelayed());
    }

    private IEnumerator ShowVictoryDelayed()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, victoryScreenDelay));
        if (!GameOverManager.IsGameOver && GameOverManager.Instance != null)
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
        string unitName,
        bool registerInActiveWave = true,
        bool playSpawnVfx = true)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[StageManager] Thiếu Enemy Prefab để sinh quái!");
            return null;
        }

        Vector3 spawnPos = arenaCenter;
        bool hasNavMeshPoint = false;

        if (maxRadius <= 0.01f)
        {
            spawnPos = SnapPositionToTerrain(arenaCenter);
            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit directHit, 7f, NavMesh.AllAreas))
            {
                spawnPos = directHit.position;
                hasNavMeshPoint = true;
            }
        }
        else
        {
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
        }

        GameObject enemy = Instantiate(prefab, spawnPos, Quaternion.identity);
        enemy.name = unitName;
        enemy.transform.localScale = Vector3.one * scale;
        if (playSpawnVfx)
        {
            CombatVfxManager.SpawnEnemySpawnEffect(spawnPos, isBoss);
        }
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
            if (registerInActiveWave)
            {
                activeWaveEnemies.Add(eh);
            }
            else
            {
                roadEnemies.Add(eh);
            }
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

    // Đặt sẵn quái đứng gác và đi tuần dọc đường dẫn giữa các khu vực mục tiêu
    private void SpawnRoadEnemies()
    {
        if (!spawnRoadEnemies) return;

        SpawnRoadSegmentEnemies(stage1Center, stage2Center, road1To2EnemyCount, false, "Road12");
        SpawnRoadSegmentEnemies(stage2Center, stage3Center, road2To3EnemyCount, true, "Road23");
    }

    private void SpawnRoadSegmentEnemies(Vector3 fromCenter, Vector3 toCenter, int count, bool isTier2, string tagPrefix)
    {
        if (count <= 0) return;

        Vector3 flatDelta = toCenter - fromCenter;
        flatDelta.y = 0f;
        float dist = flatDelta.magnitude;
        if (dist < 10f) return;

        Vector3 forwardDir = flatDelta / dist;
        Vector3 sideDir = new Vector3(-forwardDir.z, 0f, forwardDir.x);

        float safeMargin = Mathf.Max(zoneTriggerRadius + 6f, 32f);
        float tMin = Mathf.Clamp(safeMargin / dist, 0.24f, 0.42f);
        float tMax = 1f - tMin;
        if (tMin >= tMax)
        {
            tMin = 0.35f;
            tMax = 0.65f;
        }

        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : Mathf.Lerp(tMin, tMax, (float)i / (count - 1));
            float lateralOffset = (i % 2 == 0) ? 3.5f : -3.5f;
            Vector3 desiredPos = Vector3.Lerp(fromCenter, toCenter, t) + sideDir * lateralOffset;
            Vector3 spawnPos = FindWalkableRoadPoint(desiredPos);

            GameObject enemy;
            if (!isTier2)
            {
                GameObject fallback = (i == count - 1 && axeOrcPrefab != null) ? axeOrcPrefab : scoutOrcPrefab;
                GameObject pref = i == 0 ? scoutOrcPrefab : PickWavePrefab(stage1ExtraEnemyPrefabs, i - 1, fallback);
                bool isHeavy = i == count - 1;
                int hp = isHeavy ? 115 : 90;
                int dmg = isHeavy ? 15 : 12;
                float spd = isHeavy ? 3.75f : 3.5f;
                float kb = isHeavy ? 0.14f : 0.06f;
                float sc = isHeavy ? 1.06f : 1.0f;
                int scoreVal = isHeavy ? 115 : 95;
                string pName = pref != null ? pref.name : "Unit";
                enemy = SpawnOrc(pref, spawnPos, 0f, 0f, hp, dmg, spd, 1.85f, kb, 1.58f, sc, scoreVal, false, $"{tagPrefix}_{i + 1}_{pName}", registerInActiveWave: false, playSpawnVfx: false);
            }
            else
            {
                bool isTank = i == count / 2;
                GameObject tankFallback = shieldTankPrefab != null ? shieldTankPrefab : scoutOrcPrefab;
                GameObject strikerFallback = axeOrcPrefab != null ? axeOrcPrefab : scoutOrcPrefab;
                GameObject pref = isTank
                    ? PickWavePrefab(stage2TankPrefabs, i, tankFallback)
                    : PickWavePrefab(stage2StrikerPrefabs, i, strikerFallback);
                int hp = isTank ? 165 : 115;
                int dmg = isTank ? 16 : 17;
                float spd = isTank ? 3.25f : 4.0f;
                float range = isTank ? 2.0f : 1.9f;
                float kb = isTank ? 0.55f : 0.16f;
                float cd = isTank ? 1.75f : 1.52f;
                float sc = isTank ? 1.15f : 1.05f;
                int scoreVal = isTank ? 150 : 130;
                string pName = pref != null ? pref.name : "Unit";
                enemy = SpawnOrc(pref, spawnPos, 0f, 0f, hp, dmg, spd, range, kb, cd, sc, scoreVal, false, $"{tagPrefix}_{i + 1}_{pName}", registerInActiveWave: false, playSpawnVfx: false);
                if (isTank && enemy != null)
                {
                    AddUnitAuraLight(enemy, stage2UnitAuraColor, 3.4f, 1.4f);
                }
            }

            if (enemy == null) continue;

            OrcEnemyAI ai = enemy.GetComponent<OrcEnemyAI>();
            if (ai == null) continue;

            if (i % 2 == 0)
            {
                Vector3 patrolStep = sideDir * 4.5f + forwardDir * 2.5f;
                Vector3 wpA = FindWalkableRoadPoint(spawnPos + patrolStep);
                Vector3 wpB = FindWalkableRoadPoint(spawnPos - patrolStep);
                ai.ConfigurePatrol(new[] { wpA, wpB }, roadEnemyDetectRadius, roadEnemyLeashRadius, 0.55f, 1.8f);
            }
            else
            {
                Vector3 lookDir = fromCenter - spawnPos;
                ai.ConfigureStandGuard(spawnPos, lookDir, roadEnemyDetectRadius, roadEnemyLeashRadius);
            }
        }
    }

    private Vector3 FindWalkableRoadPoint(Vector3 desiredPos)
    {
        Vector3 snapped = SnapPositionToTerrain(desiredPos);
        if (NavMesh.SamplePosition(snapped, out NavMeshHit navHit, 8f, NavMesh.AllAreas))
        {
            snapped = navHit.position;
        }

        if (!Physics.CheckSphere(snapped + Vector3.up * 1.45f, 0.55f, ~0, QueryTriggerInteraction.Ignore))
        {
            return snapped;
        }

        for (int attempt = 0; attempt < 6; attempt++)
        {
            float angle = attempt * 60f * Mathf.Deg2Rad;
            Vector3 probe = desiredPos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.4f;
            probe = SnapPositionToTerrain(probe);
            if (NavMesh.SamplePosition(probe, out NavMeshHit altHit, 7f, NavMesh.AllAreas))
            {
                probe = altHit.position;
            }
            if (!Physics.CheckSphere(probe + Vector3.up * 1.45f, 0.55f, ~0, QueryTriggerInteraction.Ignore))
            {
                return probe;
            }
        }

        return snapped;
    }

    private static void ApplyRuntimePerformanceOptimizations()
    {
        GameSettingsManager.ApplyCurrentGraphicsSettings();
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

    private void AddUnitAuraLight(GameObject target, Color color, float range, float intensity, bool essential = false)
    {
        if (target == null) return;
        if (!essential && GameSettingsManager.CurrentGraphicsPreset == GraphicsPreset.Low) return;

        GameObject lightObj = new GameObject("AuraLight");
        lightObj.transform.SetParent(target.transform, false);
        lightObj.transform.localPosition = Vector3.up * 1.4f;
        Light l = lightObj.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
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
            if (beaconBeamMat.HasProperty(BaseColorId)) beaconBeamMat.SetColor(BaseColorId, initCol);
            if (beaconBeamMat.HasProperty(ColorId)) beaconBeamMat.SetColor(ColorId, initCol);
            pr.sharedMaterial = beaconBeamMat;
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
        rmr.sharedMaterial = beaconRingMat;
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
        rend.sharedMaterial = CombatVfxManager.GetAdditiveStarMaterial();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        AddUnitAuraLight(beaconObject, new Color(1f, 0.8f, 0.2f), 16f, 4.5f, essential: true);
        beaconLight = beaconObject.GetComponentInChildren<Light>(true);
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

        if (beaconLight != null) beaconLight.color = color;

        beaconObject.SetActive(true);
        if (beaconMotesPs != null) beaconMotesPs.Play();
    }

    private void HideWaypointBeacon()
    {
        if (beaconObject != null) beaconObject.SetActive(false);
    }

    // Liên kết và nâng cấp giao diện bảng nhiệm vụ, thanh máu Boss và banner đã đặt sẵn trên Canvas
    private void BuildRuntimeHud()
    {
        Canvas canvas = FantasyUiThemeHelper.FindSceneCanvas(gameObject);
        if (canvas == null) return;

        Transform existingMission = canvas.transform.Find("StageMissionHUD");
        if (existingMission != null)
        {
            GameObject missionPanel = existingMission.gameObject;
            hudStageTitleText = existingMission.Find("StageTitleText")?.GetComponent<TMP_Text>();
            hudObjectiveText = existingMission.Find("ObjectiveText")?.GetComponent<TMP_Text>();
            hudDashText = existingMission.Find("DashStatusText")?.GetComponent<TMP_Text>();
            CombatHudStyler.StyleMissionHud(missionPanel, hudStageTitleText, hudObjectiveText, hudDashText);
        }

        Transform existingBossBar = canvas.transform.Find("BossHpBarHUD");
        if (existingBossBar != null)
        {
            bossBarRoot = existingBossBar.gameObject;
            bossNameText = existingBossBar.Find("BossNameText")?.GetComponent<TMP_Text>();
            bossHpValueText = existingBossBar.Find("BossHpValueText")?.GetComponent<TMP_Text>();
            bossHpSlider = existingBossBar.Find("BossSlider")?.GetComponent<Slider>();
            CombatHudStyler.StyleBossHud(
                bossBarRoot,
                bossHpSlider,
                bossNameText,
                bossHpValueText,
                out bossFillImage,
                out bossGhostFillImage,
                out bossFrameImage);
            if (bossBarRoot.GetComponent<Canvas>() == null)
            {
                bossBarRoot.AddComponent<Canvas>();
            }
            bossBarRoot.SetActive(false);
        }

        Transform existingBanner = canvas.transform.Find("StageBannerHUD");
        if (existingBanner != null)
        {
            bannerRoot = existingBanner.gameObject;
            bannerText = existingBanner.Find("BannerText")?.GetComponent<TMP_Text>();
            CombatHudStyler.StyleStageBanner(bannerRoot, bannerText, out bannerCanvasGroup);
            if (bannerRoot.GetComponent<Canvas>() == null)
            {
                bannerRoot.AddComponent<Canvas>();
            }
            bannerRoot.SetActive(false);
        }
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
            hudDashText.text = "<color=#FFD966><b>[RMB]</b> ĐANG ĐỠ ĐÒN</color>";
        }
        else if (state == 2)
        {
            hudDashText.text = "<color=#FFAE4A><b>[KHÓA MỤC TIÊU]</b></color>  <color=#5C6A85>•</color>  <color=#E8C56E><b>[Tab]</b></color> Đổi  <color=#5C6A85>•</color>  <color=#E8C56E><b>[Q]</b></color> Bỏ Khóa";
        }
        else
        {
            hudDashText.text = "<color=#7BE5FF><b>[LMB]</b></color> Chém  <color=#5C6A85>•</color>  <color=#7BE5FF><b>[RMB]</b></color> Đỡ  <color=#5C6A85>•</color>  <color=#E8C56E><b>[B]</b></color> Lò Rèn  <color=#5C6A85>•</color>  <color=#7BE5FF><b>[H]</b></color> Sổ Tay";
        }
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

        for (int i = roadEnemies.Count - 1; i >= 0; i--)
        {
            if (roadEnemies[i] == null || roadEnemies[i].IsDead)
            {
                roadEnemies.RemoveAt(i);
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
