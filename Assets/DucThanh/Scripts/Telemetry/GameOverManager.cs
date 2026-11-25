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
        public Button quitButton;
        public FloatingEmbersUI embersEffect;
    }

    private EndScreenRefs uiRefs;
    private CanvasGroup panelCanvasGroup;
    private string cachedNextSceneName;
    private float sessionStartRealtime;
    private float targetCardScale = 1f;
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
        ResolveTitleText();
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
        BuildEndScreenLayout(gameOverPanel, ref preservedTitle, destroyImmediate: false, out uiRefs);
        titleText = preservedTitle;

        ConfigureActionButton(uiRefs.nextLevelButton, NextLevel);
        ConfigureActionButton(uiRefs.tryAgainButton, TryAgain);
        ConfigureActionButton(uiRefs.quitButton, QuitGame);
    }

    // Khởi tạo cấu trúc UI màn hình Win/Lose gồm tiêu đề, 3 ô thống kê và hàng nút thao tác
    public static void BuildEndScreenLayout(
        GameObject panel,
        ref TMP_Text preservedTitle,
        bool destroyImmediate,
        out EndScreenRefs refs,
        Sprite softGlowSprite = null)
    {
        refs = default;
        if (panel == null) return;
        Sprite glowSprite = softGlowSprite != null ? softGlowSprite : FantasyUiThemeHelper.GetSoftDotSprite();

        RectTransform panelRt = panel.GetComponent<RectTransform>();
        if (panelRt != null)
        {
            FantasyUiThemeHelper.StretchFull(panelRt);
        }

        refs.backdropImage = panel.GetComponent<Image>();
        if (refs.backdropImage == null) refs.backdropImage = panel.AddComponent<Image>();
        refs.backdropImage.sprite = null;
        refs.backdropImage.raycastTarget = true;

        if (panel.GetComponent<CanvasGroup>() == null)
        {
            panel.AddComponent<CanvasGroup>();
        }

        if (preservedTitle == null)
        {
            Transform foundTitle = FindDescendantByName(panel.transform, "GameOverText");
            if (foundTitle != null) preservedTitle = foundTitle.GetComponent<TMP_Text>();
        }
        Transform oldTryAgain = FindDescendantByName(panel.transform, "TryAgainButton");
        Transform oldQuit = FindDescendantByName(panel.transform, "QuitButton");
        Transform oldNext = FindDescendantByName(panel.transform, "NextLevelButton");

        if (preservedTitle != null) preservedTitle.transform.SetParent(panel.transform, false);
        if (oldTryAgain != null) oldTryAgain.SetParent(panel.transform, false);
        if (oldQuit != null) oldQuit.SetParent(panel.transform, false);
        if (oldNext != null) oldNext.SetParent(panel.transform, false);

        DestroyNamedChild(panel.transform, "EndCard", destroyImmediate);
        DestroyNamedChild(panel.transform, "EndScreenEmbers", destroyImmediate);
        DestroyNamedChild(panel.transform, "EndScreenHalo", destroyImmediate);

        TMP_FontAsset sharedFont = preservedTitle != null
            ? preservedTitle.font
            : panel.GetComponentInChildren<TMP_Text>(true)?.font;

        GameObject haloGo = new GameObject("EndScreenHalo", typeof(RectTransform), typeof(Image));
        haloGo.transform.SetParent(panel.transform, false);
        RectTransform haloRt = haloGo.GetComponent<RectTransform>();
        haloRt.anchorMin = new Vector2(0.5f, 0.5f);
        haloRt.anchorMax = new Vector2(0.5f, 0.5f);
        haloRt.pivot = new Vector2(0.5f, 0.5f);
        haloRt.anchoredPosition = Vector2.zero;
        haloRt.sizeDelta = new Vector2(1480f, 1080f);
        refs.haloImage = haloGo.GetComponent<Image>();
        refs.haloImage.sprite = glowSprite;
        refs.haloImage.color = new Color(WinGold.r, WinGold.g, WinGold.b, 0.14f);
        refs.haloImage.raycastTarget = false;

        GameObject emberGo = new GameObject("EndScreenEmbers", typeof(RectTransform));
        emberGo.transform.SetParent(panel.transform, false);
        FantasyUiThemeHelper.StretchFull(emberGo.GetComponent<RectTransform>());
        refs.embersEffect = emberGo.AddComponent<FloatingEmbersUI>();

        GameObject cardGo = new GameObject("EndCard", typeof(RectTransform), typeof(Image));
        cardGo.transform.SetParent(panel.transform, false);
        refs.cardRect = cardGo.GetComponent<RectTransform>();
        refs.cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        refs.cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        refs.cardRect.pivot = new Vector2(0.5f, 0.5f);
        refs.cardRect.anchoredPosition = Vector2.zero;
        refs.cardRect.sizeDelta = new Vector2(EndCardWidth, EndCardHeight);

        refs.cardBgImage = cardGo.GetComponent<Image>();
        refs.cardBgImage.sprite = null;
        refs.cardBgImage.raycastTarget = false;

        refs.cardOutline = cardGo.AddComponent<Outline>();
        refs.cardOutline.effectDistance = new Vector2(2.2f, -2.2f);

        Shadow cardShadow = cardGo.AddComponent<Shadow>();
        cardShadow.effectColor = new Color(0f, 0f, 0f, 0.78f);
        cardShadow.effectDistance = new Vector2(0f, -22f);

        GameObject glowGo = new GameObject("HeaderGlowStrip", typeof(RectTransform), typeof(Image));
        glowGo.transform.SetParent(cardGo.transform, false);
        SetCardRect(glowGo.GetComponent<RectTransform>(), 0f, -66f, 720f, 110f);
        refs.headerGlowImage = glowGo.GetComponent<Image>();
        refs.headerGlowImage.sprite = glowSprite;
        refs.headerGlowImage.color = new Color(WinGold.r, WinGold.g, WinGold.b, 0.14f);
        refs.headerGlowImage.raycastTarget = false;

        refs.topAccentBar = FantasyUiThemeHelper.CreateAccentLine(cardGo.transform, "TopAccentBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 6f), WinGold).GetComponent<Image>();

        refs.bottomAccentBar = FantasyUiThemeHelper.CreateAccentLine(cardGo.transform, "BottomAccentBar",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            Vector2.zero, new Vector2(0f, 4f), WinGold).GetComponent<Image>();

        BuildEndCardOrnamentalFrame(cardGo.transform, WinGold, out refs.innerFrameLines, out refs.cornerBracketLines);

        GameObject badgePill = new GameObject("StatusBadgePill", typeof(RectTransform), typeof(Image));
        badgePill.transform.SetParent(cardGo.transform, false);
        SetCardRect(badgePill.GetComponent<RectTransform>(), 0f, -34f, 480f, 34f);
        refs.badgeBgImage = badgePill.GetComponent<Image>();
        refs.badgeBgImage.color = new Color(0.08f, 0.11f, 0.18f, 0.85f);
        refs.badgeBgImage.raycastTarget = false;
        refs.badgeOutline = badgePill.AddComponent<Outline>();
        refs.badgeOutline.effectColor = new Color(WinGold.r, WinGold.g, WinGold.b, 0.45f);
        refs.badgeOutline.effectDistance = new Vector2(1f, -1f);

        refs.badgeText = FantasyUiThemeHelper.CreateText(badgePill.transform, "StatusBadge", "CHIẾN DỊCH HOÀN TẤT  •  VANGUARD",
            14f, FontStyles.Bold, WinGoldBright, TextAlignmentOptions.Center, sharedFont);
        StretchWithPadding(refs.badgeText.rectTransform, 18f, 2f);
        ResetTmpAlignment(refs.badgeText, TextAlignmentOptions.Center, noWrap: true, minSize: 12f, maxSize: 15f, charSpacing: 3.5f);

        if (preservedTitle != null)
        {
            preservedTitle.transform.SetParent(cardGo.transform, false);
            refs.titleText = preservedTitle;
        }
        else
        {
            refs.titleText = FantasyUiThemeHelper.CreateText(cardGo.transform, "GameOverText", "CHIẾN THẮNG!",
                66f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, sharedFont);
            preservedTitle = refs.titleText;
        }
        SetCardRect(refs.titleText.rectTransform, 0f, -82f, 840f, 88f);
        ResetTmpAlignment(refs.titleText, TextAlignmentOptions.Center, noWrap: true, minSize: 38f, maxSize: 68f, charSpacing: 4f);
        refs.titleText.fontStyle = FontStyles.Bold;
        refs.titleText.color = Color.white;
        refs.titleText.raycastTarget = false;
        ApplyTitleGradient(refs.titleText, WinTitleTop, WinTitleBottom);

        Shadow titleShadow = refs.titleText.GetComponent<Shadow>();
        if (titleShadow == null) titleShadow = refs.titleText.gameObject.AddComponent<Shadow>();
        titleShadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        titleShadow.effectDistance = new Vector2(0f, -5f);

        refs.dividerParts = BuildOrnamentalDivider(cardGo.transform, -180f, WinGold);

        refs.subtitleText = FantasyUiThemeHelper.CreateText(cardGo.transform, "SubtitleText",
            "Đại Tướng Orc Warlord đã bị tiêu diệt. Mỏ Vàng và khu rừng đã được giải phóng!",
            19f, FontStyles.Normal, new Color(0.86f, 0.90f, 0.96f, 0.96f), TextAlignmentOptions.Center, sharedFont);
        SetCardRect(refs.subtitleText.rectTransform, 0f, -206f, 780f, 64f);
        ResetTmpAlignment(refs.subtitleText, TextAlignmentOptions.Center, noWrap: false, minSize: 15f, maxSize: 20f, charSpacing: 0.4f);
        refs.subtitleText.lineSpacing = 5f;

        GameObject statsRow = new GameObject("MatchStatsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        statsRow.transform.SetParent(cardGo.transform, false);
        SetCardRect(statsRow.GetComponent<RectTransform>(), 0f, -286f, 824f, 138f);

        HorizontalLayoutGroup hlg = statsRow.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 20f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        refs.tileAccentBars = new Image[3];
        refs.statStageValue = CreateEndStatTile(statsRow.transform, "TileStage", "TIẾN ĐỘ MÀN", "Màn 3 / 3", sharedFont, out refs.tileAccentBars[0], Color.white);
        refs.statScoreValue = CreateEndStatTile(statsRow.transform, "TileScore", "ĐIỂM  •  VÀNG", "0  •  0 G", sharedFont, out refs.tileAccentBars[1], WinGoldBright);
        refs.statTimeValue = CreateEndStatTile(statsRow.transform, "TileTime", "THỜI LƯỢNG", "00:00", sharedFont, out refs.tileAccentBars[2], Color.white);

        GameObject actionsRow = new GameObject("ActionButtonsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        actionsRow.transform.SetParent(cardGo.transform, false);
        SetCardRect(actionsRow.GetComponent<RectTransform>(), 0f, -454f, 824f, 66f);

        HorizontalLayoutGroup btnLayout = actionsRow.GetComponent<HorizontalLayoutGroup>();
        btnLayout.spacing = 20f;
        btnLayout.childAlignment = TextAnchor.MiddleCenter;
        btnLayout.childControlWidth = true;
        btnLayout.childControlHeight = true;
        btnLayout.childForceExpandWidth = true;
        btnLayout.childForceExpandHeight = true;

        refs.nextLevelButton = PrepareActionButton(oldNext, actionsRow.transform, "NextLevelButton", "MÀN TIẾP THEO →", sharedFont);
        refs.tryAgainButton = PrepareActionButton(oldTryAgain, actionsRow.transform, "TryAgainButton", "CHƠI LẠI CHIẾN DỊCH", sharedFont);
        refs.quitButton = PrepareActionButton(oldQuit, actionsRow.transform, "QuitButton", "THOÁT GAME", sharedFont);

        refs.nextLevelButton.gameObject.SetActive(false);
    }

    private static Button PrepareActionButton(Transform existing, Transform parent, string defaultName, string defaultLabel, TMP_FontAsset font)
    {
        Button btn;
        if (existing != null)
        {
            existing.SetParent(parent, false);
            btn = existing.GetComponent<Button>();
            if (btn == null) btn = existing.gameObject.AddComponent<Button>();
        }
        else
        {
            GameObject go = new GameObject(defaultName, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            btn = go.GetComponent<Button>();
        }

        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.sizeDelta = new Vector2(260f, 66f);

        TMP_Text labelTmp = btn.GetComponentInChildren<TMP_Text>(true);
        if (labelTmp == null)
        {
            labelTmp = FantasyUiThemeHelper.CreateText(btn.transform, "Text (TMP)", defaultLabel, 20f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font);
        }
        if (font != null && labelTmp.font == null) labelTmp.font = font;

        return btn;
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
        RectTransform panelRt = gameOverPanel != null ? gameOverPanel.GetComponent<RectTransform>() : null;
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
        if (!isAnimatingIn && uiRefs.cardRect != null)
        {
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
        if (uiRefs.cardRect != null) uiRefs.cardRect.localScale = Vector3.one * targetCardScale;
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

    public static void BuildEndCardOrnamentalFrame(
        Transform cardParent,
        Color accent,
        out Image[] innerFrameLines,
        out Image[] cornerBrackets)
    {
        GameObject frameGo = new GameObject("InnerFrame", typeof(RectTransform));
        frameGo.transform.SetParent(cardParent, false);
        RectTransform frt = frameGo.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(14f, 14f);
        frt.offsetMax = new Vector2(-14f, -14f);

        Color lineCol = new Color(accent.r, accent.g, accent.b, 0.24f);
        innerFrameLines = new[]
        {
            CreateStretchLine(frameGo.transform, "InnerTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1.5f), lineCol),
            CreateStretchLine(frameGo.transform, "InnerBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 1.5f), lineCol),
            CreateStretchLine(frameGo.transform, "InnerLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(1.5f, 0f), lineCol),
            CreateStretchLine(frameGo.transform, "InnerRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(1.5f, 0f), lineCol)
        };

        Color bracketCol = new Color(accent.r, accent.g, accent.b, 0.88f);
        var brackets = new Image[8];
        Vector2[] anchors =
        {
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0f, 0f),
            new Vector2(1f, 0f)
        };
        for (int i = 0; i < anchors.Length; i++)
        {
            Vector2 a = anchors[i];
            brackets[i * 2] = CreateCornerSegment(frameGo.transform, $"CornerH_{i}", a, new Vector2(20f, 3f), bracketCol);
            brackets[i * 2 + 1] = CreateCornerSegment(frameGo.transform, $"CornerV_{i}", a, new Vector2(3f, 20f), bracketCol);
        }
        cornerBrackets = brackets;
    }

    public static Image[] BuildOrnamentalDivider(Transform cardParent, float yFromTop, Color accent)
    {
        GameObject divGo = new GameObject("TitleDivider", typeof(RectTransform));
        divGo.transform.SetParent(cardParent, false);
        SetCardRect(divGo.GetComponent<RectTransform>(), 0f, yFromTop, 400f, 16f);

        Image leftBar = FantasyUiThemeHelper.CreateAccentLine(divGo.transform, "LeftBar",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            Vector2.zero, new Vector2(174f, 2f), new Color(accent.r, accent.g, accent.b, 0.85f)).GetComponent<Image>();

        Image rightBar = FantasyUiThemeHelper.CreateAccentLine(divGo.transform, "RightBar",
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            Vector2.zero, new Vector2(174f, 2f), new Color(accent.r, accent.g, accent.b, 0.85f)).GetComponent<Image>();

        GameObject diamondGo = FantasyUiThemeHelper.CreateAccentLine(divGo.transform, "CenterDiamond",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(10f, 10f), accent);
        diamondGo.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Image diamondImg = diamondGo.GetComponent<Image>();

        return new[] { leftBar, rightBar, diamondImg };
    }

    public static TMP_Text CreateEndStatTile(
        Transform parent,
        string name,
        string label,
        string defaultValue,
        TMP_FontAsset font,
        out Image accentStrip,
        Color valueColor)
    {
        GameObject tile = new GameObject(name, typeof(RectTransform), typeof(Image));
        tile.transform.SetParent(parent, false);

        Image bg = tile.GetComponent<Image>();
        bg.color = new Color(0.080f, 0.110f, 0.180f, 0.90f);
        bg.raycastTarget = false;

        Outline outline = tile.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.14f);
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        GameObject sheen = FantasyUiThemeHelper.CreateAccentLine(tile.transform, "TopSheen",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 24f), new Color(1f, 1f, 1f, 0.03f));
        sheen.GetComponent<Image>().raycastTarget = false;

        accentStrip = FantasyUiThemeHelper.CreateAccentLine(tile.transform, "AccentStrip",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 3f), WinGold).GetComponent<Image>();

        TMP_Text lbl = FantasyUiThemeHelper.CreateText(tile.transform, "Label", label,
            14f, FontStyles.Bold, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.Center, font);
        RectTransform lrt = lbl.rectTransform;
        lrt.anchorMin = new Vector2(0f, 1f);
        lrt.anchorMax = new Vector2(1f, 1f);
        lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0f, -18f);
        lrt.sizeDelta = new Vector2(-16f, 24f);
        ResetTmpAlignment(lbl, TextAlignmentOptions.Center, noWrap: true, minSize: 11f, maxSize: 14f, charSpacing: 2f);

        TMP_Text val = FantasyUiThemeHelper.CreateText(tile.transform, "Value", defaultValue,
            28f, FontStyles.Bold, valueColor, TextAlignmentOptions.Center, font);
        RectTransform vrt = val.rectTransform;
        vrt.anchorMin = new Vector2(0f, 0f);
        vrt.anchorMax = new Vector2(1f, 1f);
        vrt.offsetMin = new Vector2(14f, 14f);
        vrt.offsetMax = new Vector2(-14f, -48f);
        ResetTmpAlignment(val, TextAlignmentOptions.Center, noWrap: true, minSize: 18f, maxSize: 29f, charSpacing: 0.5f);

        return val;
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
            StretchWithPadding(txt.rectTransform, 14f, 6f);
            ResetTmpAlignment(txt, TextAlignmentOptions.Center, noWrap: true, minSize: 15f, maxSize: 21f, charSpacing: 1.5f);
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

    private static void DestroyNamedChild(Transform parent, string childName, bool immediate)
    {
        Transform child = parent.Find(childName);
        if (child == null) return;
        if (immediate) DestroyImmediate(child.gameObject);
        else Destroy(child.gameObject);
    }

    private static Image CreateStretchLine(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 sizeDelta, Color color)
    {
        GameObject go = FantasyUiThemeHelper.CreateAccentLine(parent, name, aMin, aMax, pivot, Vector2.zero, sizeDelta, color);
        return go.GetComponent<Image>();
    }

    private static Image CreateCornerSegment(Transform parent, string name, Vector2 cornerAnchor, Vector2 size, Color color)
    {
        GameObject go = FantasyUiThemeHelper.CreateAccentLine(parent, name, cornerAnchor, cornerAnchor, cornerAnchor, Vector2.zero, size, color);
        return go.GetComponent<Image>();
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

    private static void SetCardRect(RectTransform rt, float x, float yFromTop, float w, float h)
    {
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, yFromTop);
        rt.sizeDelta = new Vector2(w, h);
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
