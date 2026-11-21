using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Điều phối toàn bộ kịch bản Gameplay 3 Màn (Action Roguelite - Chinh Phục 3 Cứ Điểm) chuẩn Đề 9:
/// - Màn 1: Trại Rừng Ngoại Vi (Tutorial & 2 Wave Orc Trinh Sát, Skybox Bình Minh)
/// - Màn 2: Thung Lũng Bẫy & Lính Khiên (Bẫy Gai + Orc Tanker Khiên + Orc Rìu, Skybox Hoàng Hôn)
/// - Màn 3: Mỏ Vàng Bị Chiếm (Boss Đại Tướng Orc Warlord + Phase 2 gọi đệ tử, Skybox Đêm Trăng)
/// Tự động gửi đầy đủ Telemetry chuẩn Đề 9: level_start, level_complete, boss_killed, checkpoint_reached, item_pickup, trap_hit, Win/Lose.
/// </summary>
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

    [Header("Enemy Prefabs (Tận dụng sẵn từ Assets/DucThanh/Prefabs)")]
    public GameObject scoutOrcPrefab;
    public GameObject axeOrcPrefab;
    public GameObject shieldTankPrefab;
    public GameObject bossOrcPrefab;

    [Header("Environment & Interactive Props")]
    public GameObject spikeTrapPrefab;
    public GameObject mushroomPickupPrefab;
    public GameObject runeStonePrefab;
    public GameObject goldMinePrefab;
    public GameObject trolleyGoldPrefab;
    public GameObject stoneWallPrefab;
    public GameObject fireflyFxPrefab;

    [Header("Skyboxes theo từng Màn (LaFinca Midgard)")]
    public Material skyboxStage1Dawn;
    public Material skyboxStage2Sunset;
    public Material skyboxStage3Lunar;

    [Header("Tọa độ 3 Khu vực trên Terrain")]
    public Vector3 stage1Center = new Vector3(132f, 25.5f, 368f);
    public Vector3 stage2Center = new Vector3(298f, 15f, 296f);
    public Vector3 stage3Center = new Vector3(230f, 18f, 240f);
    public float zoneTriggerRadius = 28f;

    public int CurrentStage { get; private set; } = 1;

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

    // Waypoint Beacon dẫn đường (HDR Light Pillar + Rotating Rune Ring + Motes)
    private GameObject beaconObject;
    private Transform beaconRingTransform;
    private Material beaconBeamMat;
    private Material beaconRingMat;
    private ParticleSystem beaconMotesPs;
    private Vector3 currentWaypointTarget;

    // Runtime HUD
    private TMP_Text hudStageTitleText;
    private TMP_Text hudObjectiveText;
    private TMP_Text hudDashText;
    private GameObject bossBarRoot;
    private Slider bossHpSlider;
    private TMP_Text bossNameText;
    private TMP_Text bossHpValueText;
    private GameObject bannerRoot;
    private TMP_Text bannerText;
    private float bannerHideTime;

    // Dirty-tracking cho HUD (tránh tạo string & rebuild TMP mỗi frame)
    private StagePhase lastHudPhase = (StagePhase)(-1);
    private int lastHudWave = -1;
    private int lastHudMetric = int.MinValue;
    private int lastDashBlockingState = -1;

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

    private void Start()
    {
        ApplyRuntimePerformanceOptimizations();
        ResolvePlayer();

        // Đồng bộ để ScoreManager chỉ quản lý EXP nhân vật, không gửi trùng level_start/complete của Màn chơi
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.manageStageTelemetry = false;
        }

        // Bám mặt đất chính xác cho tọa độ 3 khu vực
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

    private void Update()
    {
        if (beaconRingTransform != null && beaconObject != null && beaconObject.activeSelf)
        {
            beaconRingTransform.Rotate(Vector3.up, 38f * Time.deltaTime, Space.World);
        }

        if (bannerRoot != null && bannerRoot.activeSelf && Time.unscaledTime >= bannerHideTime)
        {
            bannerRoot.SetActive(false);
        }

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

    public void ShowBanner(string message, Color color, float duration = 2.5f)
    {
        if (bannerRoot == null || bannerText == null) return;
        bannerText.text = message;
        bannerText.color = color;
        bannerRoot.SetActive(true);
        bannerHideTime = Time.unscaledTime + duration;
    }

    private IEnumerator BeginStage1Routine()
    {
        phase = StagePhase.Intro;
        CurrentStage = 1;

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackLevelStart(1);
        }

        SetHudStage("MÀN 1/3 • TRẠI RỪNG NGOẠI VI", "Chuẩn bị chiến đấu...");
        ShowBanner("MÀN 1: TRẠI RỪNG NGOẠI VI\nTiêu diệt quân trinh sát Orc!", new Color(1f, 0.88f, 0.45f), 3.0f);

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
                    SetHudStage("MÀN 1/3 • TRẠI RỪNG NGOẠI VI", $"Đợt {currentWaveIndex}/{totalWavesInStage} — Quái còn lại: {count1}");
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
                    SetHudStage("ĐẾN MÀN 2 • THUNG LŨNG BẪY", $"Theo cột sáng vàng tới Trại 2 ({dist2Int}m)");
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
                    SetHudStage("MÀN 2/3 • THUNG LŨNG BẪY & LÍNH KHIÊN", $"Đợt {currentWaveIndex}/{totalWavesInStage} — Quái còn lại: {count2} (Coi chừng bẫy gai!)");
                }
                if (!spawningWave && count2 == 0)
                {
                    if (currentWaveIndex < totalWavesInStage)
                    {
                        currentWaveIndex++;
                        StartCoroutine(DelayedWaveSpawn(() => SpawnStage2Wave(currentWaveIndex), 1.8f, "ĐỢT 2 — LÍNH KHIÊN & RÌU TĂNG VIỆN!"));
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
                    SetHudStage("ĐẾN MÀN 3 • MỎ VÀNG BỊ CHIẾM (BOSS)", $"Theo cột sáng đỏ tới Đấu Trường Boss ({dist3Int}m)");
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
                        ? $"Hạ Đại Tướng Orc Warlord (Còn {adds} hộ vệ)"
                        : "Hạ Đại Tướng Orc Warlord để giành chiến thắng!";
                    SetHudStage("MÀN 3/3 • ĐẤU TRƯỜNG MỎ VÀNG", subObj);
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

    // ==================== STAGE 1 ====================
    private void SpawnStage1Wave(int wave)
    {
        if (wave == 1)
        {
            for (int i = 0; i < 3; i++)
            {
                SpawnOrc(scoutOrcPrefab, stage1Center, 9f, 14f, 75, 12, 3.4f, 1.8f, 0.05f, 1.65f, 1.0f, 100, false, "Orc_Scout");
            }
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                SpawnOrc(scoutOrcPrefab, stage1Center, 10f, 15f, 90, 14, 3.6f, 1.8f, 0.1f, 1.6f, 1.0f, 110, false, "Orc_Scout");
            }
            SpawnOrc(axeOrcPrefab != null ? axeOrcPrefab : scoutOrcPrefab, stage1Center, 11f, 15f, 105, 16, 3.8f, 1.85f, 0.15f, 1.55f, 1.05f, 130, false, "Orc_Raider");
        }
    }

    private void CompleteStage1()
    {
        phase = StagePhase.TravelToStage2;
        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackLevelComplete(1);
        }

        ShowBanner("HOÀN THÀNH MÀN 1!\nHãy theo cột sáng vàng đến Thung Lũng Bẫy!", new Color(0.25f, 0.95f, 0.55f), 3.6f);
        GameAudioManager.PlayStageClear();
        SetWaypointBeacon(stage2Center, new Color(1f, 0.78f, 0.15f));

        // Thả thêm 1 nấm hồi máu thưởng gần Trại 1
        SpawnHealthPickupAt(stage1Center + new Vector3(4f, 0f, -2f), 45);
    }

    // ==================== STAGE 2 ====================
    private void StartStage2()
    {
        phase = StagePhase.Stage2Combat;
        CurrentStage = 2;
        HideWaypointBeacon();
        ApplySkybox(skyboxStage2Sunset);

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackLevelStart(2);
        }

        ShowBanner("MÀN 2: THUNG LŨNG BẪY & LÍNH KHIÊN\nCẩn thận bẫy gai và Orc Tanker!", new Color(1f, 0.58f, 0.2f), 3.2f);

        currentWaveIndex = 1;
        totalWavesInStage = 2;
        SpawnStage2Wave(1);
    }

    private void SpawnStage2Wave(int wave)
    {
        GameObject tankPref = shieldTankPrefab != null ? shieldTankPrefab : scoutOrcPrefab;
        GameObject axePref = axeOrcPrefab != null ? axeOrcPrefab : scoutOrcPrefab;

        // 2 Lính Khiên Tanker (máu trâu, khó bị đẩy lùi)
        for (int i = 0; i < 2; i++)
        {
            GameObject tank = SpawnOrc(tankPref, stage2Center, 9f, 15f, 165, 16, 3.1f, 1.95f, 0.65f, 1.75f, 1.18f, 150, false, "Orc_Shield_Tank");
            AddUnitAuraLight(tank, new Color(0.3f, 0.7f, 1f), 3.5f, 1.6f);
        }

        // Lính Rìu Sát Thương cao (Wave 1: 2 con, Wave 2: 3 con)
        int axeCount = wave == 1 ? 2 : 3;
        for (int i = 0; i < axeCount; i++)
        {
            SpawnOrc(axePref, stage2Center, 10f, 16f, 95, 20, 4.2f, 1.85f, 0.15f, 1.45f, 1.05f, 130, false, "Orc_Axe_Berserker");
        }
    }

    private void CompleteStage2()
    {
        phase = StagePhase.TravelToStage3;
        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackLevelComplete(2);
        }

        ShowBanner("HOÀN THÀNH MÀN 2!\nTiến tới Mỏ Vàng Bị Chiếm tiêu diệt BOSS!", new Color(0.25f, 0.95f, 0.55f), 3.6f);
        GameAudioManager.PlayStageClear();
        SetWaypointBeacon(stage3Center, new Color(1f, 0.22f, 0.22f));

        SpawnHealthPickupAt(stage2Center + new Vector3(-4f, 0f, -3f), 55);
    }

    // ==================== STAGE 3 (BOSS) ====================
    private void StartStage3Boss()
    {
        phase = StagePhase.Stage3Boss;
        CurrentStage = 3;
        bossEnraged = false;
        HideWaypointBeacon();
        ApplySkybox(skyboxStage3Lunar);

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackLevelStart(3);
        }

        ShowBanner("MÀN 3: ĐẠI TƯỚNG ORC WARLORD!\nHạ gục Boss để giải phóng Mỏ Vàng!", new Color(1f, 0.28f, 0.28f), 3.5f);
        GameAudioManager.PlayBgm(GameAudioManager.BgmTrack.Boss, 1.2f);
        GameAudioManager.PlayBossRoar();

        GameObject bPrefab = bossOrcPrefab != null ? bossOrcPrefab : (shieldTankPrefab != null ? shieldTankPrefab : scoutOrcPrefab);
        GameObject bossObj = SpawnOrc(bPrefab, stage3Center, 5f, 9f, 560, 26, 4.0f, 2.4f, 0.78f, 1.45f, 1.55f, 500, true, "BOSS_Orc_Warlord");

        if (bossObj != null)
        {
            AddUnitAuraLight(bossObj, new Color(1f, 0.18f, 0.12f), 7f, 3.2f);
            activeBoss = bossObj.GetComponent<EnemyHealth>();
            activeBossAI = bossObj.GetComponent<OrcEnemyAI>();

            if (activeBoss != null)
            {
                activeBoss.OnHealthChanged += HandleBossHealthChanged;
                activeBoss.OnDied += HandleBossDied;
                ShowBossBar(activeBoss.CurrentHealth, activeBoss.maxHealth);
            }
        }
    }

    private void HandleBossHealthChanged(int currentHp, int maxHp)
    {
        if (bossHpSlider != null)
        {
            bossHpSlider.maxValue = maxHp;
            bossHpSlider.value = currentHp;
        }

        if (bossHpValueText != null)
        {
            bossHpValueText.text = $"{currentHp} / {maxHp}";
        }

        // Phase 2: Cuồng nộ khi Boss xuống dưới 50% HP
        if (!bossEnraged && currentHp > 0 && currentHp <= maxHp * 0.5f)
        {
            bossEnraged = true;
            if (activeBossAI != null)
            {
                activeBossAI.ApplyCombatStats(4.7f, 32, 2.5f, 0.85f, 1.15f);
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

            // Triệu hồi 2 Orc hộ vệ
            GameObject addPref = axeOrcPrefab != null ? axeOrcPrefab : scoutOrcPrefab;
            for (int i = 0; i < 2; i++)
            {
                SpawnOrc(addPref, stage3Center, 8f, 13f, 85, 16, 4.0f, 1.85f, 0.1f, 1.55f, 1.0f, 100, false, "Orc_BossGuard");
            }
        }
    }

    private void HandleBossDied()
    {
        if (phase == StagePhase.Completed) return;
        phase = StagePhase.Completed;

        if (bossBarRoot != null) bossBarRoot.SetActive(false);

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackLevelComplete(3);
            TelemetryManager.Instance.TrackGameEnded("Win");
        }

        ShowBanner("CHIẾN THẮNG! ĐẠI TƯỚNG ORC ĐÃ BỊ TIÊU DIỆT!", new Color(0.2f, 1f, 0.45f), 4.0f);
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

    // ==================== SPAWNING HELPERS ====================
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

        Vector2 dir2D = Random.insideUnitCircle.normalized;
        if (dir2D.sqrMagnitude < 0.01f) dir2D = Vector2.right;

        Vector3 spawnPos = arenaCenter + new Vector3(dir2D.x, 0f, dir2D.y) * Random.Range(minRadius, maxRadius);
        spawnPos = SnapPositionToTerrain(spawnPos);

        GameObject enemy = Instantiate(prefab, spawnPos, Quaternion.identity);
        enemy.name = unitName;
        enemy.transform.localScale = Vector3.one * scale;
        CombatVfxManager.SpawnEnemySpawnEffect(spawnPos, isBoss);

        EnemyHealth eh = enemy.GetComponent<EnemyHealth>();
        if (eh != null)
        {
            eh.isBoss = isBoss;
            eh.scoreValue = scoreVal;
            eh.goldReward = isBoss ? 200 : Mathf.Max(30, Mathf.RoundToInt(scoreVal * 0.42f));
            GameObject potionPrefab = HealthPickup.GetPotionPrefab();
            eh.healPickupPrefab = potionPrefab != null ? potionPrefab : mushroomPickupPrefab;
            eh.dropHealChance = isBoss ? 0f : 0.30f;
            eh.SetMaxHealth(hp);
            activeWaveEnemies.Add(eh);
        }

        OrcEnemyAI ai = enemy.GetComponent<OrcEnemyAI>();
        if (ai != null)
        {
            ai.ApplyCombatStats(speed, damage, range, kbResist, cooldown);
        }

        return enemy;
    }

    private void BuildStageEnvironments()
    {
        // Nếu đã bake sẵn StageProps_Generated trực tiếp trong Scene thì dùng luôn, không tạo trùng
        if (GameObject.Find("StageProps_Generated") != null)
        {
            return;
        }

        GameObject propsRoot = new GameObject("StageProps_Generated");

        // --- CHECKPOINTS tại 3 khu vực ---
        CreateCheckpoint("Checkpoint_Stage1", stage1Center + new Vector3(-2.5f, 0f, 2f), 1, "Trại Rừng Ngoại Vi", 30, propsRoot.transform);
        CreateCheckpoint("Checkpoint_Stage2", stage2Center + new Vector3(3f, 0f, 0f), 2, "Thung Lũng Bẫy", 50, propsRoot.transform);
        CreateCheckpoint("Checkpoint_Stage3", stage3Center + new Vector3(0f, 0f, 16f), 3, "Cổng Mỏ Vàng", 60, propsRoot.transform);

        // --- MÀN 1: Cột Cổ Ngữ & Nấm hồi máu ---
        if (runeStonePrefab != null)
        {
            SpawnPropOnTerrain(runeStonePrefab, stage1Center + new Vector3(9f, 0f, 6f), Quaternion.Euler(0f, -35f, 0f), Vector3.one * 1.3f, propsRoot.transform, "Stage1_RuneStone");
        }
        SpawnHealthPickupAt(stage1Center + new Vector3(-6f, 0f, -5f), 40, propsRoot.transform);

        // --- ĐƯỜNG TỪ MÀN 1 SANG MÀN 2: 2 Nấm hồi máu dẫn đường ---
        Vector3 mid12A = Vector3.Lerp(stage1Center, stage2Center, 0.35f);
        Vector3 mid12B = Vector3.Lerp(stage1Center, stage2Center, 0.68f);
        SpawnHealthPickupAt(mid12A, 40, propsRoot.transform);
        SpawnHealthPickupAt(mid12B, 40, propsRoot.transform);

        // --- MÀN 2: 6 Bẫy Gai quanh Thung Lũng + 2 Nấm Hồi Máu ---
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
        SpawnHealthPickupAt(stage2Center + new Vector3(-11f, 0f, 1f), 50, propsRoot.transform);
        SpawnHealthPickupAt(stage2Center + new Vector3(11f, 0f, -2f), 50, propsRoot.transform);

        // --- ĐƯỜNG TỪ MÀN 2 SANG MÀN 3: 1 Nấm hồi máu ---
        Vector3 mid23 = Vector3.Lerp(stage2Center, stage3Center, 0.5f);
        SpawnHealthPickupAt(mid23, 50, propsRoot.transform);

        // --- MÀN 3: Đấu trường Mỏ Vàng Bị Chiếm (Gold_Mine + Trolley_Gold + Tường Đá + Đom Đóm) ---
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
                // Chừa lối vào ở hướng gần Trại 2
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

        // 2 Nấm hồi máu dự phòng ở hai bên đấu trường Boss
        SpawnHealthPickupAt(stage3Center + new Vector3(-12f, 0f, 4f), 60, propsRoot.transform);
        SpawnHealthPickupAt(stage3Center + new Vector3(12f, 0f, 4f), 60, propsRoot.transform);
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
        hazard.damage = 22;
        hazard.triggerRadius = 1.75f;
    }

    /// <summary>
    /// Tối ưu hiệu năng runtime cho Terrain, Camera và Frame Pacing mà không cần sửa trực tiếp Prefab map dùng chung.
    /// </summary>
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
        DynamicGI.UpdateEnvironment();
    }

    // ==================== WAYPOINT BEACON (HDR LIGHT PILLAR + RUNE RING + MOTES) ====================
    private void CreateWaypointBeacon()
    {
        beaconObject = new GameObject("StageWaypointBeacon");

        // 1. Trụ sáng HDR mờ dần lên trời
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

        // 2. Vòng ma pháp xoay tròn trên mặt đất
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

        // 3. Các đốm sáng bay dọc theo trụ dẫn đường
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

    // ==================== RUNTIME HUD ====================
    private void BuildRuntimeHud()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        // 1. Mission Panel (dùng lại nếu đã bake sẵn trong Scene)
        Transform existingMission = canvas.transform.Find("StageMissionHUD");
        if (existingMission != null)
        {
            hudStageTitleText = existingMission.Find("StageTitleText")?.GetComponent<TMP_Text>();
            hudObjectiveText = existingMission.Find("ObjectiveText")?.GetComponent<TMP_Text>();
            hudDashText = existingMission.Find("DashStatusText")?.GetComponent<TMP_Text>();
        }
        else
        {
            GameObject missionPanel = CreateUiPanel("StageMissionHUD", canvas.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -20f), new Vector2(410f, 108f),
                new Color(0.05f, 0.08f, 0.14f, 0.78f));

            hudStageTitleText = CreateUiText("StageTitleText", missionPanel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(-28f, 28f),
                "MÀN 1/3 • TRẠI RỪNG NGOẠI VI", 18, FontStyles.Bold, new Color(1f, 0.84f, 0.32f), TextAlignmentOptions.Left);

            hudObjectiveText = CreateUiText("ObjectiveText", missionPanel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -42f), new Vector2(-28f, 26f),
                "Đang khởi tạo nhiệm vụ...", 15, FontStyles.Normal, Color.white, TextAlignmentOptions.Left);

            hudDashText = CreateUiText("DashStatusText", missionPanel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -72f), new Vector2(-28f, 24f),
                "[Chuột Trái] Chém   •   [Giữ Chuột Phải] Đỡ đòn (Block)", 13, FontStyles.Normal, new Color(0.55f, 0.92f, 1f), TextAlignmentOptions.Left);
        }

        // 2. Boss HP Bar (dùng lại nếu đã bake sẵn trong Scene)
        Transform existingBossBar = canvas.transform.Find("BossHpBarHUD");
        if (existingBossBar != null)
        {
            bossBarRoot = existingBossBar.gameObject;
            bossNameText = existingBossBar.Find("BossNameText")?.GetComponent<TMP_Text>();
            bossHpValueText = existingBossBar.Find("BossHpValueText")?.GetComponent<TMP_Text>();
            bossHpSlider = existingBossBar.Find("BossSlider")?.GetComponent<Slider>();
            bossBarRoot.SetActive(false);
        }
        else
        {
            bossBarRoot = CreateUiPanel("BossHpBarHUD", canvas.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -22f), new Vector2(520f, 68f),
                new Color(0.12f, 0.03f, 0.05f, 0.85f));

            bossNameText = CreateUiText("BossNameText", bossBarRoot.transform,
                new Vector2(0f, 1f), new Vector2(0.75f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -8f), new Vector2(0f, 24f),
                "BOSS: ĐẠI TƯỚNG ORC WARLORD", 16, FontStyles.Bold, new Color(1f, 0.38f, 0.32f), TextAlignmentOptions.Left);

            bossHpValueText = CreateUiText("BossHpValueText", bossBarRoot.transform,
                new Vector2(0.65f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -8f), new Vector2(0f, 24f),
                "560 / 560", 14, FontStyles.Bold, Color.white, TextAlignmentOptions.Right);

            GameObject sliderGo = new GameObject("BossSlider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(bossBarRoot.transform, false);
            RectTransform sRt = sliderGo.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0f, 0f);
            sRt.anchorMax = new Vector2(1f, 0f);
            sRt.pivot = new Vector2(0.5f, 0f);
            sRt.anchoredPosition = new Vector2(0f, 12f);
            sRt.sizeDelta = new Vector2(-32f, 18f);

            GameObject bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(sliderGo.transform, false);
            StretchRect(bgGo.GetComponent<RectTransform>());
            bgGo.GetComponent<Image>().color = new Color(0.25f, 0.08f, 0.08f, 0.95f);

            GameObject fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            StretchRect(fillArea.GetComponent<RectTransform>());

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(fillArea.transform, false);
            StretchRect(fillGo.GetComponent<RectTransform>());
            Image fillImg = fillGo.GetComponent<Image>();
            fillImg.color = new Color(0.92f, 0.18f, 0.22f, 1f);

            bossHpSlider = sliderGo.GetComponent<Slider>();
            bossHpSlider.interactable = false;
            bossHpSlider.transition = Selectable.Transition.None;
            bossHpSlider.fillRect = fillGo.GetComponent<RectTransform>();
            bossHpSlider.minValue = 0f;
            bossHpSlider.maxValue = 560f;
            bossHpSlider.value = 560f;

            bossBarRoot.SetActive(false);
        }

        // 3. Banner Thông Báo (dùng lại nếu đã bake sẵn trong Scene)
        Transform existingBanner = canvas.transform.Find("StageBannerHUD");
        if (existingBanner != null)
        {
            bannerRoot = existingBanner.gameObject;
            bannerText = existingBanner.Find("BannerText")?.GetComponent<TMP_Text>();
            bannerRoot.SetActive(false);
        }
        else
        {
            bannerRoot = CreateUiPanel("StageBannerHUD", canvas.transform,
                new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(640f, 84f),
                new Color(0.04f, 0.06f, 0.1f, 0.82f));

            bannerText = CreateUiText("BannerText", bannerRoot.transform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-24f, -12f),
                "", 20, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);

            bannerRoot.SetActive(false);
        }
    }

    private void ShowBossBar(int currentHp, int maxHp)
    {
        if (bossBarRoot == null) return;
        bossBarRoot.SetActive(true);
        HandleBossHealthChanged(currentHp, maxHp);
    }

    private void SetHudStage(string title, string objective)
    {
        if (hudStageTitleText != null) hudStageTitleText.text = title;
        if (hudObjectiveText != null) hudObjectiveText.text = objective;
    }

    private void UpdateDashHud()
    {
        if (hudDashText == null || playerController == null) return;

        int state = playerController.IsBlocking ? 1 : 0;
        if (state == lastDashBlockingState) return;
        lastDashBlockingState = state;

        if (state == 1)
        {
            hudDashText.text = "[Chuột Phải] ĐANG ĐỠ ĐÒN (BLOCK / PARRY)";
            hudDashText.color = new Color(1f, 0.86f, 0.32f);
        }
        else
        {
            hudDashText.text = "[LMB] Chém  •  [RMB] Đỡ  •  [B] Cửa Hàng";
            hudDashText.color = new Color(0.55f, 0.92f, 1f);
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

    // ==================== UTILITY ====================
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

        // Ưu tiên bề mặt TerrainCollider trước
        for (int i = 0; i < hitCount; i++)
        {
            if (TerrainHitBuffer[i].collider is TerrainCollider)
            {
                return new Vector3(rawPos.x, TerrainHitBuffer[i].point.y, rawPos.z);
            }
        }

        // Fallback: bề mặt cao nhất bất kỳ
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
