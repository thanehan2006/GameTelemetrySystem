using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Điều khiển Menu Chính tách riêng thành Scene độc lập ngay sau màn hình Đăng nhập.
/// Sử dụng bộ khung viền & biểu tượng Kenney CC0 kết hợp Key Art Dark Fantasy.
/// </summary>
public class MainMenuSceneController : MonoBehaviour
{
    [Header("Scene Setup")]
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private string defaultPlayScene = "PickCharacter";
    [SerializeField] private string loginSceneName = "LoginScene";

    private RectTransform canvasRt;
    private RectTransform keyArtRect;
    private RectTransform menuCardRt;
    private TMP_Text warriorPlaqueText;
    private bool isTransitioning;

    private void Start()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        GameSettingsManager.EnsureInitialized();
        GameMenuManager.EnsureInstance();

        ResolveTargetCanvas();
        if (targetCanvas != null)
        {
            FantasyUiThemeHelper.ConfigureResponsiveCanvas(targetCanvas);
            canvasRt = targetCanvas.GetComponent<RectTransform>();
            BuildMainMenuLayout();
        }
    }

    private void Update()
    {
        if (keyArtRect != null)
        {
            float scale = 1.0f + Mathf.PingPong(Time.unscaledTime * 0.008f, 0.045f);
            keyArtRect.localScale = new Vector3(scale, scale, 1f);
        }

        if (menuCardRt != null && canvasRt != null)
        {
            Vector2 size = canvasRt.rect.size;
            if (size.x > 100f && size.y > 100f)
            {
                float fit = Mathf.Min(1f, Mathf.Min((size.x * 0.44f) / 530f, (size.y * 0.88f) / 620f));
                menuCardRt.localScale = Vector3.one * fit;
            }
        }
    }

    private void ResolveTargetCanvas()
    {
        if (targetCanvas != null && targetCanvas.gameObject.scene == gameObject.scene) return;

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null && canvases[i].gameObject.scene == gameObject.scene)
            {
                targetCanvas = canvases[i];
                return;
            }
        }

        GameObject cGo = new GameObject("MainMenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        targetCanvas = cGo.GetComponent<Canvas>();
        targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
    }

    private void BuildMainMenuLayout()
    {
        Transform root = targetCanvas.transform;

        RawImage keyArt = FantasyUiThemeHelper.CreateFullscreenKeyArt(root, "UI/LoginBg", "MainMenuKeyArt");
        keyArtRect = keyArt.rectTransform;

        Image rightVeil = FantasyUiThemeHelper.CreateGradientOverlay(root, "MainMenuRightGradient", true, new Color(0.016f, 0.014f, 0.014f, 0.94f));
        rightVeil.transform.SetSiblingIndex(1);

        GameObject emberLayer = new GameObject("MainMenuEmbers", typeof(RectTransform));
        emberLayer.transform.SetParent(root, false);
        emberLayer.transform.SetSiblingIndex(2);
        FantasyUiThemeHelper.StretchFull(emberLayer.GetComponent<RectTransform>());
        FloatingEmbersUI embers = emberLayer.AddComponent<FloatingEmbersUI>();
        embers.Initialize(28, 0.04f, 0.44f, new Color(1f, 0.62f, 0.16f, 1f), new Color(1f, 0.88f, 0.45f, 1f));

        BuildLeftHeroBranding(root);
        BuildRightCommandPanel(root);
    }

    private static void BuildLeftHeroBranding(Transform root)
    {
        GameObject brandGroup = new GameObject("HeroBrandGroup", typeof(RectTransform));
        brandGroup.transform.SetParent(root, false);
        RectTransform brandRt = brandGroup.GetComponent<RectTransform>();
        brandRt.anchorMin = new Vector2(0f, 1f);
        brandRt.anchorMax = new Vector2(0f, 1f);
        brandRt.pivot = new Vector2(0f, 1f);
        brandRt.anchoredPosition = new Vector2(82f, -56f);
        brandRt.sizeDelta = new Vector2(720f, 175f);

        TMP_Text badge = FantasyUiThemeHelper.CreateText(brandGroup.transform, "BrandBadge", "HÀNH ĐỘNG SINH TỒN  •  DARK FANTASY",
            15.5f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.Left);
        SetTopLeftRect(badge.rectTransform, 0f, 0f, 680f, 24f);
        badge.characterSpacing = 5.5f;

        TMP_Text title = FantasyUiThemeHelper.CreateText(brandGroup.transform, "HeroTitle", "VANGUARD",
            66f, FontStyles.Bold, Color.white, TextAlignmentOptions.Left);
        SetTopLeftRect(title.rectTransform, -3f, -26f, 680f, 76f);
        title.characterSpacing = 9f;
        MedievalSettingsArt.ApplyTitleGoldGradient(title);
        AddShadow(title, -4f);

        FantasyUiThemeHelper.CreateAccentLine(brandGroup.transform, "HeroGoldLine",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, -106f), new Vector2(240f, 3f), MedievalSettingsArt.TitleGoldBottom);

        TMP_Text sub = FantasyUiThemeHelper.CreateText(brandGroup.transform, "HeroSubtitle", "CUỘC TRUY QUÉT RỪNG ORC",
            22f, FontStyles.Bold, MedievalSettingsArt.IvoryText, TextAlignmentOptions.Left);
        SetTopLeftRect(sub.rectTransform, 0f, -118f, 680f, 34f);
        sub.characterSpacing = 5f;

        TMP_Text footerTag = FantasyUiThemeHelper.CreateText(root, "MainMenuFooterTag",
            "3 CỨ ĐIỂM CHIẾN DỊCH   •   ĐẤU TRƯỜNG BOSS   •   VANGUARD",
            15f, FontStyles.Bold, new Color(0.84f, 0.78f, 0.68f, 0.78f), TextAlignmentOptions.Left);
        RectTransform ftRt = footerTag.rectTransform;
        ftRt.anchorMin = new Vector2(0f, 0f);
        ftRt.anchorMax = new Vector2(0f, 0f);
        ftRt.pivot = new Vector2(0f, 0f);
        ftRt.anchoredPosition = new Vector2(82f, 38f);
        ftRt.sizeDelta = new Vector2(860f, 28f);
        footerTag.characterSpacing = 2f;
    }

    private void BuildRightCommandPanel(Transform root)
    {
        GameObject card = MedievalSettingsArt.CreateFramedPanel(
            root, "MainMenuCard", 492f, 584f,
            MedievalSettingsArt.PanelGothic, MedievalSettingsArt.FrameObsidian,
            MedievalSettingsArt.BorderGothic, MedievalSettingsArt.GoldOrnament,
            raycastTarget: true);

        menuCardRt = card.GetComponent<RectTransform>();
        menuCardRt.anchorMin = new Vector2(1f, 0.5f);
        menuCardRt.anchorMax = new Vector2(1f, 0.5f);
        menuCardRt.pivot = new Vector2(1f, 0.5f);
        menuCardRt.anchoredPosition = new Vector2(-92f, -6f);

        GameObject body = new GameObject("MainMenuBody", typeof(RectTransform), typeof(VerticalLayoutGroup));
        body.transform.SetParent(card.transform, false);
        RectTransform bodyRt = body.GetComponent<RectTransform>();
        bodyRt.anchorMin = Vector2.zero;
        bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = new Vector2(40f, 34f);
        bodyRt.offsetMax = new Vector2(-40f, -34f);

        VerticalLayoutGroup vlg = body.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 11f;
        vlg.padding = new RectOffset(0, 0, 4, 4);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;

        TMP_Text kicker = FantasyUiThemeHelper.CreateText(body.transform, "MenuKicker", "✦  ĐIỆN CHỈ HUY  ✦",
            13.5f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.Center);
        kicker.textWrappingMode = TextWrappingModes.NoWrap;
        kicker.characterSpacing = 3.5f;
        MedievalSettingsArt.SetFixedSize(kicker.gameObject, 404f, 22f);

        TMP_Text headerTitle = FantasyUiThemeHelper.CreateText(body.transform, "MenuTitle", "MENU CHÍNH",
            34f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        headerTitle.textWrappingMode = TextWrappingModes.NoWrap;
        headerTitle.characterSpacing = 4.5f;
        MedievalSettingsArt.ApplyTitleGoldGradient(headerTitle);
        AddShadow(headerTitle, -2.5f);
        MedievalSettingsArt.SetFixedSize(headerTitle.gameObject, 404f, 44f);

        MedievalSettingsArt.CreateSymmetricalDivider(
            body.transform, "MenuOrnateDivider", 376f, 16f, MedievalSettingsArt.GoldOrnament, ornate: true);

        // Thẻ tên chiến binh đã đăng nhập (1 dòng gọn gàng, không bọc chữ xấu)
        GameObject plaque = MedievalSettingsArt.CreateFramedPanel(
            body.transform, "WarriorPlaque", 404f, 42f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.InnerObsidian,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.BronzeBorder);

        string playerName = GameMenuManager.LoggedInPlayerName;
        string plaqueLabel = string.IsNullOrEmpty(playerName)
            ? "◆  CHIẾN BINH: VANGUARD"
            : $"◆  CHIẾN BINH: {playerName.ToUpperInvariant()}";

        warriorPlaqueText = FantasyUiThemeHelper.CreateText(plaque.transform, "WarriorText", plaqueLabel,
            15.5f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.Center);
        RectTransform wRt = warriorPlaqueText.rectTransform;
        wRt.anchorMin = Vector2.zero;
        wRt.anchorMax = Vector2.one;
        wRt.offsetMin = new Vector2(14f, 0f);
        wRt.offsetMax = new Vector2(-14f, 0f);
        warriorPlaqueText.textWrappingMode = TextWrappingModes.NoWrap;
        warriorPlaqueText.overflowMode = TextOverflowModes.Ellipsis;
        warriorPlaqueText.characterSpacing = 1.4f;

        GameObject gap = new GameObject("TopGap", typeof(RectTransform));
        gap.transform.SetParent(body.transform, false);
        MedievalSettingsArt.SetFixedSize(gap, 404f, 6f);

        MedievalSettingsArt.CreateFramedButton(
            body.transform, "BtnPlayGame", "CHƠI NGAY", 404f, 58f, 20.5f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.HeroGoldFill,
            MedievalSettingsArt.BorderGothic, MedievalSettingsArt.HeroGoldBorder,
            MedievalSettingsArt.DarkInkText, MedievalSettingsArt.IconPlay, OnPlayClicked);

        MedievalSettingsArt.CreateFramedButton(
            body.transform, "BtnOpenSettings", "CÀI ĐẶT", 404f, 48f, 18f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.BronzeFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.GoldOrnament,
            MedievalSettingsArt.BrightGoldText, MedievalSettingsArt.IconGear, OnSettingsClicked);

        MedievalSettingsArt.CreateFramedButton(
            body.transform, "BtnOpenControls", "HƯỚNG DẪN ĐIỀU KHIỂN", 404f, 48f, 17.5f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.BronzeFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.GoldOrnament,
            MedievalSettingsArt.BrightGoldText, MedievalSettingsArt.IconControls, OnControlsClicked);

        MedievalSettingsArt.CreateFramedButton(
            body.transform, "BtnLogout", "ĐĂNG XUẤT", 404f, 46f, 17f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.BronzeBorder,
            MedievalSettingsArt.IvoryText, MedievalSettingsArt.IconReturn, OnLogoutClicked);

        MedievalSettingsArt.CreateFramedButton(
            body.transform, "BtnQuitGame", "THOÁT GAME", 404f, 46f, 17f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.CrimsonFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.CrimsonBorder,
            MedievalSettingsArt.CrimsonText, MedievalSettingsArt.IconPower, OnQuitClicked);
    }

    private void OnPlayClicked()
    {
        if (isTransitioning || GameMenuManager.IsMenuOpen) return;
        isTransitioning = true;

        GameAudioManager.PlayWeaponEquip();
        string target = string.IsNullOrWhiteSpace(GameMenuManager.MainMenuPlayTargetScene)
            ? defaultPlayScene
            : GameMenuManager.MainMenuPlayTargetScene;

        if (!Application.CanStreamedLevelBeLoaded(target))
        {
            target = Application.CanStreamedLevelBeLoaded(defaultPlayScene) ? defaultPlayScene : "Lv1";
        }
        LoadingController.LoadScene(target);
    }

    private static void OnSettingsClicked()
    {
        GameAudioManager.PlayUiClick();
        GameMenuManager.OpenSettingsOverlay(GameMenuManager.SettingsTab.Graphics);
    }

    private static void OnControlsClicked()
    {
        GameAudioManager.PlayUiClick();
        GameMenuManager.OpenSettingsOverlay(GameMenuManager.SettingsTab.Controls);
    }

    private void OnLogoutClicked()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        GameAudioManager.PlayUiClick();
        PlayerPrefs.DeleteKey("PlayerId");
        PlayerPrefs.DeleteKey("PlayerName");
        PlayerPrefs.DeleteKey("SessionId");
        PlayerPrefs.DeleteKey("PendingLoginEvent");
        PlayerPrefs.Save();
        GameMenuManager.ClearLoggedInSession();
        if (Application.CanStreamedLevelBeLoaded(loginSceneName))
        {
            SceneManager.LoadScene(loginSceneName);
        }
    }

    private static void OnQuitClicked()
    {
        GameAudioManager.PlayUiClick();
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void SetTopLeftRect(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void AddShadow(TMP_Text text, float yOffset)
    {
        Shadow sh = text.gameObject.GetComponent<Shadow>() ?? text.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
        sh.effectDistance = new Vector2(0f, yOffset);
    }
}
