using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Quản lý màn hình Kết Thúc (Victory / Game Over) phong cách Action RPG AAA cỡ lớn:
/// - Khung kính tối Obsidian cỡ lớn (900 x 640) cân đối, 3 ô thông số bằng nhau tuyệt đối bằng HorizontalLayoutGroup.
/// - Phông chữ to rõ, đều đặn, tự co giãn hợp lý không tràn viền.
/// - Hai chủ đề riêng biệt: Vàng Kim (Chiến Thắng) / Đỏ Huyết (Thất Bại) kèm hạt tàn lửa bay và hiệu ứng mở mượt.
/// </summary>
public class GameOverManager : MonoBehaviour
{
    public static GameOverManager Instance { get; private set; }
    public static bool IsGameOver { get; private set; }

    [Header("UI Reference")]
    public GameObject gameOverPanel;
    public TMP_Text titleText;

    private RectTransform cardRect;
    private Image backdropImage;
    private Image cardBgImage;
    private Outline cardOutline;
    private Image topAccentBar;
    private Image bottomAccentBar;
    private Image dividerBar;
    private Image[] tileAccentBars;
    private TMP_Text badgeText;
    private TMP_Text subtitleText;
    private TMP_Text statStageValue;
    private TMP_Text statScoreValue;
    private TMP_Text statTimeValue;
    private TMP_Text telemetryNoteText;
    private Button tryAgainButton;
    private Button quitButton;
    private FloatingEmbersUI embersEffect;
    private CanvasGroup panelCanvasGroup;

    private float sessionStartRealtime;
    private bool uiBuilt;

    private static readonly Color WinGold = new Color(0.95f, 0.76f, 0.28f, 1f);
    private static readonly Color WinGoldBright = new Color(1f, 0.88f, 0.48f, 1f);
    private static readonly Color WinBackdrop = new Color(0.03f, 0.05f, 0.09f, 0.86f);
    private static readonly Color WinCardBg = new Color(0.052f, 0.072f, 0.12f, 0.95f);

    private static readonly Color LoseCrimson = new Color(0.88f, 0.22f, 0.24f, 1f);
    private static readonly Color LoseCrimsonBright = new Color(1f, 0.36f, 0.36f, 1f);
    private static readonly Color LoseBackdrop = new Color(0.095f, 0.024f, 0.035f, 0.90f);
    private static readonly Color LoseCardBg = new Color(0.085f, 0.038f, 0.052f, 0.95f);

    private void Awake()
    {
        Instance = this;
        IsGameOver = false;
        sessionStartRealtime = Time.realtimeSinceStartup;
        ResolveTitleText();
    }

    private void ResolveTitleText()
    {
        if (titleText != null || gameOverPanel == null) return;

        Transform found = gameOverPanel.transform.Find("GameOverText");
        if (found == null) found = gameOverPanel.transform.Find("EndCard/GameOverText");
        if (found != null)
        {
            titleText = found.GetComponent<TMP_Text>();
        }
    }

    [ContextMenu("Preview Game Over (Thua)")]
    public void ShowGameOver()
    {
        ShowEndScreen(false);
        GameAudioManager.PlayGameOver();
        Debug.Log("[GameOverManager] GAME OVER - Game Paused");
    }

    [ContextMenu("Preview Victory (Thắng)")]
    public void ShowVictory()
    {
        ShowEndScreen(true);
        GameAudioManager.PlayVictory();
        Debug.Log("[GameOverManager] VICTORY - Game Paused");
    }

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

        StartCoroutine(AnimateEndScreenIn());
    }

    private void EnsureStyledEndScreen()
    {
        if (uiBuilt || gameOverPanel == null) return;
        uiBuilt = true;

        RectTransform panelRt = gameOverPanel.GetComponent<RectTransform>();
        if (panelRt != null)
        {
            FantasyUiThemeHelper.StretchFull(panelRt);
        }

        backdropImage = gameOverPanel.GetComponent<Image>();
        if (backdropImage == null) backdropImage = gameOverPanel.AddComponent<Image>();
        backdropImage.sprite = null;
        backdropImage.raycastTarget = true;

        panelCanvasGroup = gameOverPanel.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null) panelCanvasGroup = gameOverPanel.AddComponent<CanvasGroup>();

        // Bảo toàn GameOverText, TryAgainButton, QuitButton (kể cả khi trước đó đã bake trong EndCard cũ)
        ResolveTitleText();
        Transform oldTryAgain = gameOverPanel.transform.Find("TryAgainButton") ?? gameOverPanel.transform.Find("EndCard/TryAgainButton");
        Transform oldQuit = gameOverPanel.transform.Find("QuitButton") ?? gameOverPanel.transform.Find("EndCard/QuitButton");

        if (titleText != null) titleText.transform.SetParent(gameOverPanel.transform, false);
        if (oldTryAgain != null) oldTryAgain.SetParent(gameOverPanel.transform, false);
        if (oldQuit != null) oldQuit.SetParent(gameOverPanel.transform, false);

        // Xoá khung EndCard / Embers cũ (nếu có) để dựng lại đúng kích thước lớn 900x640 cân đối
        Transform oldCard = gameOverPanel.transform.Find("EndCard");
        if (oldCard != null) Destroy(oldCard.gameObject);
        Transform oldEmbers = gameOverPanel.transform.Find("EndScreenEmbers");
        if (oldEmbers != null) Destroy(oldEmbers.gameObject);

        TMP_FontAsset sharedFont = titleText != null ? titleText.font : gameOverPanel.GetComponentInChildren<TMP_Text>(true)?.font;

        // 1. Hạt tàn lửa bay nền
        GameObject emberGo = new GameObject("EndScreenEmbers", typeof(RectTransform));
        emberGo.transform.SetParent(gameOverPanel.transform, false);
        FantasyUiThemeHelper.StretchFull(emberGo.GetComponent<RectTransform>());
        embersEffect = emberGo.AddComponent<FloatingEmbersUI>();

        // 2. Thẻ kính tối cỡ lớn trung tâm (900 x 640)
        GameObject cardGo = new GameObject("EndCard", typeof(RectTransform), typeof(Image));
        cardGo.transform.SetParent(gameOverPanel.transform, false);
        cardRect = cardGo.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.anchoredPosition = Vector2.zero;
        cardRect.sizeDelta = new Vector2(900f, 640f);

        cardBgImage = cardGo.GetComponent<Image>();
        cardBgImage.sprite = null;

        cardOutline = cardGo.AddComponent<Outline>();
        cardOutline.effectDistance = new Vector2(2.4f, -2.4f);

        Shadow cardShadow = cardGo.AddComponent<Shadow>();
        cardShadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
        cardShadow.effectDistance = new Vector2(0f, -18f);

        // Thanh viền nhấn trên & dưới
        topAccentBar = FantasyUiThemeHelper.CreateAccentLine(cardGo.transform, "TopAccentBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 6f), WinGold).GetComponent<Image>();

        bottomAccentBar = FantasyUiThemeHelper.CreateAccentLine(cardGo.transform, "BottomAccentBar",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            Vector2.zero, new Vector2(0f, 4f), WinGold).GetComponent<Image>();

        // Badge trạng thái
        badgeText = FantasyUiThemeHelper.CreateText(cardGo.transform, "StatusBadge", "VANGUARD  •  RAID OF THE ORC WOODS",
            18f, FontStyles.Bold, WinGoldBright, TextAlignmentOptions.Center, sharedFont);
        SetCardRect(badgeText.rectTransform, 0f, -40f, 800f, 28f);
        badgeText.characterSpacing = 6f;

        // Tiêu đề chính cỡ lớn (72pt)
        if (titleText != null)
        {
            titleText.transform.SetParent(cardGo.transform, false);
        }
        else
        {
            titleText = FantasyUiThemeHelper.CreateText(cardGo.transform, "GameOverText", "CHIẾN THẮNG!",
                72f, FontStyles.Bold, WinGoldBright, TextAlignmentOptions.Center, sharedFont);
        }
        SetCardRect(titleText.rectTransform, 0f, -80f, 820f, 92f);
        titleText.fontSize = 72f;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.characterSpacing = 8f;
        if (titleText.GetComponent<Shadow>() == null)
        {
            Shadow ts = titleText.gameObject.AddComponent<Shadow>();
            ts.effectColor = new Color(0f, 0f, 0f, 0.82f);
            ts.effectDistance = new Vector2(0f, -5f);
        }

        // Đường kẻ trang trí cân đối
        dividerBar = FantasyUiThemeHelper.CreateAccentLine(cardGo.transform, "TitleDivider",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -182f), new Vector2(280f, 3f), WinGold).GetComponent<Image>();

        // Dòng mô tả rõ ràng (21pt)
        subtitleText = FantasyUiThemeHelper.CreateText(cardGo.transform, "SubtitleText", "",
            21f, FontStyles.Normal, new Color(0.84f, 0.88f, 0.94f, 0.95f), TextAlignmentOptions.Center, sharedFont);
        SetCardRect(subtitleText.rectTransform, 0f, -202f, 780f, 58f);

        // Khung 3 ô thông số đều tăm tắp bằng HorizontalLayoutGroup (810 x 132)
        GameObject statsRow = new GameObject("MatchStatsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        statsRow.transform.SetParent(cardGo.transform, false);
        SetCardRect(statsRow.GetComponent<RectTransform>(), 0f, -280f, 810f, 132f);

        HorizontalLayoutGroup hlg = statsRow.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 22f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        tileAccentBars = new Image[3];
        statStageValue = CreateStatTile(statsRow.transform, "TileStage", "TIẾN ĐỘ MÀN", "1 / 3", sharedFont, out tileAccentBars[0]);
        statScoreValue = CreateStatTile(statsRow.transform, "TileScore", "ĐIỂM  •  VÀNG", "0  •  0 G", sharedFont, out tileAccentBars[1]);
        statTimeValue = CreateStatTile(statsRow.transform, "TileTime", "THỜI LƯỢNG", "00:00", sharedFont, out tileAccentBars[2]);

        // Dòng xác nhận Telemetry
        telemetryNoteText = FantasyUiThemeHelper.CreateText(cardGo.transform, "TelemetryNote",
            "✓ Kết quả trận đấu đã được lưu lên hệ thống Aegis Telemetry",
            16f, FontStyles.Italic, new Color(0.52f, 0.92f, 0.68f, 0.92f), TextAlignmentOptions.Center, sharedFont);
        SetCardRect(telemetryNoteText.rectTransform, 0f, -438f, 780f, 30f);

        // 2 nút bấm lớn cân xứng (mỗi nút 360 x 68)
        if (oldTryAgain != null)
        {
            oldTryAgain.SetParent(cardGo.transform, false);
            tryAgainButton = oldTryAgain.GetComponent<Button>();
            SetCardRect(oldTryAgain.GetComponent<RectTransform>(), -195f, -504f, 360f, 68f);
            ConfigureActionButton(tryAgainButton, TryAgain);
        }

        if (oldQuit != null)
        {
            oldQuit.SetParent(cardGo.transform, false);
            quitButton = oldQuit.GetComponent<Button>();
            SetCardRect(oldQuit.GetComponent<RectTransform>(), 195f, -504f, 360f, 68f);
            ConfigureActionButton(quitButton, QuitGame);
        }
    }

    private static void ConfigureActionButton(Button btn, UnityEngine.Events.UnityAction fallbackAction)
    {
        if (btn == null) return;

        btn.interactable = true;
        btn.transform.SetAsLastSibling();

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

    private void ApplyEndScreenTheme(bool isVictory)
    {
        Color accent = isVictory ? WinGold : LoseCrimson;
        Color accentBright = isVictory ? WinGoldBright : LoseCrimsonBright;

        if (backdropImage != null) backdropImage.color = isVictory ? WinBackdrop : LoseBackdrop;
        if (cardBgImage != null) cardBgImage.color = isVictory ? WinCardBg : LoseCardBg;
        if (cardOutline != null) cardOutline.effectColor = new Color(accent.r, accent.g, accent.b, 0.78f);
        if (topAccentBar != null) topAccentBar.color = accent;
        if (bottomAccentBar != null) bottomAccentBar.color = new Color(accent.r, accent.g, accent.b, 0.68f);
        if (dividerBar != null) dividerBar.color = accent;

        if (tileAccentBars != null)
        {
            for (int i = 0; i < tileAccentBars.Length; i++)
            {
                if (tileAccentBars[i] != null)
                    tileAccentBars[i].color = new Color(accent.r, accent.g, accent.b, 0.85f);
            }
        }

        if (badgeText != null)
        {
            badgeText.text = isVictory
                ? "CHIẾN DỊCH HOÀN TẤT  •  VANGUARD"
                : "CHIẾN BINH ĐÃ GỤC NGÃ  •  VANGUARD";
            badgeText.color = accentBright;
        }

        if (titleText != null)
        {
            titleText.text = isVictory ? "CHIẾN THẮNG!" : "THẤT BẠI";
            titleText.color = accentBright;
        }

        if (subtitleText != null)
        {
            subtitleText.text = isVictory
                ? "Đại Tướng Orc Warlord đã bị tiêu diệt. Mỏ Vàng và khu rừng đã được giải phóng!"
                : "Bóng tối Rừng Orc đã hạ gục bạn. Hãy đứng dậy, giữ Chuột Phải đỡ đòn và phục thù!";
        }

        if (tryAgainButton != null)
        {
            FantasyUiThemeHelper.StyleButton(tryAgainButton, isVictory ? "CHƠI LẠI CHIẾN DỊCH" : "THỬ LẠI NGAY", true, 360f, 68f);
            TMP_Text txt = tryAgainButton.GetComponentInChildren<TMP_Text>(true);
            if (txt != null) txt.fontSize = 23f;

            if (!isVictory)
            {
                Image btnImg = tryAgainButton.GetComponent<Image>();
                if (btnImg != null) btnImg.color = LoseCrimson;
                if (txt != null) txt.color = Color.white;
            }
        }

        if (quitButton != null)
        {
            FantasyUiThemeHelper.StyleButton(quitButton, "THOÁT GAME", false, 360f, 68f);
            TMP_Text txt = quitButton.GetComponentInChildren<TMP_Text>(true);
            if (txt != null) txt.fontSize = 22f;
        }

        if (embersEffect != null && embersEffect.transform.childCount == 0)
        {
            if (isVictory)
            {
                embersEffect.Initialize(32, 0.05f, 0.95f, new Color(1f, 0.86f, 0.32f, 1f), new Color(0.35f, 0.98f, 0.62f, 1f));
            }
            else
            {
                embersEffect.Initialize(26, 0.05f, 0.95f, new Color(0.95f, 0.22f, 0.18f, 1f), new Color(0.55f, 0.12f, 0.14f, 1f));
            }
        }
    }

    private void PopulateMatchStats(bool isVictory)
    {
        int stage = StageManager.Instance != null ? StageManager.Instance.CurrentStage : (TelemetryManager.Instance != null ? TelemetryManager.Instance.CurrentLevel : 1);
        int score = ScoreManager.Instance != null ? ScoreManager.Instance.score : 0;
        int totalGold = ScoreManager.Instance != null ? ScoreManager.Instance.totalGoldEarned : 0;

        float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - sessionStartRealtime);
        int mins = Mathf.FloorToInt(elapsed / 60f);
        int secs = Mathf.FloorToInt(elapsed % 60f);

        if (statStageValue != null) statStageValue.text = isVictory ? "Màn 3 / 3" : $"Màn {stage} / 3";
        if (statScoreValue != null) statScoreValue.text = $"{score:N0}  •  {totalGold:N0} G";
        if (statTimeValue != null) statTimeValue.text = $"{mins:00}:{secs:00}";

        if (telemetryNoteText != null)
        {
            string resTag = isVictory ? "Win" : "Lose";
            telemetryNoteText.text = $"✓ Kết quả [{resTag}] đã được đồng bộ lên hệ thống Aegis Telemetry";
            telemetryNoteText.color = isVictory
                ? new Color(0.45f, 0.95f, 0.65f, 0.94f)
                : new Color(0.92f, 0.72f, 0.72f, 0.90f);
        }
    }

    private IEnumerator AnimateEndScreenIn()
    {
        if (panelCanvasGroup != null) panelCanvasGroup.alpha = 0f;
        if (cardRect != null) cardRect.localScale = Vector3.one * 0.90f;

        float t = 0f;
        const float duration = 0.30f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            float easeOut = 1f - Mathf.Pow(1f - k, 3f);

            if (panelCanvasGroup != null) panelCanvasGroup.alpha = easeOut;
            if (cardRect != null) cardRect.localScale = Vector3.Lerp(Vector3.one * 0.90f, Vector3.one, easeOut);
            yield return null;
        }

        if (panelCanvasGroup != null) panelCanvasGroup.alpha = 1f;
        if (cardRect != null) cardRect.localScale = Vector3.one;
    }

    private static TMP_Text CreateStatTile(Transform parent, string name, string label, string defaultValue, TMP_FontAsset font, out Image accentStrip)
    {
        GameObject tile = new GameObject(name, typeof(RectTransform), typeof(Image));
        tile.transform.SetParent(parent, false);

        Image bg = tile.GetComponent<Image>();
        bg.color = new Color(0.085f, 0.115f, 0.185f, 0.88f);

        Outline outline = tile.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.14f);
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        accentStrip = FantasyUiThemeHelper.CreateAccentLine(tile.transform, "AccentStrip",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 3f), WinGold).GetComponent<Image>();

        TMP_Text lbl = FantasyUiThemeHelper.CreateText(tile.transform, "Label", label,
            15f, FontStyles.Bold, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.Center, font);
        RectTransform lrt = lbl.rectTransform;
        lrt.anchorMin = new Vector2(0f, 1f);
        lrt.anchorMax = new Vector2(1f, 1f);
        lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0f, -20f);
        lrt.sizeDelta = new Vector2(-16f, 26f);
        lbl.characterSpacing = 2f;

        TMP_Text val = FantasyUiThemeHelper.CreateText(tile.transform, "Value", defaultValue,
            28f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font);
        RectTransform vrt = val.rectTransform;
        vrt.anchorMin = new Vector2(0f, 0f);
        vrt.anchorMax = new Vector2(1f, 1f);
        vrt.offsetMin = new Vector2(12f, 14f);
        vrt.offsetMax = new Vector2(-12f, -50f);
        val.enableAutoSizing = true;
        val.fontSizeMin = 20f;
        val.fontSizeMax = 30f;

        return val;
    }

    private static void SetCardRect(RectTransform rt, float x, float yFromTop, float w, float h)
    {
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, yFromTop);
        rt.sizeDelta = new Vector2(w, h);
    }

    public void TryAgain()
    {
        GameAudioManager.PlayUiClick();
        Time.timeScale = 1f;
        IsGameOver = false;

        PlayerPrefs.DeleteKey("SessionId");
        PlayerPrefs.Save();

        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }

    public void QuitGame()
    {
        GameAudioManager.PlayUiClick();
        TelemetryManager.Instance?.TrackGameEnded("Quit");
        Time.timeScale = 1f;
        IsGameOver = false;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
        Debug.Log("[GameOverManager] QUIT GAME");
    }
}
