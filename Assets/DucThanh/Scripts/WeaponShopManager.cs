using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Cửa Hàng Rèn Vũ Khí & Bình Hồi Máu phong cách Action RPG AAA:
/// - Tự động chụp ảnh chân dung 3D sắc nét (Studio RenderTexture) cho từng model vũ khí thật trong WeaponUpgradeConfig.
/// - Hiển thị icon vẽ tay chuẩn RPG cho Bình Hồi Máu (icon_potion) & Vàng (icon_gold).
/// - Phân bậc phẩm chất vũ khí (Hiếm / Sử Thi / Huyền Thoại / Thần Thoại) với viền màu riêng và hiệu ứng mở mượt.
/// - Mở nhanh bằng phím [B] mọi lúc hoặc [E] gần Trại Lửa, gửi đủ Telemetry Đề 9.
/// </summary>
public class WeaponShopManager : MonoBehaviour
{
    public static WeaponShopManager Instance { get; private set; }
    public static bool IsShopOpen => Instance != null && Instance.isOpen;

    public const string DefaultConfigPath = "Assets/DucThanh/Assets/Weapons/WeaponUpgradeConfig.asset";

    [Header("Shop Config")]
    public WeaponUpgradeConfigSO weaponConfig;
    public int potionPrice = 50;
    public int potionHealAmount = 80;

    private static readonly int[] DefaultWeaponPrices = { 60, 100, 150, 210, 280, 360, 450, 550 };
    private static readonly int[] DefaultWeaponBonusDmg = { 12, 22, 34, 48, 64, 82, 102, 125 };

    private static readonly Color[] RarityColors =
    {
        new Color(0.32f, 0.86f, 0.54f, 1f), // Hiếm (Emerald)
        new Color(0.24f, 0.72f, 0.98f, 1f), // Sử Thi (Sapphire)
        new Color(0.76f, 0.48f, 1.00f, 1f), // Huyền Thoại (Amethyst)
        new Color(1.00f, 0.78f, 0.22f, 1f)  // Thần Thoại (Solar Gold)
    };

    private static readonly string[] RarityLabels = { "HIẾM", "SỬ THI", "HUYỀN THOẠI", "THẦN THOẠI" };

    private class ShopWeaponItem
    {
        public int index;
        public string displayName;
        public GameObject prefab;
        public WeaponUpgradeEntry entry;
        public int price;
        public int bonusDamage;
        public int rarityTier;
        public bool owned;
        public Texture2D thumbnail;
        public Button actionButton;
        public TMP_Text buttonText;
        public Outline cardOutline;
    }

    private readonly List<ShopWeaponItem> shopItems = new List<ShopWeaponItem>();
    private readonly Dictionary<GameObject, Texture2D> thumbnailCache = new Dictionary<GameObject, Texture2D>();

    private int equippedItemIndex = -1;
    private int basePlayerDamage = -1;
    private bool isOpen;

    // Runtime Shop UI
    private GameObject shopOverlayRoot;
    private RectTransform shopCardRect;
    private CanvasGroup shopCanvasGroup;
    private TMP_Text goldBalanceText;
    private TMP_Text playerHpSummaryText;
    private TMP_Text feedbackText;
    private Button potionButton;
    private TMP_Text potionButtonText;
    private Transform playerTransform;
    private Coroutine openAnimRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        LoadWeaponCatalog();
    }

    private void Start()
    {
        ResolvePlayer();
        CacheBasePlayerDamage();
    }

    private void Update()
    {
        if (GameOverManager.IsGameOver)
        {
            if (isOpen) CloseShop();
            return;
        }

        if (Input.GetKeyDown(KeyCode.B))
        {
            ToggleShop();
        }
        else if (isOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseShop();
        }
        else if (!isOpen && Input.GetKeyDown(KeyCode.E) && IsNearAnyCampfire())
        {
            OpenShop();
        }
    }

    private void OnDestroy()
    {
        foreach (var kv in thumbnailCache)
        {
            if (kv.Value != null) Destroy(kv.Value);
        }
        thumbnailCache.Clear();
    }

    public void ToggleShop()
    {
        if (isOpen) CloseShop();
        else OpenShop();
    }

    public void OpenShop()
    {
        if (isOpen || GameOverManager.IsGameOver) return;

        EnsureShopUi();
        if (shopOverlayRoot == null) return;

        isOpen = true;
        RefreshShopUi();

        shopOverlayRoot.transform.SetAsLastSibling();
        shopOverlayRoot.SetActive(true);

        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        GameAudioManager.PlayUiClick();

        if (openAnimRoutine != null) StopCoroutine(openAnimRoutine);
        openAnimRoutine = StartCoroutine(AnimateShopOpen());

        if (TelemetryManager.Instance != null)
        {
            int stage = StageManager.Instance != null ? StageManager.Instance.CurrentStage : TelemetryManager.Instance.CurrentLevel;
            TelemetryManager.Instance.TrackShopOpened(stage);
        }
    }

    public void CloseShop()
    {
        if (!isOpen) return;
        isOpen = false;

        if (openAnimRoutine != null)
        {
            StopCoroutine(openAnimRoutine);
            openAnimRoutine = null;
        }

        if (shopOverlayRoot != null)
        {
            shopOverlayRoot.SetActive(false);
        }

        GameAudioManager.PlayUiClick();

        if (!GameOverManager.IsGameOver)
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private IEnumerator AnimateShopOpen()
    {
        if (shopCanvasGroup != null) shopCanvasGroup.alpha = 0f;
        if (shopCardRect != null) shopCardRect.localScale = Vector3.one * 0.92f;

        float t = 0f;
        const float dur = 0.22f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            float ease = 1f - Mathf.Pow(1f - k, 3f);

            if (shopCanvasGroup != null) shopCanvasGroup.alpha = ease;
            if (shopCardRect != null) shopCardRect.localScale = Vector3.Lerp(Vector3.one * 0.92f, Vector3.one, ease);
            yield return null;
        }

        if (shopCanvasGroup != null) shopCanvasGroup.alpha = 1f;
        if (shopCardRect != null) shopCardRect.localScale = Vector3.one;
        openAnimRoutine = null;
    }

    // ==================== CATALOG & 3D THUMBNAIL STUDIO ====================

    private void LoadWeaponCatalog()
    {
        shopItems.Clear();

        if (weaponConfig == null)
        {
            weaponConfig = Resources.Load<WeaponUpgradeConfigSO>("WeaponUpgradeConfig");
        }
#if UNITY_EDITOR
        if (weaponConfig == null)
        {
            weaponConfig = AssetDatabase.LoadAssetAtPath<WeaponUpgradeConfigSO>(DefaultConfigPath);
        }
#endif

        if (weaponConfig == null || weaponConfig.weapons == null) return;

        int slot = 0;
        for (int i = 0; i < weaponConfig.weapons.Count; i++)
        {
            WeaponUpgradeEntry e = weaponConfig.weapons[i];
            if (e == null || !e.enabled || e.weaponPrefab == null) continue;

            int price = slot < DefaultWeaponPrices.Length ? DefaultWeaponPrices[slot] : 550 + (slot - DefaultWeaponPrices.Length + 1) * 100;
            int dmg = slot < DefaultWeaponBonusDmg.Length ? DefaultWeaponBonusDmg[slot] : 125 + (slot - DefaultWeaponBonusDmg.Length + 1) * 20;
            int rarity = Mathf.Clamp(slot / 2, 0, RarityColors.Length - 1);

            shopItems.Add(new ShopWeaponItem
            {
                index = slot,
                displayName = string.IsNullOrWhiteSpace(e.displayName) ? e.weaponPrefab.name : e.displayName,
                prefab = e.weaponPrefab,
                entry = e,
                price = price,
                bonusDamage = dmg,
                rarityTier = rarity,
                owned = false
            });
            slot++;
            if (slot >= 8) break; // Lưới 4x2 gọn gàng cân đối
        }
    }

    /// <summary>
    /// Chụp ảnh chân dung 3D sắc nét cho từng Prefab vũ khí thật bằng Studio Camera ngoài màn hình.
    /// </summary>
    private Texture2D GetOrRenderWeaponThumbnail(GameObject weaponPrefab, Color accentColor)
    {
        if (weaponPrefab == null) return null;
        if (thumbnailCache.TryGetValue(weaponPrefab, out Texture2D cached) && cached != null)
        {
            return cached;
        }

        const int res = 256;
        Vector3 studioOrigin = new Vector3(0f, -650f, 0f);

        GameObject studioRoot = new GameObject("TempWeaponThumbnailStudio");
        studioRoot.transform.position = studioOrigin;

        // 1. Tạo bản sao vũ khí & đặt vào góc nghiêng chéo 42° chuẩn icon RPG
        GameObject pivot = new GameObject("Pivot");
        pivot.transform.SetParent(studioRoot.transform, false);

        GameObject clone = Instantiate(weaponPrefab, pivot.transform);
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;

        // Tắt collider / script phụ nếu có trên bản sao preview
        foreach (Collider c in clone.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (MonoBehaviour mb in clone.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;

        // Đặt góc nghiêng đẹp để lộ toàn bộ lưỡi kiếm/búa/thương/lưỡi hái
        pivot.transform.localRotation = Quaternion.Euler(14f, 22f, -42f);

        Renderer[] rends = clone.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(studioOrigin, Vector3.one * 0.5f);
        bool hasBounds = false;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null) continue;
            if (!hasBounds)
            {
                bounds = rends[i].bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(rends[i].bounds);
            }
        }

        // 2. Đèn Studio (Key Light ấm + Rim Light theo màu phẩm chất)
        GameObject keyLightObj = new GameObject("KeyLight");
        keyLightObj.transform.SetParent(studioRoot.transform, false);
        keyLightObj.transform.position = bounds.center + new Vector3(1.8f, 2.2f, -2.5f);
        keyLightObj.transform.LookAt(bounds.center);
        Light keyLight = keyLightObj.AddComponent<Light>();
        keyLight.type = LightType.Point;
        keyLight.range = 16f;
        keyLight.intensity = 10f;
        keyLight.color = new Color(1f, 0.96f, 0.88f);

        GameObject rimLightObj = new GameObject("RimLight");
        rimLightObj.transform.SetParent(studioRoot.transform, false);
        rimLightObj.transform.position = bounds.center + new Vector3(-2.0f, -0.8f, 1.8f);
        Light rimLight = rimLightObj.AddComponent<Light>();
        rimLight.type = LightType.Point;
        rimLight.range = 14f;
        rimLight.intensity = 7f;
        rimLight.color = accentColor;

        // 3. Camera chụp cận cảnh tự căn vừa khít Bounds
        GameObject camObj = new GameObject("ThumbCam");
        camObj.transform.SetParent(studioRoot.transform, false);
        Camera cam = camObj.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.055f, 0.075f, 0.125f, 1f);
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 30f;
        cam.allowHDR = false;

        float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z, 0.25f);
        float dist = (maxExtent / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.18f;
        camObj.transform.position = bounds.center + new Vector3(0f, 0f, -dist);
        camObj.transform.LookAt(bounds.center);

        RenderTexture rt = new RenderTexture(res, res, 24, RenderTextureFormat.ARGB32);
        RenderTexture prevActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        tex.Apply();

        cam.targetTexture = null;
        RenderTexture.active = prevActive;
        rt.Release();
        Destroy(rt);
        Destroy(studioRoot);

        thumbnailCache[weaponPrefab] = tex;
        return tex;
    }

    // ==================== PURCHASE & EQUIP LOGIC ====================

    private void OnBuyPotionClicked()
    {
        GameObject player = GameObject.FindWithTag("Player");
        PlayerHealth hp = player != null ? player.GetComponent<PlayerHealth>() : null;
        if (hp == null) return;

        if (hp.currentHealth >= hp.maxHealth)
        {
            SetFeedback("Máu của bạn đã đầy 100%!", new Color(1f, 0.85f, 0.35f));
            return;
        }

        if (ScoreManager.Instance == null || !ScoreManager.Instance.TrySpendGold(potionPrice))
        {
            SetFeedback($"Không đủ vàng! Cần {potionPrice} Gold để mua Bình Hồi Máu.", new Color(1f, 0.38f, 0.38f));
            return;
        }

        hp.Heal(potionHealAmount);
        GameAudioManager.PlayPickup();

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackPotionPurchased(potionPrice);
        }

        SetFeedback($"Đã mua Bình Hồi Máu Huyết Ngọc (+{potionHealAmount} HP)!", new Color(0.32f, 0.96f, 0.54f));
        RefreshShopUi();
    }

    private void OnWeaponButtonClicked(ShopWeaponItem item)
    {
        if (item == null) return;

        if (item.owned)
        {
            EquipWeaponItem(item);
            SetFeedback($"Đã trang bị {item.displayName} (+{item.bonusDamage} DMG)!", FantasyUiThemeHelper.GoldBright);
            RefreshShopUi();
            return;
        }

        if (ScoreManager.Instance == null || !ScoreManager.Instance.TrySpendGold(item.price))
        {
            SetFeedback($"Chưa đủ vàng! Cần {item.price} Gold để mua {item.displayName}.", new Color(1f, 0.38f, 0.38f));
            return;
        }

        item.owned = true;
        EquipWeaponItem(item);

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackWeaponPurchased(item.price);
        }

        SetFeedback($"Rèn thành công {item.displayName} (+{item.bonusDamage} DMG)!", new Color(0.32f, 0.96f, 0.54f));
        RefreshShopUi();
    }

    private void EquipWeaponItem(ShopWeaponItem item)
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null || item.prefab == null) return;

        Transform hand = LevelUpChoiceManager.FindPlayerHandBone(player, out GameObject oldWeapon);
        if (hand == null)
        {
            Debug.LogWarning("[WeaponShopManager] Không tìm thấy xương tay phải của Player!");
            return;
        }

        Vector3 fallbackLocalPos = oldWeapon != null ? oldWeapon.transform.localPosition : Vector3.zero;
        Quaternion fallbackLocalRot = oldWeapon != null ? oldWeapon.transform.localRotation : Quaternion.identity;
        Vector3 fallbackLocalScale = oldWeapon != null ? oldWeapon.transform.localScale : Vector3.one;

        if (oldWeapon != null)
        {
            oldWeapon.transform.SetParent(null);
            Destroy(oldWeapon);
        }

        GameObject newWeapon = Instantiate(item.prefab, hand);
        newWeapon.name = item.prefab.name;

        if (item.entry != null && item.entry.hasCustomGrip)
        {
            item.entry.ApplyGripToTransform(newWeapon.transform);
        }
        else
        {
            newWeapon.transform.localPosition = fallbackLocalPos;
            newWeapon.transform.localRotation = fallbackLocalRot;
            newWeapon.transform.localScale = fallbackLocalScale;
        }

        PlayerAttack pa = player.GetComponent<PlayerAttack>();
        if (pa != null)
        {
            if (basePlayerDamage < 0) basePlayerDamage = pa.attackDamage;
            pa.attackDamage = basePlayerDamage + item.bonusDamage;
            pa.RefreshWeapon(0);
        }

        equippedItemIndex = item.index;
        GameAudioManager.PlayWeaponEquip();
    }

    // ==================== AAA ARMORY SHOP UI ====================

    private void EnsureShopUi()
    {
        if (shopOverlayRoot != null) return;

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        TMP_FontAsset font = canvas.GetComponentInChildren<TMP_Text>(true)?.font;
        Texture2D potionTex = Resources.Load<Texture2D>("UI/icon_potion");
        Texture2D goldTex = Resources.Load<Texture2D>("UI/icon_gold");

        // 1. Nền phủ tối kèm hạt tàn lửa vàng kim
        shopOverlayRoot = new GameObject("WeaponShopOverlay", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        shopOverlayRoot.transform.SetParent(canvas.transform, false);
        FantasyUiThemeHelper.StretchFull(shopOverlayRoot.GetComponent<RectTransform>());
        shopOverlayRoot.GetComponent<Image>().color = new Color(0.02f, 0.035f, 0.065f, 0.86f);
        shopCanvasGroup = shopOverlayRoot.GetComponent<CanvasGroup>();

        GameObject embersObj = new GameObject("ShopEmbers", typeof(RectTransform));
        embersObj.transform.SetParent(shopOverlayRoot.transform, false);
        FantasyUiThemeHelper.StretchFull(embersObj.GetComponent<RectTransform>());
        FloatingEmbersUI embers = embersObj.AddComponent<FloatingEmbersUI>();
        embers.Initialize(20, 0.05f, 0.95f, new Color(1f, 0.78f, 0.25f, 1f), new Color(1f, 0.45f, 0.15f, 1f));

        // 2. Khung Cửa Hàng chính (960 x 655)
        GameObject card = new GameObject("ShopCard", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(shopOverlayRoot.transform, false);
        shopCardRect = card.GetComponent<RectTransform>();
        shopCardRect.anchorMin = new Vector2(0.5f, 0.5f);
        shopCardRect.anchorMax = new Vector2(0.5f, 0.5f);
        shopCardRect.pivot = new Vector2(0.5f, 0.5f);
        shopCardRect.anchoredPosition = Vector2.zero;
        shopCardRect.sizeDelta = new Vector2(960f, 655f);

        FantasyUiThemeHelper.StyleCardPanel(card, new Color(0.048f, 0.068f, 0.115f, 0.96f), FantasyUiThemeHelper.GoldBorder);
        FantasyUiThemeHelper.CreateAccentLine(card.transform, "TopGoldBar",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 4f), FantasyUiThemeHelper.GoldPrimary);
        FantasyUiThemeHelper.CreateAccentLine(card.transform, "BottomGoldBar",
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 3f), FantasyUiThemeHelper.GoldBorder);

        // 3. Header: Tiêu đề + Ví Vàng (có icon đồng vàng) + Nút đóng
        TMP_Text title = FantasyUiThemeHelper.CreateText(card.transform, "ShopTitle", "LÒ RÈN VANGUARD  •  CỬA HÀNG",
            26f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Left, font);
        SetTopRect(title.rectTransform, 32f, -18f, 520f, 34f, 0f);
        title.characterSpacing = 3f;

        TMP_Text subtitle = FantasyUiThemeHelper.CreateText(card.transform, "ShopSubtitle",
            "Thu thập Vàng từ Rừng Orc để mua Vũ Khí huyền thoại hoặc Bình Hồi Máu",
            14f, FontStyles.Normal, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.Left, font);
        SetTopRect(subtitle.rectTransform, 32f, -52f, 540f, 22f, 0f);

        // Ví vàng có icon đồng vàng thật
        GameObject walletPill = new GameObject("GoldWalletPill", typeof(RectTransform), typeof(Image));
        walletPill.transform.SetParent(card.transform, false);
        SetTopRect(walletPill.GetComponent<RectTransform>(), -88f, -20f, 210f, 48f, 1f);
        walletPill.GetComponent<Image>().color = new Color(0.08f, 0.105f, 0.17f, 0.95f);
        Outline wo = walletPill.AddComponent<Outline>();
        wo.effectColor = FantasyUiThemeHelper.GoldPrimary;
        wo.effectDistance = new Vector2(1.2f, -1.2f);

        if (goldTex != null)
        {
            GameObject gIcon = new GameObject("GoldIcon", typeof(RectTransform), typeof(RawImage));
            gIcon.transform.SetParent(walletPill.transform, false);
            RectTransform giRt = gIcon.GetComponent<RectTransform>();
            giRt.anchorMin = new Vector2(0f, 0.5f);
            giRt.anchorMax = new Vector2(0f, 0.5f);
            giRt.pivot = new Vector2(0f, 0.5f);
            giRt.anchoredPosition = new Vector2(8f, 0f);
            giRt.sizeDelta = new Vector2(36f, 36f);
            gIcon.GetComponent<RawImage>().texture = goldTex;
        }

        goldBalanceText = FantasyUiThemeHelper.CreateText(walletPill.transform, "GoldBalance", "0 G",
            22f, FontStyles.Bold, new Color(1f, 0.86f, 0.30f, 1f), TextAlignmentOptions.MidlineRight, font);
        RectTransform gbRt = goldBalanceText.rectTransform;
        gbRt.anchorMin = Vector2.zero;
        gbRt.anchorMax = Vector2.one;
        gbRt.offsetMin = new Vector2(48f, 0f);
        gbRt.offsetMax = new Vector2(-14f, 0f);

        Button closeBtn = CreateSimpleButton(card.transform, "CloseShopBtn", "X", false, 48f, 48f, font);
        SetTopRect(closeBtn.GetComponent<RectTransform>(), -28f, -20f, 48f, 48f, 1f);
        closeBtn.onClick.AddListener(CloseShop);

        FantasyUiThemeHelper.CreateAccentLine(card.transform, "HeaderDivider",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(-64f, 2f), FantasyUiThemeHelper.GoldBorder);

        // 4. Thẻ Bình Hồi Máu nổi bật (có hình ảnh icon_potion.jpg tuyệt đẹp)
        GameObject potionRow = new GameObject("PotionBanner", typeof(RectTransform), typeof(Image));
        potionRow.transform.SetParent(card.transform, false);
        SetTopCenteredRect(potionRow.GetComponent<RectTransform>(), 0f, -98f, 896f, 88f);
        potionRow.GetComponent<Image>().color = new Color(0.11f, 0.065f, 0.085f, 0.92f);
        Outline po = potionRow.AddComponent<Outline>();
        po.effectColor = new Color(0.95f, 0.32f, 0.38f, 0.65f);
        po.effectDistance = new Vector2(1.4f, -1.4f);

        // Khung ảnh Bình Hồi Máu
        GameObject potionFrame = new GameObject("PotionIconFrame", typeof(RectTransform), typeof(Image));
        potionFrame.transform.SetParent(potionRow.transform, false);
        RectTransform pfRt = potionFrame.GetComponent<RectTransform>();
        pfRt.anchorMin = new Vector2(0f, 0.5f);
        pfRt.anchorMax = new Vector2(0f, 0.5f);
        pfRt.pivot = new Vector2(0f, 0.5f);
        pfRt.anchoredPosition = new Vector2(14f, 0f);
        pfRt.sizeDelta = new Vector2(68f, 68f);
        potionFrame.GetComponent<Image>().color = new Color(0.05f, 0.07f, 0.12f, 1f);
        Outline pfo = potionFrame.AddComponent<Outline>();
        pfo.effectColor = new Color(1f, 0.78f, 0.32f, 0.75f);
        pfo.effectDistance = new Vector2(1.2f, -1.2f);

        if (potionTex != null)
        {
            GameObject pImg = new GameObject("PotionRawImage", typeof(RectTransform), typeof(RawImage));
            pImg.transform.SetParent(potionFrame.transform, false);
            RectTransform piRt = pImg.GetComponent<RectTransform>();
            piRt.anchorMin = Vector2.zero;
            piRt.anchorMax = Vector2.one;
            piRt.offsetMin = new Vector2(3f, 3f);
            piRt.offsetMax = new Vector2(-3f, -3f);
            pImg.GetComponent<RawImage>().texture = potionTex;
        }

        TMP_Text pName = FantasyUiThemeHelper.CreateText(potionRow.transform, "PotionName",
            $"BÌNH HỒI MÁU HUYẾT NGỌC   (+{potionHealAmount} HP)",
            19f, FontStyles.Bold, new Color(1f, 0.55f, 0.58f, 1f), TextAlignmentOptions.Left, font);
        SetTopRect(pName.rectTransform, 98f, -16f, 520f, 26f, 0f);

        playerHpSummaryText = FantasyUiThemeHelper.CreateText(potionRow.transform, "PotionDesc",
            "Hồi phục tức thì trong giao tranh  •  HP hiện tại: 180 / 180",
            14f, FontStyles.Normal, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.Left, font);
        SetTopRect(playerHpSummaryText.rectTransform, 98f, -48f, 540f, 24f, 0f);

        potionButton = CreateSimpleButton(potionRow.transform, "BuyPotionBtn", $"MUA  •  {potionPrice} G", true, 185f, 48f, font);
        RectTransform pbRt = potionButton.GetComponent<RectTransform>();
        pbRt.anchorMin = new Vector2(1f, 0.5f);
        pbRt.anchorMax = new Vector2(1f, 0.5f);
        pbRt.pivot = new Vector2(1f, 0.5f);
        pbRt.anchoredPosition = new Vector2(-18f, 0f);
        potionButtonText = potionButton.GetComponentInChildren<TMP_Text>(true);
        potionButton.onClick.AddListener(OnBuyPotionClicked);

        // 5. Lưới 4x2 Thẻ Vũ Khí kèm Hình Ảnh 3D Thật
        GameObject gridObj = new GameObject("WeaponsGrid", typeof(RectTransform), typeof(GridLayoutGroup));
        gridObj.transform.SetParent(card.transform, false);
        SetTopCenteredRect(gridObj.GetComponent<RectTransform>(), 0f, -202f, 896f, 396f);

        GridLayoutGroup grid = gridObj.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(212f, 190f);
        grid.spacing = new Vector2(16f, 16f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;

        for (int i = 0; i < shopItems.Count; i++)
        {
            ShopWeaponItem item = shopItems[i];
            Color rarityCol = RarityColors[item.rarityTier];
            string rarityLabel = RarityLabels[item.rarityTier];

            item.thumbnail = GetOrRenderWeaponThumbnail(item.prefab, rarityCol);

            GameObject tile = new GameObject($"WeaponCard_{i}", typeof(RectTransform), typeof(Image));
            tile.transform.SetParent(gridObj.transform, false);
            tile.GetComponent<Image>().color = new Color(0.075f, 0.10f, 0.165f, 0.95f);

            Outline to = tile.AddComponent<Outline>();
            to.effectColor = new Color(rarityCol.r, rarityCol.g, rarityCol.b, 0.45f);
            to.effectDistance = new Vector2(1.4f, -1.4f);
            item.cardOutline = to;

            // Thanh màu phẩm chất ở đỉnh thẻ
            FantasyUiThemeHelper.CreateAccentLine(tile.transform, "RarityTopStrip",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 3f), rarityCol);

            // Khung hiển thị Hình Ảnh 3D của chính vũ khí đó
            GameObject thumbFrame = new GameObject("ThumbFrame", typeof(RectTransform), typeof(Image));
            thumbFrame.transform.SetParent(tile.transform, false);
            SetTopCenteredRect(thumbFrame.GetComponent<RectTransform>(), 0f, -10f, 192f, 82f);
            thumbFrame.GetComponent<Image>().color = new Color(0.045f, 0.06f, 0.105f, 1f);
            Outline tfo = thumbFrame.AddComponent<Outline>();
            tfo.effectColor = new Color(rarityCol.r, rarityCol.g, rarityCol.b, 0.30f);
            tfo.effectDistance = new Vector2(1f, -1f);

            if (item.thumbnail != null)
            {
                GameObject rawGo = new GameObject("WeaponPortrait", typeof(RectTransform), typeof(RawImage));
                rawGo.transform.SetParent(thumbFrame.transform, false);
                RectTransform rrt = rawGo.GetComponent<RectTransform>();
                rrt.anchorMin = new Vector2(0.5f, 0.5f);
                rrt.anchorMax = new Vector2(0.5f, 0.5f);
                rrt.pivot = new Vector2(0.5f, 0.5f);
                rrt.anchoredPosition = Vector2.zero;
                rrt.sizeDelta = new Vector2(78f, 78f);
                rawGo.GetComponent<RawImage>().texture = item.thumbnail;
            }

            // Nhãn phẩm chất nhỏ góc trên-trái ảnh
            TMP_Text rTag = FantasyUiThemeHelper.CreateText(thumbFrame.transform, "RarityTag", rarityLabel,
                10f, FontStyles.Bold, rarityCol, TextAlignmentOptions.Left, font);
            SetTopRect(rTag.rectTransform, 6f, -4f, 100f, 16f, 0f);
            rTag.characterSpacing = 1.5f;

            // Tên vũ khí & Sát thương
            TMP_Text wName = FantasyUiThemeHelper.CreateText(tile.transform, "Name", item.displayName,
                16f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font);
            SetTopCenteredRect(wName.rectTransform, 0f, -97f, 196f, 22f);

            TMP_Text wDmg = FantasyUiThemeHelper.CreateText(tile.transform, "Damage", $"+{item.bonusDamage} SÁT THƯƠNG",
                12f, FontStyles.Bold, rarityCol, TextAlignmentOptions.Center, font);
            SetTopCenteredRect(wDmg.rectTransform, 0f, -120f, 196f, 18f);

            // Nút Mua / Trang bị
            Button btn = CreateSimpleButton(tile.transform, "ActionBtn", $"MUA • {item.price} G", true, 186f, 36f, font);
            SetTopCenteredRect(btn.GetComponent<RectTransform>(), 0f, -144f, 186f, 36f);

            item.actionButton = btn;
            item.buttonText = btn.GetComponentInChildren<TMP_Text>(true);
            ShopWeaponItem captured = item;
            btn.onClick.AddListener(() => OnWeaponButtonClicked(captured));
        }

        // 6. Dòng thông báo chân cửa hàng
        feedbackText = FantasyUiThemeHelper.CreateText(card.transform, "FeedbackText",
            "Giết quái Orc để nhặt Vàng văng ra trên bản đồ  •  Nhấn [B] hoặc [Esc] để đóng",
            14f, FontStyles.Bold, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.Center, font);
        SetTopCenteredRect(feedbackText.rectTransform, 0f, -612f, 896f, 28f);

        shopOverlayRoot.SetActive(false);
    }

    private void RefreshShopUi()
    {
        int currentGold = ScoreManager.Instance != null ? ScoreManager.Instance.gold : 0;

        if (goldBalanceText != null)
        {
            goldBalanceText.text = $"{currentGold:N0} G";
        }

        GameObject player = GameObject.FindWithTag("Player");
        PlayerHealth hp = player != null ? player.GetComponent<PlayerHealth>() : null;
        if (playerHpSummaryText != null && hp != null)
        {
            playerHpSummaryText.text = $"Hồi phục tức thì trong giao tranh  •  HP hiện tại: {hp.currentHealth} / {hp.maxHealth}";
        }

        if (potionButton != null && potionButtonText != null)
        {
            bool fullHp = hp != null && hp.currentHealth >= hp.maxHealth;
            bool canAffordPotion = !fullHp && currentGold >= potionPrice;
            potionButtonText.text = fullHp ? "ĐÃ ĐẦY MÁU" : $"MUA  •  {potionPrice} G";
            potionButtonText.color = canAffordPotion ? Color.white : new Color(0.72f, 0.75f, 0.80f, 1f);
            potionButton.interactable = canAffordPotion;
            Image img = potionButton.GetComponent<Image>();
            if (img != null) img.color = canAffordPotion ? new Color(0.84f, 0.20f, 0.26f, 1f) : new Color(0.22f, 0.25f, 0.30f, 0.85f);
        }

        for (int i = 0; i < shopItems.Count; i++)
        {
            ShopWeaponItem item = shopItems[i];
            if (item.actionButton == null || item.buttonText == null) continue;

            Color rarityCol = RarityColors[item.rarityTier];
            Image btnImg = item.actionButton.GetComponent<Image>();

            if (equippedItemIndex == item.index)
            {
                item.buttonText.text = "ĐANG CẦM";
                item.buttonText.color = Color.white;
                item.actionButton.interactable = false;
                if (btnImg != null) btnImg.color = new Color(0.16f, 0.65f, 0.42f, 0.95f);
                if (item.cardOutline != null) item.cardOutline.effectColor = new Color(0.28f, 0.95f, 0.56f, 0.95f);
            }
            else if (item.owned)
            {
                item.buttonText.text = "TRANG BỊ";
                item.buttonText.color = FantasyUiThemeHelper.GoldBright;
                item.actionButton.interactable = true;
                if (btnImg != null) btnImg.color = FantasyUiThemeHelper.GhostButtonBg;
                if (item.cardOutline != null) item.cardOutline.effectColor = new Color(rarityCol.r, rarityCol.g, rarityCol.b, 0.65f);
            }
            else
            {
                bool canAfford = currentGold >= item.price;
                item.buttonText.text = $"MUA  •  {item.price} G";
                item.buttonText.color = canAfford ? FantasyUiThemeHelper.DarkButtonText : new Color(0.65f, 0.68f, 0.75f, 1f);
                item.actionButton.interactable = canAfford;
                if (btnImg != null) btnImg.color = canAfford ? FantasyUiThemeHelper.GoldPrimary : new Color(0.18f, 0.22f, 0.29f, 0.88f);
                if (item.cardOutline != null) item.cardOutline.effectColor = new Color(rarityCol.r, rarityCol.g, rarityCol.b, canAfford ? 0.55f : 0.25f);
            }
        }
    }

    private void SetFeedback(string msg, Color color)
    {
        if (feedbackText == null) return;
        feedbackText.text = msg;
        feedbackText.color = color;
    }

    private static Button CreateSimpleButton(Transform parent, string name, string label, bool isPrimary, float w, float h, TMP_FontAsset font)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Button btn = go.GetComponent<Button>();

        TMP_Text txt = FantasyUiThemeHelper.CreateText(go.transform, "Text", label, 14f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font);
        FantasyUiThemeHelper.StretchFull(txt.rectTransform);
        FantasyUiThemeHelper.StyleButton(btn, label, isPrimary, w, h);
        txt.fontSize = 14f;
        return btn;
    }

    private static void SetTopRect(RectTransform rt, float x, float y, float w, float h, float anchorX)
    {
        rt.anchorMin = new Vector2(anchorX, 1f);
        rt.anchorMax = new Vector2(anchorX, 1f);
        rt.pivot = new Vector2(anchorX, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void SetTopCenteredRect(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private bool IsNearAnyCampfire()
    {
        if (playerTransform == null) ResolvePlayer();
        if (playerTransform == null) return false;

        CampfireCheckpoint[] camps = FindObjectsByType<CampfireCheckpoint>(FindObjectsSortMode.None);
        for (int i = 0; i < camps.Length; i++)
        {
            Vector3 diff = playerTransform.position - camps[i].transform.position;
            diff.y = 0f;
            if (diff.sqrMagnitude <= 49f) return true;
        }
        return false;
    }

    private void CacheBasePlayerDamage()
    {
        GameObject player = GameObject.FindWithTag("Player");
        PlayerAttack pa = player != null ? player.GetComponent<PlayerAttack>() : null;
        if (pa != null) basePlayerDamage = pa.attackDamage;
    }

    private void ResolvePlayer()
    {
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) playerTransform = p.transform;
    }
}
