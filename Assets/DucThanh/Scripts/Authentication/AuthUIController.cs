using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Quản lý giao diện đăng nhập, đăng ký tài khoản người chơi và xác thực hợp lệ trước khi gọi API
public class AuthUIController : MonoBehaviour
{
    private const int MinUsernameLength = 3;
    private const int MaxUsernameLength = 50;
    private const int MinPasswordLength = 6;

    [Header("API & Navigation")]
    [SerializeField] private AuthApiClient authApiClient;
    [SerializeField] private string nextSceneName = "PickCharacter";

    [Header("Panels")]
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject registerPanel;

    [Header("Login Fields")]
    [SerializeField] private TMP_InputField loginUsernameInput;
    [SerializeField] private TMP_InputField loginPasswordInput;
    [SerializeField] private TMP_Text loginMessageText;
    [SerializeField] private Button loginButton;
    [SerializeField] private Button openRegisterButton;

    [Header("Register Fields")]
    [SerializeField] private TMP_InputField registerUsernameInput;
    [SerializeField] private TMP_InputField registerPasswordInput;
    [SerializeField] private TMP_InputField registerConfirmPasswordInput;
    [SerializeField] private TMP_Text registerMessageText;
    [SerializeField] private Button registerButton;
    [SerializeField] private Button openLoginButton;

    private static readonly Color ErrorColor = new Color(1f, 0.38f, 0.38f, 1f);
    private static readonly Color SuccessColor = new Color(0.28f, 0.95f, 0.52f, 1f);
    private static readonly Color InfoColor = new Color(1f, 0.86f, 0.42f, 1f);

    [Header("UI References")]
    [SerializeField] private RectTransform keyArtRect;

    private Coroutine panelFadeRoutine;

    // Tìm tham chiếu bổ sung và ẩn thông báo lỗi cũ
    private void Awake()
    {
        if (authApiClient == null)
        {
            authApiClient = FindFirstObjectByType<AuthApiClient>();
        }

        ResolveOptionalReferences();
        ConfigurePasswordField(loginPasswordInput);
        ConfigurePasswordField(registerPasswordInput);
        ConfigurePasswordField(registerConfirmPasswordInput);
    }

    // Áp dụng giao diện nền tối và mở mặc định bảng đăng nhập
    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        EnsureLoginVisuals();
        if (GameMenuManager.ConsumePendingMainMenuOnLoginScene())
        {
            if (Application.CanStreamedLevelBeLoaded("MainMenuScene"))
            {
                SceneManager.LoadScene("MainMenuScene");
                return;
            }

            if (loginPanel != null) loginPanel.SetActive(false);
            if (registerPanel != null) registerPanel.SetActive(false);
            GameMenuManager.OpenMainMenu(GameMenuManager.LoggedInPlayerName, GameMenuManager.MainMenuPlayTargetScene);
        }
        else
        {
            ShowLoginPanel();
        }
    }

    private Canvas ResolveLoginCanvas()
    {
        if (loginPanel != null)
        {
            Canvas panelCanvas = loginPanel.GetComponentInParent<Canvas>(true);
            if (panelCanvas != null && panelCanvas.rootCanvas != null && panelCanvas.rootCanvas.gameObject.name != "GameMenuCanvas")
            {
                return panelCanvas.rootCanvas;
            }
        }
        return FantasyUiThemeHelper.FindSceneCanvas(gameObject);
    }

    private void EnsureLoginVisuals()
    {
        Canvas canvas = ResolveLoginCanvas();
        if (canvas == null) return;

        Transform bakedArt = canvas.transform.Find("LoginKeyArt");
        if (bakedArt != null)
        {
            if (keyArtRect == null) keyArtRect = bakedArt.GetComponent<RectTransform>();
            FantasyUiThemeHelper.RefreshKeyArtTexture(bakedArt, "UI/LoginBg");

            Image rightVeil = canvas.transform.Find("LoginRightGradient")?.GetComponent<Image>();
            if (rightVeil != null)
            {
                FantasyUiThemeHelper.EnsureGradientOverlaySprite(rightVeil, true, new Color(0.03f, 0.045f, 0.08f, 1f));
            }

            TMP_Text bakedBadge = canvas.transform.Find("HeroBrandGroup/BrandBadge")?.GetComponent<TMP_Text>();
            if (bakedBadge != null) bakedBadge.text = "HÀNH ĐỘNG SINH TỒN  •  DARK FANTASY";

            TMP_Text bakedFooter = canvas.transform.Find("LoginFooterTag")?.GetComponent<TMP_Text>();
            if (bakedFooter != null) bakedFooter.text = "3 CỨ ĐIỂM CHIẾN DỊCH   •   ĐẤU TRƯỜNG BOSS   •   VANGUARD";

            if (canvas.transform.Find("CampfireEmbersUI") == null)
            {
                GameObject emberLayer = new GameObject("CampfireEmbersUI", typeof(RectTransform));
                emberLayer.transform.SetParent(canvas.transform, false);
                emberLayer.transform.SetSiblingIndex(2);
                FantasyUiThemeHelper.StretchFull(emberLayer.GetComponent<RectTransform>());
                FloatingEmbersUI embers = emberLayer.AddComponent<FloatingEmbersUI>();
                embers.Initialize(28, 0.04f, 0.44f, new Color(1f, 0.62f, 0.16f, 1f), new Color(1f, 0.88f, 0.45f, 1f));
            }
            return;
        }

        ApplyLoginTheme();
    }

    // Hỗ trợ nhấn Tab chuyển ô nhập và Enter để gửi biểu mẫu nhanh
    private void Update()
    {

        if (keyArtRect != null)
        {
            float scale = 1.0f + Mathf.PingPong(Time.unscaledTime * 0.008f, 0.045f);
            keyArtRect.localScale = new Vector3(scale, scale, 1f);
        }

        if (GameMenuManager.IsMenuOpen || (authApiClient != null && authApiClient.IsBusy)) return;

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            HandleTabNavigation();
        }
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (loginPanel != null && loginPanel.activeSelf) Login();
            else if (registerPanel != null && registerPanel.activeSelf) Register();
        }
    }

    // Dựng ảnh nền, bố cục thẻ đăng nhập/đăng ký và hiệu ứng tàn lửa bay
    private void ApplyLoginTheme()
    {
        Canvas canvas = ResolveLoginCanvas();
        if (canvas == null) return;

        FantasyUiThemeHelper.ConfigureResponsiveCanvas(canvas);
        TMP_FontAsset sharedFont = loginMessageText != null
            ? loginMessageText.font
            : (loginUsernameInput != null && loginUsernameInput.textComponent != null ? loginUsernameInput.textComponent.font : null);

        RawImage keyArt = FantasyUiThemeHelper.CreateFullscreenKeyArt(canvas.transform, "UI/LoginBg", "LoginKeyArt");
        keyArtRect = keyArt.rectTransform;

        Image rightVeil = FantasyUiThemeHelper.CreateGradientOverlay(canvas.transform, "LoginRightGradient", true, new Color(0.03f, 0.045f, 0.08f, 1f));
        rightVeil.transform.SetSiblingIndex(1);

        GameObject emberLayer = new GameObject("CampfireEmbersUI", typeof(RectTransform));
        emberLayer.transform.SetParent(canvas.transform, false);
        emberLayer.transform.SetSiblingIndex(2);
        FantasyUiThemeHelper.StretchFull(emberLayer.GetComponent<RectTransform>());
        FloatingEmbersUI embers = emberLayer.AddComponent<FloatingEmbersUI>();
        embers.Initialize(28, 0.04f, 0.44f, new Color(1f, 0.62f, 0.16f, 1f), new Color(1f, 0.88f, 0.45f, 1f));

        GameObject brandGroup = new GameObject("HeroBrandGroup", typeof(RectTransform));
        brandGroup.transform.SetParent(canvas.transform, false);
        brandGroup.transform.SetSiblingIndex(3);
        RectTransform brandRt = brandGroup.GetComponent<RectTransform>();
        brandRt.anchorMin = new Vector2(0f, 1f);
        brandRt.anchorMax = new Vector2(0f, 1f);
        brandRt.pivot = new Vector2(0f, 1f);
        brandRt.anchoredPosition = new Vector2(82f, -56f);
        brandRt.sizeDelta = new Vector2(720f, 165f);

        TMP_Text badge = FantasyUiThemeHelper.CreateText(brandGroup.transform, "BrandBadge", "HÀNH ĐỘNG SINH TỒN  •  DARK FANTASY",
            15f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Left, sharedFont);
        SetTopLeftRect(badge.rectTransform, 0f, 0f, 680f, 24f);
        badge.characterSpacing = 6f;

        TMP_Text gameTitle = FantasyUiThemeHelper.CreateText(brandGroup.transform, "HeroTitle", "VANGUARD",
            66f, FontStyles.Bold, Color.white, TextAlignmentOptions.Left, sharedFont);
        SetTopLeftRect(gameTitle.rectTransform, -3f, -26f, 680f, 76f);
        gameTitle.characterSpacing = 10f;
        Shadow titleShadow = gameTitle.gameObject.AddComponent<Shadow>();
        titleShadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        titleShadow.effectDistance = new Vector2(0f, -4f);

        FantasyUiThemeHelper.CreateAccentLine(brandGroup.transform, "HeroGoldLine",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, -106f), new Vector2(230f, 3f), FantasyUiThemeHelper.GoldPrimary);

        TMP_Text heroSub = FantasyUiThemeHelper.CreateText(brandGroup.transform, "HeroSubtitle", "CUỘC TRUY QUÉT RỪNG ORC",
            22f, FontStyles.Bold, new Color(0.90f, 0.82f, 0.64f, 0.96f), TextAlignmentOptions.Left, sharedFont);
        SetTopLeftRect(heroSub.rectTransform, 0f, -118f, 680f, 34f);
        heroSub.characterSpacing = 5f;

        TMP_Text footerTag = FantasyUiThemeHelper.CreateText(canvas.transform, "LoginFooterTag",
            "3 CỨ ĐIỂM CHIẾN DỊCH   •   ĐẤU TRƯỜNG BOSS   •   VANGUARD",
            15f, FontStyles.Bold, new Color(0.76f, 0.80f, 0.88f, 0.72f), TextAlignmentOptions.Left, sharedFont);
        footerTag.transform.SetSiblingIndex(4);
        RectTransform ftRt = footerTag.rectTransform;
        ftRt.anchorMin = new Vector2(0f, 0f);
        ftRt.anchorMax = new Vector2(0f, 0f);
        ftRt.pivot = new Vector2(0f, 0f);
        ftRt.anchoredPosition = new Vector2(82f, 40f);
        ftRt.sizeDelta = new Vector2(900f, 28f);
        footerTag.characterSpacing = 2f;

        StyleLoginCard(sharedFont);

        StyleRegisterCard(sharedFont);
    }

    private void StyleLoginCard(TMP_FontAsset sharedFont)
    {
        if (loginPanel == null) return;

        const float cardW = 480f;
        const float cardH = 540f;
        const float fieldW = 396f;

        RectTransform rt = loginPanel.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-110f, 0f);
            rt.sizeDelta = new Vector2(cardW, cardH);
        }

        FantasyUiThemeHelper.StyleCardPanel(loginPanel, FantasyUiThemeHelper.ObsidianCard, FantasyUiThemeHelper.GoldBorder);
        FantasyUiThemeHelper.CreateAccentLine(loginPanel.transform, "TopGoldBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 4f), FantasyUiThemeHelper.GoldPrimary);

        StylePanelHeader(loginPanel, "ĐĂNG NHẬP CHIẾN BINH", "Nhập tài khoản để bắt đầu chiến dịch", sharedFont);

        PositionCenteredInCard(loginUsernameInput, 0f, -158f);
        FantasyUiThemeHelper.StyleInputField(loginUsernameInput, "Tên đăng nhập...", fieldW, 54f);

        PositionCenteredInCard(loginPasswordInput, 0f, -228f);
        FantasyUiThemeHelper.StyleInputField(loginPasswordInput, "Mật khẩu...", fieldW, 54f);

        StyleMessageBox(loginMessageText, fieldW, -296f, 42f);

        PositionCenteredInCard(loginButton, 0f, -356f);
        FantasyUiThemeHelper.StyleButton(loginButton, "VÀO TRẬN  •  ĐĂNG NHẬP", true, fieldW, 56f);

        PositionCenteredInCard(openRegisterButton, 0f, -430f);
        FantasyUiThemeHelper.StyleButton(openRegisterButton, "Chưa có tài khoản? Đăng ký ngay", false, fieldW, 48f);
    }

    private void StyleRegisterCard(TMP_FontAsset sharedFont)
    {
        if (registerPanel == null) return;

        const float cardW = 480f;
        const float cardH = 620f;
        const float fieldW = 396f;

        RectTransform rt = registerPanel.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-110f, 0f);
            rt.sizeDelta = new Vector2(cardW, cardH);
        }

        FantasyUiThemeHelper.StyleCardPanel(registerPanel, FantasyUiThemeHelper.ObsidianCard, FantasyUiThemeHelper.GoldBorder);
        FantasyUiThemeHelper.CreateAccentLine(registerPanel.transform, "TopGoldBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 4f), FantasyUiThemeHelper.GoldPrimary);

        StylePanelHeader(registerPanel, "TẠO TÀI KHOẢN MỚI", "Gia nhập quân đoàn Vanguard săn Boss Mỏ Vàng", sharedFont);

        PositionCenteredInCard(registerUsernameInput, 0f, -156f);
        FantasyUiThemeHelper.StyleInputField(registerUsernameInput, "Tên đăng nhập (3 - 50 ký tự)...", fieldW, 52f);

        PositionCenteredInCard(registerPasswordInput, 0f, -224f);
        FantasyUiThemeHelper.StyleInputField(registerPasswordInput, "Mật khẩu (tối thiểu 6 ký tự)...", fieldW, 52f);

        PositionCenteredInCard(registerConfirmPasswordInput, 0f, -292f);
        FantasyUiThemeHelper.StyleInputField(registerConfirmPasswordInput, "Nhập lại mật khẩu...", fieldW, 52f);

        StyleMessageBox(registerMessageText, fieldW, -358f, 44f);

        PositionCenteredInCard(registerButton, 0f, -424f);
        FantasyUiThemeHelper.StyleButton(registerButton, "ĐĂNG KÝ TÀI KHOẢN", true, fieldW, 56f);

        PositionCenteredInCard(openLoginButton, 0f, -498f);
        FantasyUiThemeHelper.StyleButton(openLoginButton, "Đã có tài khoản? Quay lại đăng nhập", false, fieldW, 48f);
    }

    private void StylePanelHeader(GameObject panel, string titleStr, string subtitleStr, TMP_FontAsset sharedFont)
    {
        TMP_Text title = FindChildComponent<TMP_Text>(panel, "TitleText");
        if (title == null)
        {
            title = FantasyUiThemeHelper.CreateText(panel.transform, "TitleText", titleStr,
                28f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Center, sharedFont);
        }
        else
        {
            title.text = titleStr;
            title.fontSize = 28f;
            title.fontStyle = FontStyles.Bold;
            title.color = FantasyUiThemeHelper.GoldBright;
            title.alignment = TextAlignmentOptions.Center;
        }

        title.characterSpacing = 2.5f;
        RectTransform trt = title.rectTransform;
        trt.anchorMin = new Vector2(0.5f, 1f);
        trt.anchorMax = new Vector2(0.5f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, -40f);
        trt.sizeDelta = new Vector2(420f, 40f);

        TMP_Text sub = FantasyUiThemeHelper.CreateText(panel.transform, "SubtitleText", subtitleStr,
            16f, FontStyles.Normal, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.Center, sharedFont);
        RectTransform srt = sub.rectTransform;
        srt.anchorMin = new Vector2(0.5f, 1f);
        srt.anchorMax = new Vector2(0.5f, 1f);
        srt.pivot = new Vector2(0.5f, 1f);
        srt.anchoredPosition = new Vector2(0f, -86f);
        srt.sizeDelta = new Vector2(400f, 28f);

        FantasyUiThemeHelper.CreateAccentLine(panel.transform, "HeaderDivider",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -124f), new Vector2(140f, 2f), FantasyUiThemeHelper.GoldBorder);
    }

    private static void PositionCenteredInCard(Component comp, float x, float yFromTop)
    {
        if (comp == null) return;
        RectTransform rt = comp.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, yFromTop);
    }

    private static void StyleMessageBox(TMP_Text msgText, float width, float yFromTop, float height)
    {
        if (msgText == null) return;
        RectTransform rt = msgText.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, yFromTop);
        rt.sizeDelta = new Vector2(width, height);

        msgText.fontSize = 16f;
        msgText.fontStyle = FontStyles.Bold;
        msgText.alignment = TextAlignmentOptions.Center;
        msgText.textWrappingMode = TextWrappingModes.Normal;
    }

    private static void SetTopLeftRect(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private void FadeInPanel(GameObject panel)
    {
        if (panel == null) return;
        CanvasGroup cg = panel.GetComponent<CanvasGroup>();
        if (cg == null) cg = panel.AddComponent<CanvasGroup>();

        if (panelFadeRoutine != null) StopCoroutine(panelFadeRoutine);
        panelFadeRoutine = StartCoroutine(FadePanelRoutine(cg));
    }

    private IEnumerator FadePanelRoutine(CanvasGroup cg)
    {
        cg.alpha = 0f;
        float t = 0f;
        const float duration = 0.22f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            if (cg != null) cg.alpha = Mathf.Clamp01(t / duration);
            yield return null;
        }
        if (cg != null) cg.alpha = 1f;
        panelFadeRoutine = null;
    }

    // Hiển thị lại biểu mẫu đăng nhập khi người chơi đăng xuất từ Menu Chính
    public void ReturnToLoginForm()
    {
        SetInteractable(true);
        ClearPasswordInputs();
        ShowLoginPanel();
        SetMessage(loginMessageText, "Đã đăng xuất. Mời đăng nhập lại!", InfoColor);
    }

    // Chuyển giao diện sang chế độ Đăng nhập
    public void ShowLoginPanel()
    {
        if (loginPanel != null) loginPanel.SetActive(true);
        if (registerPanel != null) registerPanel.SetActive(false);
        ClearMessages();
        ClearPasswordInputs();
        FadeInPanel(loginPanel);

        if (loginUsernameInput != null)
        {
            loginUsernameInput.ActivateInputField();
        }
    }

    // Chuyển giao diện sang chế độ Đăng ký tài khoản
    public void ShowRegisterPanel()
    {
        GameAudioManager.PlayUiClick();
        if (loginPanel != null) loginPanel.SetActive(false);
        if (registerPanel != null) registerPanel.SetActive(true);
        ClearMessages();
        ClearPasswordInputs();
        FadeInPanel(registerPanel);

        if (registerUsernameInput != null)
        {
            registerUsernameInput.ActivateInputField();
        }
    }

    // Kiểm tra thông tin nhập và gửi yêu cầu đăng nhập tới AuthApiClient
    public void Login()
    {
        GameAudioManager.PlayUiClick();
        if (authApiClient == null)
        {
            SetMessage(loginMessageText, "Chưa gắn AuthApiClient!", ErrorColor);
            return;
        }
        if (authApiClient.IsBusy) return;

        string playerName = loginUsernameInput != null ? loginUsernameInput.text.Trim() : string.Empty;
        string password = loginPasswordInput != null ? loginPasswordInput.text : string.Empty;

        if (string.IsNullOrEmpty(playerName))
        {
            SetMessage(loginMessageText, "Vui lòng nhập tên đăng nhập!", ErrorColor);
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            SetMessage(loginMessageText, "Vui lòng nhập mật khẩu!", ErrorColor);
            return;
        }

        SetInteractable(false);
        SetMessage(loginMessageText, "Đang đăng nhập...", InfoColor);

        authApiClient.Login(playerName, password, (success, response, message) =>
        {
            if (success)
            {
                SetInteractable(true);
                SetMessage(loginMessageText, message, SuccessColor);
                Debug.Log($"[AuthUI] Đăng nhập thành công: #{response.playerId} ({response.playerName})");
                string targetScene = Application.CanStreamedLevelBeLoaded(nextSceneName) ? nextSceneName : "Lv1";
                GameMenuManager.SetLoggedInSession(response.playerName, targetScene);
                if (Application.CanStreamedLevelBeLoaded("MainMenuScene"))
                {
                    SceneManager.LoadScene("MainMenuScene");
                }
                else
                {
                    if (loginPanel != null) loginPanel.SetActive(false);
                    if (registerPanel != null) registerPanel.SetActive(false);
                    GameMenuManager.OpenMainMenu(response.playerName, targetScene);
                }
            }
            else
            {
                SetInteractable(true);
                SetMessage(loginMessageText, message, ErrorColor);
                if (loginPasswordInput != null)
                {
                    loginPasswordInput.text = string.Empty;
                    loginPasswordInput.ActivateInputField();
                }
            }
        });
    }

    // Kiểm tra tên tài khoản/mật khẩu hợp lệ và gửi yêu cầu đăng ký lên Backend
    public void Register()
    {
        GameAudioManager.PlayUiClick();
        if (authApiClient == null)
        {
            SetMessage(registerMessageText, "Chưa gắn AuthApiClient!", ErrorColor);
            return;
        }
        if (authApiClient.IsBusy) return;

        string playerName = registerUsernameInput != null ? registerUsernameInput.text.Trim() : string.Empty;
        string password = registerPasswordInput != null ? registerPasswordInput.text : string.Empty;

        if (playerName.Length < MinUsernameLength || playerName.Length > MaxUsernameLength)
        {
            SetMessage(registerMessageText, $"Tên đăng nhập phải từ {MinUsernameLength} đến {MaxUsernameLength} ký tự!", ErrorColor);
            return;
        }

        if (password.Length < MinPasswordLength)
        {
            SetMessage(registerMessageText, $"Mật khẩu phải có ít nhất {MinPasswordLength} ký tự!", ErrorColor);
            return;
        }

        if (registerConfirmPasswordInput != null && registerConfirmPasswordInput.text != password)
        {
            SetMessage(registerMessageText, "Mật khẩu nhập lại không khớp!", ErrorColor);
            return;
        }

        SetInteractable(false);
        SetMessage(registerMessageText, "Đang tạo tài khoản...", InfoColor);

        authApiClient.Register(playerName, password, (success, response, message) =>
        {
            SetInteractable(true);

            if (success)
            {
                Debug.Log($"[AuthUI] Đăng ký thành công: #{response.playerId} ({response.playerName})");
                if (registerUsernameInput != null) registerUsernameInput.text = string.Empty;
                ShowLoginPanel();

                if (loginUsernameInput != null) loginUsernameInput.text = playerName;
                if (loginPasswordInput != null) loginPasswordInput.ActivateInputField();
                SetMessage(loginMessageText, message, SuccessColor);
            }
            else
            {
                SetMessage(registerMessageText, message, ErrorColor);
            }
        });
    }

    private void ResolveOptionalReferences()
    {
        if (loginPanel != null)
        {
            if (loginPasswordInput == null) loginPasswordInput = FindChildComponent<TMP_InputField>(loginPanel, "PasswordInput");
            if (loginButton == null) loginButton = FindChildComponent<Button>(loginPanel, "LoginButton");
            if (openRegisterButton == null) openRegisterButton = FindChildComponent<Button>(loginPanel, "OpenRegisterButton");
        }

        if (registerPanel != null)
        {
            if (registerPasswordInput == null) registerPasswordInput = FindChildComponent<TMP_InputField>(registerPanel, "PasswordInput");
            if (registerConfirmPasswordInput == null) registerConfirmPasswordInput = FindChildComponent<TMP_InputField>(registerPanel, "ConfirmPasswordInput");
            if (registerButton == null) registerButton = FindChildComponent<Button>(registerPanel, "RegisterButton");
            if (openLoginButton == null) openLoginButton = FindChildComponent<Button>(registerPanel, "OpenLoginButton");
        }
    }

    private static T FindChildComponent<T>(GameObject parent, string childName) where T : Component
    {
        Transform child = parent.transform.Find(childName);
        return child != null ? child.GetComponent<T>() : null;
    }

    private static void ConfigurePasswordField(TMP_InputField field)
    {
        if (field == null) return;
        field.contentType = TMP_InputField.ContentType.Password;
        field.ForceLabelUpdate();
    }

    private void HandleTabNavigation()
    {
        if (loginPanel != null && loginPanel.activeSelf)
        {
            if (loginUsernameInput != null && loginUsernameInput.isFocused && loginPasswordInput != null)
                loginPasswordInput.ActivateInputField();
            else if (loginUsernameInput != null)
                loginUsernameInput.ActivateInputField();
        }
        else if (registerPanel != null && registerPanel.activeSelf)
        {
            if (registerUsernameInput != null && registerUsernameInput.isFocused && registerPasswordInput != null)
                registerPasswordInput.ActivateInputField();
            else if (registerPasswordInput != null && registerPasswordInput.isFocused && registerConfirmPasswordInput != null)
                registerConfirmPasswordInput.ActivateInputField();
            else if (registerUsernameInput != null)
                registerUsernameInput.ActivateInputField();
        }
    }

    private void SetInteractable(bool interactable)
    {
        if (loginButton != null) loginButton.interactable = interactable;
        if (openRegisterButton != null) openRegisterButton.interactable = interactable;
        if (registerButton != null) registerButton.interactable = interactable;
        if (openLoginButton != null) openLoginButton.interactable = interactable;
    }

    private void ClearMessages()
    {
        if (loginMessageText != null) loginMessageText.text = string.Empty;
        if (registerMessageText != null) registerMessageText.text = string.Empty;
    }

    private void ClearPasswordInputs()
    {
        if (loginPasswordInput != null) loginPasswordInput.text = string.Empty;
        if (registerPasswordInput != null) registerPasswordInput.text = string.Empty;
        if (registerConfirmPasswordInput != null) registerConfirmPasswordInput.text = string.Empty;
    }

    private static void SetMessage(TMP_Text target, string message, Color color)
    {
        if (target == null) return;
        target.text = message;
        target.color = color;
    }
}
