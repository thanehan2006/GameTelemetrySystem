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

    [Header("Baked UI References")]
    [SerializeField] private RectTransform keyArtRect;
    [SerializeField] private Image rightGradientImage;
    [SerializeField] private RectTransform menuCardRt;
    [SerializeField] private TMP_Text warriorPlaqueText;
    [SerializeField] private FloatingEmbersUI embersEffect;
    [SerializeField] private Button warriorPlaqueButton;
    [SerializeField] private Button playButton;
    [SerializeField] private Button profileButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button controlsButton;
    [SerializeField] private Button logoutButton;
    [SerializeField] private Button quitButton;

    private RectTransform canvasRt;
    private Vector2 lastCanvasSize = new Vector2(-1f, -1f);
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
            BindBakedMainMenu();
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
            if (size.x > 100f && size.y > 100f && (size - lastCanvasSize).sqrMagnitude > 0.25f)
            {
                lastCanvasSize = size;
                float fit = Mathf.Min(1f, Mathf.Min((size.x * 0.44f) / 530f, (size.y * 0.88f) / 676f));
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
    }

    // Liên kết UI đã đặt sẵn trên Scene, làm mới sprite/hiệu ứng và gán sự kiện bấm nút
    private void BindBakedMainMenu()
    {
        if (targetCanvas == null) return;
        Transform root = targetCanvas.transform;

        if (keyArtRect == null) keyArtRect = root.Find("MainMenuKeyArt")?.GetComponent<RectTransform>();
        if (rightGradientImage == null) rightGradientImage = root.Find("MainMenuRightGradient")?.GetComponent<Image>();
        if (menuCardRt == null) menuCardRt = root.Find("MainMenuCard")?.GetComponent<RectTransform>();
        if (embersEffect == null) embersEffect = root.Find("MainMenuEmbers")?.GetComponent<FloatingEmbersUI>();
        if (warriorPlaqueButton == null) warriorPlaqueButton = root.Find("MainMenuCard/MainMenuBody/WarriorPlaque")?.GetComponent<Button>();
        if (warriorPlaqueText == null) warriorPlaqueText = root.Find("MainMenuCard/MainMenuBody/WarriorPlaque/WarriorText")?.GetComponent<TMP_Text>();
        if (playButton == null) playButton = root.Find("MainMenuCard/MainMenuBody/BtnPlayGame")?.GetComponent<Button>();
        if (profileButton == null) profileButton = root.Find("MainMenuCard/MainMenuBody/BtnWarriorProfile")?.GetComponent<Button>();
        if (settingsButton == null) settingsButton = root.Find("MainMenuCard/MainMenuBody/BtnOpenSettings")?.GetComponent<Button>();
        if (controlsButton == null) controlsButton = root.Find("MainMenuCard/MainMenuBody/BtnOpenControls")?.GetComponent<Button>();
        if (logoutButton == null) logoutButton = root.Find("MainMenuCard/MainMenuBody/BtnLogout")?.GetComponent<Button>();
        if (quitButton == null) quitButton = root.Find("MainMenuCard/MainMenuBody/BtnQuitGame")?.GetComponent<Button>();

        if (keyArtRect != null) FantasyUiThemeHelper.RefreshKeyArtTexture(keyArtRect, "UI/LoginBg");
        if (rightGradientImage != null) FantasyUiThemeHelper.EnsureGradientOverlaySprite(rightGradientImage, true, new Color(0.016f, 0.014f, 0.014f, 0.94f));
        if (embersEffect != null && embersEffect.transform.childCount == 0)
        {
            embersEffect.Initialize(28, 0.04f, 0.44f, new Color(1f, 0.62f, 0.16f, 1f), new Color(1f, 0.88f, 0.45f, 1f));
        }

        if (warriorPlaqueText != null)
        {
            string playerName = GameMenuManager.LoggedInPlayerName;
            if (string.IsNullOrWhiteSpace(playerName))
            {
                playerName = PlayerPrefs.GetString("PlayerName", string.Empty);
            }
            warriorPlaqueText.text = string.IsNullOrEmpty(playerName)
                ? "◆  CHIẾN BINH: VANGUARD"
                : $"◆  CHIẾN BINH: {playerName.Trim().ToUpperInvariant()}";
        }

        BindBakedButton(warriorPlaqueButton, MedievalSettingsArt.InnerObsidian, OnProfileClicked, 1.02f, 0.98f);
        BindBakedButton(playButton, MedievalSettingsArt.HeroGoldFill, OnPlayClicked);
        BindBakedButton(profileButton, MedievalSettingsArt.BronzeFill, OnProfileClicked);
        BindBakedButton(settingsButton, MedievalSettingsArt.BronzeFill, OnSettingsClicked);
        BindBakedButton(controlsButton, MedievalSettingsArt.BronzeFill, OnControlsClicked);
        BindBakedButton(logoutButton, MedievalSettingsArt.DarkSlateFill, OnLogoutClicked);
        BindBakedButton(quitButton, MedievalSettingsArt.CrimsonFill, OnQuitClicked);
    }

    private static void BindBakedButton(
        Button btn,
        Color baseFillColor,
        UnityEngine.Events.UnityAction onClick,
        float hoverScale = 1.045f,
        float pressScale = 0.96f)
    {
        if (btn == null) return;
        btn.transition = Selectable.Transition.None;
        MedievalSettingsArt.AttachButtonFeedback(btn, baseFillColor, hoverScale, pressScale);
        btn.onClick.RemoveAllListeners();
        if (onClick != null) btn.onClick.AddListener(onClick);
    }

    private void OnPlayClicked()
    {
        if (isTransitioning || GameMenuManager.IsMenuOpen || WarriorProfileOverlay.IsOpen) return;
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

    private void OnProfileClicked()
    {
        if (isTransitioning || GameMenuManager.IsMenuOpen || WarriorProfileOverlay.IsOpen) return;
        GameAudioManager.PlayUiClick();
        WarriorProfileOverlay.Open();
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
        WarriorProfileOverlay.Close();
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
}
