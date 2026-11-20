#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tạo sẵn toàn bộ GameObject UI & Props trực tiếp vào các Scene (LoadingScene, LoginScene, Lv1)
/// để nhìn thấy và chỉnh sửa ngay trong Edit Mode mà không cần bấm Play.
/// Menu:
/// - DucThanh > Bake All Scenes (Loading + Login + Lv1)
/// - DucThanh > Bake LoadingScene UI
/// - DucThanh > Bake LoginScene UI
/// - DucThanh > Bake Lv1 Props & HUD
/// </summary>
public static class SceneObjectBakerTool
{
    private const string LoadingScenePath = "Assets/DucThanh/Scene/LoadingScene.unity";
    private const string LoginScenePath = "Assets/DucThanh/Scene/LoginScene.unity";
    private const string Lv1ScenePath = "Assets/DucThanh/Scene/Lv1.unity";

    private const string UiAssetsDir = "Assets/DucThanh/Assets/UI";
    private const string LoadingVeilPath = UiAssetsDir + "/LoadingGradientVeil.png";
    private const string LoginVeilPath = UiAssetsDir + "/LoginRightGradient.png";
    private const string LoadingBgAssetPath = "Assets/Resources/UI/LoadingBg.jpg";
    private const string LoginBgAssetPath = "Assets/Resources/UI/LoginBg.jpg";

    [MenuItem("DucThanh/Bake All Scenes (Loading + Login + Lv1)")]
    public static void BakeAllScenes()
    {
        string activeScenePath = EditorSceneManager.GetActiveScene().path;
        EnsurePersistentGradientSprites();

        BakeLoadingSceneInternal();
        BakeLoginSceneInternal();
        BakeLv1SceneInternal();

        if (!string.IsNullOrEmpty(activeScenePath) && File.Exists(activeScenePath))
        {
            EditorSceneManager.OpenScene(activeScenePath, OpenSceneMode.Single);
        }

        Debug.Log("[SceneObjectBakerTool] Đã tạo sẵn toàn bộ GameObject trực tiếp vào LoadingScene, LoginScene và Lv1!");
    }

    [MenuItem("DucThanh/Bake LoadingScene UI")]
    public static void BakeLoadingScene()
    {
        EnsurePersistentGradientSprites();
        BakeLoadingSceneInternal();
    }

    [MenuItem("DucThanh/Bake LoginScene UI")]
    public static void BakeLoginScene()
    {
        EnsurePersistentGradientSprites();
        BakeLoginSceneInternal();
    }

    [MenuItem("DucThanh/Bake Lv1 Props & HUD")]
    public static void BakeLv1Scene()
    {
        BakeLv1SceneInternal();
    }

    [MenuItem("DucThanh/Bake End Screen (Win & Lose) in Lv1")]
    public static void BakeLv1EndScreenOnly()
    {
        var scene = EditorSceneManager.OpenScene(Lv1ScenePath, OpenSceneMode.Single);
        BakeEndScreenPanelEditor();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SceneObjectBakerTool] Đã bake màn hình Win/Lose (GameOverPanel/EndCard) vào Lv1.unity!");
    }

    // ==================== 1. BAKE LOADING SCENE ====================

    private static void BakeLoadingSceneInternal()
    {
        var scene = EditorSceneManager.OpenScene(LoadingScenePath, OpenSceneMode.Single);
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        LoadingController ctrl = Object.FindFirstObjectByType<LoadingController>();
        if (canvas == null || ctrl == null)
        {
            Debug.LogError("[SceneObjectBakerTool] Không tìm thấy Canvas hoặc LoadingController trong LoadingScene!");
            return;
        }

        FantasyUiThemeHelper.ConfigureResponsiveCanvas(canvas);
        SerializedObject ctrlSo = new SerializedObject(ctrl);

        Slider progressBar = ctrlSo.FindProperty("progressBar")?.objectReferenceValue as Slider;
        TMP_Text progressText = ctrlSo.FindProperty("progressText")?.objectReferenceValue as TMP_Text;
        TMP_Text loadingStatus = ctrlSo.FindProperty("loadingStatus")?.objectReferenceValue as TMP_Text;
        TMP_FontAsset sharedFont = progressText != null ? progressText.font : (loadingStatus != null ? loadingStatus.font : null);

        // Nếu các UI gốc đang nằm bên trong LoadingFooterHUD cũ thì đưa tạm ra Canvas trước khi xoá vỏ cũ
        if (progressBar != null) progressBar.transform.SetParent(canvas.transform, false);
        if (progressText != null) progressText.transform.SetParent(canvas.transform, false);
        if (loadingStatus != null) loadingStatus.transform.SetParent(canvas.transform, false);

        DestroyChildIfExists(canvas.transform, "LoadingKeyArt");
        DestroyChildIfExists(canvas.transform, "LoadingGradientVeil");
        DestroyChildIfExists(canvas.transform, "LoadingEmbers");
        DestroyChildIfExists(canvas.transform, "GameBrandHeader");
        DestroyChildIfExists(canvas.transform, "LoadingFooterHUD");

        Transform oldBg = canvas.transform.Find("Background");
        if (oldBg != null) oldBg.gameObject.SetActive(false);

        Transform oldTitle = canvas.transform.Find("LoadingTitle");
        if (oldTitle != null) oldTitle.gameObject.SetActive(false);

        // 1. Key Art
        GameObject artGo = new GameObject("LoadingKeyArt", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        artGo.transform.SetParent(canvas.transform, false);
        artGo.transform.SetSiblingIndex(0);
        RectTransform artRt = artGo.GetComponent<RectTransform>();
        FantasyUiThemeHelper.StretchFull(artRt);

        RawImage artRaw = artGo.GetComponent<RawImage>();
        Texture2D bgTex = AssetDatabase.LoadAssetAtPath<Texture2D>(LoadingBgAssetPath);
        artRaw.texture = bgTex;
        artRaw.color = Color.white;
        artRaw.raycastTarget = false;

        AspectRatioFitter artFitter = artGo.GetComponent<AspectRatioFitter>();
        artFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        artFitter.aspectRatio = bgTex != null && bgTex.height > 0 ? (float)bgTex.width / bgTex.height : 16f / 9f;

        // 2. Gradient Veil (dùng Sprite lưu thật trên ổ đĩa)
        GameObject veilGo = new GameObject("LoadingGradientVeil", typeof(RectTransform), typeof(Image));
        veilGo.transform.SetParent(canvas.transform, false);
        veilGo.transform.SetSiblingIndex(1);
        FantasyUiThemeHelper.StretchFull(veilGo.GetComponent<RectTransform>());
        Image veilImg = veilGo.GetComponent<Image>();
        veilImg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LoadingVeilPath);
        veilImg.color = new Color(0.03f, 0.05f, 0.09f, 1f);
        veilImg.raycastTarget = false;

        // 3. Game Brand Header
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

        // 4. Loading Footer HUD
        GameObject footer = new GameObject("LoadingFooterHUD", typeof(RectTransform));
        footer.transform.SetParent(canvas.transform, false);
        RectTransform footRt = footer.GetComponent<RectTransform>();
        footRt.anchorMin = new Vector2(0f, 0f);
        footRt.anchorMax = new Vector2(1f, 0f);
        footRt.pivot = new Vector2(0.5f, 0f);
        footRt.anchoredPosition = new Vector2(0f, 54f);
        footRt.sizeDelta = new Vector2(-176f, 130f);

        TMP_Text tip = FantasyUiThemeHelper.CreateText(footer.transform, "GameplayTipText",
            "Mẹo chiến đấu: Giữ [Chuột Phải] để Đỡ Đòn (giảm 80% sát thương) — bấm đúng lúc sẽ Parry đẩy choáng quái!",
            19f, FontStyles.Normal, new Color(0.88f, 0.91f, 0.96f, 0.95f), TextAlignmentOptions.Left, sharedFont);
        RectTransform tipRt = tip.rectTransform;
        tipRt.anchorMin = new Vector2(0f, 1f);
        tipRt.anchorMax = new Vector2(1f, 1f);
        tipRt.pivot = new Vector2(0.5f, 1f);
        tipRt.anchoredPosition = Vector2.zero;
        tipRt.sizeDelta = new Vector2(0f, 36f);

        if (progressBar != null)
        {
            progressBar.transform.SetParent(footer.transform, false);
            progressBar.interactable = false;
            progressBar.transition = Selectable.Transition.None;
            progressBar.minValue = 0f;
            progressBar.maxValue = 1f;
            progressBar.value = 0.35f;

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

        if (loadingStatus != null)
        {
            loadingStatus.transform.SetParent(footer.transform, false);
            RectTransform stRt = loadingStatus.rectTransform;
            stRt.anchorMin = new Vector2(0f, 0f);
            stRt.anchorMax = new Vector2(0.75f, 0f);
            stRt.pivot = new Vector2(0f, 0f);
            stRt.anchoredPosition = new Vector2(0f, 6f);
            stRt.sizeDelta = new Vector2(0f, 30f);

            loadingStatus.text = "Đang nạp thế giới Rừng Orc...";
            loadingStatus.fontSize = 18f;
            loadingStatus.fontStyle = FontStyles.Normal;
            loadingStatus.color = FantasyUiThemeHelper.TextMuted;
            loadingStatus.alignment = TextAlignmentOptions.MidlineLeft;
        }

        if (progressText != null)
        {
            progressText.transform.SetParent(footer.transform, false);
            RectTransform ptRt = progressText.rectTransform;
            ptRt.anchorMin = new Vector2(0.75f, 0f);
            ptRt.anchorMax = new Vector2(1f, 0f);
            ptRt.pivot = new Vector2(1f, 0f);
            ptRt.anchoredPosition = new Vector2(0f, 4f);
            ptRt.sizeDelta = new Vector2(0f, 34f);

            progressText.text = "35%";
            progressText.fontSize = 24f;
            progressText.fontStyle = FontStyles.Bold;
            progressText.color = FantasyUiThemeHelper.GoldBright;
            progressText.alignment = TextAlignmentOptions.MidlineRight;
        }

        ctrlSo.FindProperty("keyArtRect").objectReferenceValue = artRt;
        ctrlSo.FindProperty("tipText").objectReferenceValue = tip;
        ctrlSo.FindProperty("startDelay").floatValue = 0.35f;
        ctrlSo.FindProperty("loadingDuration").floatValue = 3.2f;
        ctrlSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SceneObjectBakerTool] Đã bake xong UI vào LoadingScene.unity!");
    }

    // ==================== 2. BAKE LOGIN SCENE ====================

    private static void BakeLoginSceneInternal()
    {
        var scene = EditorSceneManager.OpenScene(LoginScenePath, OpenSceneMode.Single);
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        AuthUIController ctrl = Object.FindFirstObjectByType<AuthUIController>();
        if (canvas == null || ctrl == null)
        {
            Debug.LogError("[SceneObjectBakerTool] Không tìm thấy Canvas hoặc AuthUIController trong LoginScene!");
            return;
        }

        FantasyUiThemeHelper.ConfigureResponsiveCanvas(canvas);
        SerializedObject ctrlSo = new SerializedObject(ctrl);

        GameObject loginPanel = ctrlSo.FindProperty("loginPanel")?.objectReferenceValue as GameObject;
        GameObject registerPanel = ctrlSo.FindProperty("registerPanel")?.objectReferenceValue as GameObject;
        TMP_InputField loginUser = ctrlSo.FindProperty("loginUsernameInput")?.objectReferenceValue as TMP_InputField;
        TMP_InputField loginPass = ctrlSo.FindProperty("loginPasswordInput")?.objectReferenceValue as TMP_InputField;
        TMP_Text loginMsg = ctrlSo.FindProperty("loginMessageText")?.objectReferenceValue as TMP_Text;
        Button loginBtn = ctrlSo.FindProperty("loginButton")?.objectReferenceValue as Button;
        Button openRegBtn = ctrlSo.FindProperty("openRegisterButton")?.objectReferenceValue as Button;

        TMP_InputField regUser = ctrlSo.FindProperty("registerUsernameInput")?.objectReferenceValue as TMP_InputField;
        TMP_InputField regPass = ctrlSo.FindProperty("registerPasswordInput")?.objectReferenceValue as TMP_InputField;
        TMP_InputField regConfirm = ctrlSo.FindProperty("registerConfirmPasswordInput")?.objectReferenceValue as TMP_InputField;
        TMP_Text regMsg = ctrlSo.FindProperty("registerMessageText")?.objectReferenceValue as TMP_Text;
        Button regBtn = ctrlSo.FindProperty("registerButton")?.objectReferenceValue as Button;
        Button openLoginBtn = ctrlSo.FindProperty("openLoginButton")?.objectReferenceValue as Button;

        TMP_FontAsset sharedFont = loginMsg != null
            ? loginMsg.font
            : (loginUser != null && loginUser.textComponent != null ? loginUser.textComponent.font : null);

        DestroyChildIfExists(canvas.transform, "LoginKeyArt");
        DestroyChildIfExists(canvas.transform, "LoginRightGradient");
        DestroyChildIfExists(canvas.transform, "CampfireEmbersUI");
        DestroyChildIfExists(canvas.transform, "HeroBrandGroup");
        DestroyChildIfExists(canvas.transform, "LoginFooterTag");

        // 1. Key Art
        GameObject artGo = new GameObject("LoginKeyArt", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        artGo.transform.SetParent(canvas.transform, false);
        artGo.transform.SetSiblingIndex(0);
        RectTransform artRt = artGo.GetComponent<RectTransform>();
        FantasyUiThemeHelper.StretchFull(artRt);

        RawImage artRaw = artGo.GetComponent<RawImage>();
        Texture2D bgTex = AssetDatabase.LoadAssetAtPath<Texture2D>(LoginBgAssetPath);
        artRaw.texture = bgTex;
        artRaw.color = Color.white;
        artRaw.raycastTarget = false;

        AspectRatioFitter artFitter = artGo.GetComponent<AspectRatioFitter>();
        artFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        artFitter.aspectRatio = bgTex != null && bgTex.height > 0 ? (float)bgTex.width / bgTex.height : 16f / 9f;

        // 2. Gradient Veil bên phải
        GameObject veilGo = new GameObject("LoginRightGradient", typeof(RectTransform), typeof(Image));
        veilGo.transform.SetParent(canvas.transform, false);
        veilGo.transform.SetSiblingIndex(1);
        FantasyUiThemeHelper.StretchFull(veilGo.GetComponent<RectTransform>());
        Image veilImg = veilGo.GetComponent<Image>();
        veilImg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LoginVeilPath);
        veilImg.color = new Color(0.03f, 0.045f, 0.08f, 1f);
        veilImg.raycastTarget = false;

        // 3. Brand Header bên trái
        GameObject brandGroup = new GameObject("HeroBrandGroup", typeof(RectTransform));
        brandGroup.transform.SetParent(canvas.transform, false);
        brandGroup.transform.SetSiblingIndex(2);
        RectTransform brandRt = brandGroup.GetComponent<RectTransform>();
        brandRt.anchorMin = new Vector2(0f, 1f);
        brandRt.anchorMax = new Vector2(0f, 1f);
        brandRt.pivot = new Vector2(0f, 1f);
        brandRt.anchoredPosition = new Vector2(82f, -56f);
        brandRt.sizeDelta = new Vector2(720f, 165f);

        TMP_Text badge = FantasyUiThemeHelper.CreateText(brandGroup.transform, "BrandBadge", "AEGIS TELEMETRY  •  ACTION ROGUELITE",
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

        TMP_Text heroSub = FantasyUiThemeHelper.CreateText(brandGroup.transform, "HeroSubtitle", "RAID OF THE ORC WOODS",
            22f, FontStyles.Bold, new Color(0.90f, 0.82f, 0.64f, 0.96f), TextAlignmentOptions.Left, sharedFont);
        SetTopLeftRect(heroSub.rectTransform, 0f, -118f, 680f, 34f);
        heroSub.characterSpacing = 5f;

        // 4. Footer Tag góc dưới-trái
        TMP_Text footerTag = FantasyUiThemeHelper.CreateText(canvas.transform, "LoginFooterTag",
            "3 CỨ ĐIỂM RỪNG ORC   •   ĐẤU TRƯỜNG BOSS MỎ VÀNG   •   GAME TELEMETRY v1.0",
            15f, FontStyles.Bold, new Color(0.76f, 0.80f, 0.88f, 0.72f), TextAlignmentOptions.Left, sharedFont);
        footerTag.transform.SetSiblingIndex(3);
        RectTransform ftRt = footerTag.rectTransform;
        ftRt.anchorMin = new Vector2(0f, 0f);
        ftRt.anchorMax = new Vector2(0f, 0f);
        ftRt.pivot = new Vector2(0f, 0f);
        ftRt.anchoredPosition = new Vector2(82f, 40f);
        ftRt.sizeDelta = new Vector2(900f, 28f);
        footerTag.characterSpacing = 2f;

        // 5. Style LoginPanel
        if (loginPanel != null)
        {
            loginPanel.SetActive(true);
            StyleCardBase(loginPanel, 480f, 540f, "ĐĂNG NHẬP CHIẾN BINH", "Nhập tài khoản để bắt đầu chiến dịch", sharedFont);
            PositionInCard(loginUser, 0f, -158f);
            FantasyUiThemeHelper.StyleInputField(loginUser, "Tên đăng nhập...", 396f, 54f);
            PositionInCard(loginPass, 0f, -228f);
            FantasyUiThemeHelper.StyleInputField(loginPass, "Mật khẩu...", 396f, 54f);
            StyleMsg(loginMsg, 396f, -296f, 42f);
            PositionInCard(loginBtn, 0f, -356f);
            FantasyUiThemeHelper.StyleButton(loginBtn, "VÀO TRẬN  •  ĐĂNG NHẬP", true, 396f, 56f);
            PositionInCard(openRegBtn, 0f, -430f);
            FantasyUiThemeHelper.StyleButton(openRegBtn, "Chưa có tài khoản? Đăng ký ngay", false, 396f, 48f);
        }

        // 6. Style RegisterPanel (ẩn sẵn trong Edit Mode để nhìn gọn LoginPanel, bật lên xem bất cứ lúc nào)
        if (registerPanel != null)
        {
            StyleCardBase(registerPanel, 480f, 620f, "TẠO TÀI KHOẢN MỚI", "Gia nhập quân đoàn Vanguard săn Boss Mỏ Vàng", sharedFont);
            PositionInCard(regUser, 0f, -156f);
            FantasyUiThemeHelper.StyleInputField(regUser, "Tên đăng nhập (3 - 50 ký tự)...", 396f, 52f);
            PositionInCard(regPass, 0f, -224f);
            FantasyUiThemeHelper.StyleInputField(regPass, "Mật khẩu (tối thiểu 6 ký tự)...", 396f, 52f);
            PositionInCard(regConfirm, 0f, -292f);
            FantasyUiThemeHelper.StyleInputField(regConfirm, "Nhập lại mật khẩu...", 396f, 52f);
            StyleMsg(regMsg, 396f, -358f, 44f);
            PositionInCard(regBtn, 0f, -424f);
            FantasyUiThemeHelper.StyleButton(regBtn, "ĐĂNG KÝ TÀI KHOẢN", true, 396f, 56f);
            PositionInCard(openLoginBtn, 0f, -498f);
            FantasyUiThemeHelper.StyleButton(openLoginBtn, "Đã có tài khoản? Quay lại đăng nhập", false, 396f, 48f);
            registerPanel.SetActive(false);
        }

        ctrlSo.FindProperty("keyArtRect").objectReferenceValue = artRt;
        ctrlSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SceneObjectBakerTool] Đã bake xong UI vào LoginScene.unity!");
    }

    // ==================== 3. BAKE LV1 PROPS & HUD ====================

    private static void BakeLv1SceneInternal()
    {
        var scene = EditorSceneManager.OpenScene(Lv1ScenePath, OpenSceneMode.Single);
        StageManager sm = Object.FindFirstObjectByType<StageManager>();
        if (sm == null)
        {
            Debug.LogError("[SceneObjectBakerTool] Không tìm thấy StageManager trong Lv1.unity!");
            return;
        }

        Terrain terrain = Object.FindFirstObjectByType<Terrain>();
        Vector3 s1 = SnapToTerrainEditor(sm.stage1Center, terrain);
        Vector3 s2 = SnapToTerrainEditor(sm.stage2Center, terrain);
        Vector3 s3 = SnapToTerrainEditor(sm.stage3Center, terrain);

        // Xoá nhóm props cũ nếu đã bake trước đó
        GameObject oldProps = GameObject.Find("StageProps_Generated");
        if (oldProps != null) Object.DestroyImmediate(oldProps);

        GameObject propsRoot = new GameObject("StageProps_Generated");

        CreateCheckpointEditor("Checkpoint_Stage1", SnapToTerrainEditor(s1 + new Vector3(-2.5f, 0f, 2f), terrain), 1, "Trại Rừng Ngoại Vi", 30, propsRoot.transform);
        CreateCheckpointEditor("Checkpoint_Stage2", SnapToTerrainEditor(s2 + new Vector3(3f, 0f, 0f), terrain), 2, "Thung Lũng Bẫy", 50, propsRoot.transform);
        CreateCheckpointEditor("Checkpoint_Stage3", SnapToTerrainEditor(s3 + new Vector3(0f, 0f, 16f), terrain), 3, "Cổng Mỏ Vàng", 60, propsRoot.transform);

        if (sm.runeStonePrefab != null)
        {
            SpawnPrefabEditor(sm.runeStonePrefab, SnapToTerrainEditor(s1 + new Vector3(9f, 0f, 6f), terrain), Quaternion.Euler(0f, -35f, 0f), Vector3.one * 1.3f, propsRoot.transform, "Stage1_RuneStone");
        }
        SpawnMushroomEditor(sm.mushroomPickupPrefab, SnapToTerrainEditor(s1 + new Vector3(-6f, 0f, -5f), terrain), 40, propsRoot.transform, "Stage1_HealthMushroom");
        SpawnMushroomEditor(sm.mushroomPickupPrefab, SnapToTerrainEditor(Vector3.Lerp(s1, s2, 0.35f), terrain), 40, propsRoot.transform, "Trail12_HealthMushroom_1");
        SpawnMushroomEditor(sm.mushroomPickupPrefab, SnapToTerrainEditor(Vector3.Lerp(s1, s2, 0.68f), terrain), 40, propsRoot.transform, "Trail12_HealthMushroom_2");

        Vector3[] trapOffsets =
        {
            new Vector3(-7f, 0f, 5f),
            new Vector3(6f, 0f, 7f),
            new Vector3(-9f, 0f, -6f),
            new Vector3(8f, 0f, -5f),
            new Vector3(0f, 0f, 9f),
            new Vector3(-2f, 0f, -9f)
        };
        for (int i = 0; i < trapOffsets.Length; i++)
        {
            SpawnTrapEditor(sm.spikeTrapPrefab, SnapToTerrainEditor(s2 + trapOffsets[i], terrain), propsRoot.transform, $"Stage2_SpikeTrap_{i + 1}");
        }
        SpawnMushroomEditor(sm.mushroomPickupPrefab, SnapToTerrainEditor(s2 + new Vector3(-11f, 0f, 1f), terrain), 50, propsRoot.transform, "Stage2_HealthMushroom_1");
        SpawnMushroomEditor(sm.mushroomPickupPrefab, SnapToTerrainEditor(s2 + new Vector3(11f, 0f, -2f), terrain), 50, propsRoot.transform, "Stage2_HealthMushroom_2");
        SpawnMushroomEditor(sm.mushroomPickupPrefab, SnapToTerrainEditor(Vector3.Lerp(s2, s3, 0.5f), terrain), 50, propsRoot.transform, "Trail23_HealthMushroom");

        if (sm.goldMinePrefab != null)
        {
            SpawnPrefabEditor(sm.goldMinePrefab, SnapToTerrainEditor(s3 + new Vector3(0f, 0f, -12f), terrain), Quaternion.Euler(0f, 180f, 0f), Vector3.one * 1.4f, propsRoot.transform, "Stage3_GoldMine");
        }
        if (sm.trolleyGoldPrefab != null)
        {
            SpawnPrefabEditor(sm.trolleyGoldPrefab, SnapToTerrainEditor(s3 + new Vector3(5.5f, 0f, -8f), terrain), Quaternion.Euler(0f, 35f, 0f), Vector3.one * 1.25f, propsRoot.transform, "Stage3_TrolleyGold_1");
            SpawnPrefabEditor(sm.trolleyGoldPrefab, SnapToTerrainEditor(s3 + new Vector3(-6.5f, 0f, -7f), terrain), Quaternion.Euler(0f, -25f, 0f), Vector3.one * 1.2f, propsRoot.transform, "Stage3_TrolleyGold_2");
        }
        if (sm.stoneWallPrefab != null)
        {
            int wallCount = 8;
            float ringRadius = 19f;
            for (int i = 0; i < wallCount; i++)
            {
                if (i == 1 || i == 2) continue;
                float angle = i * (360f / wallCount) * Mathf.Deg2Rad;
                Vector3 wallPos = SnapToTerrainEditor(s3 + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ringRadius, terrain);
                Quaternion wallRot = Quaternion.LookRotation(new Vector3(s3.x - wallPos.x, 0f, s3.z - wallPos.z));
                SpawnPrefabEditor(sm.stoneWallPrefab, wallPos, wallRot, Vector3.one * 1.4f, propsRoot.transform, $"Stage3_ArenaWall_{i}");
            }
        }
        if (sm.fireflyFxPrefab != null)
        {
            SpawnPrefabEditor(sm.fireflyFxPrefab, s3 + Vector3.up * 1.5f, Quaternion.identity, Vector3.one, propsRoot.transform, "Stage3_Fireflies");
        }
        SpawnMushroomEditor(sm.mushroomPickupPrefab, SnapToTerrainEditor(s3 + new Vector3(-12f, 0f, 4f), terrain), 60, propsRoot.transform, "Stage3_HealthMushroom_1");
        SpawnMushroomEditor(sm.mushroomPickupPrefab, SnapToTerrainEditor(s3 + new Vector3(12f, 0f, 4f), terrain), 60, propsRoot.transform, "Stage3_HealthMushroom_2");

        // Bake HUD Panels trực tiếp lên Canvas Lv1 để nhìn thấy ngay ở Edit Mode
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas != null)
        {
            DestroyChildIfExists(canvas.transform, "StageMissionHUD");
            DestroyChildIfExists(canvas.transform, "BossHpBarHUD");
            DestroyChildIfExists(canvas.transform, "StageBannerHUD");

            TMP_Text existingTxt = canvas.GetComponentInChildren<TMP_Text>(true);
            TMP_FontAsset font = existingTxt != null ? existingTxt.font : null;

            GameObject missionPanel = CreatePanelEditor("StageMissionHUD", canvas.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -20f), new Vector2(410f, 108f), new Color(0.05f, 0.08f, 0.14f, 0.78f));

            CreateHudTextEditor("StageTitleText", missionPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(-28f, 28f), "MÀN 1/3 • TRẠI RỪNG NGOẠI VI", 18, FontStyles.Bold, new Color(1f, 0.84f, 0.32f), TextAlignmentOptions.Left, font);
            CreateHudTextEditor("ObjectiveText", missionPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -42f), new Vector2(-28f, 26f), "Tiêu diệt quân trinh sát Orc", 15, FontStyles.Normal, Color.white, TextAlignmentOptions.Left, font);
            CreateHudTextEditor("DashStatusText", missionPanel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -72f), new Vector2(-28f, 24f), "[Chuột Trái] Chém   •   [Giữ Chuột Phải] Đỡ đòn", 13, FontStyles.Normal, new Color(0.55f, 0.92f, 1f), TextAlignmentOptions.Left, font);

            GameObject bossBarRoot = CreatePanelEditor("BossHpBarHUD", canvas.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -22f), new Vector2(520f, 68f), new Color(0.12f, 0.03f, 0.05f, 0.85f));
            CreateHudTextEditor("BossNameText", bossBarRoot.transform, new Vector2(0f, 1f), new Vector2(0.75f, 1f), new Vector2(0f, 1f),
                new Vector2(16f, -8f), new Vector2(0f, 24f), "BOSS: ĐẠI TƯỚNG ORC WARLORD", 16, FontStyles.Bold, new Color(1f, 0.38f, 0.32f), TextAlignmentOptions.Left, font);
            CreateHudTextEditor("BossHpValueText", bossBarRoot.transform, new Vector2(0.65f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-16f, -8f), new Vector2(0f, 24f), "560 / 560", 14, FontStyles.Bold, Color.white, TextAlignmentOptions.Right, font);

            GameObject sliderGo = new GameObject("BossSlider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(bossBarRoot.transform, false);
            RectTransform sRt = sliderGo.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0f, 0f);
            sRt.anchorMax = new Vector2(1f, 0f);
            sRt.pivot = new Vector2(0.5f, 0f);
            sRt.anchoredPosition = new Vector2(0f, 12f);
            sRt.sizeDelta = new Vector2(-32f, 18f);

            GameObject bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(sliderGo.transform, false);
            FantasyUiThemeHelper.StretchFull(bgGo.GetComponent<RectTransform>());
            bgGo.GetComponent<Image>().color = new Color(0.25f, 0.08f, 0.08f, 0.95f);

            GameObject fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            FantasyUiThemeHelper.StretchFull(fillArea.GetComponent<RectTransform>());

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(fillArea.transform, false);
            FantasyUiThemeHelper.StretchFull(fillGo.GetComponent<RectTransform>());
            fillGo.GetComponent<Image>().color = new Color(0.92f, 0.18f, 0.22f, 1f);

            Slider bossSlider = sliderGo.GetComponent<Slider>();
            bossSlider.interactable = false;
            bossSlider.transition = Selectable.Transition.None;
            bossSlider.fillRect = fillGo.GetComponent<RectTransform>();
            bossSlider.minValue = 0f;
            bossSlider.maxValue = 560f;
            bossSlider.value = 560f;
            bossBarRoot.SetActive(false);

            GameObject bannerRoot = CreatePanelEditor("StageBannerHUD", canvas.transform,
                new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(640f, 84f), new Color(0.04f, 0.06f, 0.1f, 0.82f));
            CreateHudTextEditor("BannerText", bannerRoot.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-24f, -12f), "", 20, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font);
            bannerRoot.SetActive(false);

            BakeEndScreenPanelEditor();
        }

        if (sm.skyboxStage1Dawn != null)
        {
            RenderSettings.skybox = sm.skyboxStage1Dawn;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[SceneObjectBakerTool] Đã bake xong StageProps_Generated, HUD & Màn hình Win/Lose trực tiếp vào Lv1.unity!");
    }

    private static void BakeEndScreenPanelEditor()
    {
        GameOverManager gom = Object.FindFirstObjectByType<GameOverManager>();
        if (gom == null || gom.gameOverPanel == null) return;

        GameObject panel = gom.gameOverPanel;
        bool wasActive = panel.activeSelf;
        panel.SetActive(true);

        RectTransform panelRt = panel.GetComponent<RectTransform>();
        if (panelRt != null) FantasyUiThemeHelper.StretchFull(panelRt);

        Image bgImg = panel.GetComponent<Image>() ?? panel.AddComponent<Image>();
        bgImg.sprite = null;
        bgImg.color = new Color(0.03f, 0.05f, 0.09f, 0.84f);

        if (panel.GetComponent<CanvasGroup>() == null) panel.AddComponent<CanvasGroup>();

        Transform oldTitle = panel.transform.Find("GameOverText") ?? panel.transform.Find("EndCard/GameOverText");
        Transform oldTryAgain = panel.transform.Find("TryAgainButton") ?? panel.transform.Find("EndCard/TryAgainButton");
        Transform oldQuit = panel.transform.Find("QuitButton") ?? panel.transform.Find("EndCard/QuitButton");

        if (oldTitle != null) oldTitle.SetParent(panel.transform, false);
        if (oldTryAgain != null) oldTryAgain.SetParent(panel.transform, false);
        if (oldQuit != null) oldQuit.SetParent(panel.transform, false);

        DestroyChildIfExists(panel.transform, "EndCard");
        DestroyChildIfExists(panel.transform, "EndScreenEmbers");

        TMP_FontAsset font = oldTitle != null ? oldTitle.GetComponent<TMP_Text>()?.font : panel.GetComponentInChildren<TMP_Text>(true)?.font;

        GameObject emberGo = new GameObject("EndScreenEmbers", typeof(RectTransform));
        emberGo.transform.SetParent(panel.transform, false);
        FantasyUiThemeHelper.StretchFull(emberGo.GetComponent<RectTransform>());
        emberGo.AddComponent<FloatingEmbersUI>();

        GameObject cardGo = new GameObject("EndCard", typeof(RectTransform), typeof(Image));
        cardGo.transform.SetParent(panel.transform, false);
        RectTransform cardRt = cardGo.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.anchoredPosition = Vector2.zero;
        cardRt.sizeDelta = new Vector2(900f, 640f);

        Color gold = new Color(0.95f, 0.76f, 0.28f, 1f);
        Color goldBright = new Color(1f, 0.88f, 0.48f, 1f);
        FantasyUiThemeHelper.StyleCardPanel(cardGo, new Color(0.052f, 0.072f, 0.12f, 0.95f), new Color(gold.r, gold.g, gold.b, 0.78f));

        FantasyUiThemeHelper.CreateAccentLine(cardGo.transform, "TopAccentBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 6f), gold);
        FantasyUiThemeHelper.CreateAccentLine(cardGo.transform, "BottomAccentBar",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 4f), new Color(gold.r, gold.g, gold.b, 0.68f));

        TMP_Text badge = FantasyUiThemeHelper.CreateText(cardGo.transform, "StatusBadge", "CHIẾN DỊCH HOÀN TẤT  •  VANGUARD",
            18f, FontStyles.Bold, goldBright, TextAlignmentOptions.Center, font);
        SetCenteredCardRectEditor(badge.rectTransform, 0f, -40f, 800f, 28f);
        badge.characterSpacing = 6f;

        TMP_Text titleTxt = oldTitle != null ? oldTitle.GetComponent<TMP_Text>() : null;
        if (titleTxt != null) oldTitle.SetParent(cardGo.transform, false);
        else titleTxt = FantasyUiThemeHelper.CreateText(cardGo.transform, "GameOverText", "CHIẾN THẮNG!", 72f, FontStyles.Bold, goldBright, TextAlignmentOptions.Center, font);

        SetCenteredCardRectEditor(titleTxt.rectTransform, 0f, -80f, 820f, 92f);
        titleTxt.text = "CHIẾN THẮNG!";
        titleTxt.fontSize = 72f;
        titleTxt.fontStyle = FontStyles.Bold;
        titleTxt.color = goldBright;
        titleTxt.alignment = TextAlignmentOptions.Center;
        titleTxt.characterSpacing = 8f;
        gom.titleText = titleTxt;
        EditorUtility.SetDirty(gom);

        FantasyUiThemeHelper.CreateAccentLine(cardGo.transform, "TitleDivider",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -182f), new Vector2(280f, 3f), gold);

        TMP_Text sub = FantasyUiThemeHelper.CreateText(cardGo.transform, "SubtitleText",
            "Đại Tướng Orc Warlord đã bị tiêu diệt. Mỏ Vàng và khu rừng đã được giải phóng!",
            21f, FontStyles.Normal, new Color(0.84f, 0.88f, 0.94f, 0.95f), TextAlignmentOptions.Center, font);
        SetCenteredCardRectEditor(sub.rectTransform, 0f, -202f, 780f, 58f);

        GameObject statsRow = new GameObject("MatchStatsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        statsRow.transform.SetParent(cardGo.transform, false);
        SetCenteredCardRectEditor(statsRow.GetComponent<RectTransform>(), 0f, -280f, 810f, 132f);

        HorizontalLayoutGroup hlg = statsRow.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 22f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        CreateStatTileEditor(statsRow.transform, "TileStage", "TIẾN ĐỘ MÀN", "Màn 3 / 3", gold, font);
        CreateStatTileEditor(statsRow.transform, "TileScore", "ĐIỂM  •  VÀNG", "1,250  •  320 G", gold, font);
        CreateStatTileEditor(statsRow.transform, "TileTime", "THỜI LƯỢNG", "02:30", gold, font);

        TMP_Text note = FantasyUiThemeHelper.CreateText(cardGo.transform, "TelemetryNote",
            "✓ Kết quả đã được đồng bộ lên hệ thống Aegis Telemetry",
            16f, FontStyles.Italic, new Color(0.45f, 0.95f, 0.65f, 0.94f), TextAlignmentOptions.Center, font);
        SetCenteredCardRectEditor(note.rectTransform, 0f, -438f, 780f, 30f);

        if (oldTryAgain != null)
        {
            oldTryAgain.SetParent(cardGo.transform, false);
            SetCenteredCardRectEditor(oldTryAgain.GetComponent<RectTransform>(), -195f, -504f, 360f, 68f);
            FantasyUiThemeHelper.StyleButton(oldTryAgain.GetComponent<Button>(), "CHƠI LẠI CHIẾN DỊCH", true, 360f, 68f);
            TMP_Text bTxt = oldTryAgain.GetComponentInChildren<TMP_Text>(true);
            if (bTxt != null) bTxt.fontSize = 23f;
        }

        if (oldQuit != null)
        {
            oldQuit.SetParent(cardGo.transform, false);
            SetCenteredCardRectEditor(oldQuit.GetComponent<RectTransform>(), 195f, -504f, 360f, 68f);
            FantasyUiThemeHelper.StyleButton(oldQuit.GetComponent<Button>(), "THOÁT GAME", false, 360f, 68f);
            TMP_Text bTxt = oldQuit.GetComponentInChildren<TMP_Text>(true);
            if (bTxt != null) bTxt.fontSize = 22f;
        }

        panel.SetActive(wasActive);
    }

    private static void CreateStatTileEditor(Transform parent, string name, string label, string valText, Color accent, TMP_FontAsset font)
    {
        GameObject tile = new GameObject(name, typeof(RectTransform), typeof(Image));
        tile.transform.SetParent(parent, false);
        tile.GetComponent<Image>().color = new Color(0.085f, 0.115f, 0.185f, 0.88f);

        Outline outline = tile.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.14f);
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        FantasyUiThemeHelper.CreateAccentLine(tile.transform, "AccentStrip",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 3f), accent);

        TMP_Text lbl = FantasyUiThemeHelper.CreateText(tile.transform, "Label", label, 15f, FontStyles.Bold, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.Center, font);
        RectTransform lrt = lbl.rectTransform;
        lrt.anchorMin = new Vector2(0f, 1f);
        lrt.anchorMax = new Vector2(1f, 1f);
        lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0f, -20f);
        lrt.sizeDelta = new Vector2(-16f, 26f);
        lbl.characterSpacing = 2f;

        TMP_Text val = FantasyUiThemeHelper.CreateText(tile.transform, "Value", valText, 28f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font);
        RectTransform vrt = val.rectTransform;
        vrt.anchorMin = new Vector2(0f, 0f);
        vrt.anchorMax = new Vector2(1f, 1f);
        vrt.offsetMin = new Vector2(12f, 14f);
        vrt.offsetMax = new Vector2(-12f, -50f);
        val.enableAutoSizing = true;
        val.fontSizeMin = 20f;
        val.fontSizeMax = 30f;
    }

    private static void SetCenteredCardRectEditor(RectTransform rt, float x, float yFromTop, float w, float h)
    {
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, yFromTop);
        rt.sizeDelta = new Vector2(w, h);
    }

    // ==================== PERSISTENT GRADIENT ASSETS ====================

    private static void EnsurePersistentGradientSprites()
    {
        if (!Directory.Exists(UiAssetsDir))
        {
            Directory.CreateDirectory(UiAssetsDir);
        }

        WriteGradientPng(LoadingVeilPath, false);
        WriteGradientPng(LoginVeilPath, true);
        AssetDatabase.Refresh();

        ConfigureSpriteImporter(LoadingVeilPath);
        ConfigureSpriteImporter(LoginVeilPath);
    }

    private static void WriteGradientPng(string assetPath, bool horizontal)
    {
        Texture2D tex;
        if (!horizontal)
        {
            const int h = 128;
            tex = new Texture2D(4, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                float t = (float)y / (h - 1);
                float bottomDark = Mathf.Pow(Mathf.Clamp01(1f - t / 0.45f), 1.6f) * 0.94f;
                float topDark = Mathf.Pow(Mathf.Clamp01((t - 0.72f) / 0.28f), 1.8f) * 0.48f;
                Color c = new Color(1f, 1f, 1f, Mathf.Clamp01(bottomDark + topDark));
                for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
            }
        }
        else
        {
            const int w = 128;
            tex = new Texture2D(w, 4, TextureFormat.RGBA32, false);
            for (int x = 0; x < w; x++)
            {
                float t = (float)x / (w - 1);
                float rightDark = Mathf.Pow(Mathf.Clamp01((t - 0.35f) / 0.65f), 1.35f) * 0.78f;
                Color c = new Color(1f, 1f, 1f, rightDark);
                for (int y = 0; y < 4; y++) tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        File.WriteAllBytes(assetPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    private static void ConfigureSpriteImporter(string assetPath)
    {
        TextureImporter imp = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (imp == null) return;
        if (imp.textureType != TextureImporterType.Sprite || imp.wrapMode != TextureWrapMode.Clamp)
        {
            imp.textureType = TextureImporterType.Sprite;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
        }
    }

    // ==================== SMALL HELPERS ====================

    private static void StyleCardBase(GameObject panel, float w, float h, string titleStr, string subtitleStr, TMP_FontAsset font)
    {
        RectTransform rt = panel.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-110f, 0f);
            rt.sizeDelta = new Vector2(w, h);
        }

        DestroyChildIfExists(panel.transform, "TopGoldBar");
        DestroyChildIfExists(panel.transform, "SubtitleText");
        DestroyChildIfExists(panel.transform, "HeaderDivider");

        FantasyUiThemeHelper.StyleCardPanel(panel, FantasyUiThemeHelper.ObsidianCard, FantasyUiThemeHelper.GoldBorder);
        FantasyUiThemeHelper.CreateAccentLine(panel.transform, "TopGoldBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, 4f), FantasyUiThemeHelper.GoldPrimary);

        Transform titleTf = panel.transform.Find("TitleText");
        TMP_Text title = titleTf != null ? titleTf.GetComponent<TMP_Text>() : null;
        if (title == null)
        {
            title = FantasyUiThemeHelper.CreateText(panel.transform, "TitleText", titleStr,
                28f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Center, font);
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
            16f, FontStyles.Normal, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.Center, font);
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

    private static void PositionInCard(Component comp, float x, float yFromTop)
    {
        if (comp == null) return;
        RectTransform rt = comp.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, yFromTop);
    }

    private static void StyleMsg(TMP_Text msgText, float w, float yFromTop, float h)
    {
        if (msgText == null) return;
        RectTransform rt = msgText.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, yFromTop);
        rt.sizeDelta = new Vector2(w, h);
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

    private static void DestroyChildIfExists(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null) Object.DestroyImmediate(child.gameObject);
    }

    private static Vector3 SnapToTerrainEditor(Vector3 pos, Terrain terrain)
    {
        if (terrain != null)
        {
            float y = terrain.SampleHeight(pos) + terrain.transform.position.y;
            return new Vector3(pos.x, y, pos.z);
        }
        return pos;
    }

    private static GameObject SpawnPrefabEditor(GameObject prefab, Vector3 pos, Quaternion rot, Vector3 scale, Transform parent, string name)
    {
        GameObject obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        obj.name = name;
        obj.transform.SetPositionAndRotation(pos, rot);
        obj.transform.localScale = scale;
        return obj;
    }

    private static void CreateCheckpointEditor(string name, Vector3 pos, int stage, string title, int heal, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        CampfireCheckpoint cp = go.AddComponent<CampfireCheckpoint>();
        cp.stageNumber = stage;
        cp.checkpointName = title;
        cp.healBonus = heal;
        cp.activationRadius = 6f;
    }

    private static void SpawnTrapEditor(GameObject prefab, Vector3 pos, Transform parent, string name)
    {
        GameObject trap = prefab != null
            ? SpawnPrefabEditor(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), Vector3.one * 1.35f, parent, name)
            : GameObject.CreatePrimitive(PrimitiveType.Cylinder);

        if (prefab == null)
        {
            trap.name = name;
            trap.transform.SetParent(parent, false);
            trap.transform.position = pos;
        }

        TrapHazard hz = trap.GetComponent<TrapHazard>() ?? trap.AddComponent<TrapHazard>();
        hz.damage = 22;
        hz.triggerRadius = 1.75f;
    }

    private static void SpawnMushroomEditor(GameObject prefab, Vector3 pos, int heal, Transform parent, string name)
    {
        GameObject obj = prefab != null
            ? SpawnPrefabEditor(prefab, pos + Vector3.up * 0.2f, Quaternion.identity, Vector3.one * 1.6f, parent, name)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);

        if (prefab == null)
        {
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.position = pos + Vector3.up * 0.2f;
        }

        HealthPickup hp = obj.GetComponent<HealthPickup>() ?? obj.AddComponent<HealthPickup>();
        hp.healAmount = heal;
    }

    private static GameObject CreatePanelEditor(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = color;
        return go;
    }

    private static void CreateHudTextEditor(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size, string text, float sizePt, FontStyles style, Color color, TextAlignmentOptions align, TMP_FontAsset font)
    {
        TMP_Text tmp = FantasyUiThemeHelper.CreateText(parent, name, text, sizePt, style, color, align, font);
        RectTransform rt = tmp.rectTransform;
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
#endif
