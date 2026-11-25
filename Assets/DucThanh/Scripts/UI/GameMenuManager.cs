using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Quản lý giao diện Cài đặt Dark Fantasy sử dụng bộ asset Kenney CC0 (Đồ họa, Độ phân giải,
/// Hiển thị, Âm thanh, Điều khiển), Menu Tạm Dừng (Esc) và nút Cài đặt góc phải trong map chiến đấu.
/// </summary>
public class GameMenuManager : MonoBehaviour
{
    public enum ViewMode
    {
        None,
        MainMenu,
        PauseMenu,
        Settings,
        Controls
    }

    public enum SettingsTab
    {
        Graphics = 0,
        Audio = 1,
        Controls = 2,
        System = 3
    }

    private const float SettingsWindowW = 1280f;
    private const float SettingsWindowH = 740f;
    private const float PauseWindowW = 510f;
    private const float PauseWindowH = 620f;

    public static GameMenuManager Instance { get; private set; }
    public static bool IsMenuOpen => Instance != null && Instance.isMenuOpen;
    public static bool PendingMainMenuOnLoginScene { get; private set; }
    public static string LoggedInPlayerName { get; private set; } = string.Empty;
    public static string MainMenuPlayTargetScene { get; private set; } = "PickCharacter";

    private static int suppressEscapeFrame = -1;

    private Canvas menuCanvas;
    private RectTransform canvasRt;
    private Button cornerMenuBtn;
    private RectTransform cornerMenuRt;
    private TMP_Text cornerMenuLabel;

    private GameObject fpsBadgeObj;
    private TMP_Text fpsBadgeText;
    private float fpsAccumTime;
    private int fpsFrameCount;

    private GameObject backdropObj;

    // --- SETTINGS WINDOW (1280x740) ---
    private GameObject settingsWindowObj;
    private RectTransform settingsWindowRt;
    private CanvasGroup settingsCanvasGroup;
    private TMP_Text settingsBannerSubText;

    private readonly Button[] tabButtons = new Button[4];
    private readonly GameObject[] tabPages = new GameObject[4];
    private SettingsTab activeTab = SettingsTab.Graphics;
    private TMP_Text sidebarWarriorText;

    // Tab 0: Graphics & Display
    private Button resolutionDropdownBtn;
    private TMP_Text resolutionValueText;
    private Button dispFullscreenBtn;
    private Button dispWindowedBtn;
    private Button dispBorderlessBtn;
    private Button gfxLowBtn;
    private Button gfxMedBtn;
    private Button gfxHighBtn;
    private TMP_Text gfxDescText;
    private Button vsyncToggleBtn;
    private Button fpsToggleBtn;

    // Resolution popup
    private GameObject resolutionDismissLayer;
    private GameObject resolutionPopupObj;
    private RectTransform resolutionPopupRt;
    private Transform resolutionListRoot;

    // Tab 1: Audio
    private Button masterToggleBtn;
    private Slider masterSlider;
    private TMP_Text masterValText;

    private Button bgmToggleBtn;
    private Slider bgmSlider;
    private TMP_Text bgmValText;

    private Button sfxToggleBtn;
    private Slider sfxSlider;
    private TMP_Text sfxValText;

    // Tab 2: Controls
    private Slider sensitivitySlider;
    private TMP_Text sensitivityValText;

    // Tab 3: System Summary
    private TMP_Text sysGfxValueText;
    private TMP_Text sysDisplayValueText;
    private TMP_Text sysAudioValueText;
    private TMP_Text sysAccountValueText;

    // Footer feedback toast
    private TMP_Text footerToastText;
    private float toastTimer;

    // --- PAUSE / FALLBACK MENU WINDOW (510x620) ---
    private GameObject pauseWindowObj;
    private RectTransform pauseWindowRt;
    private CanvasGroup pauseCanvasGroup;
    private TMP_Text pauseBannerTitleText;
    private TMP_Text pauseBannerSubText;
    private GameObject mainMenuContent;
    private GameObject pauseMenuContent;

    private CanvasGroup activeAnimGroup;
    private float modalAnimTime = 1f;
    private const float ModalAnimDuration = 0.16f;

    private bool uiBuilt;
    private bool isMenuOpen;
    private bool isQuitting;
    private ViewMode currentView = ViewMode.None;
    private ViewMode returnView = ViewMode.None;

    private int cachedSceneHandle = int.MinValue;
    private string cachedSceneName = string.Empty;
    private bool cachedIsLoadingScene;
    private bool cachedIsGameplayScene;
    private int lastShowCorner = -1;
    private int lastShowFps = -1;
    private bool lastCornerStyled;
    private Vector2 lastModalCanvasSize;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
        PendingMainMenuOnLoginScene = false;
        LoggedInPlayerName = string.Empty;
        MainMenuPlayTargetScene = "PickCharacter";
        suppressEscapeFrame = -1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        EnsureInstance();
    }

    public static GameMenuManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        Instance = FindFirstObjectByType<GameMenuManager>();
        if (Instance == null)
        {
            GameObject go = new GameObject("GameMenuManager");
            Instance = go.AddComponent<GameMenuManager>();
        }
        return Instance;
    }

    public static void SuppressEscapeThisFrame()
    {
        suppressEscapeFrame = Time.frameCount;
    }

    public static void SetLoggedInSession(string playerName, string playTargetScene = "PickCharacter")
    {
        LoggedInPlayerName = string.IsNullOrWhiteSpace(playerName) ? string.Empty : playerName.Trim();
        if (!string.IsNullOrWhiteSpace(playTargetScene))
        {
            MainMenuPlayTargetScene = playTargetScene;
        }
        PendingMainMenuOnLoginScene = false;
    }

    public static void ClearLoggedInSession()
    {
        LoggedInPlayerName = string.Empty;
        PendingMainMenuOnLoginScene = false;
    }

    public static void OpenMainMenu(string playerName, string playTargetScene)
    {
        SetLoggedInSession(playerName, playTargetScene);
        if (Application.CanStreamedLevelBeLoaded("MainMenuScene")
            && SceneManager.GetActiveScene().name != "MainMenuScene")
        {
            SceneManager.LoadScene("MainMenuScene");
            return;
        }

        GameMenuManager mgr = EnsureInstance();
        mgr.ShowView(ViewMode.MainMenu, ViewMode.None, SettingsTab.Graphics);
    }

    public static bool ConsumePendingMainMenuOnLoginScene()
    {
        bool val = PendingMainMenuOnLoginScene;
        PendingMainMenuOnLoginScene = false;
        return val;
    }

    public static void OpenSettingsOverlay() => OpenSettingsOverlay(SettingsTab.Graphics);

    public static void OpenSettingsOverlay(SettingsTab initialTab)
    {
        GameMenuManager mgr = EnsureInstance();
        mgr.ShowView(ViewMode.Settings, ViewMode.None, initialTab);
    }

    public static void CloseMenu()
    {
        if (Instance != null)
        {
            Instance.HideMenuInternal(restoreGameplay: true);
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        GameSettingsManager.EnsureInitialized();
        EnsureUiBuilt();
        SyncForActiveScene(SceneManager.GetActiveScene().name);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        GameSettingsManager.OnSettingsChanged += HandleSettingsChanged;
        GameAudioManager.OnAudioSettingsChanged += HandleSettingsChanged;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        GameSettingsManager.OnSettingsChanged -= HandleSettingsChanged;
        GameAudioManager.OnAudioSettingsChanged -= HandleSettingsChanged;
        GameSettingsManager.FlushPendingPrefs();
        GameAudioManager.FlushPendingPrefs();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void RefreshCachedSceneInfo(Scene scene)
    {
        cachedSceneHandle = scene.handle;
        cachedSceneName = scene.name;
        cachedIsLoadingScene = cachedSceneName == "LoadingScene";
        cachedIsGameplayScene = cachedSceneName == "Lv1"
            || cachedSceneName == "Lv2"
            || cachedSceneName == "Lv3"
            || StageManager.Instance != null;
    }

    private void EnsureCachedSceneInfo()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.handle != cachedSceneHandle || (!cachedIsGameplayScene && StageManager.Instance != null))
        {
            RefreshCachedSceneInfo(active);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RefreshCachedSceneInfo(scene);
        SyncForActiveScene(cachedSceneName);
    }

    private void SyncForActiveScene(string sceneName)
    {
        EnsureUiBuilt();

        if (isMenuOpen && sceneName != "LoginScene")
        {
            HideMenuInternal(restoreGameplay: false);
        }

        lastShowCorner = -1;
        lastShowFps = -1;
        UpdateCornerAndFpsVisibility(sceneName);
        RefreshSettingsUi();
    }

    private void Update()
    {
        EnsureCachedSceneInfo();
        UpdateCornerAndFpsVisibility(cachedSceneName);
        UpdateFpsCounter();
        UpdateModalScaleAndTransition();
        UpdateFooterToastTimer();

        if (Time.frameCount == suppressEscapeFrame) return;
        if (cachedIsLoadingScene) return;
        if (GameOverManager.IsGameOver)
        {
            if (isMenuOpen) HideMenuInternal(false);
            return;
        }
        if (WeaponShopManager.Instance != null && WeaponShopManager.Instance.IsActualShopOpen) return;
        if (StoryGuideManager.IsAnyModalOpen) return;

        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        if (resolutionPopupObj != null && resolutionPopupObj.activeSelf)
        {
            CloseResolutionPopup();
            return;
        }

        if (isMenuOpen)
        {
            HandleEscapeWhileOpen(cachedSceneName);
        }
        else if (cachedIsGameplayScene)
        {
            GameAudioManager.PlayUiClick();
            ShowView(ViewMode.PauseMenu, ViewMode.None, SettingsTab.Graphics);
        }
    }

    private void UpdateModalScaleAndTransition()
    {
        if (!isMenuOpen || canvasRt == null) return;

        Vector2 size = canvasRt.rect.size;
        if (size.x < 100f || size.y < 100f) return;

        bool animFinished = modalAnimTime >= ModalAnimDuration;
        if (animFinished && size == lastModalCanvasSize && (activeAnimGroup == null || activeAnimGroup.alpha >= 0.999f))
        {
            return;
        }

        lastModalCanvasSize = size;

        float animScale = 1f;
        if (!animFinished)
        {
            modalAnimTime += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(modalAnimTime / ModalAnimDuration));
            if (activeAnimGroup != null) activeAnimGroup.alpha = k;
            animScale = Mathf.Lerp(0.968f, 1f, k);
        }
        else if (activeAnimGroup != null && activeAnimGroup.alpha < 0.999f)
        {
            activeAnimGroup.alpha = 1f;
        }

        if (settingsWindowRt != null && settingsWindowObj.activeSelf)
        {
            float fit = Mathf.Min(1f, Mathf.Min((size.x * 0.94f) / 1320f, (size.y * 0.92f) / 780f));
            settingsWindowRt.localScale = Vector3.one * (fit * animScale);
        }

        if (pauseWindowRt != null && pauseWindowObj.activeSelf)
        {
            float fit = Mathf.Min(1f, Mathf.Min((size.x * 0.92f) / 560f, (size.y * 0.92f) / 660f));
            pauseWindowRt.localScale = Vector3.one * (fit * animScale);
        }
    }

    private void UpdateFooterToastTimer()
    {
        if (toastTimer <= 0f || footerToastText == null) return;

        toastTimer -= Time.unscaledDeltaTime;
        if (toastTimer <= 0f)
        {
            footerToastText.text = string.Empty;
        }
        else if (toastTimer < 0.5f)
        {
            Color c = footerToastText.color;
            c.a = Mathf.Clamp01(toastTimer / 0.5f);
            footerToastText.color = c;
        }
    }

    private void HandleEscapeWhileOpen(string sceneName)
    {
        if (currentView == ViewMode.Settings || currentView == ViewMode.Controls)
        {
            GameAudioManager.PlayUiClick();
            if (returnView != ViewMode.None)
            {
                ShowView(returnView, ViewMode.None, SettingsTab.Graphics);
            }
            else
            {
                HideMenuInternal(restoreGameplay: IsGameplayScene(sceneName));
            }
            return;
        }

        if (currentView == ViewMode.PauseMenu)
        {
            GameAudioManager.PlayUiClick();
            HideMenuInternal(restoreGameplay: true);
        }
    }

    private void UpdateCornerAndFpsVisibility(string sceneName)
    {
        bool isLoading = cachedSceneHandle != int.MinValue ? cachedIsLoadingScene : sceneName == "LoadingScene";
        if (menuCanvas != null && menuCanvas.gameObject.activeSelf == isLoading)
        {
            menuCanvas.gameObject.SetActive(!isLoading);
        }

        bool gameplay = cachedSceneHandle != int.MinValue ? cachedIsGameplayScene : IsGameplayScene(sceneName);
        bool modalBlocking = GameOverManager.IsGameOver
            || (WeaponShopManager.Instance != null && WeaponShopManager.Instance.IsActualShopOpen)
            || StoryGuideManager.IsAnyModalOpen;

        // Nút Cài đặt / Menu góc trên phải CHỈ hiện khi đã vào trong map chiến đấu (Lv1 / Lv2 / Lv3)
        if (cornerMenuBtn != null)
        {
            int showCornerInt = (gameplay && !isMenuOpen && !modalBlocking) ? 1 : 0;
            if (lastShowCorner != showCornerInt)
            {
                lastShowCorner = showCornerInt;
                bool showCorner = showCornerInt == 1;
                if (cornerMenuBtn.gameObject.activeSelf != showCorner)
                {
                    cornerMenuBtn.gameObject.SetActive(showCorner);
                }

                if (showCorner && !lastCornerStyled && cornerMenuRt != null && cornerMenuLabel != null)
                {
                    lastCornerStyled = true;
                    Vector2 targetPos = new Vector2(-22f, -114f);
                    Vector2 targetSize = new Vector2(194f, 40f);
                    string targetLabel = "CÀI ĐẶT [ESC]";

                    if (cornerMenuRt.anchoredPosition != targetPos) cornerMenuRt.anchoredPosition = targetPos;
                    if (cornerMenuRt.sizeDelta != targetSize) cornerMenuRt.sizeDelta = targetSize;
                    if (cornerMenuLabel.text != targetLabel) cornerMenuLabel.text = targetLabel;
                }
            }
        }

        if (fpsBadgeObj != null)
        {
            int showFpsInt = (!isLoading && GameSettingsManager.ShowFpsCounter) ? 1 : 0;
            if (lastShowFps != showFpsInt)
            {
                lastShowFps = showFpsInt;
                bool showFps = showFpsInt == 1;
                if (fpsBadgeObj.activeSelf != showFps)
                {
                    fpsBadgeObj.SetActive(showFps);
                    fpsAccumTime = 0f;
                    fpsFrameCount = 0;
                }
            }
        }
    }

    private void UpdateFpsCounter()
    {
        if (fpsBadgeObj == null || !fpsBadgeObj.activeSelf || fpsBadgeText == null) return;

        fpsAccumTime += Time.unscaledDeltaTime;
        fpsFrameCount++;

        if (fpsAccumTime >= 0.4f)
        {
            int fps = Mathf.RoundToInt(fpsFrameCount / Mathf.Max(0.001f, fpsAccumTime));
            string gfxLabel = GameSettingsManager.GetGraphicsPresetLabel(GameSettingsManager.CurrentGraphicsPreset);
            fpsBadgeText.text = $"FPS: {fps}  •  {gfxLabel}";
            fpsAccumTime = 0f;
            fpsFrameCount = 0;
        }
    }

    private static bool IsGameplayScene(string sceneName)
    {
        return sceneName == "Lv1" || sceneName == "Lv2" || sceneName == "Lv3" || StageManager.Instance != null;
    }

    private void ShowView(ViewMode mode, ViewMode backTarget, SettingsTab tab)
    {
        EnsureUiBuilt();
        if (menuCanvas != null && !menuCanvas.gameObject.activeSelf)
        {
            menuCanvas.gameObject.SetActive(true);
        }

        if (mode == ViewMode.Controls)
        {
            mode = ViewMode.Settings;
            tab = SettingsTab.Controls;
        }

        currentView = mode;
        returnView = backTarget;
        isMenuOpen = true;

        bool isGameplay = IsGameplayScene(SceneManager.GetActiveScene().name);
        if (isGameplay)
        {
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        CloseResolutionPopup();
        if (backdropObj != null) backdropObj.SetActive(true);

        bool showSettings = mode == ViewMode.Settings;
        if (settingsWindowObj != null) settingsWindowObj.SetActive(showSettings);
        if (pauseWindowObj != null) pauseWindowObj.SetActive(!showSettings);

        if (showSettings)
        {
            activeAnimGroup = settingsCanvasGroup;
            SelectSettingsTab(tab, playSound: false);
            if (settingsBannerSubText != null)
            {
                settingsBannerSubText.text = string.IsNullOrEmpty(LoggedInPlayerName)
                    ? "Tùy chỉnh Đồ họa, Âm thanh, Màn hình & Điều khiển  •  VANGUARD"
                    : $"Chiến binh: {LoggedInPlayerName.ToUpperInvariant()}  •  VANGUARD — CUỘC TRUY QUÉT RỪNG ORC";
            }
        }
        else
        {
            activeAnimGroup = pauseCanvasGroup;

            if (mainMenuContent != null) mainMenuContent.SetActive(mode == ViewMode.MainMenu);
            if (pauseMenuContent != null) pauseMenuContent.SetActive(mode == ViewMode.PauseMenu);

            if (pauseBannerTitleText != null)
            {
                pauseBannerTitleText.text = mode == ViewMode.MainMenu ? "MENU CHÍNH" : "TẠM DỪNG";
            }
            if (pauseBannerSubText != null)
            {
                pauseBannerSubText.text = mode == ViewMode.MainMenu
                    ? (string.IsNullOrEmpty(LoggedInPlayerName)
                        ? "VANGUARD — CUỘC TRUY QUÉT RỪNG ORC"
                        : $"CHIẾN BINH: {LoggedInPlayerName.ToUpperInvariant()}  •  CUỘC TRUY QUÉT RỪNG ORC")
                    : "Tạm dừng trận đấu  •  Nhấn [ESC] để tiếp tục";
            }
        }

        if (activeAnimGroup != null) activeAnimGroup.alpha = 0f;
        modalAnimTime = 0f;
        lastModalCanvasSize = Vector2.zero;

        if (footerToastText != null) footerToastText.text = string.Empty;
        toastTimer = 0f;

        RefreshSettingsUi();
    }

    private void HideMenuInternal(bool restoreGameplay)
    {
        isMenuOpen = false;
        currentView = ViewMode.None;
        returnView = ViewMode.None;
        CloseResolutionPopup();
        GameSettingsManager.FlushPendingPrefs();
        GameAudioManager.FlushPendingPrefs();

        if (backdropObj != null) backdropObj.SetActive(false);
        if (settingsWindowObj != null) settingsWindowObj.SetActive(false);
        if (pauseWindowObj != null) pauseWindowObj.SetActive(false);

        if (restoreGameplay && IsGameplayScene(SceneManager.GetActiveScene().name))
        {
            bool otherModalOpen = GameOverManager.IsGameOver
                || StoryGuideManager.IsAnyModalOpen
                || (WeaponShopManager.Instance != null && WeaponShopManager.Instance.IsActualShopOpen);

            if (!otherModalOpen)
            {
                Time.timeScale = 1f;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    private void SelectSettingsTab(SettingsTab tab, bool playSound)
    {
        if (playSound) GameAudioManager.PlayUiClick();
        activeTab = tab;
        CloseResolutionPopup();

        for (int i = 0; i < 4; i++)
        {
            bool active = (int)tab == i;
            if (tabPages[i] != null) tabPages[i].SetActive(active);

            if (tabButtons[i] != null)
            {
                MedievalSettingsArt.UpdateFramedButtonColors(
                    tabButtons[i],
                    active ? MedievalSettingsArt.TabActiveFill : MedievalSettingsArt.DarkSlateFill,
                    active ? MedievalSettingsArt.HeroGoldBorder : MedievalSettingsArt.BronzeBorder,
                    active ? MedievalSettingsArt.TitleGoldTop : MedievalSettingsArt.IvoryText);
            }
        }

        RefreshSettingsUi();
    }

    private void HandleSettingsChanged()
    {
        if (uiBuilt && isMenuOpen) RefreshSettingsUi();
    }

    private void RefreshSettingsUi()
    {
        if (!uiBuilt) return;

        if (sidebarWarriorText != null)
        {
            sidebarWarriorText.text = string.IsNullOrEmpty(LoggedInPlayerName)
                ? "VANGUARD"
                : LoggedInPlayerName.ToUpperInvariant();
        }

        if (resolutionValueText != null)
        {
            resolutionValueText.text = $"{GameSettingsManager.ResolutionWidth} x {GameSettingsManager.ResolutionHeight}";
        }

        DisplayModeOption disp = GameSettingsManager.CurrentDisplayMode;
        ApplySegmentedStyle(dispFullscreenBtn, disp == DisplayModeOption.Fullscreen);
        ApplySegmentedStyle(dispWindowedBtn, disp == DisplayModeOption.Windowed);
        ApplySegmentedStyle(dispBorderlessBtn, disp == DisplayModeOption.Borderless);

        GraphicsPreset preset = GameSettingsManager.CurrentGraphicsPreset;
        ApplySegmentedStyle(gfxLowBtn, preset == GraphicsPreset.Low);
        ApplySegmentedStyle(gfxMedBtn, preset == GraphicsPreset.Medium);
        ApplySegmentedStyle(gfxHighBtn, preset == GraphicsPreset.High);
        if (gfxDescText != null)
        {
            gfxDescText.text = GameSettingsManager.GetGraphicsPresetDescription(preset);
        }

        ApplyPillToggleStyle(vsyncToggleBtn, GameSettingsManager.VSyncEnabled);
        ApplyPillToggleStyle(fpsToggleBtn, GameSettingsManager.ShowFpsCounter);

        bool masterOn = !GameAudioManager.IsMasterMuted;
        float masterVol = GameAudioManager.MasterVolume;
        ApplyPillToggleStyle(masterToggleBtn, masterOn);
        if (masterSlider != null) masterSlider.SetValueWithoutNotify(masterVol);
        if (masterValText != null) masterValText.text = masterOn ? $"{Mathf.RoundToInt(masterVol * 100f)}%" : "TẮT";

        bool bgmOn = !GameAudioManager.IsBgmMuted;
        float bgmVol = GameAudioManager.BgmVolume;
        ApplyPillToggleStyle(bgmToggleBtn, bgmOn);
        if (bgmSlider != null) bgmSlider.SetValueWithoutNotify(bgmVol);
        if (bgmValText != null) bgmValText.text = bgmOn ? $"{Mathf.RoundToInt(bgmVol * 100f)}%" : "TẮT";

        bool sfxOn = !GameAudioManager.IsSfxMuted;
        float sfxVol = GameAudioManager.SfxVolume;
        ApplyPillToggleStyle(sfxToggleBtn, sfxOn);
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(sfxVol);
        if (sfxValText != null) sfxValText.text = sfxOn ? $"{Mathf.RoundToInt(sfxVol * 100f)}%" : "TẮT";

        float sens = GameSettingsManager.MouseSensitivityMultiplier;
        if (sensitivitySlider != null) sensitivitySlider.SetValueWithoutNotify(sens);
        if (sensitivityValText != null) sensitivityValText.text = $"{sens:0.0}x";

        if (sysGfxValueText != null)
        {
            sysGfxValueText.text = $"{GameSettingsManager.GetGraphicsPresetLabel(preset)}\nVSync: {(GameSettingsManager.VSyncEnabled ? "BẬT" : "TẮT")}  •  FPS: {(GameSettingsManager.ShowFpsCounter ? "HIỆN" : "ẨN")}";
        }
        if (sysDisplayValueText != null)
        {
            string modeLabel = disp switch
            {
                DisplayModeOption.Fullscreen => "TOÀN MÀN HÌNH",
                DisplayModeOption.Borderless => "KHÔNG VIỀN",
                _ => "CỬA SỔ"
            };
            sysDisplayValueText.text = $"{GameSettingsManager.ResolutionWidth} x {GameSettingsManager.ResolutionHeight}\n{modeLabel}";
        }
        if (sysAudioValueText != null)
        {
            string mStr = masterOn ? $"{Mathf.RoundToInt(masterVol * 100f)}%" : "TẮT";
            string bStr = bgmOn ? $"{Mathf.RoundToInt(bgmVol * 100f)}%" : "TẮT";
            string sStr = sfxOn ? $"{Mathf.RoundToInt(sfxVol * 100f)}%" : "TẮT";
            sysAudioValueText.text = $"Tổng: {mStr}  •  Nhạc: {bStr}\nHiệu ứng: {sStr}";
        }
        if (sysAccountValueText != null)
        {
            string warrior = string.IsNullOrEmpty(LoggedInPlayerName) ? "Khách" : LoggedInPlayerName;
            sysAccountValueText.text = $"Chiến binh: {warrior}\nNgôn ngữ: Tiếng Việt  •  Độ nhạy: {sens:0.0}x";
        }
    }

    // ==================== ACTIONS ====================

    private void OnCornerButtonClicked()
    {
        GameAudioManager.PlayUiClick();
        ShowView(ViewMode.PauseMenu, ViewMode.None, SettingsTab.Graphics);
    }

    private void ToggleResolutionPopup()
    {
        GameAudioManager.PlayUiClick();
        if (resolutionPopupObj == null) return;

        bool nextState = !resolutionPopupObj.activeSelf;
        if (nextState)
        {
            PopulateResolutionPopup();
            if (resolutionDropdownBtn != null && settingsWindowRt != null && resolutionPopupRt != null)
            {
                Canvas.ForceUpdateCanvases();
                RectTransform btnRt = resolutionDropdownBtn.GetComponent<RectTransform>();
                Vector3[] corners = new Vector3[4];
                btnRt.GetWorldCorners(corners);
                Vector3 localBR = settingsWindowRt.InverseTransformPoint(corners[3]);
                resolutionPopupRt.anchorMin = new Vector2(0.5f, 0.5f);
                resolutionPopupRt.anchorMax = new Vector2(0.5f, 0.5f);
                resolutionPopupRt.pivot = new Vector2(1f, 1f);
                resolutionPopupRt.anchoredPosition = new Vector2(localBR.x, localBR.y - 4f);
            }
        }
        resolutionPopupObj.SetActive(nextState);
        if (resolutionDismissLayer != null) resolutionDismissLayer.SetActive(nextState);
    }

    private void CloseResolutionPopup()
    {
        if (resolutionPopupObj != null) resolutionPopupObj.SetActive(false);
        if (resolutionDismissLayer != null) resolutionDismissLayer.SetActive(false);
    }

    private void PopulateResolutionPopup()
    {
        if (resolutionListRoot == null) return;

        for (int i = resolutionListRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(resolutionListRoot.GetChild(i).gameObject);
        }

        List<ResolutionOption> options = GameSettingsManager.GetSupportedResolutions();
        int visibleCount = Mathf.Min(options.Count, 7);
        float rowH = 38f;
        float spacing = 6f;
        float pad = 20f;
        float popupH = pad + visibleCount * rowH + Mathf.Max(0, visibleCount - 1) * spacing;

        if (resolutionPopupRt != null)
        {
            MedievalSettingsArt.SetFixedSize(resolutionPopupObj, 380f, popupH);
        }

        for (int i = 0; i < visibleCount; i++)
        {
            ResolutionOption opt = options[i];
            bool selected = opt.width == GameSettingsManager.ResolutionWidth && opt.height == GameSettingsManager.ResolutionHeight;
            int wCap = opt.width;
            int hCap = opt.height;

            MedievalSettingsArt.CreateFramedButton(
                resolutionListRoot, "ResOpt_" + opt.Label, $"{opt.width} x {opt.height}",
                356f, rowH, 16.5f,
                MedievalSettingsArt.PanelClean,
                selected ? MedievalSettingsArt.ActiveGoldFill : MedievalSettingsArt.DarkSlateFill,
                MedievalSettingsArt.BorderClean,
                selected ? MedievalSettingsArt.HeroGoldBorder : MedievalSettingsArt.BronzeBorder,
                selected ? MedievalSettingsArt.DarkInkText : MedievalSettingsArt.IvoryText,
                selected ? MedievalSettingsArt.IconCheck : null,
                () =>
                {
                    GameAudioManager.PlayUiClick();
                    GameSettingsManager.SetResolution(wCap, hCap);
                    CloseResolutionPopup();
                });
        }
    }

    private void OnApplySettingsClicked()
    {
        GameAudioManager.PlayWeaponEquip();
        GameSettingsManager.ApplyAndSaveAll();
        ShowFooterToast("✓  Đã áp dụng & lưu toàn bộ cài đặt!", new Color(0.54f, 0.96f, 0.56f, 1f));
    }

    private void OnDefaultSettingsClicked()
    {
        GameAudioManager.PlayUiClick();
        GameSettingsManager.ResetAllDefaults();
        ShowFooterToast("◆  Đã khôi phục cấu hình mặc định!", MedievalSettingsArt.BrightGoldText);
    }

    private void ShowFooterToast(string message, Color color)
    {
        if (footerToastText == null) return;
        footerToastText.text = message;
        footerToastText.color = color;
        toastTimer = 2.6f;
    }

    private void OnPlayFromMainMenuClicked()
    {
        GameAudioManager.PlayWeaponEquip();
        string target = string.IsNullOrWhiteSpace(MainMenuPlayTargetScene) ? "PickCharacter" : MainMenuPlayTargetScene;
        if (!Application.CanStreamedLevelBeLoaded(target)) target = "Lv1";
        HideMenuInternal(restoreGameplay: false);
        Time.timeScale = 1f;
        LoadingController.LoadScene(target);
    }

    private void OnResumeGameClicked()
    {
        GameAudioManager.PlayUiClick();
        HideMenuInternal(restoreGameplay: true);
    }

    private static void CloseActiveGameplaySession(string result = "Quit")
    {
        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackGameEnded(result);
        }

        PlayerPrefs.DeleteKey("SessionId");
        PlayerPrefs.Save();
    }

    private void OnRestartStageClicked()
    {
        GameAudioManager.PlayUiClick();
        CloseActiveGameplaySession("Quit");
        string currentScene = SceneManager.GetActiveScene().name;
        HideMenuInternal(restoreGameplay: false);
        Time.timeScale = 1f;
        LoadingController.LoadScene(currentScene);
    }

    private void OnGoToCharacterSelectClicked()
    {
        GameAudioManager.PlayUiClick();
        CloseActiveGameplaySession("Quit");
        HideMenuInternal(restoreGameplay: false);
        Time.timeScale = 1f;
        if (Application.CanStreamedLevelBeLoaded("PickCharacter"))
        {
            LoadingController.LoadScene("PickCharacter");
        }
    }

    private void OnReturnToMainMenuClicked()
    {
        GameAudioManager.PlayUiClick();
        CloseActiveGameplaySession("Quit");
        HideMenuInternal(restoreGameplay: false);
        Time.timeScale = 1f;
        if (Application.CanStreamedLevelBeLoaded("MainMenuScene"))
        {
            LoadingController.LoadScene("MainMenuScene");
        }
        else
        {
            PendingMainMenuOnLoginScene = true;
            LoadingController.LoadScene("LoginScene");
        }
    }

    private void OnLogoutClicked()
    {
        GameAudioManager.PlayUiClick();
        CloseActiveGameplaySession("Quit");
        PlayerPrefs.DeleteKey("PlayerId");
        PlayerPrefs.DeleteKey("PlayerName");
        PlayerPrefs.DeleteKey("SessionId");
        PlayerPrefs.DeleteKey("PendingLoginEvent");
        PlayerPrefs.Save();
        ClearLoggedInSession();
        HideMenuInternal(restoreGameplay: false);
        Time.timeScale = 1f;

        AuthUIController authUi = FindFirstObjectByType<AuthUIController>();
        if (authUi != null)
        {
            authUi.ReturnToLoginForm();
        }
        else if (Application.CanStreamedLevelBeLoaded("LoginScene"))
        {
            SceneManager.LoadScene("LoginScene");
        }
    }

    private void OnQuitGameClicked()
    {
        if (isQuitting) return;
        isQuitting = true;

        GameAudioManager.PlayUiClick();
        Time.timeScale = 1f;

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.EndSessionAndQuit("Quit", PerformApplicationQuit);
        }
        else
        {
            PerformApplicationQuit();
        }
    }

    private static void PerformApplicationQuit()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnSubViewBackClicked()
    {
        GameAudioManager.PlayUiClick();
        CloseResolutionPopup();
        if (returnView != ViewMode.None)
        {
            ShowView(returnView, ViewMode.None, SettingsTab.Graphics);
        }
        else
        {
            HideMenuInternal(restoreGameplay: IsGameplayScene(SceneManager.GetActiveScene().name));
        }
    }

    // ==================== UI BUILDER ====================

    private void EnsureUiBuilt()
    {
        if (uiBuilt && menuCanvas != null) return;
        uiBuilt = true;

        GameObject canvasGo = new GameObject("GameMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        menuCanvas = canvasGo.GetComponent<Canvas>();
        menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        menuCanvas.sortingOrder = 500;
        FantasyUiThemeHelper.ConfigureResponsiveCanvas(menuCanvas);
        canvasRt = canvasGo.GetComponent<RectTransform>();

        BuildCornerAndFpsOverlay(canvasGo.transform);

        backdropObj = new GameObject("MenuBackdrop", typeof(RectTransform), typeof(Image));
        backdropObj.transform.SetParent(canvasGo.transform, false);
        FantasyUiThemeHelper.StretchFull(backdropObj.GetComponent<RectTransform>());
        Image bdImg = backdropObj.GetComponent<Image>();
        bdImg.color = MedievalSettingsArt.BackdropDark;
        bdImg.raycastTarget = true;

        BuildSettingsWindow(canvasGo.transform);
        BuildPauseWindow(canvasGo.transform);

        backdropObj.SetActive(false);
        settingsWindowObj.SetActive(false);
        pauseWindowObj.SetActive(false);
    }

    private void BuildCornerAndFpsOverlay(Transform canvasRoot)
    {
        cornerMenuBtn = MedievalSettingsArt.CreateFramedButton(
            canvasRoot, "CornerMenuBtn", "CÀI ĐẶT [ESC]", 194f, 40f, 14f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.BronzeFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.GoldOrnament,
            MedievalSettingsArt.BrightGoldText, MedievalSettingsArt.IconGear, OnCornerButtonClicked);

        cornerMenuRt = cornerMenuBtn.GetComponent<RectTransform>();
        cornerMenuRt.anchorMin = new Vector2(1f, 1f);
        cornerMenuRt.anchorMax = new Vector2(1f, 1f);
        cornerMenuRt.pivot = new Vector2(1f, 1f);
        cornerMenuRt.anchoredPosition = new Vector2(-22f, -114f);
        cornerMenuLabel = cornerMenuBtn.transform.Find("Text").GetComponent<TMP_Text>();
        cornerMenuBtn.gameObject.SetActive(false);

        GameObject fpsGo = MedievalSettingsArt.CreateFramedPanel(
            canvasRoot, "FpsCounterBadge", 210f, 32f,
            MedievalSettingsArt.PanelClean, new Color(0.05f, 0.045f, 0.042f, 0.88f),
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.BronzeBorder);
        fpsBadgeObj = fpsGo;
        RectTransform fpsRt = fpsGo.GetComponent<RectTransform>();
        fpsRt.anchorMin = new Vector2(1f, 0f);
        fpsRt.anchorMax = new Vector2(1f, 0f);
        fpsRt.pivot = new Vector2(1f, 0f);
        fpsRt.anchoredPosition = new Vector2(-22f, 18f);

        fpsBadgeText = FantasyUiThemeHelper.CreateText(fpsGo.transform, "Text", "FPS: 60  •  CAO",
            13.5f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.Center);
        FantasyUiThemeHelper.StretchFull(fpsBadgeText.rectTransform);
        fpsBadgeText.textWrappingMode = TextWrappingModes.NoWrap;
        fpsBadgeObj.SetActive(false);
    }

    private void BuildSettingsWindow(Transform canvasRoot)
    {
        settingsWindowObj = MedievalSettingsArt.CreateFramedPanel(
            canvasRoot, "SettingsWindow", SettingsWindowW, SettingsWindowH,
            MedievalSettingsArt.PanelGothic, MedievalSettingsArt.FrameObsidian,
            MedievalSettingsArt.BorderGothic, MedievalSettingsArt.GoldOrnament,
            raycastTarget: true);
        settingsWindowRt = settingsWindowObj.GetComponent<RectTransform>();
        settingsWindowRt.anchorMin = new Vector2(0.5f, 0.5f);
        settingsWindowRt.anchorMax = new Vector2(0.5f, 0.5f);
        settingsWindowRt.pivot = new Vector2(0.5f, 0.5f);
        settingsWindowRt.anchoredPosition = Vector2.zero;
        settingsCanvasGroup = settingsWindowObj.AddComponent<CanvasGroup>();

        // HEADER TIÊU ĐỀ (vẽ cánh hoa văn đối xứng Kenney hai bên chữ GAME SETTINGS)
        GameObject headerArea = new GameObject("SettingsHeader", typeof(RectTransform));
        headerArea.transform.SetParent(settingsWindowObj.transform, false);
        RectTransform haRt = headerArea.GetComponent<RectTransform>();
        haRt.anchorMin = new Vector2(0f, 1f);
        haRt.anchorMax = new Vector2(1f, 1f);
        haRt.pivot = new Vector2(0.5f, 1f);
        haRt.anchoredPosition = new Vector2(0f, -20f);
        haRt.sizeDelta = new Vector2(-80f, 88f);

        GameObject titleRow = new GameObject("TitleRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        titleRow.transform.SetParent(headerArea.transform, false);
        RectTransform trRt = titleRow.GetComponent<RectTransform>();
        trRt.anchorMin = new Vector2(0.5f, 1f);
        trRt.anchorMax = new Vector2(0.5f, 1f);
        trRt.pivot = new Vector2(0.5f, 1f);
        trRt.anchoredPosition = Vector2.zero;
        trRt.sizeDelta = new Vector2(920f, 44f);
        HorizontalLayoutGroup trHlg = titleRow.GetComponent<HorizontalLayoutGroup>();
        trHlg.spacing = 18f;
        trHlg.childAlignment = TextAnchor.MiddleCenter;
        trHlg.childControlWidth = true;
        trHlg.childControlHeight = true;
        trHlg.childForceExpandWidth = false;
        trHlg.childForceExpandHeight = false;

        GameObject leftWing = new GameObject("HeaderLeftWing", typeof(RectTransform), typeof(Image));
        leftWing.transform.SetParent(titleRow.transform, false);
        MedievalSettingsArt.SetFixedSize(leftWing, 220f, 28f);
        Image lwImg = leftWing.GetComponent<Image>();
        lwImg.sprite = MedievalSettingsArt.DividerFadeOrnate;
        lwImg.color = MedievalSettingsArt.GoldOrnament;
        lwImg.raycastTarget = false;

        TMP_Text titleTxt = FantasyUiThemeHelper.CreateText(titleRow.transform, "TitleText", "CÀI ĐẶT GAME",
            34f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        titleTxt.textWrappingMode = TextWrappingModes.NoWrap;
        titleTxt.characterSpacing = 5f;
        MedievalSettingsArt.ApplyTitleGoldGradient(titleTxt);
        AddStrongTextShadow(titleTxt);
        MedievalSettingsArt.SetFixedSize(titleTxt.gameObject, 420f, 44f);

        GameObject rightWing = new GameObject("HeaderRightWing", typeof(RectTransform), typeof(Image));
        rightWing.transform.SetParent(titleRow.transform, false);
        MedievalSettingsArt.SetFixedSize(rightWing, 220f, 28f);
        Image rwImg = rightWing.GetComponent<Image>();
        rwImg.sprite = MedievalSettingsArt.DividerFadeOrnate;
        rwImg.color = MedievalSettingsArt.GoldOrnament;
        rwImg.raycastTarget = false;
        rightWing.transform.localScale = new Vector3(-1f, 1f, 1f);

        settingsBannerSubText = FantasyUiThemeHelper.CreateText(headerArea.transform, "SubtitleText",
            "Tùy chỉnh Đồ họa, Âm thanh, Màn hình & Điều khiển  •  VANGUARD",
            14.5f, FontStyles.Normal, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Center);
        RectTransform sRt = settingsBannerSubText.rectTransform;
        sRt.anchorMin = new Vector2(0.5f, 1f);
        sRt.anchorMax = new Vector2(0.5f, 1f);
        sRt.pivot = new Vector2(0.5f, 1f);
        sRt.anchoredPosition = new Vector2(0f, -46f);
        sRt.sizeDelta = new Vector2(960f, 22f);
        settingsBannerSubText.textWrappingMode = TextWrappingModes.NoWrap;

        GameObject headDiv = MedievalSettingsArt.CreateSymmetricalDivider(
            headerArea.transform, "HeaderBottomDivider", 1140f, 12f, MedievalSettingsArt.GoldOrnament, ornate: false);
        RectTransform hdRt = headDiv.GetComponent<RectTransform>();
        hdRt.anchorMin = new Vector2(0.5f, 0f);
        hdRt.anchorMax = new Vector2(0.5f, 0f);
        hdRt.pivot = new Vector2(0.5f, 0f);
        hdRt.anchoredPosition = Vector2.zero;

        // VÙNG THÂN 2 CỘT (1200 x 600)
        GameObject innerBody = new GameObject("InnerBody", typeof(RectTransform));
        innerBody.transform.SetParent(settingsWindowObj.transform, false);
        RectTransform ibRt = innerBody.GetComponent<RectTransform>();
        ibRt.anchorMin = Vector2.zero;
        ibRt.anchorMax = Vector2.one;
        ibRt.offsetMin = new Vector2(40f, 28f);
        ibRt.offsetMax = new Vector2(-40f, -112f);

        // --- CỘT TRÁI: SIDEBAR DANH MỤC (252 x 600) ---
        GameObject sidebar = MedievalSettingsArt.CreateFramedPanel(
            innerBody.transform, "SidebarPanel", 252f, 600f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.InnerObsidian,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.BronzeBorder);
        RectTransform sbRt = sidebar.GetComponent<RectTransform>();
        sbRt.anchorMin = new Vector2(0f, 0f);
        sbRt.anchorMax = new Vector2(0f, 1f);
        sbRt.pivot = new Vector2(0f, 0.5f);
        sbRt.anchoredPosition = Vector2.zero;
        sbRt.sizeDelta = new Vector2(252f, 0f);

        GameObject tabList = new GameObject("TabList", typeof(RectTransform), typeof(VerticalLayoutGroup));
        tabList.transform.SetParent(sidebar.transform, false);
        RectTransform tlRt = tabList.GetComponent<RectTransform>();
        tlRt.anchorMin = new Vector2(0f, 1f);
        tlRt.anchorMax = new Vector2(1f, 1f);
        tlRt.pivot = new Vector2(0.5f, 1f);
        tlRt.anchoredPosition = new Vector2(0f, -18f);
        tlRt.sizeDelta = new Vector2(-24f, 320f);

        VerticalLayoutGroup tlVlg = tabList.GetComponent<VerticalLayoutGroup>();
        tlVlg.spacing = 12f;
        tlVlg.childAlignment = TextAnchor.UpperCenter;
        tlVlg.childControlWidth = true;
        tlVlg.childControlHeight = true;
        tlVlg.childForceExpandWidth = false;
        tlVlg.childForceExpandHeight = false;

        tabButtons[0] = MedievalSettingsArt.CreateFramedButton(
            tabList.transform, "Tab_Graphics", "ĐỒ HỌA", 224f, 54f, 18f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.TabActiveFill,
            MedievalSettingsArt.BorderKnotted, MedievalSettingsArt.HeroGoldBorder,
            MedievalSettingsArt.TitleGoldTop, MedievalSettingsArt.IconGear,
            () => SelectSettingsTab(SettingsTab.Graphics, true), alignLeft: true);

        tabButtons[1] = MedievalSettingsArt.CreateFramedButton(
            tabList.transform, "Tab_Audio", "ÂM THANH", 224f, 54f, 18f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderKnotted, MedievalSettingsArt.BronzeBorder,
            MedievalSettingsArt.IvoryText, MedievalSettingsArt.IconAudioOn,
            () => SelectSettingsTab(SettingsTab.Audio, true), alignLeft: true);

        tabButtons[2] = MedievalSettingsArt.CreateFramedButton(
            tabList.transform, "Tab_Controls", "ĐIỀU KHIỂN", 224f, 54f, 18f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderKnotted, MedievalSettingsArt.BronzeBorder,
            MedievalSettingsArt.IvoryText, MedievalSettingsArt.IconControls,
            () => SelectSettingsTab(SettingsTab.Controls, true), alignLeft: true);

        tabButtons[3] = MedievalSettingsArt.CreateFramedButton(
            tabList.transform, "Tab_System", "HỆ THỐNG", 224f, 54f, 18f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderKnotted, MedievalSettingsArt.BronzeBorder,
            MedievalSettingsArt.IvoryText, MedievalSettingsArt.IconSystem,
            () => SelectSettingsTab(SettingsTab.System, true), alignLeft: true);

        // Thẻ nhỏ đáy Sidebar
        GameObject sbDiv = MedievalSettingsArt.CreateSymmetricalDivider(
            sidebar.transform, "SidebarDivider", 210f, 12f, MedievalSettingsArt.BronzeBorder, ornate: false);
        RectTransform sbdRt = sbDiv.GetComponent<RectTransform>();
        sbdRt.anchorMin = new Vector2(0.5f, 0f);
        sbdRt.anchorMax = new Vector2(0.5f, 0f);
        sbdRt.pivot = new Vector2(0.5f, 0.5f);
        sbdRt.anchoredPosition = new Vector2(0f, 88f);

        sidebarWarriorText = FantasyUiThemeHelper.CreateText(sidebar.transform, "SidebarWarrior", "VANGUARD",
            15f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.Center);
        RectTransform swRt = sidebarWarriorText.rectTransform;
        swRt.anchorMin = new Vector2(0f, 0f);
        swRt.anchorMax = new Vector2(1f, 0f);
        swRt.pivot = new Vector2(0.5f, 0f);
        swRt.anchoredPosition = new Vector2(0f, 48f);
        swRt.sizeDelta = new Vector2(-20f, 24f);
        sidebarWarriorText.textWrappingMode = TextWrappingModes.NoWrap;
        sidebarWarriorText.overflowMode = TextOverflowModes.Ellipsis;

        TMP_Text escHint = FantasyUiThemeHelper.CreateText(sidebar.transform, "SidebarHint", "Nhấn [ESC] để đóng",
            13.5f, FontStyles.Italic, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Center);
        RectTransform shRt = escHint.rectTransform;
        shRt.anchorMin = new Vector2(0f, 0f);
        shRt.anchorMax = new Vector2(1f, 0f);
        shRt.pivot = new Vector2(0.5f, 0f);
        shRt.anchoredPosition = new Vector2(0f, 22f);
        shRt.sizeDelta = new Vector2(-20f, 20f);
        escHint.textWrappingMode = TextWrappingModes.NoWrap;

        // --- CỘT PHẢI: CONTENT SURFACE + FOOTER (932 x 600) ---
        GameObject rightCol = new GameObject("RightColumn", typeof(RectTransform));
        rightCol.transform.SetParent(innerBody.transform, false);
        RectTransform rcRt = rightCol.GetComponent<RectTransform>();
        rcRt.anchorMin = Vector2.zero;
        rcRt.anchorMax = Vector2.one;
        rcRt.offsetMin = new Vector2(268f, 0f);
        rcRt.offsetMax = Vector2.zero;

        GameObject contentSurface = MedievalSettingsArt.CreateFramedPanel(
            rightCol.transform, "ContentSurface", 932f, 518f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.InnerObsidian,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.BronzeBorder);
        RectTransform csRt = contentSurface.GetComponent<RectTransform>();
        csRt.anchorMin = Vector2.zero;
        csRt.anchorMax = Vector2.one;
        csRt.offsetMin = new Vector2(0f, 82f);
        csRt.offsetMax = Vector2.zero;

        GameObject pagesRoot = new GameObject("PagesRoot", typeof(RectTransform));
        pagesRoot.transform.SetParent(contentSurface.transform, false);
        RectTransform prRt = pagesRoot.GetComponent<RectTransform>();
        prRt.anchorMin = Vector2.zero;
        prRt.anchorMax = Vector2.one;
        prRt.offsetMin = new Vector2(26f, 16f);
        prRt.offsetMax = new Vector2(-26f, -16f);

        tabPages[0] = BuildGraphicsTab(pagesRoot.transform);
        tabPages[1] = BuildAudioTab(pagesRoot.transform);
        tabPages[2] = BuildControlsTab(pagesRoot.transform);
        tabPages[3] = BuildSystemTab(pagesRoot.transform);

        BuildSettingsFooter(rightCol.transform);
        BuildResolutionPopupOverlay(settingsWindowObj.transform);
    }

    private GameObject BuildGraphicsTab(Transform parent)
    {
        GameObject page = CreateSettingsPage("Page_Graphics", parent, 8f);

        // 1. Resolution
        GameObject resRow = CreateSettingsRow(page.transform, "Row_Resolution", 58f);
        CreateRowTitleColumn(resRow.transform, "ĐỘ PHÂN GIẢI", "Độ phân giải màn hình", 246f);
        CreateFlexibleSpacer(resRow.transform);

        resolutionDropdownBtn = MedievalSettingsArt.CreateFramedButton(
            resRow.transform, "BtnResolutionDropdown", "1920 x 1080", 380f, 44f, 18f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.BronzeBorder,
            MedievalSettingsArt.IvoryText, null, ToggleResolutionPopup);
        resolutionValueText = resolutionDropdownBtn.transform.Find("Text").GetComponent<TMP_Text>();
        resolutionValueText.rectTransform.offsetMax = new Vector2(-42f, 0f);

        GameObject arrowGo = new GameObject("ArrowIcon", typeof(RectTransform), typeof(Image));
        arrowGo.transform.SetParent(resolutionDropdownBtn.transform, false);
        RectTransform aRt = arrowGo.GetComponent<RectTransform>();
        aRt.anchorMin = new Vector2(1f, 0.5f);
        aRt.anchorMax = new Vector2(1f, 0.5f);
        aRt.pivot = new Vector2(1f, 0.5f);
        aRt.anchoredPosition = new Vector2(-14f, 0f);
        aRt.sizeDelta = new Vector2(20f, 20f);
        Image aImg = arrowGo.GetComponent<Image>();
        aImg.sprite = MedievalSettingsArt.IconDown;
        aImg.color = MedievalSettingsArt.BrightGoldText;
        aImg.preserveAspect = true;
        aImg.raycastTarget = false;

        CreateRowDivider(page.transform);

        // 2. Display Mode (Fullscreen / Windowed / Borderless)
        GameObject dispRow = CreateSettingsRow(page.transform, "Row_DisplayMode", 58f);
        CreateRowTitleColumn(dispRow.transform, "CHẾ ĐỘ HIỂN THỊ", "Chế độ khung hình", 246f);
        CreateFlexibleSpacer(dispRow.transform);

        GameObject dispGroup = CreateHorizontalGroup(dispRow.transform, "DisplaySegGroup", 12f, 576f, 46f);
        dispFullscreenBtn = CreateSegmentedButton(dispGroup.transform, "BtnFullscreen", "TOÀN MÀN HÌNH", 184f, 44f,
            () => { GameAudioManager.PlayUiClick(); GameSettingsManager.SetDisplayMode(DisplayModeOption.Fullscreen); });
        dispWindowedBtn = CreateSegmentedButton(dispGroup.transform, "BtnWindowed", "CỬA SỔ", 184f, 44f,
            () => { GameAudioManager.PlayUiClick(); GameSettingsManager.SetDisplayMode(DisplayModeOption.Windowed); });
        dispBorderlessBtn = CreateSegmentedButton(dispGroup.transform, "BtnBorderless", "KHÔNG VIỀN", 184f, 44f,
            () => { GameAudioManager.PlayUiClick(); GameSettingsManager.SetDisplayMode(DisplayModeOption.Borderless); });

        CreateRowDivider(page.transform);

        // 3. Graphics Quality (LOW / MEDIUM / HIGH)
        GameObject gfxWrap = new GameObject("Row_GraphicsQualityWrap", typeof(RectTransform), typeof(VerticalLayoutGroup));
        gfxWrap.transform.SetParent(page.transform, false);
        MedievalSettingsArt.SetFixedSize(gfxWrap, 872f, 92f);
        VerticalLayoutGroup gwVlg = gfxWrap.GetComponent<VerticalLayoutGroup>();
        gwVlg.spacing = 6f;
        gwVlg.childControlWidth = true;
        gwVlg.childControlHeight = true;
        gwVlg.childForceExpandWidth = false;
        gwVlg.childForceExpandHeight = false;

        GameObject gfxTopRow = CreateSettingsRow(gfxWrap.transform, "GfxButtonsRow", 56f);
        CreateRowTitleColumn(gfxTopRow.transform, "CHẤT LƯỢNG ĐỒ HỌA", "Chất lượng đồ họa 3D", 246f);
        CreateFlexibleSpacer(gfxTopRow.transform);

        GameObject gfxGroup = CreateHorizontalGroup(gfxTopRow.transform, "GfxSegGroup", 12f, 576f, 46f);
        gfxLowBtn = CreateSegmentedButton(gfxGroup.transform, "BtnGfxLow", "THẤP", 184f, 44f,
            () => { GameAudioManager.PlayUiClick(); GameSettingsManager.SetGraphicsPreset(GraphicsPreset.Low); });
        gfxMedBtn = CreateSegmentedButton(gfxGroup.transform, "BtnGfxMedium", "TRUNG BÌNH", 184f, 44f,
            () => { GameAudioManager.PlayUiClick(); GameSettingsManager.SetGraphicsPreset(GraphicsPreset.Medium); });
        gfxHighBtn = CreateSegmentedButton(gfxGroup.transform, "BtnGfxHigh", "CAO", 184f, 44f,
            () => { GameAudioManager.PlayUiClick(); GameSettingsManager.SetGraphicsPreset(GraphicsPreset.High); });

        GameObject descRow = CreateSettingsRow(gfxWrap.transform, "GfxDescRow", 26f);
        CreateFlexibleSpacer(descRow.transform);
        gfxDescText = FantasyUiThemeHelper.CreateText(descRow.transform, "GfxDescText",
            GameSettingsManager.GetGraphicsPresetDescription(GameSettingsManager.CurrentGraphicsPreset),
            14.5f, FontStyles.Italic, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.MidlineRight);
        gfxDescText.textWrappingMode = TextWrappingModes.NoWrap;
        gfxDescText.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(gfxDescText.gameObject, 850f, 24f);

        CreateRowDivider(page.transform);

        // 4. VSync
        GameObject vsyncRow = CreateSettingsRow(page.transform, "Row_VSync", 56f);
        CreateRowTitleColumn(vsyncRow.transform, "VSync:", "Đồng bộ khung hình chống xé hình", 480f);
        CreateFlexibleSpacer(vsyncRow.transform);
        vsyncToggleBtn = CreatePillToggleButton(vsyncRow.transform, "BtnVsyncToggle", 144f, 42f,
            () => { GameAudioManager.PlayUiClick(); GameSettingsManager.SetVSync(!GameSettingsManager.VSyncEnabled); });

        CreateRowDivider(page.transform);

        // 5. FPS Counter
        GameObject fpsRow = CreateSettingsRow(page.transform, "Row_Fps", 56f);
        CreateRowTitleColumn(fpsRow.transform, "HIỂN THỊ FPS", "Hiển thị tốc độ khung hình ở góc phải", 480f);
        CreateFlexibleSpacer(fpsRow.transform);
        fpsToggleBtn = CreatePillToggleButton(fpsRow.transform, "BtnFpsToggle", 144f, 42f,
            () => { GameAudioManager.PlayUiClick(); GameSettingsManager.SetShowFps(!GameSettingsManager.ShowFpsCounter); });

        return page;
    }

    private GameObject BuildAudioTab(Transform parent)
    {
        GameObject page = CreateSettingsPage("Page_Audio", parent, 10f);

        CreateAudioControlRow(page.transform, "Row_MasterAudio", MedievalSettingsArt.IconAudioOn,
            "ÂM LƯỢNG TỔNG", "Âm lượng toàn bộ trò chơi",
            out masterToggleBtn, out masterSlider, out masterValText,
            () => { GameAudioManager.SetMasterMuted(!GameAudioManager.IsMasterMuted); GameAudioManager.PlayUiClick(); },
            val => { if (val > 0.01f && GameAudioManager.IsMasterMuted) GameAudioManager.SetMasterMuted(false); GameAudioManager.SetMasterVolume(val); });

        CreateRowDivider(page.transform);

        CreateAudioControlRow(page.transform, "Row_MusicAudio", MedievalSettingsArt.IconMusic,
            "ÂM LƯỢNG NHẠC NỀN", "Âm lượng nhạc nền chiến dịch (BGM)",
            out bgmToggleBtn, out bgmSlider, out bgmValText,
            () => { GameAudioManager.PlayUiClick(); GameAudioManager.SetBgmMuted(!GameAudioManager.IsBgmMuted); },
            val => { if (val > 0.01f && GameAudioManager.IsBgmMuted) GameAudioManager.SetBgmMuted(false); GameAudioManager.SetBgmVolume(val); });

        CreateRowDivider(page.transform);

        CreateAudioControlRow(page.transform, "Row_SfxAudio", MedievalSettingsArt.IconSfx,
            "ÂM LƯỢNG HIỆU ỨNG", "Âm lượng hiệu ứng kỹ năng & giao diện",
            out sfxToggleBtn, out sfxSlider, out sfxValText,
            () => { GameAudioManager.SetSfxMuted(!GameAudioManager.IsSfxMuted); GameAudioManager.PlayUiClick(); },
            val => { if (val > 0.01f && GameAudioManager.IsSfxMuted) GameAudioManager.SetSfxMuted(false); GameAudioManager.SetSfxVolume(val); });

        CreateRowDivider(page.transform);

        GameObject previewRow = CreateSettingsRow(page.transform, "Row_AudioPreview", 68f);
        MedievalSettingsArt.CreateFramedButton(
            previewRow.transform, "BtnTestSfx", "NGHE THỬ SFX", 226f, 44f, 16.5f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.BronzeFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.GoldOrnament,
            MedievalSettingsArt.BrightGoldText, MedievalSettingsArt.IconPlay,
            () => GameAudioManager.PlayWeaponEquip());

        CreateFlexibleSpacer(previewRow.transform);

        TMP_Text muteHint = FantasyUiThemeHelper.CreateText(previewRow.transform, "AudioShortcutHint",
            "Mẹo: Nhấn phím [M] bất kỳ lúc nào để tắt / bật nhanh toàn bộ âm thanh.",
            14.5f, FontStyles.Italic, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.MidlineRight);
        muteHint.textWrappingMode = TextWrappingModes.NoWrap;
        MedievalSettingsArt.SetFixedSize(muteHint.gameObject, 580f, 36f);

        return page;
    }

    private GameObject BuildControlsTab(Transform parent)
    {
        GameObject page = CreateSettingsPage("Page_Controls", parent, 8f);

        GameObject sensRow = CreateSettingsRow(page.transform, "Row_MouseSens", 66f);
        CreateRowIcon(sensRow.transform, MedievalSettingsArt.IconControls, 34f);
        CreateRowTitleColumn(sensRow.transform, "ĐỘ NHẠY CHUỘT", "Độ nhạy xoay góc nhìn camera", 280f);
        CreateFlexibleSpacer(sensRow.transform);

        sensitivitySlider = CreateGoldenSlider(sensRow.transform, "SensSlider", 0.5f, 2.5f, 350f);
        sensitivitySlider.onValueChanged.AddListener(val => GameSettingsManager.SetMouseSensitivity(val));

        sensitivityValText = FantasyUiThemeHelper.CreateText(sensRow.transform, "SensVal", "1.0x",
            19f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.MidlineRight);
        sensitivityValText.textWrappingMode = TextWrappingModes.NoWrap;
        MedievalSettingsArt.SetFixedSize(sensitivityValText.gameObject, 72f, 38f);

        CreateRowDivider(page.transform);

        TMP_Text guideTitle = FantasyUiThemeHelper.CreateText(page.transform, "ControlsHeading",
            "◆  BẢNG PHÍM ĐIỀU KHIỂN CHIẾN BINH",
            16.5f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.MidlineLeft);
        guideTitle.textWrappingMode = TextWrappingModes.NoWrap;
        guideTitle.characterSpacing = 2f;
        MedievalSettingsArt.SetFixedSize(guideTitle.gameObject, 872f, 28f);

        GameObject gridObj = new GameObject("KeybindGrid", typeof(RectTransform), typeof(GridLayoutGroup));
        gridObj.transform.SetParent(page.transform, false);
        MedievalSettingsArt.SetFixedSize(gridObj, 872f, 326f);

        GridLayoutGroup grid = gridObj.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(428f, 56f);
        grid.spacing = new Vector2(16f, 10f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;

        CreateControlTile(gridObj.transform, "W / A / S / D", "Di chuyển nhân vật");
        CreateControlTile(gridObj.transform, "B  /  E (LỬA TRẠI)", "Mở Lò Rèn mua vũ khí & hồi HP");
        CreateControlTile(gridObj.transform, "CHUỘT TRÁI", "Tấn công & thực hiện chuỗi Combo");
        CreateControlTile(gridObj.transform, "F", "Mở Rương Cổ nhận Vàng & ký sự");
        CreateControlTile(gridObj.transform, "GIỮ CHUỘT PHẢI", "Đỡ đòn (căn đúng lúc để Parry)");
        CreateControlTile(gridObj.transform, "H  /  F1", "Mở Sổ Tay Hành Trình");
        CreateControlTile(gridObj.transform, "Q  /  CHUỘT GIỮA", "Khóa / Bỏ khóa mục tiêu");
        CreateControlTile(gridObj.transform, "M", "Tắt / Mở nhanh toàn bộ âm thanh");
        CreateControlTile(gridObj.transform, "TAB", "Chuyển mục tiêu kế bên khi khóa");
        CreateControlTile(gridObj.transform, "ESC", "Mở / Đóng Menu Tạm Dừng");

        return page;
    }

    private GameObject BuildSystemTab(Transform parent)
    {
        GameObject page = CreateSettingsPage("Page_System", parent, 12f);

        TMP_Text heading = FantasyUiThemeHelper.CreateText(page.transform, "SystemHeading",
            "◆  TỔNG QUAN CẤU HÌNH HIỆN TẠI",
            17.5f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.MidlineLeft);
        heading.textWrappingMode = TextWrappingModes.NoWrap;
        heading.characterSpacing = 2.2f;
        MedievalSettingsArt.SetFixedSize(heading.gameObject, 872f, 32f);

        GameObject gridObj = new GameObject("SystemGrid", typeof(RectTransform), typeof(GridLayoutGroup));
        gridObj.transform.SetParent(page.transform, false);
        MedievalSettingsArt.SetFixedSize(gridObj, 872f, 296f);

        GridLayoutGroup grid = gridObj.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(428f, 138f);
        grid.spacing = new Vector2(16f, 16f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;

        sysGfxValueText = CreateSystemTile(gridObj.transform, "CHẤT LƯỢNG ĐỒ HỌA", MedievalSettingsArt.IconGear);
        sysDisplayValueText = CreateSystemTile(gridObj.transform, "MÀN HÌNH & ĐỘ PHÂN GIẢI", MedievalSettingsArt.IconSystem);
        sysAudioValueText = CreateSystemTile(gridObj.transform, "KÊNH ÂM THANH", MedievalSettingsArt.IconAudioOn);
        sysAccountValueText = CreateSystemTile(gridObj.transform, "CHIẾN BINH & ĐIỀU KHIỂN", MedievalSettingsArt.IconControls);

        CreateRowDivider(page.transform);

        TMP_Text footerNote = FantasyUiThemeHelper.CreateText(page.transform, "SystemSaveNote",
            "Mọi thiết lập được tự động lưu bền vững. Nhấn [ÁP DỤNG] để xác nhận hoặc [MẶC ĐỊNH] để khôi phục cài đặt gốc.",
            15f, FontStyles.Italic, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Center);
        MedievalSettingsArt.SetFixedSize(footerNote.gameObject, 850f, 44f);

        return page;
    }

    private void BuildSettingsFooter(Transform rightCol)
    {
        GameObject footer = new GameObject("SettingsFooter", typeof(RectTransform));
        footer.transform.SetParent(rightCol, false);
        RectTransform fRt = footer.GetComponent<RectTransform>();
        fRt.anchorMin = new Vector2(0f, 0f);
        fRt.anchorMax = new Vector2(1f, 0f);
        fRt.pivot = new Vector2(0.5f, 0f);
        fRt.anchoredPosition = new Vector2(0f, 4f);
        fRt.sizeDelta = new Vector2(0f, 66f);

        footerToastText = FantasyUiThemeHelper.CreateText(footer.transform, "FooterToast", string.Empty,
            16f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.MidlineLeft);
        RectTransform tRt = footerToastText.rectTransform;
        tRt.anchorMin = new Vector2(0f, 0f);
        tRt.anchorMax = new Vector2(1f, 1f);
        tRt.offsetMin = new Vector2(6f, 0f);
        tRt.offsetMax = new Vector2(-548f, 0f);
        footerToastText.textWrappingMode = TextWrappingModes.NoWrap;

        GameObject btnGroup = new GameObject("FooterButtons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        btnGroup.transform.SetParent(footer.transform, false);
        RectTransform bgRt = btnGroup.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(1f, 0.5f);
        bgRt.anchorMax = new Vector2(1f, 0.5f);
        bgRt.pivot = new Vector2(1f, 0.5f);
        bgRt.anchoredPosition = Vector2.zero;
        bgRt.sizeDelta = new Vector2(536f, 54f);

        HorizontalLayoutGroup hlg = btnGroup.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 14f;
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        MedievalSettingsArt.CreateFramedButton(
            btnGroup.transform, "BtnApply", "ÁP DỤNG", 168f, 50f, 18.5f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.EmeraldFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.EmeraldBorder,
            MedievalSettingsArt.EmeraldText, MedievalSettingsArt.IconCheck, OnApplySettingsClicked);

        MedievalSettingsArt.CreateFramedButton(
            btnGroup.transform, "BtnDefault", "MẶC ĐỊNH", 168f, 50f, 18f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.BronzeFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.GoldOrnament,
            MedievalSettingsArt.BrightGoldText, MedievalSettingsArt.IconReturn, OnDefaultSettingsClicked);

        MedievalSettingsArt.CreateFramedButton(
            btnGroup.transform, "BtnBack", "QUAY LẠI", 168f, 50f, 18.5f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.CrimsonFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.CrimsonBorder,
            MedievalSettingsArt.CrimsonText, MedievalSettingsArt.IconCross, OnSubViewBackClicked);
    }

    private void BuildResolutionPopupOverlay(Transform windowRoot)
    {
        resolutionDismissLayer = new GameObject("ResolutionDismissLayer", typeof(RectTransform), typeof(Image), typeof(Button));
        resolutionDismissLayer.transform.SetParent(windowRoot, false);
        RectTransform dlRt = resolutionDismissLayer.GetComponent<RectTransform>();
        dlRt.anchorMin = new Vector2(-0.5f, -0.5f);
        dlRt.anchorMax = new Vector2(1.5f, 1.5f);
        dlRt.offsetMin = Vector2.zero;
        dlRt.offsetMax = Vector2.zero;
        Image dImg = resolutionDismissLayer.GetComponent<Image>();
        dImg.color = new Color(0f, 0f, 0f, 0.01f);
        Button dismissBtn = resolutionDismissLayer.GetComponent<Button>();
        dismissBtn.transition = Selectable.Transition.None;
        dismissBtn.onClick.AddListener(CloseResolutionPopup);
        resolutionDismissLayer.SetActive(false);

        resolutionPopupObj = MedievalSettingsArt.CreateFramedPanel(
            windowRoot, "ResolutionDropdownPopup", 380f, 200f,
            MedievalSettingsArt.PanelBevel, new Color(0.055f, 0.050f, 0.048f, 0.99f),
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.GoldOrnament,
            raycastTarget: true);
        resolutionPopupRt = resolutionPopupObj.GetComponent<RectTransform>();
        resolutionPopupRt.anchorMin = new Vector2(1f, 1f);
        resolutionPopupRt.anchorMax = new Vector2(1f, 1f);
        resolutionPopupRt.pivot = new Vector2(1f, 1f);
        resolutionPopupRt.anchoredPosition = new Vector2(-82f, -187f);

        GameObject listRootGo = new GameObject("ResolutionListRoot", typeof(RectTransform), typeof(VerticalLayoutGroup));
        listRootGo.transform.SetParent(resolutionPopupObj.transform, false);
        FantasyUiThemeHelper.StretchFull(listRootGo.GetComponent<RectTransform>());

        VerticalLayoutGroup vlg = listRootGo.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f;
        vlg.padding = new RectOffset(12, 12, 10, 10);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;

        resolutionListRoot = listRootGo.transform;
        resolutionPopupObj.SetActive(false);
    }

    private void BuildPauseWindow(Transform canvasRoot)
    {
        pauseWindowObj = MedievalSettingsArt.CreateFramedPanel(
            canvasRoot, "PauseWindow", PauseWindowW, PauseWindowH,
            MedievalSettingsArt.PanelGothic, MedievalSettingsArt.FrameObsidian,
            MedievalSettingsArt.BorderGothic, MedievalSettingsArt.GoldOrnament,
            raycastTarget: true);
        pauseWindowRt = pauseWindowObj.GetComponent<RectTransform>();
        pauseWindowRt.anchorMin = new Vector2(0.5f, 0.5f);
        pauseWindowRt.anchorMax = new Vector2(0.5f, 0.5f);
        pauseWindowRt.pivot = new Vector2(0.5f, 0.5f);
        pauseWindowRt.anchoredPosition = Vector2.zero;
        pauseCanvasGroup = pauseWindowObj.AddComponent<CanvasGroup>();

        GameObject header = new GameObject("PauseHeader", typeof(RectTransform));
        header.transform.SetParent(pauseWindowObj.transform, false);
        RectTransform hRt = header.GetComponent<RectTransform>();
        hRt.anchorMin = new Vector2(0f, 1f);
        hRt.anchorMax = new Vector2(1f, 1f);
        hRt.pivot = new Vector2(0.5f, 1f);
        hRt.anchoredPosition = new Vector2(0f, -26f);
        hRt.sizeDelta = new Vector2(-68f, 82f);

        pauseBannerTitleText = FantasyUiThemeHelper.CreateText(header.transform, "Title", "TẠM DỪNG",
            32f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        RectTransform ptRt = pauseBannerTitleText.rectTransform;
        ptRt.anchorMin = new Vector2(0f, 1f);
        ptRt.anchorMax = new Vector2(1f, 1f);
        ptRt.pivot = new Vector2(0.5f, 1f);
        ptRt.anchoredPosition = Vector2.zero;
        ptRt.sizeDelta = new Vector2(0f, 40f);
        pauseBannerTitleText.textWrappingMode = TextWrappingModes.NoWrap;
        pauseBannerTitleText.characterSpacing = 4f;
        MedievalSettingsArt.ApplyTitleGoldGradient(pauseBannerTitleText);
        AddStrongTextShadow(pauseBannerTitleText);

        pauseBannerSubText = FantasyUiThemeHelper.CreateText(header.transform, "Subtitle", "Tạm dừng trận đấu",
            14f, FontStyles.Normal, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Center);
        RectTransform psRt = pauseBannerSubText.rectTransform;
        psRt.anchorMin = new Vector2(0f, 1f);
        psRt.anchorMax = new Vector2(1f, 1f);
        psRt.pivot = new Vector2(0.5f, 1f);
        psRt.anchoredPosition = new Vector2(0f, -44f);
        psRt.sizeDelta = new Vector2(0f, 22f);
        pauseBannerSubText.textWrappingMode = TextWrappingModes.NoWrap;

        GameObject hDiv = MedievalSettingsArt.CreateSymmetricalDivider(
            header.transform, "PauseDivider", 380f, 14f, MedievalSettingsArt.GoldOrnament, ornate: true);
        RectTransform hdRt = hDiv.GetComponent<RectTransform>();
        hdRt.anchorMin = new Vector2(0.5f, 0f);
        hdRt.anchorMax = new Vector2(0.5f, 0f);
        hdRt.pivot = new Vector2(0.5f, 0f);
        hdRt.anchoredPosition = Vector2.zero;

        GameObject body = new GameObject("PauseBody", typeof(RectTransform));
        body.transform.SetParent(pauseWindowObj.transform, false);
        RectTransform bRt = body.GetComponent<RectTransform>();
        bRt.anchorMin = Vector2.zero;
        bRt.anchorMax = Vector2.one;
        bRt.offsetMin = new Vector2(40f, 32f);
        bRt.offsetMax = new Vector2(-40f, -118f);

        // Fallback MainMenu view
        mainMenuContent = CreateVerticalContainer("MainMenuContent", body.transform, 14f, TextAnchor.UpperCenter);
        MedievalSettingsArt.CreateFramedButton(
            mainMenuContent.transform, "BtnMainPlay", "CHƠI NGAY", 412f, 58f, 20f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.HeroGoldFill,
            MedievalSettingsArt.BorderGothic, MedievalSettingsArt.HeroGoldBorder,
            MedievalSettingsArt.DarkInkText, MedievalSettingsArt.IconPlay, OnPlayFromMainMenuClicked);
        MedievalSettingsArt.CreateFramedButton(
            mainMenuContent.transform, "BtnMainSettings", "CÀI ĐẶT", 412f, 50f, 18f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.BronzeFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.GoldOrnament,
            MedievalSettingsArt.BrightGoldText, MedievalSettingsArt.IconGear,
            () => { GameAudioManager.PlayUiClick(); ShowView(ViewMode.Settings, ViewMode.MainMenu, SettingsTab.Graphics); });
        MedievalSettingsArt.CreateFramedButton(
            mainMenuContent.transform, "BtnMainControls", "HƯỚNG DẪN ĐIỀU KHIỂN", 412f, 50f, 17.5f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.BronzeFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.GoldOrnament,
            MedievalSettingsArt.BrightGoldText, MedievalSettingsArt.IconControls,
            () => { GameAudioManager.PlayUiClick(); ShowView(ViewMode.Settings, ViewMode.MainMenu, SettingsTab.Controls); });
        MedievalSettingsArt.CreateFramedButton(
            mainMenuContent.transform, "BtnMainLogout", "ĐĂNG XUẤT", 412f, 46f, 17f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.BronzeBorder,
            MedievalSettingsArt.IvoryText, MedievalSettingsArt.IconReturn, OnLogoutClicked);
        MedievalSettingsArt.CreateFramedButton(
            mainMenuContent.transform, "BtnMainQuit", "THOÁT GAME", 412f, 46f, 17f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.CrimsonFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.CrimsonBorder,
            MedievalSettingsArt.CrimsonText, MedievalSettingsArt.IconPower, OnQuitGameClicked);

        // Pause Menu view trong trận
        pauseMenuContent = CreateVerticalContainer("PauseMenuContent", body.transform, 11f, TextAnchor.UpperCenter);
        MedievalSettingsArt.CreateFramedButton(
            pauseMenuContent.transform, "BtnResume", "TIẾP TỤC CHƠI", 412f, 56f, 19.5f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.HeroGoldFill,
            MedievalSettingsArt.BorderGothic, MedievalSettingsArt.HeroGoldBorder,
            MedievalSettingsArt.DarkInkText, MedievalSettingsArt.IconPlay, OnResumeGameClicked);
        MedievalSettingsArt.CreateFramedButton(
            pauseMenuContent.transform, "BtnPauseSettings", "CÀI ĐẶT  (ĐỒ HỌA & ÂM THANH)", 412f, 48f, 17f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.BronzeFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.GoldOrnament,
            MedievalSettingsArt.BrightGoldText, MedievalSettingsArt.IconGear,
            () => { GameAudioManager.PlayUiClick(); ShowView(ViewMode.Settings, ViewMode.PauseMenu, SettingsTab.Graphics); });
        MedievalSettingsArt.CreateFramedButton(
            pauseMenuContent.transform, "BtnPauseControls", "HƯỚNG DẪN ĐIỀU KHIỂN", 412f, 48f, 17f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.BronzeFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.GoldOrnament,
            MedievalSettingsArt.BrightGoldText, MedievalSettingsArt.IconControls,
            () => { GameAudioManager.PlayUiClick(); ShowView(ViewMode.Settings, ViewMode.PauseMenu, SettingsTab.Controls); });
        MedievalSettingsArt.CreateFramedButton(
            pauseMenuContent.transform, "BtnRestart", "CHƠI LẠI MÀN NÀY", 412f, 45f, 16.5f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.BronzeBorder,
            MedievalSettingsArt.IvoryText, null, OnRestartStageClicked);
        MedievalSettingsArt.CreateFramedButton(
            pauseMenuContent.transform, "BtnHeroSelect", "CHỌN NHÂN VẬT", 412f, 45f, 16.5f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.BronzeBorder,
            MedievalSettingsArt.IvoryText, null, OnGoToCharacterSelectClicked);
        MedievalSettingsArt.CreateFramedButton(
            pauseMenuContent.transform, "BtnToMainMenu", "VỀ MENU CHÍNH", 412f, 45f, 16.5f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.BronzeBorder,
            MedievalSettingsArt.IvoryText, MedievalSettingsArt.IconReturn, OnReturnToMainMenuClicked);
        MedievalSettingsArt.CreateFramedButton(
            pauseMenuContent.transform, "BtnPauseQuit", "THOÁT GAME", 412f, 46f, 17f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.CrimsonFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.CrimsonBorder,
            MedievalSettingsArt.CrimsonText, MedievalSettingsArt.IconPower, OnQuitGameClicked);
    }

    // ==================== UI PRIMITIVES ====================

    private static GameObject CreateSettingsPage(string name, Transform parent, float spacing)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
        go.transform.SetParent(parent, false);
        FantasyUiThemeHelper.StretchFull(go.GetComponent<RectTransform>());

        VerticalLayoutGroup vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = spacing;
        vlg.padding = new RectOffset(4, 4, 6, 6);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        return go;
    }

    private static GameObject CreateVerticalContainer(string name, Transform parent, float spacing, TextAnchor align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
        go.transform.SetParent(parent, false);
        FantasyUiThemeHelper.StretchFull(go.GetComponent<RectTransform>());

        VerticalLayoutGroup vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = spacing;
        vlg.childAlignment = align;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(0, 0, 8, 6);
        return go;
    }

    private static GameObject CreateHorizontalGroup(Transform parent, string name, float spacing, float width, float height)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        go.transform.SetParent(parent, false);
        MedievalSettingsArt.SetFixedSize(go, width, height);

        HorizontalLayoutGroup hlg = go.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = spacing;
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        return go;
    }

    private static GameObject CreateSettingsRow(Transform parent, string name, float height)
    {
        GameObject row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);
        MedievalSettingsArt.SetFixedSize(row, 872f, height);

        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12f;
        hlg.padding = new RectOffset(8, 8, 2, 2);
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        return row;
    }

    private static void CreateFlexibleSpacer(Transform parent)
    {
        GameObject sp = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        sp.transform.SetParent(parent, false);
        LayoutElement le = sp.GetComponent<LayoutElement>();
        le.minWidth = 4f;
        le.flexibleWidth = 1f;
    }

    private static void CreateRowIcon(Transform parent, Sprite iconSprite, float size)
    {
        GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(parent, false);
        MedievalSettingsArt.SetFixedSize(iconGo, size, size);

        Image img = iconGo.GetComponent<Image>();
        img.sprite = iconSprite;
        img.color = MedievalSettingsArt.BrightGoldText;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    private static void CreateRowTitleColumn(Transform parent, string title, string subtitle, float width)
    {
        GameObject col = new GameObject("TitleCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
        col.transform.SetParent(parent, false);
        MedievalSettingsArt.SetFixedSize(col, width, 50f);

        VerticalLayoutGroup vlg = col.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 2f;
        vlg.childAlignment = TextAnchor.MiddleLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;

        TMP_Text t = FantasyUiThemeHelper.CreateText(col.transform, "Title", title,
            19f, FontStyles.Bold, MedievalSettingsArt.IvoryText, TextAlignmentOptions.MidlineLeft);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        AddSoftTextShadow(t);
        MedievalSettingsArt.SetFixedSize(t.gameObject, width, 26f);

        TMP_Text sub = FantasyUiThemeHelper.CreateText(col.transform, "Sub", subtitle,
            13.5f, FontStyles.Normal, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.MidlineLeft);
        sub.textWrappingMode = TextWrappingModes.NoWrap;
        sub.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(sub.gameObject, width, 20f);
    }

    private static void CreateRowDivider(Transform parent)
    {
        MedievalSettingsArt.CreateSymmetricalDivider(
            parent, "RowDivider", 860f, 10f, new Color(0.72f, 0.56f, 0.30f, 0.42f), ornate: false);
    }

    private void CreateAudioControlRow(
        Transform parent,
        string rowName,
        Sprite iconSprite,
        string title,
        string subtitle,
        out Button toggleBtn,
        out Slider slider,
        out TMP_Text valLabel,
        UnityEngine.Events.UnityAction onToggle,
        UnityEngine.Events.UnityAction<float> onSlide)
    {
        GameObject row = CreateSettingsRow(parent, rowName, 78f);
        CreateRowIcon(row.transform, iconSprite, 36f);
        CreateRowTitleColumn(row.transform, title, subtitle, 236f);

        toggleBtn = CreatePillToggleButton(row.transform, "ToggleBtn", 116f, 42f, onToggle);

        slider = CreateGoldenSlider(row.transform, "Slider", 0f, 1f, 320f);
        slider.onValueChanged.AddListener(onSlide);

        valLabel = FantasyUiThemeHelper.CreateText(row.transform, "Value", "100%",
            19f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.MidlineRight);
        valLabel.textWrappingMode = TextWrappingModes.NoWrap;
        MedievalSettingsArt.SetFixedSize(valLabel.gameObject, 68f, 38f);
    }

    private static void CreateControlTile(Transform parent, string keyBadge, string description)
    {
        GameObject tile = MedievalSettingsArt.CreateFramedPanel(
            parent, "Tile_" + keyBadge, 428f, 56f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.BronzeBorder);

        HorizontalLayoutGroup hlg = tile.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(14, 14, 4, 4);
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        TMP_Text keyTxt = FantasyUiThemeHelper.CreateText(tile.transform, "Key", keyBadge,
            14f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.MidlineLeft);
        keyTxt.textWrappingMode = TextWrappingModes.NoWrap;
        MedievalSettingsArt.SetFixedSize(keyTxt.gameObject, 168f, 40f);

        TMP_Text descTxt = FantasyUiThemeHelper.CreateText(tile.transform, "Desc", description,
            14.5f, FontStyles.Normal, MedievalSettingsArt.IvoryText, TextAlignmentOptions.MidlineLeft);
        descTxt.textWrappingMode = TextWrappingModes.NoWrap;
        descTxt.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(descTxt.gameObject, 222f, 40f);
    }

    private static TMP_Text CreateSystemTile(Transform parent, string title, Sprite iconSprite)
    {
        GameObject tile = MedievalSettingsArt.CreateFramedPanel(
            parent, "SysTile_" + title, 428f, 138f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.BronzeBorder);

        GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(tile.transform, false);
        RectTransform iRt = iconGo.GetComponent<RectTransform>();
        iRt.anchorMin = new Vector2(0f, 1f);
        iRt.anchorMax = new Vector2(0f, 1f);
        iRt.pivot = new Vector2(0f, 1f);
        iRt.anchoredPosition = new Vector2(18f, -18f);
        iRt.sizeDelta = new Vector2(30f, 30f);
        Image iImg = iconGo.GetComponent<Image>();
        iImg.sprite = iconSprite;
        iImg.color = MedievalSettingsArt.BrightGoldText;
        iImg.preserveAspect = true;
        iImg.raycastTarget = false;

        TMP_Text head = FantasyUiThemeHelper.CreateText(tile.transform, "Head", title,
            15.5f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.MidlineLeft);
        RectTransform hRt = head.rectTransform;
        hRt.anchorMin = new Vector2(0f, 1f);
        hRt.anchorMax = new Vector2(1f, 1f);
        hRt.pivot = new Vector2(0.5f, 1f);
        hRt.anchoredPosition = new Vector2(20f, -18f);
        hRt.sizeDelta = new Vector2(-78f, 30f);
        head.textWrappingMode = TextWrappingModes.NoWrap;

        TMP_Text body = FantasyUiThemeHelper.CreateText(tile.transform, "Body", "-",
            15.5f, FontStyles.Normal, MedievalSettingsArt.IvoryText, TextAlignmentOptions.TopLeft);
        RectTransform bRt = body.rectTransform;
        bRt.anchorMin = new Vector2(0f, 0f);
        bRt.anchorMax = new Vector2(1f, 1f);
        bRt.offsetMin = new Vector2(20f, 14f);
        bRt.offsetMax = new Vector2(-18f, -58f);
        body.lineSpacing = 4f;
        return body;
    }

    private static Button CreateSegmentedButton(
        Transform parent,
        string name,
        string label,
        float width,
        float height,
        UnityEngine.Events.UnityAction onClick)
    {
        return MedievalSettingsArt.CreateFramedButton(
            parent, name, label, width, height, 16f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.BronzeBorder,
            MedievalSettingsArt.WarmBronzeText, null, onClick);
    }

    private static Button CreatePillToggleButton(
        Transform parent,
        string name,
        float width,
        float height,
        UnityEngine.Events.UnityAction onClick)
    {
        return MedievalSettingsArt.CreateFramedButton(
            parent, name, "ON", width, height, 16f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.EmeraldFill,
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.EmeraldBorder,
            MedievalSettingsArt.EmeraldText, MedievalSettingsArt.IconCheck, onClick);
    }

    private static Slider CreateGoldenSlider(Transform parent, string name, float minVal, float maxVal, float width)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Slider));
        go.transform.SetParent(parent, false);
        MedievalSettingsArt.SetFixedSize(go, width, 30f);

        GameObject bgObj = MedievalSettingsArt.CreateFramedPanel(
            go.transform, "Background", width, 14f,
            MedievalSettingsArt.PanelClean, new Color(0.045f, 0.040f, 0.038f, 1f),
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.BronzeBorder);
        RectTransform bgRt = bgObj.GetComponent<RectTransform>();
        bgRt.anchorMin = new Vector2(0f, 0.5f);
        bgRt.anchorMax = new Vector2(1f, 0.5f);
        bgRt.sizeDelta = new Vector2(0f, 14f);

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        RectTransform faRt = fillArea.GetComponent<RectTransform>();
        faRt.anchorMin = new Vector2(0f, 0.5f);
        faRt.anchorMax = new Vector2(1f, 0.5f);
        faRt.sizeDelta = new Vector2(-6f, 8f);

        GameObject fillObj = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillObj.transform.SetParent(fillArea.transform, false);
        RectTransform fRt = fillObj.GetComponent<RectTransform>();
        fRt.anchorMin = Vector2.zero;
        fRt.anchorMax = new Vector2(0f, 1f);
        fRt.sizeDelta = Vector2.zero;
        Image fillImg = fillObj.GetComponent<Image>();
        fillImg.sprite = null;
        fillImg.type = Image.Type.Simple;
        fillImg.color = MedievalSettingsArt.HeroGoldFill;

        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        RectTransform haRt = handleArea.GetComponent<RectTransform>();
        haRt.anchorMin = Vector2.zero;
        haRt.anchorMax = Vector2.one;
        haRt.offsetMin = new Vector2(9f, 0f);
        haRt.offsetMax = new Vector2(-9f, 0f);

        GameObject handleObj = MedievalSettingsArt.CreateFramedPanel(
            handleArea.transform, "Handle", 18f, 28f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.HeroGoldFill,
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.HeroGoldBorder,
            raycastTarget: true);
        RectTransform hRt = handleObj.GetComponent<RectTransform>();
        hRt.sizeDelta = new Vector2(18f, 0f);

        Slider slider = go.GetComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.minValue = minVal;
        slider.maxValue = maxVal;
        slider.fillRect = fRt;
        slider.handleRect = hRt;
        slider.targetGraphic = handleObj.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;

        MedievalSettingsArt.AttachSliderFeedback(slider);
        return slider;
    }

    private static void ApplySegmentedStyle(Button btn, bool active)
    {
        if (btn == null) return;
        MedievalSettingsArt.UpdateFramedButtonColors(
            btn,
            active ? MedievalSettingsArt.ActiveGoldFill : MedievalSettingsArt.DarkSlateFill,
            active ? MedievalSettingsArt.HeroGoldBorder : MedievalSettingsArt.BronzeBorder,
            active ? MedievalSettingsArt.DarkInkText : MedievalSettingsArt.WarmBronzeText);
    }

    private static void ApplyPillToggleStyle(Button btn, bool isOn)
    {
        if (btn == null) return;
        Transform iconTf = btn.transform.Find("Icon");
        if (iconTf != null && iconTf.TryGetComponent(out Image iconImg))
        {
            iconImg.sprite = isOn ? MedievalSettingsArt.IconCheck : MedievalSettingsArt.IconCross;
        }

        TMP_Text txt = btn.transform.Find("Text")?.GetComponent<TMP_Text>();
        if (txt != null) txt.text = isOn ? "BẬT" : "TẮT";

        MedievalSettingsArt.UpdateFramedButtonColors(
            btn,
            isOn ? MedievalSettingsArt.EmeraldFill : MedievalSettingsArt.CrimsonFill,
            isOn ? MedievalSettingsArt.EmeraldBorder : MedievalSettingsArt.CrimsonBorder,
            isOn ? MedievalSettingsArt.EmeraldText : MedievalSettingsArt.CrimsonText);
    }

    private static void AddSoftTextShadow(TMP_Text text)
    {
        if (text == null) return;
        Shadow sh = text.gameObject.GetComponent<Shadow>() ?? text.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.72f);
        sh.effectDistance = new Vector2(0f, -1.5f);
    }

    private static void AddStrongTextShadow(TMP_Text text)
    {
        if (text == null) return;
        Shadow sh = text.gameObject.GetComponent<Shadow>() ?? text.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.88f);
        sh.effectDistance = new Vector2(0f, -2.5f);
    }
}
