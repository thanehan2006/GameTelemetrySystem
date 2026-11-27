using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Quản lý màn hình kết thúc trận Win/Lose, dựng giao diện thẻ kết quả, thống kê trận đấu và nút điều hướng
public class GameOverManager : MonoBehaviour
{
    public const float EndCardWidth = 920f;
    public const float EndCardHeight = 590f;

    public static GameOverManager Instance { get; private set; }
    public static bool IsGameOver { get; private set; }

    [Header("UI Reference")]
    public GameObject gameOverPanel;
    public TMP_Text titleText;

    public struct EndScreenRefs
    {
        public RectTransform cardRect;
        public Image backdropImage;
        public Image haloImage;
        public Image cardBgImage;
        public Outline cardOutline;
        public Image headerGlowImage;
        public Image topAccentBar;
        public Image bottomAccentBar;
        public Image[] innerFrameLines;
        public Image[] cornerBracketLines;
        public Image badgeBgImage;
        public Outline badgeOutline;
        public TMP_Text badgeText;
        public TMP_Text titleText;
        public Image[] dividerParts;
        public TMP_Text subtitleText;
        public Image[] tileAccentBars;
        public TMP_Text statStageValue;
        public TMP_Text statScoreValue;
        public TMP_Text statTimeValue;
        public Button nextLevelButton;
        public Button tryAgainButton;
        public Button mainMenuButton;
        public Button quitButton;
        public FloatingEmbersUI embersEffect;
    }

    private EndScreenRefs uiRefs;
    private RectTransform gameOverPanelRect;
    private CanvasGroup panelCanvasGroup;
    private string cachedNextSceneName;
    private float sessionStartRealtime;
    private float targetCardScale = 1f;
    private float lastAppliedCardScale = -1f;
    private bool isAnimatingIn;
    private bool uiBuilt;
    private bool isQuitting;

    public static readonly Color WinGold = new Color(0.95f, 0.76f, 0.28f, 1f);
    public static readonly Color WinGoldBright = new Color(1f, 0.88f, 0.48f, 1f);
    public static readonly Color WinTitleTop = new Color(1f, 0.973f, 0.863f, 1f);
    public static readonly Color WinTitleBottom = new Color(0.918f, 0.702f, 0.220f, 1f);
    public static readonly Color WinBackdrop = new Color(0.025f, 0.042f, 0.082f, 0.88f);
    public static readonly Color WinCardBg = new Color(0.050f, 0.070f, 0.118f, 0.96f);

    public static readonly Color LoseCrimson = new Color(0.88f, 0.22f, 0.24f, 1f);
    public static readonly Color LoseCrimsonBright = new Color(1f, 0.42f, 0.40f, 1f);
    public static readonly Color LoseTitleTop = new Color(1f, 0.690f, 0.659f, 1f);
    public static readonly Color LoseTitleBottom = new Color(0.863f, 0.149f, 0.149f, 1f);
    public static readonly Color LoseBackdrop = new Color(0.085f, 0.020f, 0.032f, 0.90f);
    public static readonly Color LoseCardBg = new Color(0.082f, 0.036f, 0.050f, 0.96f);

    // Khởi tạo Singleton và ẩn panel kết thúc trận khi mới bắt đầu màn
    private void Awake()
    {
        Instance = this;
        IsGameOver = false;
        sessionStartRealtime = Time.realtimeSinceStartup;
        if (gameOverPanel != null) gameOverPanelRect = gameOverPanel.GetComponent<RectTransform>();
        ResolveTitleText();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!IsGameOver || gameOverPanel == null || !gameOverPanel.activeInHierarchy) return;

        UpdateResponsiveCardScale();

        if (uiRefs.haloImage != null)
        {
            float pulse = 0.96f + 0.04f * Mathf.Sin(Time.unscaledTime * 1.6f);
            uiRefs.haloImage.rectTransform.localScale = Vector3.one * (targetCardScale * pulse);
        }
    }

    private void ResolveTitleText()
    {
        if (titleText != null || gameOverPanel == null) return;

        Transform found = FindDescendantByName(gameOverPanel.transform, "GameOverText");
        if (found != null)
        {
            titleText = found.GetComponent<TMP_Text>();
        }
    }

    // Hiển thị giao diện thất bại khi nhân vật hết máu
    public void ShowGameOver()
    {
        ShowEndScreen(false);
        GameAudioManager.PlayGameOver();
    }

    // Hiển thị giao diện chiến thắng khi tiêu diệt Boss
    public void ShowVictory()
    {
        ShowEndScreen(true);
        GameAudioManager.PlayVictory();
    }

    // Dừng thời gian trận đấu, mở khóa chuột, phát âm thanh và kích hoạt hoạt ảnh hiện thẻ kết quả
    private void ShowEndScreen(bool isVictory)
    {
        if (IsGameOver) return;
        IsGameOver = true;

        EnsureStyledEndScreen();

        if (gameOverPanel != null)
        {
            gameOverPanel.transform.SetAsLastSibling();
            gameOverPanel.SetActive(true);
        }

        ApplyEndScreenTheme(isVictory);
        PopulateMatchStats(isVictory);

        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        targetCardScale = ComputeResponsiveCardScale();
        StartCoroutine(AnimateEndScreenIn());
    }

    private void EnsureStyledEndScreen()
    {
        if (uiBuilt || gameOverPanel == null) return;
        uiBuilt = true;

        panelCanvasGroup = gameOverPanel.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null) panelCanvasGroup = gameOverPanel.AddComponent<CanvasGroup>();

        ResolveTitleText();
        TMP_Text preservedTitle = titleText;
        BindBakedEndScreen(gameOverPanel, ref preservedTitle, out uiRefs);
        titleText = preservedTitle;

        ConfigureActionButton(uiRefs.nextLevelButton, NextLevel);
        ConfigureActionButton(uiRefs.tryAgainButton, TryAgain);
        ConfigureActionButton(uiRefs.mainMenuButton, ReturnToMainMenu);
        ConfigureActionButton(uiRefs.quitButton, QuitGame);
        UniversalButtonHoverInstaller.RequestScan();
    }

    // Liên kết các thành phần UI màn hình Win/Lose đã đặt sẵn trong Scene
    private static void BindBakedEndScreen(GameObject panel, ref TMP_Text preservedTitle, out EndScreenRefs refs)
    {
        refs = default;
        if (panel == null) return;

        RectTransform panelRt = panel.GetComponent<RectTransform>();
        if (panelRt != null)
        {
            FantasyUiThemeHelper.StretchFull(panelRt);
        }

        refs.backdropImage = panel.GetComponent<Image>();
        if (refs.backdropImage != null)
        {
            refs.backdropImage.sprite = null;
            refs.backdropImage.raycastTarget = true;
        }

        Sprite glowSprite = FantasyUiThemeHelper.GetSoftDotSprite();

        Transform haloTf = panel.transform.Find("EndScreenHalo");
        if (haloTf != null)
        {
            refs.haloImage = haloTf.GetComponent<Image>();
            if (refs.haloImage != null)
            {
                refs.haloImage.sprite = glowSprite;
                refs.haloImage.raycastTarget = false;
            }
        }

        Transform emberTf = panel.transform.Find("EndScreenEmbers");
        if (emberTf != null)
        {
            refs.embersEffect = emberTf.GetComponent<FloatingEmbersUI>();
        }

        Transform cardTf = panel.transform.Find("EndCard");
        if (cardTf != null)
        {
            refs.cardRect = cardTf.GetComponent<RectTransform>();
            refs.cardBgImage = cardTf.GetComponent<Image>();
            refs.cardOutline = cardTf.GetComponent<Outline>();

            Transform headerGlowTf = cardTf.Find("HeaderGlowStrip");
            if (headerGlowTf != null)
            {
                refs.headerGlowImage = headerGlowTf.GetComponent<Image>();
                if (refs.headerGlowImage != null)
                {
                    refs.headerGlowImage.sprite = glowSprite;
                    refs.headerGlowImage.raycastTarget = false;
                }
            }

            refs.topAccentBar = cardTf.Find("TopAccentBar")?.GetComponent<Image>();
            refs.bottomAccentBar = cardTf.Find("BottomAccentBar")?.GetComponent<Image>();

            Transform frameTf = cardTf.Find("InnerFrame");
            if (frameTf != null)
            {
                refs.innerFrameLines = new[]
                {
                    frameTf.Find("InnerTop")?.GetComponent<Image>(),
                    frameTf.Find("InnerBottom")?.GetComponent<Image>(),
                    frameTf.Find("InnerLeft")?.GetComponent<Image>(),
                    frameTf.Find("InnerRight")?.GetComponent<Image>()
                };
                refs.cornerBracketLines = new Image[8];
                for (int i = 0; i < 4; i++)
                {
                    refs.cornerBracketLines[i * 2] = frameTf.Find($"CornerH_{i}")?.GetComponent<Image>();
                    refs.cornerBracketLines[i * 2 + 1] = frameTf.Find($"CornerV_{i}")?.GetComponent<Image>();
                }
            }

            Transform badgePillTf = cardTf.Find("StatusBadgePill");
            if (badgePillTf != null)
            {
                refs.badgeBgImage = badgePillTf.GetComponent<Image>();
                refs.badgeOutline = badgePillTf.GetComponent<Outline>();
                refs.badgeText = badgePillTf.Find("StatusBadge")?.GetComponent<TMP_Text>();
            }

            refs.titleText = preservedTitle != null
                ? preservedTitle
                : cardTf.Find("GameOverText")?.GetComponent<TMP_Text>();
            if (preservedTitle == null) preservedTitle = refs.titleText;

            Transform divTf = cardTf.Find("TitleDivider");
            if (divTf != null)
            {
                refs.dividerParts = new[]
                {
                    divTf.Find("LeftBar")?.GetComponent<Image>(),
                    divTf.Find("RightBar")?.GetComponent<Image>(),
                    divTf.Find("CenterDiamond")?.GetComponent<Image>()
                };
            }

            refs.subtitleText = cardTf.Find("SubtitleText")?.GetComponent<TMP_Text>();

            Transform statsRowTf = cardTf.Find("MatchStatsRow");
            if (statsRowTf != null)
            {
                refs.tileAccentBars = new[]
                {
                    statsRowTf.Find("TileStage/AccentStrip")?.GetComponent<Image>(),
                    statsRowTf.Find("TileScore/AccentStrip")?.GetComponent<Image>(),
                    statsRowTf.Find("TileTime/AccentStrip")?.GetComponent<Image>()
                };
                refs.statStageValue = statsRowTf.Find("TileStage/Value")?.GetComponent<TMP_Text>();
                refs.statScoreValue = statsRowTf.Find("TileScore/Value")?.GetComponent<TMP_Text>();
                refs.statTimeValue = statsRowTf.Find("TileTime/Value")?.GetComponent<TMP_Text>();
            }

            Transform actionsRowTf = cardTf.Find("ActionButtonsRow");
            refs.nextLevelButton = FindActionButton(actionsRowTf, panel.transform, "NextLevelButton");
            refs.tryAgainButton = FindActionButton(actionsRowTf, panel.transform, "TryAgainButton");
            refs.mainMenuButton = FindActionButton(actionsRowTf, panel.transform, "MainMenuButton");
            refs.quitButton = FindActionButton(actionsRowTf, panel.transform, "QuitButton");
        }

        if (refs.nextLevelButton != null)
        {
            refs.nextLevelButton.gameObject.SetActive(false);
        }
    }

    private static Button FindActionButton(Transform actionsRowTf, Transform panelRoot, string buttonName)
    {
        Transform t = actionsRowTf != null ? actionsRowTf.Find(buttonName) : null;
        if (t == null) t = FindDescendantByName(panelRoot, buttonName);
        return t != null ? t.GetComponent<Button>() : null;
    }

    private static void ConfigureActionButton(Button btn, UnityEngine.Events.UnityAction fallbackAction)
    {
        if (btn == null) return;

        btn.interactable = true;

        Image bg = btn.GetComponent<Image>();
        if (bg != null) bg.raycastTarget = true;

        Graphic[] childGraphics = btn.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < childGraphics.Length; i++)
        {
            if (childGraphics[i] != null && childGraphics[i] != bg)
            {
                childGraphics[i].raycastTarget = false;
            }
        }

        if (fallbackAction != null && btn.onClick.GetPersistentEventCount() == 0)
        {
            btn.onClick.RemoveListener(fallbackAction);
            btn.onClick.AddListener(fallbackAction);
        }
    }

    // Đổi bảng màu, viền trang trí và nội dung nút bấm theo trạng thái Thắng hoặc Thua
    private void ApplyEndScreenTheme(bool isVictory)
    {
        Color accent = isVictory ? WinGold : LoseCrimson;
        Color accentBright = isVictory ? WinGoldBright : LoseCrimsonBright;

        if (uiRefs.backdropImage != null) uiRefs.backdropImage.color = isVictory ? WinBackdrop : LoseBackdrop;
        if (uiRefs.haloImage != null) uiRefs.haloImage.color = new Color(accent.r, accent.g, accent.b, isVictory ? 0.16f : 0.18f);
        if (uiRefs.cardBgImage != null) uiRefs.cardBgImage.color = isVictory ? WinCardBg : LoseCardBg;
        if (uiRefs.cardOutline != null) uiRefs.cardOutline.effectColor = new Color(accent.r, accent.g, accent.b, 0.80f);
        if (uiRefs.headerGlowImage != null) uiRefs.headerGlowImage.color = new Color(accent.r, accent.g, accent.b, 0.14f);
        if (uiRefs.topAccentBar != null) uiRefs.topAccentBar.color = accent;
        if (uiRefs.bottomAccentBar != null) uiRefs.bottomAccentBar.color = new Color(accent.r, accent.g, accent.b, 0.72f);

        SetGroupColor(uiRefs.innerFrameLines, new Color(accent.r, accent.g, accent.b, 0.24f));
        SetGroupColor(uiRefs.cornerBracketLines, new Color(accentBright.r, accentBright.g, accentBright.b, 0.90f));
        SetGroupColor(uiRefs.dividerParts, accent);

        if (uiRefs.badgeBgImage != null)
        {
            uiRefs.badgeBgImage.color = isVictory
                ? new Color(0.11f, 0.10f, 0.06f, 0.86f)
                : new Color(0.15f, 0.05f, 0.06f, 0.86f);
        }
        if (uiRefs.badgeOutline != null) uiRefs.badgeOutline.effectColor = new Color(accent.r, accent.g, accent.b, 0.50f);
        if (uiRefs.badgeText != null)
        {
            uiRefs.badgeText.text = isVictory
                ? "CHIẾN DỊCH HOÀN TẤT  •  VANGUARD"
                : "CHIẾN BINH ĐÃ GỤC NGÃ  •  VANGUARD";
            uiRefs.badgeText.color = accentBright;
        }

        if (uiRefs.tileAccentBars != null)
        {
            for (int i = 0; i < uiRefs.tileAccentBars.Length; i++)
            {
                if (uiRefs.tileAccentBars[i] != null)
                {
                    uiRefs.tileAccentBars[i].color = new Color(accent.r, accent.g, accent.b, 0.88f);
                }
            }
        }

        if (uiRefs.statScoreValue != null)
        {
            uiRefs.statScoreValue.color = isVictory ? WinGoldBright : new Color(1f, 0.84f, 0.56f, 1f);
        }

        if (uiRefs.titleText != null)
        {
            uiRefs.titleText.text = isVictory ? "CHIẾN THẮNG!" : "THẤT BẠI";
            uiRefs.titleText.color = Color.white;
            ApplyTitleGradient(uiRefs.titleText, isVictory ? WinTitleTop : LoseTitleTop, isVictory ? WinTitleBottom : LoseTitleBottom);
        }

        if (uiRefs.subtitleText != null)
        {
            string winSub = StageManager.Instance != null && !string.IsNullOrWhiteSpace(StageManager.Instance.victorySubtitle)
                ? StageManager.Instance.victorySubtitle
                : "Đại Tướng Orc Warlord đã bị tiêu diệt. Mỏ Vàng và khu rừng đã được giải phóng!";
            uiRefs.subtitleText.text = isVictory
                ? winSub
                : "Bóng tối đã hạ gục bạn. Hãy đứng dậy, giữ Chuột Phải đỡ đòn và phục thù!";
        }

        cachedNextSceneName = null;
        if (isVictory && StageManager.Instance != null && !string.IsNullOrWhiteSpace(StageManager.Instance.nextMapSceneName))
        {
            string candidate = StageManager.Instance.nextMapSceneName.Trim();
            if (Application.CanStreamedLevelBeLoaded(candidate))
            {
                cachedNextSceneName = candidate;
            }
        }
        bool showNextLevel = !string.IsNullOrEmpty(cachedNextSceneName);

        if (uiRefs.nextLevelButton != null)
        {
            uiRefs.nextLevelButton.gameObject.SetActive(showNextLevel);
            if (showNextLevel)
            {
                StyleEndActionButton(uiRefs.nextLevelButton, "MÀN TIẾP THEO →",
                    WinGold, FantasyUiThemeHelper.DarkButtonText, new Color(1f, 0.92f, 0.62f, 0.85f));
            }
        }

        if (uiRefs.tryAgainButton != null)
        {
            bool primaryTryAgain = !showNextLevel;
            Color btnBg = isVictory
                ? (primaryTryAgain ? WinGold : FantasyUiThemeHelper.GhostButtonBg)
                : LoseCrimson;
            Color txtCol = isVictory
                ? (primaryTryAgain ? FantasyUiThemeHelper.DarkButtonText : WinGoldBright)
                : Color.white;
            Color borderCol = isVictory
                ? (primaryTryAgain ? new Color(1f, 0.90f, 0.56f, 0.85f) : FantasyUiThemeHelper.GoldBorder)
                : new Color(1f, 0.52f, 0.50f, 0.85f);

            StyleEndActionButton(uiRefs.tryAgainButton, isVictory ? "CHƠI LẠI" : "THỬ LẠI NGAY", btnBg, txtCol, borderCol);
        }

        if (uiRefs.mainMenuButton != null)
        {
            Color menuBorder = isVictory ? FantasyUiThemeHelper.GoldBorder : new Color(0.88f, 0.36f, 0.38f, 0.72f);
            Color menuText = isVictory ? WinGoldBright : new Color(0.96f, 0.88f, 0.86f, 1f);
            StyleEndActionButton(uiRefs.mainMenuButton, "VỀ MENU", FantasyUiThemeHelper.GhostButtonBg, menuText, menuBorder);
        }

        if (uiRefs.quitButton != null)
        {
            Color quitBorder = isVictory ? FantasyUiThemeHelper.GoldBorder : new Color(0.88f, 0.30f, 0.32f, 0.65f);
            Color quitText = isVictory ? WinGoldBright : new Color(0.96f, 0.82f, 0.82f, 1f);
            StyleEndActionButton(uiRefs.quitButton, "THOÁT GAME", FantasyUiThemeHelper.GhostButtonBg, quitText, quitBorder);
        }

        if (uiRefs.embersEffect != null && uiRefs.embersEffect.transform.childCount == 0)
        {
            if (isVictory)
            {
                uiRefs.embersEffect.Initialize(34, 0.05f, 0.95f, new Color(1f, 0.86f, 0.32f, 1f), new Color(0.35f, 0.98f, 0.62f, 1f));
            }
            else
            {
                uiRefs.embersEffect.Initialize(28, 0.05f, 0.95f, new Color(0.95f, 0.22f, 0.18f, 1f), new Color(0.55f, 0.12f, 0.14f, 1f));
            }
        }
    }

    // Điền dữ liệu màn hiện tại, tổng điểm/vàng và thời gian chiến đấu lên thẻ thống kê
    private void PopulateMatchStats(bool isVictory)
    {
        int stage = StageManager.Instance != null
            ? StageManager.Instance.CurrentStage
            : (TelemetryManager.Instance != null ? TelemetryManager.Instance.CurrentLevel : 1);
        int score = ScoreManager.Instance != null ? ScoreManager.Instance.score : 0;
        int totalGold = ScoreManager.Instance != null ? ScoreManager.Instance.totalGoldEarned : 0;

        float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - sessionStartRealtime);
        int mins = Mathf.FloorToInt(elapsed / 60f);
        int secs = Mathf.FloorToInt(elapsed % 60f);

        if (uiRefs.statStageValue != null) uiRefs.statStageValue.text = isVictory ? "Màn 3 / 3" : $"Màn {stage} / 3";
        if (uiRefs.statScoreValue != null) uiRefs.statScoreValue.text = $"{score:N0}  •  {totalGold:N0} G";
        if (uiRefs.statTimeValue != null) uiRefs.statTimeValue.text = $"{mins:00}:{secs:00}";
    }

    private float ComputeResponsiveCardScale()
    {
        if (gameOverPanelRect == null && gameOverPanel != null)
        {
            gameOverPanelRect = gameOverPanel.GetComponent<RectTransform>();
        }
        RectTransform panelRt = gameOverPanelRect;
        float w = (panelRt != null && panelRt.rect.width > 10f) ? panelRt.rect.width : Screen.width;
        float h = (panelRt != null && panelRt.rect.height > 10f) ? panelRt.rect.height : Screen.height;
        if (w <= 10f || h <= 10f) return 1f;

        float fitW = (w * 0.90f) / EndCardWidth;
        float fitH = (h * 0.88f) / EndCardHeight;
        return Mathf.Clamp(Mathf.Min(fitW, fitH), 0.52f, 1.15f);
    }

    private void UpdateResponsiveCardScale()
    {
        float nextScale = ComputeResponsiveCardScale();
        if (Mathf.Abs(nextScale - targetCardScale) > 0.005f)
        {
            targetCardScale = nextScale;
        }
        if (!isAnimatingIn && uiRefs.cardRect != null && Mathf.Abs(targetCardScale - lastAppliedCardScale) > 0.001f)
        {
            lastAppliedCardScale = targetCardScale;
            uiRefs.cardRect.localScale = Vector3.one * targetCardScale;
        }
    }

    private IEnumerator AnimateEndScreenIn()
    {
        isAnimatingIn = true;
        if (panelCanvasGroup != null) panelCanvasGroup.alpha = 0f;
        if (uiRefs.cardRect != null) uiRefs.cardRect.localScale = Vector3.one * (targetCardScale * 0.92f);

        float t = 0f;
        const float duration = 0.30f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            float easeOut = 1f - Mathf.Pow(1f - k, 3f);

            targetCardScale = ComputeResponsiveCardScale();
            if (panelCanvasGroup != null) panelCanvasGroup.alpha = easeOut;
            if (uiRefs.cardRect != null)
            {
                float s = Mathf.Lerp(targetCardScale * 0.92f, targetCardScale, easeOut);
                uiRefs.cardRect.localScale = Vector3.one * s;
            }
            yield return null;
        }

        isAnimatingIn = false;
        if (panelCanvasGroup != null) panelCanvasGroup.alpha = 1f;
        if (uiRefs.cardRect != null)
        {
            lastAppliedCardScale = targetCardScale;
            uiRefs.cardRect.localScale = Vector3.one * targetCardScale;
        }
    }

    public static void ResetTmpAlignment(
        TMP_Text tmp,
        TextAlignmentOptions align,
        bool noWrap,
        float minSize,
        float maxSize,
        float charSpacing)
    {
        if (tmp == null) return;
        tmp.margin = Vector4.zero;
        tmp.rectTransform.localScale = Vector3.one;
        tmp.rectTransform.localRotation = Quaternion.identity;
        tmp.alignment = align;
        tmp.textWrappingMode = noWrap ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Truncate;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = minSize;
        tmp.fontSizeMax = maxSize;
        tmp.fontSize = maxSize;
        tmp.characterSpacing = charSpacing;
    }

    public static void ApplyTitleGradient(TMP_Text tmp, Color topCol, Color bottomCol)
    {
        if (tmp == null) return;
        tmp.enableVertexGradient = true;
        tmp.colorGradient = new VertexGradient(topCol, topCol, bottomCol, bottomCol);
    }

    public static void StyleEndActionButton(Button btn, string label, Color bgColor, Color textColor, Color borderColor)
    {
        if (btn == null) return;

        Image img = btn.GetComponent<Image>();
        if (img == null) img = btn.gameObject.AddComponent<Image>();
        img.sprite = null;
        img.color = bgColor;
        img.raycastTarget = true;

        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.16f, 1.12f, 1.04f, 1f);
        cb.pressedColor = new Color(0.84f, 0.82f, 0.80f, 1f);
        cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        btn.colors = cb;

        Outline outline = btn.GetComponent<Outline>();
        if (outline == null) outline = btn.gameObject.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1.4f, -1.4f);

        Transform existingSheen = btn.transform.Find("ButtonTopSheen");
        Image sheenImg;
        if (existingSheen != null)
        {
            sheenImg = existingSheen.GetComponent<Image>();
        }
        else
        {
            GameObject sheenGo = FantasyUiThemeHelper.CreateAccentLine(btn.transform, "ButtonTopSheen",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, new Vector2(0f, 2f), new Color(1f, 1f, 1f, 0.24f));
            sheenImg = sheenGo.GetComponent<Image>();
        }
        if (sheenImg != null)
        {
            sheenImg.color = new Color(1f, 1f, 1f, 0.22f);
            sheenImg.raycastTarget = false;
        }

        TMP_Text txt = btn.GetComponentInChildren<TMP_Text>(true);
        if (txt != null)
        {
            if (!string.IsNullOrEmpty(label)) txt.text = label;
            txt.color = textColor;
            txt.fontStyle = FontStyles.Bold;
            txt.raycastTarget = false;
            StretchWithPadding(txt.rectTransform, 10f, 6f);
            ResetTmpAlignment(txt, TextAlignmentOptions.Center, noWrap: true, minSize: 13f, maxSize: 20f, charSpacing: 1.0f);
        }
    }

    public static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null) return null;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == targetName) return child;
            Transform nested = FindDescendantByName(child, targetName);
            if (nested != null) return nested;
        }
        return null;
    }

    private static void StretchWithPadding(RectTransform rt, float padX, float padY)
    {
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }

    private static void SetGroupColor(Image[] imgs, Color col)
    {
        if (imgs == null) return;
        for (int i = 0; i < imgs.Length; i++)
        {
            if (imgs[i] != null) imgs[i].color = col;
        }
    }

    // Chuyển sang màn chiến dịch tiếp theo nếu người chơi thắng
    public void NextLevel()
    {
        if (string.IsNullOrEmpty(cachedNextSceneName)) return;

        GameAudioManager.PlayUiClick();
        Time.timeScale = 1f;
        IsGameOver = false;

        PlayerPrefs.DeleteKey("SessionId");
        PlayerPrefs.Save();

        LoadingController.LoadScene(cachedNextSceneName);
    }

    // Tải lại màn hiện tại để chơi lại
    public void TryAgain()
    {
        GameAudioManager.PlayUiClick();
        Time.timeScale = 1f;
        IsGameOver = false;

        PlayerPrefs.DeleteKey("SessionId");
        PlayerPrefs.Save();

        Scene currentScene = SceneManager.GetActiveScene();
        LoadingController.LoadScene(currentScene.name);
    }

    // Quay về Menu Chính sau khi kết thúc màn chơi
    public void ReturnToMainMenu()
    {
        GameAudioManager.PlayUiClick();
        Time.timeScale = 1f;
        IsGameOver = false;

        PlayerPrefs.DeleteKey("SessionId");
        PlayerPrefs.Save();

        if (Application.CanStreamedLevelBeLoaded("MainMenuScene"))
        {
            LoadingController.LoadScene("MainMenuScene");
        }
        else
        {
            LoadingController.LoadScene("LoginScene");
        }
    }

    // Ghi nhận kết thúc phiên chơi và thoát game sau khi đẩy telemetry
    public void QuitGame()
    {
        if (isQuitting) return;
        isQuitting = true;

        GameAudioManager.PlayUiClick();
        Time.timeScale = 1f;
        IsGameOver = false;

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.EndSessionAndQuit("Quit", PerformApplicationQuit);
        }
        else
        {
            PerformApplicationQuit();
        }
        Debug.Log("[GameOverManager] QUIT GAME");
    }

    private static void PerformApplicationQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
