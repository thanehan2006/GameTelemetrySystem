using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Điều khiển màn hình chọn bản đồ với mô hình sa bàn 3D xoay được, thông tin boss/độ khó và nút vào trận
public class MapSelectController : MonoBehaviour
{
    [Serializable]
    public class MapSlotView
    {
        public string sceneName = "Lv1";
        public string mapCode = "CHIẾN DỊCH 01";
        public string difficultyBadge = "DỄ • KHỞI ĐẦU";
        public string mapTitle = "RỪNG ORC & MỎ VÀNG";
        public string mapSubtitle = "Thung Lũng Ngoại Vi • Đại Tướng Warlord";
        [TextArea(2, 4)]
        public string description = "Bìa rừng xanh rậm nơi quân trinh sát Orc chiếm đóng mỏ vàng cổ.";
        public string bossLabel = "Boss: Đại Tướng Orc Warlord";
        public string enemiesLabel = "Quái: Orc Trinh Sát • Orc Rìu • Orc Khiên";
        public string rewardLabel = "Hệ số Map: 1.0x HP • 1.0x DMG";
        public Color accentColor = new Color(0.95f, 0.74f, 0.28f, 1f);

        [Header("Stat Bars (0 - 100)")]
        [Range(0, 100)] public int statDifficulty = 45;
        [Range(0, 100)] public int statHazard = 40;
        [Range(0, 100)] public int statBossPower = 55;
        public string difficultyValLabel = "45 / 100 (Vừa)";
        public string hazardValLabel = "Bẫy Gai Rừng";
        public string bossPowerValLabel = "700 HP Boss";

        [Header("3D Diorama References")]
        public Transform slotRoot;
        public Transform dioramaTurntable;
        public Renderer pedestalRingRenderer;
        public Collider clickCollider;
        public Vector3 basePosition;
        public Material skyboxMaterial;
    }

    [Serializable]
    public class MapCardView
    {
        public Button button;
        public Image background;
        public Image accentStrip;
        public Outline borderOutline;
        public TMP_Text titleText;
        public TMP_Text subtitleText;
    }

    [Header("Navigation")]
    [SerializeField] private string backSceneName = "PickCharacter";

    [Header("3D Stage & Camera Framing")]
    [SerializeField] private Camera showcaseCamera;
    [SerializeField] private Light mapSpotlight;
    [SerializeField] private Transform spotlightAnchor;
    [SerializeField] private Vector3 cameraOffset = new Vector3(-1.55f, 2.85f, -7.25f);
    [SerializeField] private Vector3 lookAtOffset = new Vector3(-0.75f, 1.00f, 0f);
    [SerializeField] private float cameraMoveSmooth = 7.5f;
    [SerializeField] private float selectedStepForward = 0.75f;
    [SerializeField] private float selectedScaleBoost = 1.08f;
    [SerializeField] private float unselectedScale = 0.88f;
    [SerializeField] private float autoSpinSpeed = 9f;

    [Header("3 Maps Data & Dioramas")]
    [SerializeField] private MapSlotView[] mapSlots = new MapSlotView[3];

    [Header("UI Hero Pill & Map Info Panel")]
    [SerializeField] private TMP_Text selectedHeroText;
    [SerializeField] private Image selectedHeroBadgeBg;
    [SerializeField] private Image difficultyBadgeBg;
    [SerializeField] private TMP_Text difficultyBadgeText;
    [SerializeField] private TMP_Text mapTitleText;
    [SerializeField] private TMP_Text mapSubtitleText;
    [SerializeField] private TMP_Text mapDescText;
    [SerializeField] private TMP_Text bossTargetText;
    [SerializeField] private TMP_Text enemyRosterText;
    [SerializeField] private TMP_Text scaleRewardText;
    [SerializeField] private Image infoAccentLine;

    [Header("UI Stat Bars")]
    [SerializeField] private Slider difficultySlider;
    [SerializeField] private TMP_Text difficultyValueText;
    [SerializeField] private Slider hazardSlider;
    [SerializeField] private TMP_Text hazardValueText;
    [SerializeField] private Slider bossSlider;
    [SerializeField] private TMP_Text bossValueText;

    [Header("UI Controls")]
    [SerializeField] private MapCardView[] mapCards = new MapCardView[3];
    [SerializeField] private Button playButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button prevButton;
    [SerializeField] private Button nextButton;

    private const string SelectedMapPrefKey = "SelectedMapIndex";
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private int selectedIndex;
    private float targetDiffStat, targetHazardStat, targetBossStat;
    private bool isDraggingDiorama;
    private bool isTransitioning;
    private float dragStartMouseX;
    private float[] turntableYaws = new float[3];
    private MaterialPropertyBlock ringPropBlock;

    // Mở khóa chuột cho màn hình chọn bản đồ
    private void Awake()
    {
        if (showcaseCamera == null)
        {
            showcaseCamera = Camera.main;
        }

        ringPropBlock = new MaterialPropertyBlock();

        if (mapSlots != null)
        {
            turntableYaws = new float[mapSlots.Length];
            for (int i = 0; i < mapSlots.Length; i++)
            {
                if (mapSlots[i] != null && mapSlots[i].slotRoot != null && mapSlots[i].basePosition == Vector3.zero)
                {
                    mapSlots[i].basePosition = mapSlots[i].slotRoot.position;
                }
                turntableYaws[i] = 18f;
            }
        }
    }

    // Hiển thị nhân vật đã chọn và đồng bộ sa bàn bản đồ đầu tiên
    private void Start()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        PopulateSelectedHeroBadge();
        ConfigureMapTitleText();
        BindButtons();

        int count = mapSlots != null ? mapSlots.Length : 3;
        int savedIdx = Mathf.Clamp(PlayerPrefs.GetInt(SelectedMapPrefKey, 0), 0, Mathf.Max(0, count - 1));
        SelectMap(savedIdx, instant: true);
    }

    private void PopulateSelectedHeroBadge()
    {
        CharacterProfile hero = SelectedCharacterStore.GetSelectedProfile();
        if (hero == null) return;

        if (selectedHeroText != null)
        {
            selectedHeroText.text = $"CHIẾN BINH: {hero.displayName}  •  {hero.roleBadge}";
            selectedHeroText.color = Color.Lerp(Color.white, hero.themeColor, 0.45f);
        }

        if (selectedHeroBadgeBg != null)
        {
            Color c = hero.themeColor;
            c.a = 0.18f;
            selectedHeroBadgeBg.color = c;
        }
    }

    private void BindButtons()
    {
        if (mapCards != null)
        {
            for (int i = 0; i < mapCards.Length; i++)
            {
                int idx = i;
                if (mapCards[i]?.button != null)
                {
                    mapCards[i].button.onClick.RemoveAllListeners();
                    mapCards[i].button.onClick.AddListener(() => SelectMap(idx, false));
                }
            }
        }

        if (playButton != null)
        {
            playButton.onClick.RemoveAllListeners();
            playButton.onClick.AddListener(ConfirmAndPlay);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(GoBackToCharacterSelect);
        }

        if (prevButton != null)
        {
            prevButton.onClick.RemoveAllListeners();
            prevButton.onClick.AddListener(() => CycleMap(-1));
        }

        if (nextButton != null)
        {
            nextButton.onClick.RemoveAllListeners();
            nextButton.onClick.AddListener(() => CycleMap(1));
        }
    }

    // Xử lý điều hướng bàn phím, xoay mô hình sa bàn và làm mượt thanh chỉ số
    private void Update()
    {
        HandleKeyboardInput();
        HandlePointerInput();
        UpdateStagePresentation();
        UpdateStatBarsSmooth();
    }

    // Chuyển đổi qua lại giữa 3 bản đồ chiến dịch
    public void CycleMap(int delta)
    {
        int count = mapSlots != null && mapSlots.Length > 0 ? mapSlots.Length : 3;
        int next = (selectedIndex + delta + count) % count;
        SelectMap(next, false);
    }

    // Chọn bản đồ, cập nhật thông tin chi tiết, thẻ chọn và skybox tương ứng
    public void SelectMap(int index, bool instant = false)
    {
        int count = mapSlots != null && mapSlots.Length > 0 ? mapSlots.Length : 3;
        index = Mathf.Clamp(index, 0, count - 1);

        bool changed = index != selectedIndex;
        selectedIndex = index;

        PlayerPrefs.SetInt(SelectedMapPrefKey, selectedIndex);
        PlayerPrefs.Save();

        if (!instant && changed)
        {
            GameAudioManager.PlayUiClick();
        }

        UpdateUiForSelectedMap(instant);
        UpdateCardHighlights();
        UpdatePedestalHighlights();
        ApplySelectedSkybox();

        if (instant)
        {
            SnapCameraAndSlots();
        }
    }

    // Nạp scene chiến đấu của bản đồ đã chọn qua màn hình Loading
    public void ConfirmAndPlay()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        PlayerPrefs.SetInt(SelectedMapPrefKey, selectedIndex);
        PlayerPrefs.Save();
        GameAudioManager.PlayWeaponEquip();

        MapSlotView chosen = mapSlots != null && selectedIndex < mapSlots.Length ? mapSlots[selectedIndex] : null;
        string targetScene = chosen != null && !string.IsNullOrWhiteSpace(chosen.sceneName) ? chosen.sceneName : "Lv1";
        LoadingController.LoadScene(targetScene);
    }

    // Quay về màn hình chọn nhân vật
    public void GoBackToCharacterSelect()
    {
        GameAudioManager.PlayUiClick();
        string target = string.IsNullOrWhiteSpace(backSceneName) ? "PickCharacter" : backSceneName;
        if (Application.CanStreamedLevelBeLoaded(target))
        {
            SceneManager.LoadScene(target);
        }
    }

    private void HandleKeyboardInput()
    {
        if (GameMenuManager.IsMenuOpen) return;
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
        {
            CycleMap(-1);
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
        {
            CycleMap(1);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha1)) SelectMap(0, false);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) SelectMap(1, false);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) SelectMap(2, false);
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            ConfirmAndPlay();
        }
        else if (Input.GetKeyDown(KeyCode.Escape))
        {
            GoBackToCharacterSelect();
        }
    }

    private void HandlePointerInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                isDraggingDiorama = false;
                return;
            }

            if (showcaseCamera != null && mapSlots != null)
            {
                Ray ray = showcaseCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 60f))
                {
                    for (int i = 0; i < mapSlots.Length; i++)
                    {
                        if (mapSlots[i] == null) continue;
                        if (hit.collider == mapSlots[i].clickCollider
                            || (mapSlots[i].slotRoot != null && hit.transform.IsChildOf(mapSlots[i].slotRoot)))
                        {
                            if (i != selectedIndex)
                            {
                                SelectMap(i, false);
                            }
                            break;
                        }
                    }
                }
            }

            isDraggingDiorama = true;
            dragStartMouseX = Input.mousePosition.x;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            isDraggingDiorama = false;
        }

        if (isDraggingDiorama && Input.GetMouseButton(0))
        {
            float dx = Input.mousePosition.x - dragStartMouseX;
            dragStartMouseX = Input.mousePosition.x;
            if (selectedIndex >= 0 && selectedIndex < turntableYaws.Length)
            {
                turntableYaws[selectedIndex] -= dx * 0.45f;
            }
        }
    }

    private void UpdateStagePresentation()
    {
        if (mapSlots == null || mapSlots.Length == 0) return;

        float dt = Time.unscaledDeltaTime;
        float t = 1f - Mathf.Exp(-cameraMoveSmooth * dt);
        Vector3 activeTargetPos = Vector3.zero;

        for (int i = 0; i < mapSlots.Length; i++)
        {
            MapSlotView slot = mapSlots[i];
            if (slot == null || slot.slotRoot == null) continue;

            bool isSelected = i == selectedIndex;
            Vector3 desiredPos = slot.basePosition + (isSelected ? new Vector3(0f, 0f, -selectedStepForward) : Vector3.zero);
            slot.slotRoot.position = Vector3.Lerp(slot.slotRoot.position, desiredPos, t);

            float targetScale = isSelected ? selectedScaleBoost : unselectedScale;
            slot.slotRoot.localScale = Vector3.Lerp(slot.slotRoot.localScale, Vector3.one * targetScale, t);

            if (slot.dioramaTurntable != null)
            {
                if (isSelected && !isDraggingDiorama)
                {
                    turntableYaws[i] += autoSpinSpeed * dt;
                }
                else if (!isSelected)
                {
                    turntableYaws[i] = Mathf.LerpAngle(turntableYaws[i], 18f, dt * 3f);
                }

                Quaternion desiredRot = Quaternion.Euler(0f, turntableYaws[i], 0f);
                slot.dioramaTurntable.localRotation = Quaternion.Slerp(slot.dioramaTurntable.localRotation, desiredRot, 1f - Mathf.Exp(-12f * dt));
            }

            if (isSelected)
            {
                activeTargetPos = slot.slotRoot.position;
            }
        }

        if (showcaseCamera != null)
        {
            Vector3 desiredCamPos = activeTargetPos + cameraOffset;
            showcaseCamera.transform.position = Vector3.Lerp(showcaseCamera.transform.position, desiredCamPos, t);

            Vector3 lookTarget = activeTargetPos + lookAtOffset;
            Quaternion desiredCamRot = Quaternion.LookRotation(lookTarget - showcaseCamera.transform.position);
            showcaseCamera.transform.rotation = Quaternion.Slerp(showcaseCamera.transform.rotation, desiredCamRot, t);
        }

        if (spotlightAnchor != null)
        {
            Vector3 spotTarget = activeTargetPos + new Vector3(0f, 5.2f, -1.8f);
            spotlightAnchor.position = Vector3.Lerp(spotlightAnchor.position, spotTarget, t);
        }

        if (mapSpotlight != null && selectedIndex < mapSlots.Length && mapSlots[selectedIndex] != null)
        {
            Color targetLightCol = Color.Lerp(Color.white, mapSlots[selectedIndex].accentColor, 0.40f);
            mapSpotlight.color = Color.Lerp(mapSpotlight.color, targetLightCol, t);
        }
    }

    private void SnapCameraAndSlots()
    {
        if (mapSlots == null || mapSlots.Length == 0) return;

        Vector3 activeTargetPos = Vector3.zero;
        for (int i = 0; i < mapSlots.Length; i++)
        {
            MapSlotView slot = mapSlots[i];
            if (slot == null || slot.slotRoot == null) continue;

            bool isSelected = i == selectedIndex;
            slot.slotRoot.position = slot.basePosition + (isSelected ? new Vector3(0f, 0f, -selectedStepForward) : Vector3.zero);
            slot.slotRoot.localScale = Vector3.one * (isSelected ? selectedScaleBoost : unselectedScale);
            if (slot.dioramaTurntable != null)
            {
                slot.dioramaTurntable.localRotation = Quaternion.Euler(0f, 18f, 0f);
            }

            if (isSelected) activeTargetPos = slot.slotRoot.position;
        }

        if (showcaseCamera != null)
        {
            showcaseCamera.transform.position = activeTargetPos + cameraOffset;
            showcaseCamera.transform.LookAt(activeTargetPos + lookAtOffset);
        }

        if (spotlightAnchor != null)
        {
            spotlightAnchor.position = activeTargetPos + new Vector3(0f, 5.2f, -1.8f);
        }
    }

    private void UpdateUiForSelectedMap(bool instant)
    {
        MapSlotView chosen = mapSlots != null && selectedIndex < mapSlots.Length ? mapSlots[selectedIndex] : null;
        if (chosen == null) return;

        if (difficultyBadgeText != null) difficultyBadgeText.text = chosen.difficultyBadge;
        if (difficultyBadgeBg != null)
        {
            Color badgeCol = chosen.accentColor;
            badgeCol.a = 0.24f;
            difficultyBadgeBg.color = badgeCol;
        }

        if (mapTitleText != null)
        {
            mapTitleText.text = chosen.mapTitle;
            mapTitleText.color = Color.Lerp(Color.white, chosen.accentColor, 0.28f);
        }
        if (mapSubtitleText != null) mapSubtitleText.text = chosen.mapSubtitle;
        if (mapDescText != null) mapDescText.text = chosen.description;
        if (bossTargetText != null) bossTargetText.text = chosen.bossLabel;
        if (enemyRosterText != null) enemyRosterText.text = chosen.enemiesLabel;
        if (scaleRewardText != null) scaleRewardText.text = chosen.rewardLabel;
        if (infoAccentLine != null) infoAccentLine.color = chosen.accentColor;

        targetDiffStat = chosen.statDifficulty;
        targetHazardStat = chosen.statHazard;
        targetBossStat = chosen.statBossPower;

        if (difficultyValueText != null) difficultyValueText.text = chosen.difficultyValLabel;
        if (hazardValueText != null) hazardValueText.text = chosen.hazardValLabel;
        if (bossValueText != null) bossValueText.text = chosen.bossPowerValLabel;

        if (instant)
        {
            if (difficultySlider != null) difficultySlider.value = targetDiffStat;
            if (hazardSlider != null) hazardSlider.value = targetHazardStat;
            if (bossSlider != null) bossSlider.value = targetBossStat;
        }

        if (playButton != null)
        {
            TMP_Text playTxt = playButton.GetComponentInChildren<TMP_Text>(true);
            if (playTxt != null)
            {
                playTxt.text = $"VÀO MAP {selectedIndex + 1} CHIẾN ĐẤU";
            }
        }
    }

    private void ConfigureMapTitleText()
    {
        if (mapTitleText == null) return;
        mapTitleText.textWrappingMode = TextWrappingModes.NoWrap;
        mapTitleText.overflowMode = TextOverflowModes.Truncate;
        mapTitleText.enableAutoSizing = true;
        mapTitleText.fontSizeMin = 19f;
        mapTitleText.fontSizeMax = 30f;
        mapTitleText.characterSpacing = 1.2f;
    }

    private void UpdateStatBarsSmooth()
    {
        float k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
        SmoothSlider(difficultySlider, targetDiffStat, k);
        SmoothSlider(hazardSlider, targetHazardStat, k);
        SmoothSlider(bossSlider, targetBossStat, k);
    }

    private static void SmoothSlider(Slider slider, float target, float k)
    {
        if (slider == null) return;
        float cur = slider.value;
        if (Mathf.Approximately(cur, target)) return;
        float next = Mathf.Lerp(cur, target, k);
        slider.value = Mathf.Abs(next - target) < 0.01f ? target : next;
    }

    private void UpdateCardHighlights()
    {
        if (mapCards == null) return;

        for (int i = 0; i < mapCards.Length; i++)
        {
            MapCardView card = mapCards[i];
            if (card == null) continue;

            bool isSelected = i == selectedIndex;
            Color accent = mapSlots != null && i < mapSlots.Length && mapSlots[i] != null
                ? mapSlots[i].accentColor
                : FantasyUiThemeHelper.GoldBright;

            if (card.background != null)
            {
                card.background.color = isSelected
                    ? new Color(0.11f, 0.14f, 0.22f, 0.96f)
                    : new Color(0.05f, 0.07f, 0.12f, 0.84f);
            }

            if (card.accentStrip != null)
            {
                card.accentStrip.color = isSelected ? accent : new Color(accent.r, accent.g, accent.b, 0.35f);
            }

            if (card.borderOutline != null)
            {
                card.borderOutline.effectColor = isSelected ? accent : new Color(0.45f, 0.52f, 0.65f, 0.30f);
                card.borderOutline.effectDistance = isSelected ? new Vector2(2.2f, -2.2f) : new Vector2(1.2f, -1.2f);
            }

            if (card.titleText != null)
            {
                card.titleText.color = isSelected ? Color.white : new Color(0.76f, 0.80f, 0.88f, 0.90f);
            }

            if (card.subtitleText != null)
            {
                card.subtitleText.color = isSelected ? accent : FantasyUiThemeHelper.TextMuted;
            }
        }
    }

    private void UpdatePedestalHighlights()
    {
        if (mapSlots == null || ringPropBlock == null) return;

        for (int i = 0; i < mapSlots.Length; i++)
        {
            MapSlotView slot = mapSlots[i];
            if (slot == null || slot.pedestalRingRenderer == null) continue;

            Color accent = slot.accentColor;
            bool isSelected = i == selectedIndex;
            Color c = isSelected
                ? new Color(accent.r * 1.4f, accent.g * 1.4f, accent.b * 1.4f, 1f)
                : new Color(accent.r * 0.28f, accent.g * 0.28f, accent.b * 0.28f, 1f);

            slot.pedestalRingRenderer.GetPropertyBlock(ringPropBlock);
            ringPropBlock.SetColor(BaseColorId, c);
            ringPropBlock.SetColor(ColorId, c);
            ringPropBlock.SetColor(EmissionColorId, isSelected ? accent * 1.8f : accent * 0.15f);
            slot.pedestalRingRenderer.SetPropertyBlock(ringPropBlock);
        }
    }

    private void ApplySelectedSkybox()
    {
        if (mapSlots == null || selectedIndex < 0 || selectedIndex >= mapSlots.Length) return;
        Material sky = mapSlots[selectedIndex]?.skyboxMaterial;
        if (sky != null && RenderSettings.skybox != sky)
        {
            RenderSettings.skybox = sky;
        }
    }
}
