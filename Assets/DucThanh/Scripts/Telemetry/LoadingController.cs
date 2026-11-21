using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Màn hình Loading phong cách Action RPG AAA:
/// - Hình nền nghệ thuật Rừng Orc & Mỏ Vàng kèm hiệu ứng Ken Burns zoom mượt + đom đóm bay.
/// - Logo game VANGUARD: RAID OF THE ORC WOODS, mẹo chơi luân phiên chuẩn gameplay 3 Màn.
/// - Tải trước toàn bộ asset bắt buộc (UI, Audio BGM/SFX, Nhân vật 3D, Phần thưởng, Shader) trước khi nạp Scene đích.
/// - Chỉ hoàn tất & kích hoạt Scene đích khi cả tài nguyên lẫn Scene đã tải xong 100%.
/// </summary>
public class LoadingController : MonoBehaviour
{
    public const string LoadingSceneName = "LoadingScene";

    private static string pendingTargetScene;

    [Header("UI")]
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text loadingStatus;

    [Header("Scene")]
    [SerializeField] private string sceneToLoad = "LoginScene";

    [Header("Loading Settings")]
    [SerializeField] private float startDelay = 0.25f;
    [SerializeField] private float minDisplayDuration = 0.6f;
    [SerializeField] private float progressSmoothSpeed = 2.0f;
    [SerializeField] private float readyHoldDuration = 0.25f;

    [Header("Asset Preloading")]
    [SerializeField] private bool preloadCoreAssets = true;
    [SerializeField] private bool blockOnMissingRequiredAsset = false;
    [SerializeField] private string[] extraResourcePaths;
    [SerializeField] private Object[] directPreloadAssets;
    [SerializeField] private ShaderVariantCollection[] shaderVariantCollections;

    private static readonly string[] GameplayTips =
    {
        "Mẹo chiến đấu: Giữ [Chuột Phải] để Đỡ Đòn (giảm 80% sát thương) — bấm đúng lúc sẽ Parry đẩy choáng quái & hồi 5% HP!",
        "Mẹo sinh tồn: Cẩn thận cụm Bẫy Gai ở Màn 2 (Thung Lũng Bẫy) — quan sát ánh sáng đỏ để né.",
        "Mẹo nâng cấp: Giết quái Orc nhặt Vàng rồi nhấn [B] (hoặc [E] gần Trại Lửa) để mở Cửa Hàng mua Vũ Khí & Bình Máu!",
        "Mẹo dẫn đường: Theo dõi Cột Sáng Dẫn Đường trên địa hình để tiến tới Đấu Trường Boss Mỏ Vàng.",
        "Mẹo hồi phục: Nhặt Bình Hồi Máu phát sáng trên đường hoặc rơi từ quái và chạm Trại Lửa Checkpoint để hồi HP."
    };

    [Header("Optional Pre-Baked UI References")]
    [SerializeField] private RectTransform keyArtRect;
    [SerializeField] private TMP_Text tipText;

    private float realProgress;
    private float displayedProgress;
    private float loadingStartRealtime;
    private float nextTipSwitchTime;
    private int tipIndex;
    private int lastUiUpdateFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        pendingTargetScene = null;
    }

    /// <summary>
    /// Chuyển sang bất kỳ Scene nào thông qua LoadingScene để đảm bảo asset đã được tải đầy đủ trước khi vào màn.
    /// </summary>
    public static void LoadScene(string targetSceneName)
    {
        if (string.IsNullOrWhiteSpace(targetSceneName)) return;

        Time.timeScale = 1f;
        string currentSceneName = SceneManager.GetActiveScene().name;
        if (Application.CanStreamedLevelBeLoaded(LoadingSceneName) && currentSceneName != LoadingSceneName)
        {
            pendingTargetScene = targetSceneName;
            SceneManager.LoadScene(LoadingSceneName);
            return;
        }

        SceneManager.LoadScene(targetSceneName);
    }

    private void Start()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        startDelay = Mathf.Clamp(startDelay, 0f, 1.5f);
        minDisplayDuration = Mathf.Max(0f, minDisplayDuration);

        EnsureLoadingVisuals();
        StartCoroutine(LoadSceneAsync());
    }

    private void EnsureLoadingVisuals()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        Transform bakedArt = canvas.transform.Find("LoadingKeyArt");
        if (bakedArt != null)
        {
            if (keyArtRect == null) keyArtRect = bakedArt.GetComponent<RectTransform>();
            if (tipText == null)
            {
                Transform tipTf = canvas.transform.Find("LoadingFooterHUD/GameplayTipText");
                if (tipTf != null) tipText = tipTf.GetComponent<TMP_Text>();
            }

            if (canvas.transform.Find("LoadingEmbers") == null)
            {
                GameObject emberLayer = new GameObject("LoadingEmbers", typeof(RectTransform));
                emberLayer.transform.SetParent(canvas.transform, false);
                emberLayer.transform.SetSiblingIndex(2);
                FantasyUiThemeHelper.StretchFull(emberLayer.GetComponent<RectTransform>());
                FloatingEmbersUI embers = emberLayer.AddComponent<FloatingEmbersUI>();
                embers.Initialize(26, 0.05f, 0.95f, new Color(1f, 0.85f, 0.35f, 1f), new Color(0.45f, 0.95f, 0.65f, 1f));
            }
            return;
        }

        ApplyAaaLoadingTheme();
    }

    private void Update()
    {
        // Hiệu ứng Ken Burns zoom chậm tạo chiều sâu điện ảnh cho hình nền Loading
        if (keyArtRect != null)
        {
            float scale = 1.0f + Mathf.PingPong(Time.unscaledTime * 0.012f, 0.06f);
            keyArtRect.localScale = new Vector3(scale, scale, 1f);
        }
    }

    private void ApplyAaaLoadingTheme()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        FantasyUiThemeHelper.ConfigureResponsiveCanvas(canvas);
        TMP_FontAsset sharedFont = progressText != null ? progressText.font : (loadingStatus != null ? loadingStatus.font : null);

        // Ẩn Background & LoadingTitle mặc định cũ nếu tồn tại trên Canvas
        Transform oldBg = canvas.transform.Find("Background");
        if (oldBg != null) oldBg.gameObject.SetActive(false);

        Transform oldTitle = canvas.transform.Find("LoadingTitle");
        if (oldTitle != null) oldTitle.gameObject.SetActive(false);

        // 1. Hình nền nghệ thuật Rừng Orc & Mỏ Vàng (Assets/Resources/UI/LoadingBg.jpg)
        RawImage keyArt = FantasyUiThemeHelper.CreateFullscreenKeyArt(canvas.transform, "UI/LoadingBg", "LoadingKeyArt");
        keyArtRect = keyArt.rectTransform;

        // 2. Lớp chuyển tối mượt phía trên & phía dưới giúp chữ sắc nét tuyệt đối
        Image veil = FantasyUiThemeHelper.CreateGradientOverlay(canvas.transform, "LoadingGradientVeil", false, new Color(0.03f, 0.05f, 0.09f, 1f));
        veil.transform.SetSiblingIndex(1);

        // 3. Hạt đom đóm / tàn lửa vàng bay lơ lửng
        GameObject emberLayer = new GameObject("LoadingEmbers", typeof(RectTransform));
        emberLayer.transform.SetParent(canvas.transform, false);
        emberLayer.transform.SetSiblingIndex(2);
        FantasyUiThemeHelper.StretchFull(emberLayer.GetComponent<RectTransform>());
        FloatingEmbersUI embers = emberLayer.AddComponent<FloatingEmbersUI>();
        embers.Initialize(26, 0.05f, 0.95f, new Color(1f, 0.85f, 0.35f, 1f), new Color(0.45f, 0.95f, 0.65f, 1f));

        // 4. Khối Logo Game góc trên-trái
        GameObject brandGroup = new GameObject("GameBrandHeader", typeof(RectTransform));
        brandGroup.transform.SetParent(canvas.transform, false);
        RectTransform brandRt = brandGroup.GetComponent<RectTransform>();
        brandRt.anchorMin = new Vector2(0f, 1f);
        brandRt.anchorMax = new Vector2(0f, 1f);
        brandRt.pivot = new Vector2(0f, 1f);
        brandRt.anchoredPosition = new Vector2(88f, -64f);
        brandRt.sizeDelta = new Vector2(680f, 160f);

        TMP_Text badge = FantasyUiThemeHelper.CreateText(brandGroup.transform, "Badge", "AEGIS TELEMETRY  •  ACTION ROGUELITE",
            15f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Left, sharedFont);
        SetTopLeftRect(badge.rectTransform, 0f, 0f, 650f, 24f);
        badge.characterSpacing = 6f;

        TMP_Text title = FantasyUiThemeHelper.CreateText(brandGroup.transform, "GameTitle", "VANGUARD",
            64f, FontStyles.Bold, Color.white, TextAlignmentOptions.Left, sharedFont);
        SetTopLeftRect(title.rectTransform, -3f, -26f, 650f, 74f);
        title.characterSpacing = 10f;
        Shadow titleShadow = title.gameObject.AddComponent<Shadow>();
        titleShadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
        titleShadow.effectDistance = new Vector2(0f, -4f);

        FantasyUiThemeHelper.CreateAccentLine(brandGroup.transform, "GoldDivider",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, -104f), new Vector2(220f, 3f), FantasyUiThemeHelper.GoldPrimary);

        TMP_Text sub = FantasyUiThemeHelper.CreateText(brandGroup.transform, "GameSubtitle", "RAID OF THE ORC WOODS",
            21f, FontStyles.Bold, new Color(0.88f, 0.80f, 0.62f, 0.96f), TextAlignmentOptions.Left, sharedFont);
        SetTopLeftRect(sub.rectTransform, 0f, -114f, 650f, 32f);
        sub.characterSpacing = 5f;

        // 5. Thanh Loading & Mẹo chơi sát đáy màn hình (kiểu game AAA)
        GameObject footer = new GameObject("LoadingFooterHUD", typeof(RectTransform));
        footer.transform.SetParent(canvas.transform, false);
        RectTransform footRt = footer.GetComponent<RectTransform>();
        footRt.anchorMin = new Vector2(0f, 0f);
        footRt.anchorMax = new Vector2(1f, 0f);
        footRt.pivot = new Vector2(0.5f, 0f);
        footRt.anchoredPosition = new Vector2(0f, 54f);
        footRt.sizeDelta = new Vector2(-176f, 130f);

        // Mẹo chơi
        tipText = FantasyUiThemeHelper.CreateText(footer.transform, "GameplayTipText", GameplayTips[0],
            19f, FontStyles.Normal, new Color(0.88f, 0.91f, 0.96f, 0.95f), TextAlignmentOptions.Left, sharedFont);
        RectTransform tipRt = tipText.rectTransform;
        tipRt.anchorMin = new Vector2(0f, 1f);
        tipRt.anchorMax = new Vector2(1f, 1f);
        tipRt.pivot = new Vector2(0.5f, 1f);
        tipRt.anchoredPosition = new Vector2(0f, 0f);
        tipRt.sizeDelta = new Vector2(0f, 36f);

        // Style lại ProgressBar có sẵn (bỏ Handle tròn xấu, biến thành thanh vàng kim sắc nét)
        if (progressBar != null)
        {
            progressBar.transform.SetParent(footer.transform, false);
            progressBar.interactable = false;
            progressBar.transition = Selectable.Transition.None;
            progressBar.minValue = 0f;
            progressBar.maxValue = 1f;

            RectTransform barRt = progressBar.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0f, 0.5f);
            barRt.anchorMax = new Vector2(1f, 0.5f);
            barRt.pivot = new Vector2(0.5f, 0.5f);
            barRt.anchoredPosition = new Vector2(0f, -6f);
            barRt.sizeDelta = new Vector2(0f, 14f);

            Transform handleArea = progressBar.transform.Find("Handle Slide Area");
            if (handleArea != null) handleArea.gameObject.SetActive(false);

            Transform barBg = progressBar.transform.Find("Background");
            if (barBg != null)
            {
                FantasyUiThemeHelper.StretchFull(barBg.GetComponent<RectTransform>());
                Image bgImg = barBg.GetComponent<Image>();
                if (bgImg != null)
                {
                    bgImg.sprite = null;
                    bgImg.color = new Color(0.08f, 0.11f, 0.18f, 0.92f);
                }
                Outline bgOut = barBg.GetComponent<Outline>();
                if (bgOut == null) bgOut = barBg.gameObject.AddComponent<Outline>();
                bgOut.effectColor = FantasyUiThemeHelper.GoldBorder;
                bgOut.effectDistance = new Vector2(1f, -1f);
            }

            Transform fillArea = progressBar.transform.Find("Fill Area");
            if (fillArea != null)
            {
                RectTransform faRt = fillArea.GetComponent<RectTransform>();
                faRt.anchorMin = Vector2.zero;
                faRt.anchorMax = Vector2.one;
                faRt.offsetMin = new Vector2(2f, 2f);
                faRt.offsetMax = new Vector2(-2f, -2f);
            }

            if (progressBar.fillRect != null)
            {
                progressBar.fillRect.offsetMin = Vector2.zero;
                progressBar.fillRect.offsetMax = Vector2.zero;
                Image fillImg = progressBar.fillRect.GetComponent<Image>();
                if (fillImg != null)
                {
                    fillImg.sprite = null;
                    fillImg.color = FantasyUiThemeHelper.GoldBright;
                }
            }
        }

        // Trạng thái nạp (góc dưới-trái dưới thanh progress)
        if (loadingStatus != null)
        {
            loadingStatus.transform.SetParent(footer.transform, false);
            RectTransform stRt = loadingStatus.rectTransform;
            stRt.anchorMin = new Vector2(0f, 0f);
            stRt.anchorMax = new Vector2(0.75f, 0f);
            stRt.pivot = new Vector2(0f, 0f);
            stRt.anchoredPosition = new Vector2(0f, 6f);
            stRt.sizeDelta = new Vector2(0f, 30f);

            loadingStatus.fontSize = 18f;
            loadingStatus.fontStyle = FontStyles.Normal;
            loadingStatus.color = FantasyUiThemeHelper.TextMuted;
            loadingStatus.alignment = TextAlignmentOptions.MidlineLeft;
        }

        // Phần trăm tải (góc dưới-phải dưới thanh progress)
        if (progressText != null)
        {
            progressText.transform.SetParent(footer.transform, false);
            RectTransform ptRt = progressText.rectTransform;
            ptRt.anchorMin = new Vector2(0.75f, 0f);
            ptRt.anchorMax = new Vector2(1f, 0f);
            ptRt.pivot = new Vector2(1f, 0f);
            ptRt.anchoredPosition = new Vector2(0f, 4f);
            ptRt.sizeDelta = new Vector2(0f, 34f);

            progressText.fontSize = 24f;
            progressText.fontStyle = FontStyles.Bold;
            progressText.color = FantasyUiThemeHelper.GoldBright;
            progressText.alignment = TextAlignmentOptions.MidlineRight;
        }
    }

    private static void SetTopLeftRect(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private IEnumerator LoadSceneAsync()
    {
        if (progressBar == null || progressText == null || loadingStatus == null)
        {
            Debug.LogError("[LoadingController] Chưa gán đầy đủ UI Loading!");
            yield break;
        }

        string requestedScene = pendingTargetScene;
        pendingTargetScene = null;

        // Tự chuyển hướng MainScene cũ sang LoginScene theo luồng Loading -> Login -> PickCharacter -> Lv1
        string targetScene = !string.IsNullOrWhiteSpace(requestedScene)
            ? requestedScene.Trim()
            : (string.IsNullOrWhiteSpace(sceneToLoad) || sceneToLoad == "MainScene" ? "LoginScene" : sceneToLoad.Trim());

        if (string.Equals(targetScene, LoadingSceneName, System.StringComparison.OrdinalIgnoreCase))
        {
            targetScene = "LoginScene";
        }

        if (!Application.CanStreamedLevelBeLoaded(targetScene))
        {
            progressBar.value = 0f;
            progressText.text = "0%";
            loadingStatus.text = $"Lỗi: Không tìm thấy Scene '{targetScene}' trong Build Settings!";
            Debug.LogError($"[LoadingController] Không thể tải Scene '{targetScene}' (chưa có trong Build Settings).");
            yield break;
        }

        tipIndex = Random.Range(0, GameplayTips.Length);
        if (tipText != null) tipText.text = GameplayTips[tipIndex];

        realProgress = 0f;
        displayedProgress = 0f;
        lastUiUpdateFrame = -1;
        progressBar.value = 0f;
        progressText.text = "0%";
        loadingStatus.text = "Khởi tạo hệ thống tải tài nguyên...";

        if (startDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(startDelay);
        }

        loadingStartRealtime = Time.realtimeSinceStartup;
        nextTipSwitchTime = loadingStartRealtime + 2.2f;

        // Giai đoạn 1: Tải xong toàn bộ asset trước khi gọi SceneManager.LoadSceneAsync (tránh kẹt hàng đợi PreloadManager)
        GameAssetPreloader preloader = GameAssetPreloader.EnsureInstance();
        bool needsCorePreload = preloadCoreAssets && (!GameAssetPreloader.IsCoreAssetsLoaded || GameAssetPreloader.HasMissingCoreAssets);
        bool hasExtras = (extraResourcePaths != null && extraResourcePaths.Length > 0)
            || (shaderVariantCollections != null && shaderVariantCollections.Length > 0);

        float assetWeight = needsCorePreload ? 0.70f : (hasExtras ? 0.25f : 0.10f);
        float sceneWeight = 1f - assetWeight;

        while (true)
        {
            yield return preloader.PreloadAssetsCoroutine(
                preloadCoreAssets,
                extraResourcePaths,
                directPreloadAssets,
                shaderVariantCollections,
                (assetProgress, status) => UpdateLoadingUi(assetProgress * assetWeight, status));

            if (!blockOnMissingRequiredAsset || preloader.MissingRequiredAssets.Count == 0)
            {
                break;
            }

            string firstMissing = preloader.MissingRequiredAssets[0];
            loadingStatus.text = $"Thiếu {preloader.MissingRequiredAssets.Count} tài nguyên bắt buộc ({firstMissing}). Nhấn [R] để tải lại.";

            while (!Input.GetKeyDown(KeyCode.R))
            {
                yield return null;
            }

            realProgress = 0f;
            displayedProgress = 0f;
            lastUiUpdateFrame = -1;
            progressBar.value = 0f;
            progressText.text = "0%";
            loadingStatus.text = "Đang tải lại tài nguyên...";
            yield return null;
        }

        // Giai đoạn 2: Tải bất đồng bộ Scene đích sau khi toàn bộ asset đã hoàn tất
        AsyncOperation operation = SceneManager.LoadSceneAsync(targetScene);
        if (operation == null)
        {
            loadingStatus.text = $"Lỗi: Không thể tải Scene '{targetScene}'!";
            Debug.LogError("[LoadingController] Không thể tải Scene: " + targetScene);
            yield break;
        }

        operation.allowSceneActivation = false;
        string displaySceneName = GetSceneDisplayName(targetScene);

        while (operation.progress < 0.9f
            || displayedProgress < 0.999f
            || (Time.realtimeSinceStartup - loadingStartRealtime) < minDisplayDuration)
        {
            float sceneProgress = Mathf.Clamp01(operation.progress / 0.9f);
            float targetReal = assetWeight + sceneProgress * sceneWeight;
            string status = operation.progress < 0.9f
                ? $"Đang nạp tài nguyên cảnh {displaySceneName} ({Mathf.RoundToInt(sceneProgress * 100f)}%)..."
                : $"Hoàn tất nạp tài nguyên {displaySceneName}...";

            UpdateLoadingUi(targetReal, status);
            yield return null;
        }

        // Giai đoạn 3: Chốt 100% và kích hoạt Scene đích
        progressBar.value = 1f;
        progressText.text = "100%";
        loadingStatus.text = "Đã tải xong tài nguyên — Sẵn sàng ra trận!";

        if (readyHoldDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(readyHoldDuration);
        }

        operation.allowSceneActivation = true;
        while (!operation.isDone)
        {
            yield return null;
        }
    }

    private void UpdateLoadingUi(float targetRealProgress, string statusText)
    {
        realProgress = Mathf.Max(realProgress, Mathf.Clamp01(targetRealProgress));

        int currentFrame = Time.frameCount;
        if (currentFrame != lastUiUpdateFrame)
        {
            lastUiUpdateFrame = currentFrame;
            float speed = Mathf.Max(0.1f, progressSmoothSpeed);
            displayedProgress = Mathf.Min(
                realProgress,
                Mathf.MoveTowards(displayedProgress, realProgress, speed * Time.unscaledDeltaTime));
        }

        if (progressBar != null)
        {
            progressBar.value = displayedProgress;
        }

        if (progressText != null)
        {
            int percentage = displayedProgress >= 0.999f
                ? 100
                : Mathf.Clamp(Mathf.FloorToInt(displayedProgress * 100f), 0, 99);
            progressText.text = percentage + "%";
        }

        if (loadingStatus != null && !string.IsNullOrEmpty(statusText))
        {
            loadingStatus.text = statusText;
        }

        if (tipText != null && Time.realtimeSinceStartup >= nextTipSwitchTime)
        {
            nextTipSwitchTime = Time.realtimeSinceStartup + 2.2f;
            tipIndex = (tipIndex + 1) % GameplayTips.Length;
            tipText.text = GameplayTips[tipIndex];
        }
    }

    private static string GetSceneDisplayName(string sceneName)
    {
        return sceneName switch
        {
            "LoginScene" => "Đăng Nhập",
            "PickCharacter" => "Chọn Nhân Vật",
            "Lv1" => "Rừng Orc (Màn 1)",
            _ => sceneName
        };
    }
}
