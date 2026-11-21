using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Điều khiển toàn bộ Scene Chọn Nhân Vật (CharacterSelectScene.unity):
/// - Toàn bộ 4 nhân vật 3D, bệ đá, ánh sáng và UI Canvas được bake sẵn trực tiếp trong Scene (nhìn thấy ngay ở Edit Mode).
/// - Hỗ trợ chọn bằng 4 thẻ nhân vật, click trực tiếp lên model 3D, hoặc phím 1-4 / A-D / Mũi tên.
/// - Kéo chuột trái trên sân khấu để xoay nhân vật đang chọn 360°.
/// - Nhấn "VÀO CHIẾN ĐẤU" (hoặc Enter) sẽ lưu nhân vật vào SelectedCharacterStore và tải màn Lv1.
/// </summary>
public class CharacterSelectController : MonoBehaviour
{
    [Serializable]
    public class HeroSlotView
    {
        public Transform slotRoot;
        public Transform modelTurntable;
        public Animator heroAnimator;
        public Renderer pedestalRingRenderer;
        public Collider clickCollider;
        public Vector3 basePosition;
    }

    [Serializable]
    public class HeroCardView
    {
        public Button button;
        public Image background;
        public Image accentStrip;
        public Outline borderOutline;
        public TMP_Text nameText;
        public TMP_Text roleText;
    }

    [Header("Config & Navigation")]
    [SerializeField] private CharacterRosterConfig rosterConfig;
    [SerializeField] private string gameplaySceneName = "Lv1";
    [SerializeField] private string backSceneName = "LoginScene";

    [Header("3D Stage References (Pre-Baked in Scene)")]
    [SerializeField] private Camera showcaseCamera;
    [SerializeField] private Light heroSpotlight;
    [SerializeField] private Transform spotlightAnchor;
    [SerializeField] private HeroSlotView[] heroSlots = new HeroSlotView[4];

    [Header("Camera Framing")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(-0.75f, 1.85f, -4.15f);
    [SerializeField] private Vector3 lookAtOffset = new Vector3(-0.55f, 1.05f, 0f);
    [SerializeField] private float cameraMoveSmooth = 7.5f;
    [SerializeField] private float selectedStepForward = 0.55f;
    [SerializeField] private float selectedScaleBoost = 1.08f;
    [SerializeField] private float unselectedScale = 0.94f;

    [Header("UI Hero Info Panel (Pre-Baked in Scene)")]
    [SerializeField] private Image roleBadgeBg;
    [SerializeField] private TMP_Text roleBadgeText;
    [SerializeField] private TMP_Text heroNameText;
    [SerializeField] private TMP_Text heroTitleText;
    [SerializeField] private TMP_Text heroDescText;
    [SerializeField] private TMP_Text heroPerkText;
    [SerializeField] private Image infoAccentLine;

    [Header("UI Stat Bars (Pre-Baked in Scene)")]
    [SerializeField] private Slider hpSlider;
    [SerializeField] private TMP_Text hpValueText;
    [SerializeField] private Slider atkSlider;
    [SerializeField] private TMP_Text atkValueText;
    [SerializeField] private Slider spdSlider;
    [SerializeField] private TMP_Text spdValueText;
    [SerializeField] private Slider defSlider;
    [SerializeField] private TMP_Text defValueText;

    [Header("UI Controls (Pre-Baked in Scene)")]
    [SerializeField] private HeroCardView[] heroCards = new HeroCardView[4];
    [SerializeField] private Button playButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button prevButton;
    [SerializeField] private Button nextButton;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private int selectedIndex;
    private float targetHpStat, targetAtkStat, targetSpdStat, targetDefStat;
    private bool isDraggingHero;
    private bool isTransitioning;
    private float dragStartMouseX;
    private float[] turntableYaws = new float[4];
    private MaterialPropertyBlock ringPropBlock;

    private void Awake()
    {
        if (rosterConfig == null)
        {
            rosterConfig = SelectedCharacterStore.GetRoster();
        }
        if (showcaseCamera == null)
        {
            showcaseCamera = Camera.main;
        }

        ringPropBlock = new MaterialPropertyBlock();

        if (heroSlots != null)
        {
            turntableYaws = new float[heroSlots.Length];
            for (int i = 0; i < heroSlots.Length; i++)
            {
                if (heroSlots[i] != null && heroSlots[i].slotRoot != null && heroSlots[i].basePosition == Vector3.zero)
                {
                    heroSlots[i].basePosition = heroSlots[i].slotRoot.position;
                }
                turntableYaws[i] = 180f;
            }
        }
    }

    private void Start()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        BindButtons();
        StartHeroIdleAnimations();

        int initialIdx = SelectedCharacterStore.SelectedIndex;
        int maxCount = rosterConfig != null && rosterConfig.Count > 0 ? rosterConfig.Count : heroSlots.Length;
        selectedIndex = Mathf.Clamp(initialIdx, 0, Mathf.Max(0, maxCount - 1));

        SelectCharacter(selectedIndex, instant: true);
    }

    private void StartHeroIdleAnimations()
    {
        if (heroSlots == null) return;

        for (int i = 0; i < heroSlots.Length; i++)
        {
            Animator anim = heroSlots[i]?.heroAnimator;
            if (anim == null && heroSlots[i]?.modelTurntable != null)
            {
                anim = heroSlots[i].modelTurntable.GetComponentInChildren<Animator>();
            }
            if (anim == null) continue;

            int stateHash = Animator.StringToHash($"Hero{i}_Idle");
            if (anim.HasState(0, stateHash))
            {
                anim.Play(stateHash, 0, (i * 0.23f) % 1f);
            }
        }
    }

    private void BindButtons()
    {
        if (heroCards != null)
        {
            for (int i = 0; i < heroCards.Length; i++)
            {
                int idx = i;
                if (heroCards[i]?.button != null)
                {
                    heroCards[i].button.onClick.RemoveAllListeners();
                    heroCards[i].button.onClick.AddListener(() => SelectCharacter(idx, false));
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
            backButton.onClick.AddListener(GoBackToLogin);
        }

        if (prevButton != null)
        {
            prevButton.onClick.RemoveAllListeners();
            prevButton.onClick.AddListener(() => CycleCharacter(-1));
        }

        if (nextButton != null)
        {
            nextButton.onClick.RemoveAllListeners();
            nextButton.onClick.AddListener(() => CycleCharacter(1));
        }
    }

    private void Update()
    {
        HandleKeyboardInput();
        HandleStagePointerInput();
        UpdateStagePresentation();
        UpdateStatBarsSmooth();
    }

    public void CycleCharacter(int delta)
    {
        int count = heroSlots != null && heroSlots.Length > 0 ? heroSlots.Length : 4;
        int next = (selectedIndex + delta + count) % count;
        SelectCharacter(next, false);
    }

    public void SelectCharacter(int index, bool instant = false)
    {
        int count = heroSlots != null && heroSlots.Length > 0 ? heroSlots.Length : 4;
        index = Mathf.Clamp(index, 0, count - 1);

        bool changed = index != selectedIndex;
        selectedIndex = index;
        SelectedCharacterStore.SelectedIndex = selectedIndex;

        if (!instant && changed)
        {
            GameAudioManager.PlayUiClick();
        }

        if (turntableYaws != null && selectedIndex < turntableYaws.Length)
        {
            turntableYaws[selectedIndex] = 180f;
        }

        UpdateUiForSelectedHero(instant);
        UpdateCardHighlights();
        UpdatePedestalHighlights();

        if (instant)
        {
            SnapCameraAndSlots();
        }
    }

    public void ConfirmAndPlay()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        SelectedCharacterStore.SelectedIndex = selectedIndex;
        GameAudioManager.PlayWeaponEquip();

        string target = string.IsNullOrWhiteSpace(gameplaySceneName) ? "Lv1" : gameplaySceneName;
        LoadingController.LoadScene(target);
    }

    public void GoBackToLogin()
    {
        GameAudioManager.PlayUiClick();
        string target = string.IsNullOrWhiteSpace(backSceneName) ? "LoginScene" : backSceneName;
        if (Application.CanStreamedLevelBeLoaded(target))
        {
            SceneManager.LoadScene(target);
        }
    }

    private void HandleKeyboardInput()
    {
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
        {
            CycleCharacter(-1);
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
        {
            CycleCharacter(1);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha1)) SelectCharacter(0, false);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) SelectCharacter(1, false);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) SelectCharacter(2, false);
        else if (Input.GetKeyDown(KeyCode.Alpha4)) SelectCharacter(3, false);
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            ConfirmAndPlay();
        }
        else if (Input.GetKeyDown(KeyCode.Escape))
        {
            GoBackToLogin();
        }
    }

    private void HandleStagePointerInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                isDraggingHero = false;
                return;
            }

            // Click trực tiếp lên 1 trong 4 nhân vật 3D trên sân khấu để chọn nhanh
            if (showcaseCamera != null)
            {
                Ray ray = showcaseCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 40f))
                {
                    for (int i = 0; i < heroSlots.Length; i++)
                    {
                        if (heroSlots[i] == null) continue;
                        if (hit.collider == heroSlots[i].clickCollider
                            || (heroSlots[i].slotRoot != null && hit.transform.IsChildOf(heroSlots[i].slotRoot)))
                        {
                            if (i != selectedIndex)
                            {
                                SelectCharacter(i, false);
                            }
                            break;
                        }
                    }
                }
            }

            isDraggingHero = true;
            dragStartMouseX = Input.mousePosition.x;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            isDraggingHero = false;
        }

        if (isDraggingHero && Input.GetMouseButton(0))
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
        float dt = Time.unscaledDeltaTime;
        float t = 1f - Mathf.Exp(-cameraMoveSmooth * dt);

        Vector3 activeTargetPos = Vector3.zero;

        for (int i = 0; i < heroSlots.Length; i++)
        {
            HeroSlotView slot = heroSlots[i];
            if (slot == null || slot.slotRoot == null) continue;

            bool isSelected = i == selectedIndex;
            Vector3 desiredPos = slot.basePosition + (isSelected ? new Vector3(0f, 0f, -selectedStepForward) : Vector3.zero);
            slot.slotRoot.position = Vector3.Lerp(slot.slotRoot.position, desiredPos, t);

            float targetScale = isSelected ? selectedScaleBoost : unselectedScale;
            slot.slotRoot.localScale = Vector3.Lerp(slot.slotRoot.localScale, Vector3.one * targetScale, t);

            if (slot.modelTurntable != null)
            {
                if (!isSelected)
                {
                    turntableYaws[i] = Mathf.LerpAngle(turntableYaws[i], 180f, dt * 4f);
                }

                Quaternion desiredRot = Quaternion.Euler(0f, turntableYaws[i], 0f);
                slot.modelTurntable.localRotation = Quaternion.Slerp(slot.modelTurntable.localRotation, desiredRot, 1f - Mathf.Exp(-14f * dt));
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
            Vector3 spotTarget = activeTargetPos + new Vector3(0f, 3.8f, -1.0f);
            spotlightAnchor.position = Vector3.Lerp(spotlightAnchor.position, spotTarget, t);
        }

        if (heroSpotlight != null)
        {
            CharacterProfile p = rosterConfig != null ? rosterConfig.GetProfile(selectedIndex) : null;
            Color targetLightCol = p != null ? Color.Lerp(Color.white, p.themeColor, 0.45f) : Color.white;
            heroSpotlight.color = Color.Lerp(heroSpotlight.color, targetLightCol, t);
        }
    }

    private void SnapCameraAndSlots()
    {
        Vector3 activeTargetPos = Vector3.zero;
        for (int i = 0; i < heroSlots.Length; i++)
        {
            HeroSlotView slot = heroSlots[i];
            if (slot == null || slot.slotRoot == null) continue;

            bool isSelected = i == selectedIndex;
            slot.slotRoot.position = slot.basePosition + (isSelected ? new Vector3(0f, 0f, -selectedStepForward) : Vector3.zero);
            slot.slotRoot.localScale = Vector3.one * (isSelected ? selectedScaleBoost : unselectedScale);
            if (slot.modelTurntable != null)
            {
                slot.modelTurntable.localRotation = Quaternion.Euler(0f, 180f, 0f);
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
            spotlightAnchor.position = activeTargetPos + new Vector3(0f, 3.8f, -1.0f);
        }
    }

    private void UpdateUiForSelectedHero(bool instant)
    {
        CharacterProfile profile = rosterConfig != null ? rosterConfig.GetProfile(selectedIndex) : null;
        if (profile == null) return;

        if (roleBadgeText != null) roleBadgeText.text = profile.roleBadge;
        if (roleBadgeBg != null)
        {
            Color badgeCol = profile.themeColor;
            badgeCol.a = 0.24f;
            roleBadgeBg.color = badgeCol;
        }
        if (heroNameText != null)
        {
            heroNameText.text = profile.displayName;
            heroNameText.color = Color.Lerp(Color.white, profile.themeColor, 0.28f);
        }
        if (heroTitleText != null) heroTitleText.text = profile.title;
        if (heroDescText != null) heroDescText.text = profile.description;
        if (heroPerkText != null) heroPerkText.text = profile.signaturePerk;
        if (infoAccentLine != null) infoAccentLine.color = profile.themeColor;

        targetHpStat = profile.statHealth;
        targetAtkStat = profile.statAttack;
        targetSpdStat = profile.statSpeed;
        targetDefStat = profile.statDefense;

        if (hpValueText != null) hpValueText.text = $"{profile.maxHealth} HP";
        if (atkValueText != null) atkValueText.text = $"{profile.attackDamage} DMG";
        if (spdValueText != null) spdValueText.text = $"{profile.moveSpeed:0.0} m/s";
        if (defValueText != null) defValueText.text = $"{Mathf.RoundToInt(profile.blockDamageReduction * 100f)}% Đỡ";

        if (instant)
        {
            if (hpSlider != null) hpSlider.value = targetHpStat;
            if (atkSlider != null) atkSlider.value = targetAtkStat;
            if (spdSlider != null) spdSlider.value = targetSpdStat;
            if (defSlider != null) defSlider.value = targetDefStat;
        }
    }

    private void UpdateStatBarsSmooth()
    {
        float k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
        if (hpSlider != null) hpSlider.value = Mathf.Lerp(hpSlider.value, targetHpStat, k);
        if (atkSlider != null) atkSlider.value = Mathf.Lerp(atkSlider.value, targetAtkStat, k);
        if (spdSlider != null) spdSlider.value = Mathf.Lerp(spdSlider.value, targetSpdStat, k);
        if (defSlider != null) defSlider.value = Mathf.Lerp(defSlider.value, targetDefStat, k);
    }

    private void UpdateCardHighlights()
    {
        if (heroCards == null) return;

        for (int i = 0; i < heroCards.Length; i++)
        {
            HeroCardView card = heroCards[i];
            if (card == null) continue;

            bool isSelected = i == selectedIndex;
            CharacterProfile p = rosterConfig != null ? rosterConfig.GetProfile(i) : null;
            Color accent = p != null ? p.themeColor : FantasyUiThemeHelper.GoldBright;

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
                card.borderOutline.effectColor = isSelected
                    ? accent
                    : new Color(0.45f, 0.52f, 0.65f, 0.30f);
                card.borderOutline.effectDistance = isSelected ? new Vector2(2.2f, -2.2f) : new Vector2(1.2f, -1.2f);
            }

            if (card.nameText != null)
            {
                card.nameText.color = isSelected ? Color.white : new Color(0.76f, 0.80f, 0.88f, 0.90f);
            }

            if (card.roleText != null)
            {
                card.roleText.color = isSelected ? accent : FantasyUiThemeHelper.TextMuted;
            }
        }
    }

    private void UpdatePedestalHighlights()
    {
        if (heroSlots == null || ringPropBlock == null) return;

        for (int i = 0; i < heroSlots.Length; i++)
        {
            Renderer r = heroSlots[i]?.pedestalRingRenderer;
            if (r == null) continue;

            CharacterProfile p = rosterConfig != null ? rosterConfig.GetProfile(i) : null;
            Color accent = p != null ? p.themeColor : FantasyUiThemeHelper.GoldPrimary;
            bool isSelected = i == selectedIndex;

            Color c = isSelected
                ? new Color(accent.r * 1.4f, accent.g * 1.4f, accent.b * 1.4f, 1f)
                : new Color(accent.r * 0.28f, accent.g * 0.28f, accent.b * 0.28f, 1f);

            r.GetPropertyBlock(ringPropBlock);
            ringPropBlock.SetColor(BaseColorId, c);
            ringPropBlock.SetColor(ColorId, c);
            ringPropBlock.SetColor(EmissionColorId, isSelected ? accent * 1.8f : accent * 0.15f);
            r.SetPropertyBlock(ringPropBlock);
        }
    }
}
