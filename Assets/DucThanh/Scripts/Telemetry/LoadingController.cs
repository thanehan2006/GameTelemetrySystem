using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Điều khiển màn hình tải cảnh (LoadingScene), luân phiên mẹo chơi và đồng bộ tiến trình nạp tài nguyên
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

    [Header("UI References")]
    [SerializeField] private RectTransform keyArtRect;
    [SerializeField] private TMP_Text tipText;

    private float realProgress;
    private float displayedProgress;
    private float loadingStartRealtime;
    private float nextTipSwitchTime;
    private int tipIndex;
    private int lastUiUpdateFrame = -1;
    private int lastDisplayedPercent = -1;
    private string lastStatusText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        pendingTargetScene = null;
    }

    // Lưu tên scene đích và chuyển qua LoadingScene để tải bất đồng bộ
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

    // Áp dụng giao diện Loading, ẩn mẹo phù hợp và bắt đầu coroutine tải dữ liệu
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

    private Canvas ResolveLoadingCanvas()
    {
        if (progressBar != null)
        {
            Canvas barCanvas = progressBar.GetComponentInParent<Canvas>(true);
            if (barCanvas != null && barCanvas.rootCanvas != null && barCanvas.rootCanvas.gameObject.name != "GameMenuCanvas")
            {
                return barCanvas.rootCanvas;
            }
        }
        return FantasyUiThemeHelper.FindSceneCanvas(gameObject);
    }

    private void EnsureLoadingVisuals()
    {
        Canvas canvas = ResolveLoadingCanvas();
        if (canvas == null) return;

        Transform bakedArt = canvas.transform.Find("LoadingKeyArt");
        if (bakedArt != null)
        {
            if (keyArtRect == null) keyArtRect = bakedArt.GetComponent<RectTransform>();
            FantasyUiThemeHelper.RefreshKeyArtTexture(bakedArt, "UI/LoadingBg");

            Image veil = canvas.transform.Find("LoadingGradientVeil")?.GetComponent<Image>();
            if (veil != null)
            {
                FantasyUiThemeHelper.EnsureGradientOverlaySprite(veil, false, new Color(0.03f, 0.05f, 0.09f, 1f));
            }

            if (tipText == null)
            {
                Transform tipTf = canvas.transform.Find("LoadingFooterHUD/GameplayTipText");
                if (tipTf != null) tipText = tipTf.GetComponent<TMP_Text>();
            }

            TMP_Text bakedBadge = canvas.transform.Find("GameBrandHeader/Badge")?.GetComponent<TMP_Text>();
            if (bakedBadge != null) bakedBadge.text = "HÀNH ĐỘNG SINH TỒN  •  DARK FANTASY";

            if (canvas.transform.Find("LoadingEmbers") == null)
            {
                GameObject emberLayer = new GameObject("LoadingEmbers", typeof(RectTransform));
                emberLayer.transform.SetParent(canvas.transform, false);
                emberLayer.transform.SetSiblingIndex(2);
                FantasyUiThemeHelper.StretchFull(emberLayer.GetComponent<RectTransform>());
                FloatingEmbersUI embers = emberLayer.AddComponent<FloatingEmbersUI>();
                embers.Initialize(26, 0.05f, 0.95f, new Color(1f, 0.85f, 0.35f, 1f), new Color(0.45f, 0.95f, 0.65f, 1f));
            }
        }
    }

    // Cho phép nhấn phím R để tải lại nếu thiếu tài nguyên bắt buộc
    private void Update()
    {

        if (keyArtRect != null)
        {
            float scale = 1.0f + Mathf.PingPong(Time.unscaledTime * 0.012f, 0.06f);
            keyArtRect.localScale = new Vector3(scale, scale, 1f);
        }
    }

    // Nạp trước tài nguyên game bằng GameAssetPreloader rồi kích hoạt scene chiến đấu
    private IEnumerator LoadSceneAsync()
    {
        if (progressBar == null || progressText == null || loadingStatus == null)
        {
            Debug.LogError("[LoadingController] Chưa gán đầy đủ UI Loading!");
            yield break;
        }

        string requestedScene = pendingTargetScene;
        pendingTargetScene = null;

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
        lastDisplayedPercent = 0;
        lastStatusText = "Khởi tạo hệ thống tải tài nguyên...";
        progressBar.value = 0f;
        progressText.text = "0%";
        loadingStatus.text = lastStatusText;

        if (startDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(startDelay);
        }

        loadingStartRealtime = Time.realtimeSinceStartup;
        nextTipSwitchTime = loadingStartRealtime + 2.2f;

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
            lastDisplayedPercent = 0;
            lastStatusText = "Đang tải lại tài nguyên...";
            progressBar.value = 0f;
            progressText.text = "0%";
            loadingStatus.text = lastStatusText;
            yield return null;
        }

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

        progressBar.value = 1f;
        lastDisplayedPercent = 100;
        progressText.text = "100%";
        lastStatusText = "Đã tải xong tài nguyên — Sẵn sàng ra trận!";
        loadingStatus.text = lastStatusText;

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
            if (percentage != lastDisplayedPercent)
            {
                lastDisplayedPercent = percentage;
                progressText.text = percentage + "%";
            }
        }

        if (loadingStatus != null && !string.IsNullOrEmpty(statusText)
            && !string.Equals(lastStatusText, statusText, System.StringComparison.Ordinal))
        {
            lastStatusText = statusText;
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
            "MainMenuScene" => "Menu Chính",
            "PickCharacter" => "Chọn Nhân Vật",
            "PickMap" => "Chọn Bản Đồ",
            "Lv1" => "Rừng Orc (Map 1)",
            "Lv2" => "Cao Nguyên Băng Tuyết (Map 2)",
            "Lv3" => "Pháo Đài Dung Nham (Map 3)",
            _ => sceneName
        };
    }
}
