using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Màn hình Loading phong cách Action RPG AAA:
/// - Hình nền nghệ thuật Rừng Orc & Mỏ Vàng kèm hiệu ứng Ken Burns zoom mượt + đom đóm bay.
/// - Logo game VANGUARD: RAID OF THE ORC WOODS, mẹo chơi luân phiên chuẩn gameplay 3 Màn.
/// - Thanh tiến trình vàng kim mượt mà, trạng thái nạp theo giai đoạn và chuyển sang LoginScene.
/// </summary>
public class LoadingController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text loadingStatus;

    [Header("Scene")]
    [SerializeField] private string sceneToLoad = "LoginScene";

    [Header("Loading Settings")]
    [SerializeField] private float startDelay = 0.35f;
    [SerializeField] private float loadingDuration = 3.2f;

    private static readonly string[] GameplayTips =
    {
        "Mẹo chiến đấu: Giữ [Chuột Phải] để Đỡ Đòn (giảm 80% sát thương) — bấm đúng lúc sẽ Parry đẩy choáng quái & hồi 5% HP!",
        "Mẹo sinh tồn: Cẩn thận cụm Bẫy Gai ở Màn 2 (Thung Lũng Bẫy) — quan sát ánh sáng đỏ để né.",
        "Mẹo nâng cấp: Giết quái Orc nhặt Vàng rồi nhấn [B] (hoặc [E] gần Trại Lửa) để mở Cửa Hàng mua Vũ Khí & Bình Máu!",
        "Mẹo dẫn đường: Theo dõi Cột Sáng Dẫn Đường trên địa hình để tiến tới Đấu Trường Boss Mỏ Vàng.",
        "Mẹo hồi phục: Nhặt Bình Hồi Máu phát sáng trên đường hoặc rơi từ quái và chạm Trại Lửa Checkpoint để hồi HP."
    };

    private static readonly string[] StatusStages =
    {
        "Khởi tạo hệ thống Aegis Telemetry...",
        "Nạp địa hình Rừng Orc & 3 Cứ Điểm...",
        "Chuẩn bị vũ khí & hiệu ứng chiến đấu...",
        "Đồng bộ kết nối máy chủ..."
    };

    [Header("Optional Pre-Baked UI References")]
    [SerializeField] private RectTransform keyArtRect;
    [SerializeField] private TMP_Text tipText;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Giảm độ trễ quá dài từ cấu hình cũ để cảm giác tải game mượt mà chuẩn AAA
        if (startDelay > 1.5f) startDelay = 0.35f;
        if (loadingDuration > 5f) loadingDuration = 3.2f;

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

        int tipIndex = Random.Range(0, GameplayTips.Length);
        if (tipText != null) tipText.text = GameplayTips[tipIndex];

        progressBar.value = 0f;
        progressText.text = "0%";
        loadingStatus.text = StatusStages[0];

        // Tự chuyển hướng MainScene cũ sang LoginScene theo luồng Loading -> Login -> Lv1
        string targetScene = string.IsNullOrWhiteSpace(sceneToLoad) || sceneToLoad == "MainScene"
            ? "LoginScene"
            : sceneToLoad;

        AsyncOperation operation = SceneManager.LoadSceneAsync(targetScene);
        if (operation == null)
        {
            Debug.LogError("[LoadingController] Không thể tải Scene: " + targetScene);
            yield break;
        }

        operation.allowSceneActivation = false;

        if (startDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(startDelay);
        }

        float elapsedTime = 0f;
        float nextTipSwitch = 1.9f;

        while (elapsedTime < loadingDuration || operation.progress < 0.9f)
        {
            elapsedTime += Time.unscaledDeltaTime;

            float timeProgress = loadingDuration > 0.01f ? Mathf.Clamp01(elapsedTime / loadingDuration) : 1f;
            float actualProgress = Mathf.Clamp01(operation.progress / 0.9f);
            float displayProgress = Mathf.Min(timeProgress, actualProgress);

            // Easing nhẹ cho thanh tải chạy mượt mắt
            float eased = Mathf.SmoothStep(0f, 1f, displayProgress);
            progressBar.value = eased;

            int percentage = Mathf.RoundToInt(eased * 100f);
            progressText.text = percentage + "%";

            int stageIdx = Mathf.Clamp(Mathf.FloorToInt(eased * StatusStages.Length), 0, StatusStages.Length - 1);
            loadingStatus.text = StatusStages[stageIdx];

            if (tipText != null && elapsedTime >= nextTipSwitch)
            {
                nextTipSwitch += 2.0f;
                tipIndex = (tipIndex + 1) % GameplayTips.Length;
                tipText.text = GameplayTips[tipIndex];
            }

            yield return null;
        }

        progressBar.value = 1f;
        progressText.text = "100%";
        loadingStatus.text = "Sẵn sàng ra trận!";

        yield return new WaitForSecondsRealtime(0.35f);

        operation.allowSceneActivation = true;
    }
}
