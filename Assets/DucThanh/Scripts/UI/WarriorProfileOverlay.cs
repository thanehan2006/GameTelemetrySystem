using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Cửa sổ "HỒ SƠ CHIẾN BINH" kích thước lớn (1660x950) hiển thị tổng quan chiến tích,
/// chân dung 3D & thông số chi tiết 4 lớp tướng và lịch sử các trận đấu gần nhất.
/// </summary>
public class WarriorProfileOverlay : MonoBehaviour
{
    private enum ProfileTab
    {
        Overview = 0,
        Champions = 1,
        Matches = 2
    }

    private const float WindowW = 1660f;
    private const float WindowH = 950f;
    private const float ModalAnimDuration = 0.16f;

    private static readonly Color CyanAccent = new Color(0.28f, 0.84f, 0.98f, 1f);
    private static readonly Color EmeraldAccent = new Color(0.46f, 0.92f, 0.56f, 1f);
    private static readonly Color CrimsonAccent = new Color(0.96f, 0.36f, 0.42f, 1f);
    private static readonly Color MutedSlateText = new Color(0.70f, 0.75f, 0.84f, 1f);
    private static readonly Color TrackDarkFill = new Color(0.045f, 0.040f, 0.042f, 0.96f);
    private static readonly Color MiniPillFill = new Color(0.068f, 0.062f, 0.064f, 0.94f);

    private static readonly Dictionary<string, Color> CharacterAccentLookup = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
    {
        { "vanguard", new Color(0.95f, 0.74f, 0.28f, 1f) },
        { "maria", new Color(0.28f, 0.84f, 0.98f, 1f) },
        { "vampire", new Color(0.95f, 0.26f, 0.36f, 1f) },
        { "maw", new Color(0.74f, 0.44f, 0.98f, 1f) }
    };

    public static WarriorProfileOverlay Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.isOpen;

    private Canvas overlayCanvas;
    private RectTransform canvasRt;
    private GameObject backdropObj;
    private GameObject windowObj;
    private RectTransform windowRt;
    private CanvasGroup windowCanvasGroup;

    private TMP_Text headerSubText;
    private Button refreshBtn;

    private readonly Button[] tabButtons = new Button[3];
    private readonly GameObject[] tabPages = new GameObject[3];
    private ProfileTab activeTab = ProfileTab.Overview;

    // Status overlay (Loading / Error / Not logged in)
    private GameObject statusPanelObj;
    private TMP_Text statusMessageText;
    private Button retryBtn;

    // Tab 0: Overview references
    private TMP_Text kpiMatchesVal;
    private TMP_Text kpiMatchesSub;
    private TMP_Text kpiWinRateVal;
    private TMP_Text kpiWinRateSub;
    private TMP_Text kpiKillsVal;
    private TMP_Text kpiKillsSub;
    private TMP_Text kpiPlayTimeVal;
    private TMP_Text kpiPlayTimeSub;

    private RawImage favCharPortraitImg;
    private Image favCharPortraitBorder;
    private TMP_Text favCharNameText;
    private TMP_Text favCharRoleText;
    private TMP_Text favCharTitleText;
    private TMP_Text favCharSpecsText;
    private TMP_Text favCharPerkText;
    private TMP_Text favCharMetaText;
    private TMP_Text favWeaponNameText;
    private TMP_Text favWeaponMetaText;

    private TMP_Text statParryVal;
    private TMP_Text statBlockVal;
    private TMP_Text statGoldEarnedVal;
    private TMP_Text statGoldSpentVal;

    // Tab 1 & 2: Scroll containers
    private RectTransform championsContentRt;
    private ScrollRect championsScroll;
    private RectTransform matchesContentRt;
    private ScrollRect matchesScroll;

    private PlayerProfileApiClient apiClient;
    private PlayerProfileResponse cachedProfile;

    private bool uiBuilt;
    private bool isOpen;
    private float modalAnimTime = 1f;
    private Vector2 lastCanvasSize;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
    }

    public static WarriorProfileOverlay EnsureInstance()
    {
        if (Instance != null) return Instance;

        Instance = FindFirstObjectByType<WarriorProfileOverlay>();
        if (Instance == null)
        {
            GameObject go = new GameObject("WarriorProfileOverlay");
            Instance = go.AddComponent<WarriorProfileOverlay>();
        }
        return Instance;
    }

    public static void Open()
    {
        WarriorProfileOverlay overlay = EnsureInstance();
        overlay.OpenInternal();
    }

    public static void Close()
    {
        if (Instance != null)
        {
            Instance.CloseInternal();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureApiClient();
        EnsureUiBuilt();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (isOpen)
        {
            CloseInternal();
        }
    }

    private void Update()
    {
        if (!isOpen) return;

        UpdateResponsiveScaleAndAnim();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            GameMenuManager.SuppressEscapeThisFrame();
            GameAudioManager.PlayUiClick();
            CloseInternal();
        }
    }

    private void UpdateResponsiveScaleAndAnim()
    {
        if (canvasRt == null || windowRt == null) return;

        Vector2 size = canvasRt.rect.size;
        if (size.x < 100f || size.y < 100f) return;

        bool animDone = modalAnimTime >= ModalAnimDuration;
        if (animDone && size == lastCanvasSize && (windowCanvasGroup == null || windowCanvasGroup.alpha >= 0.999f))
        {
            return;
        }

        lastCanvasSize = size;

        float animScale = 1f;
        if (!animDone)
        {
            modalAnimTime += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(modalAnimTime / ModalAnimDuration));
            if (windowCanvasGroup != null) windowCanvasGroup.alpha = k;
            animScale = Mathf.Lerp(0.968f, 1f, k);
        }
        else if (windowCanvasGroup != null && windowCanvasGroup.alpha < 0.999f)
        {
            windowCanvasGroup.alpha = 1f;
        }

        float fit = Mathf.Min(1f, Mathf.Min((size.x * 0.96f) / (WindowW + 24f), (size.y * 0.95f) / (WindowH + 24f)));
        windowRt.localScale = Vector3.one * (fit * animScale);
    }

    private void EnsureApiClient()
    {
        if (apiClient == null)
        {
            apiClient = GetComponent<PlayerProfileApiClient>() ?? gameObject.AddComponent<PlayerProfileApiClient>();
        }
    }

    private void OpenInternal()
    {
        EnsureApiClient();
        EnsureUiBuilt();

        isOpen = true;
        if (overlayCanvas != null) overlayCanvas.gameObject.SetActive(true);
        if (backdropObj != null) backdropObj.SetActive(true);
        if (windowObj != null) windowObj.SetActive(true);
        if (refreshBtn != null) refreshBtn.interactable = true;

        if (windowCanvasGroup != null) windowCanvasGroup.alpha = 0f;
        modalAnimTime = 0f;
        lastCanvasSize = Vector2.zero;

        SelectTab(ProfileTab.Overview, playSound: false);
        UpdateSubtitlePlaceholder();
        LoadProfileData();
    }

    private void CloseInternal()
    {
        isOpen = false;
        if (backdropObj != null) backdropObj.SetActive(false);
        if (windowObj != null) windowObj.SetActive(false);
    }

    private void UpdateSubtitlePlaceholder()
    {
        if (headerSubText == null) return;
        int playerId = PlayerPrefs.GetInt("PlayerId", 0);
        string playerName = ResolveDisplayPlayerName();
        if (playerId > 0)
        {
            headerSubText.text = $"CHIẾN BINH: {playerName.ToUpperInvariant()}  •  ID #{playerId}";
        }
        else
        {
            headerSubText.text = "CHIẾN BINH CHƯA ĐĂNG NHẬP";
        }
    }

    private static string ResolveDisplayPlayerName()
    {
        if (!string.IsNullOrWhiteSpace(GameMenuManager.LoggedInPlayerName))
        {
            return GameMenuManager.LoggedInPlayerName.Trim();
        }
        string prefName = PlayerPrefs.GetString("PlayerName", string.Empty);
        return string.IsNullOrWhiteSpace(prefName) ? "VANGUARD" : prefName.Trim();
    }

    private void LoadProfileData()
    {
        int playerId = PlayerPrefs.GetInt("PlayerId", 0);
        if (playerId <= 0)
        {
            ShowStatusState("Bạn chưa đăng nhập tài khoản.\nVui lòng đăng nhập để xem Hồ Sơ Chiến Binh.", showRetry: false);
            return;
        }

        ShowStatusState("Đang tải Hồ Sơ Chiến Binh từ máy chủ...", showRetry: false);
        if (refreshBtn != null) refreshBtn.interactable = false;

        apiClient.FetchProfile(playerId, (success, profile, message) =>
        {
            if (refreshBtn != null) refreshBtn.interactable = true;
            if (!isOpen) return;

            if (!success || profile == null)
            {
                ShowStatusState(string.IsNullOrWhiteSpace(message) ? "Không thể tải hồ sơ chiến binh." : message, showRetry: true);
                return;
            }

            cachedProfile = profile;
            HideStatusState();
            BindProfileData(profile);
        }, recentLimit: 25);
    }

    private void ShowStatusState(string message, bool showRetry)
    {
        if (statusPanelObj != null) statusPanelObj.SetActive(true);
        if (statusMessageText != null) statusMessageText.text = message;
        if (retryBtn != null) retryBtn.gameObject.SetActive(showRetry);

        for (int i = 0; i < tabPages.Length; i++)
        {
            if (tabPages[i] != null) tabPages[i].SetActive(false);
        }
    }

    private void HideStatusState()
    {
        if (statusPanelObj != null) statusPanelObj.SetActive(false);
        SelectTab(activeTab, playSound: false);
    }

    private void BindProfileData(PlayerProfileResponse p)
    {
        string joinedDate = FormatLocalDate(p.createdAt, "dd/MM/yyyy");
        string nameDisplay = string.IsNullOrWhiteSpace(p.playerName) ? ResolveDisplayPlayerName() : p.playerName.Trim();
        if (headerSubText != null)
        {
            headerSubText.text = string.IsNullOrEmpty(joinedDate)
                ? $"CHIẾN BINH: {nameDisplay.ToUpperInvariant()}  •  ID #{p.playerId}"
                : $"CHIẾN BINH: {nameDisplay.ToUpperInvariant()}  •  ID #{p.playerId}  •  GIA NHẬP: {joinedDate}";
        }

        // Tab 0: Overview KPIs
        if (kpiMatchesVal != null) kpiMatchesVal.text = p.totalMatches.ToString("N0");
        if (kpiMatchesSub != null) kpiMatchesSub.text = $"{p.wins} Thắng  •  {p.losses} Thua  •  {p.quitOrInProgress} Khác";

        if (kpiWinRateVal != null)
        {
            kpiWinRateVal.text = $"{p.winRatePercent:0.#}%";
            int decided = p.wins + p.losses;
            kpiWinRateVal.color = decided == 0
                ? MedievalSettingsArt.IvoryText
                : (p.winRatePercent >= 55f
                    ? EmeraldAccent
                    : (p.winRatePercent >= 40f ? MedievalSettingsArt.BrightGoldText : CrimsonAccent));
        }
        if (kpiWinRateSub != null)
        {
            int decided = p.wins + p.losses;
            kpiWinRateSub.text = decided > 0 ? $"Trên {decided} trận phân thắng bại" : "Chưa có trận hoàn tất";
        }

        if (kpiKillsVal != null) kpiKillsVal.text = $"{p.totalEnemiesKilled:N0} quái";
        if (kpiKillsSub != null) kpiKillsSub.text = $"Boss đã tiêu diệt: {p.totalBossKills}";

        if (kpiPlayTimeVal != null) kpiPlayTimeVal.text = FormatDurationMinutes(p.totalPlayTimeMinutes);
        if (kpiPlayTimeSub != null) kpiPlayTimeSub.text = "Tổng thời gian trong trận";

        BindFavoriteHeroShowcase(p);

        if (favWeaponNameText != null)
        {
            favWeaponNameText.text = string.IsNullOrWhiteSpace(p.favoriteWeaponName) ? "Chưa mua" : p.favoriteWeaponName;
        }
        if (favWeaponMetaText != null)
        {
            favWeaponMetaText.text = p.favoriteWeaponPurchases > 0
                ? $"Đã mua {p.favoriteWeaponPurchases} lần tại Cửa Hàng Lò Rèn"
                : "Chưa ghi nhận giao dịch vũ khí";
        }

        if (statParryVal != null) statParryVal.text = $"{p.totalPerfectParries:N0} pha";
        if (statBlockVal != null) statBlockVal.text = $"{p.totalBlockHits:N0} lần";
        if (statGoldEarnedVal != null) statGoldEarnedVal.text = $"+{p.totalGoldEarned:N0} G";
        if (statGoldSpentVal != null) statGoldSpentVal.text = $"-{p.totalGoldSpent:N0} G";

        PopulateChampionsTab(p.characterStats, p.favoriteCharacterId);
        PopulateMatchesTab(p.recentMatches);
    }

    private void BindFavoriteHeroShowcase(PlayerProfileResponse p)
    {
        bool hasFav = p.favoriteCharacterMatches > 0 && !string.IsNullOrWhiteSpace(p.favoriteCharacterId);
        CharacterProfile selectedFallback = SelectedCharacterStore.GetSelectedProfile();

        string showcaseId = hasFav
            ? p.favoriteCharacterId
            : (selectedFallback != null && !string.IsNullOrWhiteSpace(selectedFallback.id) ? selectedFallback.id : "vanguard");

        PlayerCharacterStatItem favStat = FindCharacterStat(p.characterStats, showcaseId);
        CharacterProfile rosterProfile = CharacterPortraitProvider.FindRosterProfile(showcaseId) ?? selectedFallback;

        Color accent = ParseHexColor(
            favStat != null ? favStat.accentHex : null,
            showcaseId,
            rosterProfile != null ? rosterProfile.themeColor : MedievalSettingsArt.BrightGoldText);

        if (favCharPortraitImg != null)
        {
            favCharPortraitImg.texture = CharacterPortraitProvider.GetPortrait(showcaseId, accent);
            favCharPortraitImg.color = Color.white;
        }
        if (favCharPortraitBorder != null)
        {
            favCharPortraitBorder.color = accent;
        }

        string displayName = hasFav
            ? (string.IsNullOrWhiteSpace(p.favoriteCharacterName) ? showcaseId.ToUpperInvariant() : p.favoriteCharacterName)
            : (rosterProfile != null && !string.IsNullOrWhiteSpace(rosterProfile.displayName)
                ? rosterProfile.displayName
                : showcaseId.ToUpperInvariant());

        if (favCharNameText != null)
        {
            favCharNameText.text = displayName;
            favCharNameText.color = accent;
        }

        string roleText = favStat != null && !string.IsNullOrWhiteSpace(favStat.roleBadge)
            ? favStat.roleBadge
            : (rosterProfile != null ? rosterProfile.roleBadge : "CHIẾN BINH");
        if (favCharRoleText != null)
        {
            favCharRoleText.text = hasFav ? $"★  SỞ TRƯỜNG  •  {roleText}" : $"◆  ĐANG CHỌN  •  {roleText}";
            favCharRoleText.color = accent;
        }

        string titleStr = favStat != null && !string.IsNullOrWhiteSpace(favStat.title)
            ? favStat.title
            : (rosterProfile != null ? rosterProfile.title : string.Empty);
        if (favCharTitleText != null)
        {
            favCharTitleText.text = titleStr;
        }

        if (favCharSpecsText != null)
        {
            favCharSpecsText.text = FormatRosterSpecs(rosterProfile);
        }

        if (favCharPerkText != null)
        {
            favCharPerkText.text = rosterProfile != null && !string.IsNullOrWhiteSpace(rosterProfile.signaturePerk)
                ? rosterProfile.signaturePerk
                : string.Empty;
        }

        if (favCharMetaText != null)
        {
            if (hasFav && favStat != null)
            {
                favCharMetaText.text = $"{favStat.matches} trận  •  {favStat.wins} Thắng / {favStat.losses} Thua  •  Tỉ lệ thắng {favStat.winRatePercent:0.#}%  •  {favStat.totalEnemiesKilled:N0} quái";
                favCharMetaText.color = MedievalSettingsArt.IvoryText;
            }
            else if (hasFav)
            {
                favCharMetaText.text = $"Đã tham chiến {p.favoriteCharacterMatches} trận";
                favCharMetaText.color = MedievalSettingsArt.IvoryText;
            }
            else
            {
                favCharMetaText.text = "Chưa có trận ghi nhận tướng — Tướng hiện đang chọn sẵn sàng ra trận";
                favCharMetaText.color = MutedSlateText;
            }
        }
    }

    private static string FormatRosterSpecs(CharacterProfile rosterProfile)
    {
        if (rosterProfile == null) return "Dữ liệu chỉ số cơ bản chưa sẵn sàng";
        int blockPct = Mathf.RoundToInt(rosterProfile.blockDamageReduction * 100f);
        int parryHealPct = Mathf.RoundToInt(rosterProfile.perfectParryHealPercent * 100f);
        return $"{rosterProfile.maxHealth} HP   •   {rosterProfile.attackDamage} DMG   •   {rosterProfile.moveSpeed:0.#} m/s   •   Đỡ {blockPct}%   •   Parry +{parryHealPct}% HP";
    }

    private static PlayerCharacterStatItem FindCharacterStat(PlayerCharacterStatItem[] list, string charId)
    {
        if (list == null || string.IsNullOrWhiteSpace(charId)) return null;
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] != null && string.Equals(list[i].characterId, charId, StringComparison.OrdinalIgnoreCase))
            {
                return list[i];
            }
        }
        return null;
    }

    private void PopulateChampionsTab(PlayerCharacterStatItem[] stats, string favoriteCharacterId)
    {
        if (championsContentRt == null) return;
        ClearChildren(championsContentRt);

        if (stats == null || stats.Length == 0)
        {
            CreateEmptyStateRow(championsContentRt, "Chưa có dữ liệu chiến tích theo tướng.", 1524f);
            RebuildScroll(championsContentRt, championsScroll);
            return;
        }

        // Sắp xếp dạng lưới 2 cột mỗi hàng (752x310 mỗi thẻ) để hiển thị rõ ảnh chân dung & chỉ số tướng
        GameObject currentRow = null;
        for (int i = 0; i < stats.Length; i++)
        {
            PlayerCharacterStatItem item = stats[i];
            if (item == null) continue;

            if (currentRow == null || currentRow.transform.childCount >= 2)
            {
                currentRow = new GameObject("ChampionGridRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                currentRow.transform.SetParent(championsContentRt, false);

                LayoutElement rowLe = currentRow.GetComponent<LayoutElement>();
                rowLe.preferredWidth = 1524f;
                rowLe.flexibleWidth = 1f;
                rowLe.minHeight = 310f;
                rowLe.preferredHeight = 310f;

                HorizontalLayoutGroup rowHlg = currentRow.GetComponent<HorizontalLayoutGroup>();
                rowHlg.spacing = 20f;
                rowHlg.childAlignment = TextAnchor.UpperLeft;
                rowHlg.childControlWidth = true;
                rowHlg.childControlHeight = true;
                rowHlg.childForceExpandWidth = false;
                rowHlg.childForceExpandHeight = true;
            }

            bool isFavorite = !string.IsNullOrWhiteSpace(favoriteCharacterId)
                && item.matches > 0
                && string.Equals(item.characterId, favoriteCharacterId, StringComparison.OrdinalIgnoreCase);

            CreateChampionCard(currentRow.transform, item, isFavorite);
        }

        RebuildScroll(championsContentRt, championsScroll);
    }

    private static void CreateChampionCard(Transform parentRow, PlayerCharacterStatItem item, bool isFavorite)
    {
        CharacterProfile rosterProfile = CharacterPortraitProvider.FindRosterProfile(item.characterId);
        Color accent = ParseHexColor(
            item.accentHex,
            item.characterId,
            rosterProfile != null ? rosterProfile.themeColor : MedievalSettingsArt.BrightGoldText);

        Color borderCol = isFavorite
            ? accent
            : (item.matches > 0 ? new Color(accent.r, accent.g, accent.b, 0.55f) : MedievalSettingsArt.BronzeBorder);

        GameObject card = MedievalSettingsArt.CreateFramedPanel(
            parentRow, "ChampCard_" + item.characterId, 752f, 310f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderStuds, borderCol);

        // Thanh màu nhận diện bên trái thẻ
        GameObject stripe = new GameObject("AccentStripe", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        stripe.transform.SetParent(card.transform, false);
        RectTransform sRt = stripe.GetComponent<RectTransform>();
        sRt.anchorMin = new Vector2(0f, 0f);
        sRt.anchorMax = new Vector2(0f, 1f);
        sRt.pivot = new Vector2(0f, 0.5f);
        sRt.anchoredPosition = new Vector2(5f, 0f);
        sRt.sizeDelta = new Vector2(7f, -14f);
        Image sImg = stripe.GetComponent<Image>();
        sImg.color = accent;
        sImg.raycastTarget = false;
        stripe.GetComponent<LayoutElement>().ignoreLayout = true;

        GameObject inner = new GameObject("CardInner", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        inner.transform.SetParent(card.transform, false);
        RectTransform iRt = inner.GetComponent<RectTransform>();
        iRt.anchorMin = Vector2.zero;
        iRt.anchorMax = Vector2.one;
        iRt.offsetMin = new Vector2(20f, 16f);
        iRt.offsetMax = new Vector2(-18f, -16f);

        HorizontalLayoutGroup hlg = inner.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 20f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        // Cột trái: Ảnh chân dung 3D + Vai trò + Trạng thái
        GameObject leftCol = new GameObject("LeftPortraitCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
        leftCol.transform.SetParent(inner.transform, false);
        MedievalSettingsArt.SetFixedSize(leftCol, 182f, 278f);

        VerticalLayoutGroup leftVlg = leftCol.GetComponent<VerticalLayoutGroup>();
        leftVlg.spacing = 8f;
        leftVlg.childAlignment = TextAnchor.UpperCenter;
        leftVlg.childControlWidth = true;
        leftVlg.childControlHeight = true;
        leftVlg.childForceExpandWidth = false;
        leftVlg.childForceExpandHeight = false;

        CreateFramedPortrait(leftCol.transform, "Portrait", 178f, item.characterId, accent, out _, out _);

        string roleStr = string.IsNullOrWhiteSpace(item.roleBadge) ? "CHIẾN BINH" : item.roleBadge;
        GameObject rolePill = MedievalSettingsArt.CreateFramedPanel(
            leftCol.transform, "RoleBadge", 178f, 30f,
            MedievalSettingsArt.PanelClean, new Color(accent.r * 0.16f, accent.g * 0.16f, accent.b * 0.16f, 0.96f),
            MedievalSettingsArt.BorderClean, new Color(accent.r, accent.g, accent.b, 0.72f));
        TMP_Text roleTxt = FantasyUiThemeHelper.CreateText(rolePill.transform, "Text", roleStr,
            12.5f, FontStyles.Bold, accent, TextAlignmentOptions.Center);
        FantasyUiThemeHelper.StretchFull(roleTxt.rectTransform);
        roleTxt.textWrappingMode = TextWrappingModes.NoWrap;
        roleTxt.overflowMode = TextOverflowModes.Ellipsis;

        string statusStr = isFavorite
            ? "★  TƯỚNG SỞ TRƯỜNG"
            : (item.matches > 0 ? "◆  ĐÃ THAM CHIẾN" : "CHƯA XUẤT TRẬN");
        Color statusCol = isFavorite
            ? MedievalSettingsArt.BrightGoldText
            : (item.matches > 0 ? EmeraldAccent : MutedSlateText);
        TMP_Text statusTxt = FantasyUiThemeHelper.CreateText(leftCol.transform, "StatusTag", statusStr,
            12.5f, FontStyles.Bold, statusCol, TextAlignmentOptions.Center);
        statusTxt.textWrappingMode = TextWrappingModes.NoWrap;
        MedievalSettingsArt.SetFixedSize(statusTxt.gameObject, 178f, 24f);

        // Cột phải: Thông tin chi tiết, chỉ số gốc, Win Rate, Quái/Boss/Thời lượng
        GameObject rightCol = new GameObject("RightInfoCol", typeof(RectTransform), typeof(VerticalLayoutGroup));
        rightCol.transform.SetParent(inner.transform, false);
        MedievalSettingsArt.SetFixedSize(rightCol, 512f, 278f);

        VerticalLayoutGroup rightVlg = rightCol.GetComponent<VerticalLayoutGroup>();
        rightVlg.spacing = 7f;
        rightVlg.childAlignment = TextAnchor.UpperLeft;
        rightVlg.childControlWidth = true;
        rightVlg.childControlHeight = true;
        rightVlg.childForceExpandWidth = true;
        rightVlg.childForceExpandHeight = false;

        // Hàng tên + danh hiệu
        GameObject titleBlock = new GameObject("TitleBlock", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        titleBlock.transform.SetParent(rightCol.transform, false);
        MedievalSettingsArt.SetFixedSize(titleBlock, 512f, 34f);
        HorizontalLayoutGroup tbHlg = titleBlock.GetComponent<HorizontalLayoutGroup>();
        tbHlg.spacing = 10f;
        tbHlg.childAlignment = TextAnchor.MiddleLeft;
        tbHlg.childControlWidth = true;
        tbHlg.childControlHeight = true;
        tbHlg.childForceExpandWidth = false;

        TMP_Text nameTxt = FantasyUiThemeHelper.CreateText(titleBlock.transform, "Name", item.characterName,
            26f, FontStyles.Bold, accent, TextAlignmentOptions.MidlineLeft);
        nameTxt.textWrappingMode = TextWrappingModes.NoWrap;
        nameTxt.overflowMode = TextOverflowModes.Ellipsis;
        AddShadow(nameTxt, -1.8f);
        MedievalSettingsArt.SetFixedSize(nameTxt.gameObject, 224f, 34f);

        TMP_Text titleTxt = FantasyUiThemeHelper.CreateText(titleBlock.transform, "Title",
            string.IsNullOrWhiteSpace(item.title) ? "Chiến Binh" : item.title,
            15.5f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.MidlineRight);
        titleTxt.textWrappingMode = TextWrappingModes.NoWrap;
        titleTxt.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(titleTxt.gameObject, 278f, 34f);

        // Chỉ số chiến đấu cơ bản & nội tại từ CharacterRosterConfig
        string specsText = rosterProfile != null
            ? FormatRosterSpecs(rosterProfile)
            : "Các phiên chơi ở bản cũ trước khi hệ thống lưu tướng";
        TMP_Text specsTxt = FantasyUiThemeHelper.CreateText(rightCol.transform, "Specs", specsText,
            13.5f, FontStyles.Bold, MedievalSettingsArt.IvoryText, TextAlignmentOptions.Left);
        specsTxt.textWrappingMode = TextWrappingModes.NoWrap;
        specsTxt.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(specsTxt.gameObject, 512f, 22f);

        string perkStr = rosterProfile != null && !string.IsNullOrWhiteSpace(rosterProfile.signaturePerk)
            ? rosterProfile.signaturePerk
            : "Tự động ghi nhận dữ liệu khi chọn tướng vào trận mới.";
        TMP_Text perkTxt = FantasyUiThemeHelper.CreateText(rightCol.transform, "Perk", perkStr,
            13.5f, FontStyles.Normal, MutedSlateText, TextAlignmentOptions.Left);
        perkTxt.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(perkTxt.gameObject, 512f, 38f);

        // Thanh Tỉ lệ thắng + Số trận
        GameObject wrTop = new GameObject("WinTopRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        wrTop.transform.SetParent(rightCol.transform, false);
        MedievalSettingsArt.SetFixedSize(wrTop, 512f, 28f);
        HorizontalLayoutGroup wrHlg = wrTop.GetComponent<HorizontalLayoutGroup>();
        wrHlg.childControlWidth = true;
        wrHlg.childControlHeight = true;
        wrHlg.childForceExpandWidth = false;

        TMP_Text matchesTxt = FantasyUiThemeHelper.CreateText(wrTop.transform, "Matches",
            $"{item.matches} trận  ({item.wins} Thắng / {item.losses} Thua)",
            16.5f, FontStyles.Bold, MedievalSettingsArt.IvoryText, TextAlignmentOptions.MidlineLeft);
        matchesTxt.textWrappingMode = TextWrappingModes.NoWrap;
        MedievalSettingsArt.SetFixedSize(matchesTxt.gameObject, 372f, 28f);

        int decided = item.wins + item.losses;
        Color rateCol = decided == 0
            ? MutedSlateText
            : (item.winRatePercent >= 55f ? EmeraldAccent : (item.winRatePercent >= 40f ? MedievalSettingsArt.BrightGoldText : CrimsonAccent));
        TMP_Text rateTxt = FantasyUiThemeHelper.CreateText(wrTop.transform, "Rate",
            decided > 0 ? $"{item.winRatePercent:0.#}%" : "--%",
            21f, FontStyles.Bold, rateCol, TextAlignmentOptions.MidlineRight);
        rateTxt.textWrappingMode = TextWrappingModes.NoWrap;
        MedievalSettingsArt.SetFixedSize(rateTxt.gameObject, 140f, 28f);

        GameObject barTrack = new GameObject("BarTrack", typeof(RectTransform), typeof(Image));
        barTrack.transform.SetParent(rightCol.transform, false);
        MedievalSettingsArt.SetFixedSize(barTrack, 512f, 14f);
        Image trackImg = barTrack.GetComponent<Image>();
        trackImg.color = TrackDarkFill;
        trackImg.raycastTarget = false;

        GameObject barFill = new GameObject("BarFill", typeof(RectTransform), typeof(Image));
        barFill.transform.SetParent(barTrack.transform, false);
        RectTransform bfRt = barFill.GetComponent<RectTransform>();
        float pct = decided > 0 ? Mathf.Clamp01(item.winRatePercent / 100f) : 0f;
        bfRt.anchorMin = new Vector2(0f, 0f);
        bfRt.anchorMax = new Vector2(pct, 1f);
        bfRt.offsetMin = Vector2.zero;
        bfRt.offsetMax = Vector2.zero;
        Image bfImg = barFill.GetComponent<Image>();
        bfImg.color = decided > 0 ? accent : MutedSlateText;
        bfImg.raycastTarget = false;

        // 3 Ô chỉ số phụ phía dưới
        GameObject pillsRow = new GameObject("MiniStatsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        pillsRow.transform.SetParent(rightCol.transform, false);
        MedievalSettingsArt.SetFixedSize(pillsRow, 512f, 70f);
        HorizontalLayoutGroup pHlg = pillsRow.GetComponent<HorizontalLayoutGroup>();
        pHlg.spacing = 10f;
        pHlg.childAlignment = TextAnchor.MiddleCenter;
        pHlg.childControlWidth = true;
        pHlg.childControlHeight = true;
        pHlg.childForceExpandWidth = true;
        pHlg.childForceExpandHeight = true;

        CreateChampMiniPill(pillsRow.transform, "QUÁI HẠ", $"{item.totalEnemiesKilled:N0}", $"TB {item.avgEnemiesPerMatch:0.#}/trận", MedievalSettingsArt.IvoryText);
        CreateChampMiniPill(pillsRow.transform, "BOSS DIỆT", $"{item.bossKills}", item.bossKills > 0 ? "Chiến tích Boss" : "Chưa hạ Boss", item.bossKills > 0 ? MedievalSettingsArt.BrightGoldText : MutedSlateText);
        CreateChampMiniPill(pillsRow.transform, "THỜI GIAN", FormatDurationMinutes(item.totalPlayTimeMinutes), "Tổng trong trận", CyanAccent);
    }

    private static void CreateChampMiniPill(Transform parent, string label, string value, string sub, Color valueColor)
    {
        GameObject pill = MedievalSettingsArt.CreateFramedPanel(
            parent, "MiniPill_" + label, 164f, 70f,
            MedievalSettingsArt.PanelClean, MiniPillFill,
            MedievalSettingsArt.BorderClean, new Color(0.38f, 0.30f, 0.18f, 0.55f));

        GameObject body = new GameObject("Body", typeof(RectTransform), typeof(VerticalLayoutGroup));
        body.transform.SetParent(pill.transform, false);
        RectTransform bRt = body.GetComponent<RectTransform>();
        bRt.anchorMin = Vector2.zero;
        bRt.anchorMax = Vector2.one;
        bRt.offsetMin = new Vector2(10f, 6f);
        bRt.offsetMax = new Vector2(-10f, -6f);

        VerticalLayoutGroup vlg = body.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 2f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        TMP_Text lTxt = FantasyUiThemeHelper.CreateText(body.transform, "Lbl", label,
            11.5f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Center);
        lTxt.textWrappingMode = TextWrappingModes.NoWrap;
        MedievalSettingsArt.SetFixedSize(lTxt.gameObject, 144f, 16f);

        TMP_Text vTxt = FantasyUiThemeHelper.CreateText(body.transform, "Val", value,
            17.5f, FontStyles.Bold, valueColor, TextAlignmentOptions.Center);
        vTxt.textWrappingMode = TextWrappingModes.NoWrap;
        vTxt.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(vTxt.gameObject, 144f, 22f);

        TMP_Text sTxt = FantasyUiThemeHelper.CreateText(body.transform, "Sub", sub,
            11.5f, FontStyles.Normal, MutedSlateText, TextAlignmentOptions.Center);
        sTxt.textWrappingMode = TextWrappingModes.NoWrap;
        sTxt.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(sTxt.gameObject, 144f, 16f);
    }

    private void PopulateMatchesTab(PlayerRecentMatchItem[] matches)
    {
        if (matchesContentRt == null) return;
        ClearChildren(matchesContentRt);

        if (matches == null || matches.Length == 0)
        {
            CreateEmptyStateRow(matchesContentRt, "Chưa có trận đấu nào được ghi nhận.\nHãy vào chiến đấu để lưu lại chiến tích của bạn!", 1524f);
            RebuildScroll(matchesContentRt, matchesScroll);
            return;
        }

        for (int i = 0; i < matches.Length; i++)
        {
            PlayerRecentMatchItem m = matches[i];
            if (m == null) continue;
            CreateMatchRow(matchesContentRt, m);
        }

        RebuildScroll(matchesContentRt, matchesScroll);
    }

    private static void CreateMatchRow(Transform parent, PlayerRecentMatchItem m)
    {
        GameObject row = MedievalSettingsArt.CreateFramedPanel(
            parent, "MatchRow_" + m.sessionId, 1524f, 68f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderClean, new Color(0.38f, 0.30f, 0.18f, 0.65f));

        LayoutElement le = row.GetComponent<LayoutElement>();
        le.preferredWidth = 1524f;
        le.flexibleWidth = 1f;
        le.minHeight = 68f;
        le.preferredHeight = 68f;

        GameObject inner = new GameObject("MatchInner", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        inner.transform.SetParent(row.transform, false);
        RectTransform iRt = inner.GetComponent<RectTransform>();
        iRt.anchorMin = Vector2.zero;
        iRt.anchorMax = Vector2.one;
        iRt.offsetMin = new Vector2(16f, 6f);
        iRt.offsetMax = new Vector2(-16f, -6f);

        HorizontalLayoutGroup hlg = inner.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        CreateMatchCellText(inner.transform, $"#{m.sessionId}", 84f, 16f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.MidlineLeft);

        string levelLabel = string.IsNullOrWhiteSpace(m.levelName)
            ? (m.levelId > 0 ? $"Level {m.levelId}" : "Chưa rõ")
            : m.levelName;
        CreateMatchCellText(inner.transform, levelLabel, 290f, 16f, FontStyles.Normal, MedievalSettingsArt.IvoryText, TextAlignmentOptions.MidlineLeft);

        // Cột Tướng kèm ảnh chân dung thu nhỏ 46x46
        Color charColor = ParseHexColor(null, m.characterId, MutedSlateText);
        bool isLegacyChar = string.IsNullOrWhiteSpace(m.characterId) || string.Equals(m.characterId, "unknown", StringComparison.OrdinalIgnoreCase);
        string charName = isLegacyChar
            ? "Bản cũ"
            : (string.IsNullOrWhiteSpace(m.characterName) ? "Bản cũ" : m.characterName);
        CreateMatchChampionCell(inner.transform, m.characterId, charName, charColor, 222f);

        CreateResultBadgeCell(inner.transform, m.result, 150f);

        string killLabel = m.bossKilled ? $"{m.enemiesKilled} quái • Boss" : $"{m.enemiesKilled} quái";
        Color killColor = m.bossKilled ? MedievalSettingsArt.BrightGoldText : MedievalSettingsArt.IvoryText;
        CreateMatchCellText(inner.transform, killLabel, 170f, 15.5f, FontStyles.Bold, killColor, TextAlignmentOptions.Center);

        string econParryLabel = $"+{m.goldEarned} G  •  {m.perfectParries} Parry";
        CreateMatchCellText(inner.transform, econParryLabel, 196f, 14.5f, FontStyles.Normal, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.Center);

        string durText = m.hasEndTime ? FormatDurationMinutes(m.durationMinutes) : "Đang chơi";
        CreateMatchCellText(inner.transform, durText, 134f, 15.5f, FontStyles.Normal, MutedSlateText, TextAlignmentOptions.Center);

        string timeText = FormatLocalDate(m.startTime, "dd/MM HH:mm");
        if (string.IsNullOrEmpty(timeText)) timeText = "--";
        CreateMatchCellText(inner.transform, timeText, 176f, 15f, FontStyles.Normal, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.MidlineRight);
    }

    private static void CreateMatchChampionCell(Transform parent, string charId, string charName, Color charColor, float width)
    {
        GameObject cell = new GameObject("ChampCell", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        cell.transform.SetParent(parent, false);
        MedievalSettingsArt.SetFixedSize(cell, width, 54f);

        HorizontalLayoutGroup hlg = cell.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        CreateFramedPortrait(cell.transform, "Thumb", 46f, charId, charColor, out _, out _);

        TMP_Text txt = FantasyUiThemeHelper.CreateText(cell.transform, "Name", charName,
            15.5f, FontStyles.Bold, charColor, TextAlignmentOptions.MidlineLeft);
        txt.textWrappingMode = TextWrappingModes.NoWrap;
        txt.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(txt.gameObject, width - 56f, 46f);
    }

    private static void CreateMatchCellText(
        Transform parent,
        string content,
        float width,
        float fontSize,
        FontStyles style,
        Color color,
        TextAlignmentOptions align)
    {
        TMP_Text txt = FantasyUiThemeHelper.CreateText(parent, "Cell", content, fontSize, style, color, align);
        txt.textWrappingMode = TextWrappingModes.NoWrap;
        txt.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(txt.gameObject, width, 50f);
    }

    private static void CreateResultBadgeCell(Transform parent, string rawResult, float width)
    {
        string r = string.IsNullOrWhiteSpace(rawResult) ? string.Empty : rawResult.Trim().ToLowerInvariant();
        string label;
        Color fill;
        Color border;
        Color txtCol;

        if (r == "win" || r == "victory" || r == "completed" || r == "thắng")
        {
            label = "THẮNG";
            fill = MedievalSettingsArt.EmeraldFill;
            border = MedievalSettingsArt.EmeraldBorder;
            txtCol = MedievalSettingsArt.EmeraldText;
        }
        else if (r == "lose" || r == "loss" || r == "defeat" || r == "dead" || r == "thua")
        {
            label = "THUA";
            fill = MedievalSettingsArt.CrimsonFill;
            border = MedievalSettingsArt.CrimsonBorder;
            txtCol = MedievalSettingsArt.CrimsonText;
        }
        else if (r == "quit" || r == "exit" || r == "abandoned")
        {
            label = "THOÁT";
            fill = MedievalSettingsArt.BronzeFill;
            border = MedievalSettingsArt.BronzeBorder;
            txtCol = MedievalSettingsArt.BrightGoldText;
        }
        else
        {
            label = "ĐANG CHƠI";
            fill = new Color(0.08f, 0.20f, 0.30f, 0.96f);
            border = CyanAccent;
            txtCol = CyanAccent;
        }

        GameObject holder = new GameObject("BadgeHolder", typeof(RectTransform));
        holder.transform.SetParent(parent, false);
        MedievalSettingsArt.SetFixedSize(holder, width, 50f);

        GameObject badge = MedievalSettingsArt.CreateFramedPanel(
            holder.transform, "ResultBadge", 132f, 34f,
            MedievalSettingsArt.PanelClean, fill,
            MedievalSettingsArt.BorderClean, border);
        RectTransform bRt = badge.GetComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0.5f, 0.5f);
        bRt.anchorMax = new Vector2(0.5f, 0.5f);
        bRt.pivot = new Vector2(0.5f, 0.5f);
        bRt.anchoredPosition = Vector2.zero;

        TMP_Text txt = FantasyUiThemeHelper.CreateText(badge.transform, "Text", label,
            13.5f, FontStyles.Bold, txtCol, TextAlignmentOptions.Center);
        FantasyUiThemeHelper.StretchFull(txt.rectTransform);
        txt.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private static void CreateEmptyStateRow(Transform parent, string message, float width)
    {
        GameObject box = MedievalSettingsArt.CreateFramedPanel(
            parent, "EmptyState", width, 180f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.BronzeBorder);
        LayoutElement le = box.GetComponent<LayoutElement>();
        le.preferredWidth = width;
        le.flexibleWidth = 1f;
        le.minHeight = 180f;
        le.preferredHeight = 180f;

        TMP_Text txt = FantasyUiThemeHelper.CreateText(box.transform, "Text", message,
            19f, FontStyles.Normal, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Center);
        RectTransform rt = txt.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(28f, 20f);
        rt.offsetMax = new Vector2(-28f, -20f);
    }

    private static GameObject CreateFramedPortrait(
        Transform parent,
        string name,
        float frameSize,
        string characterId,
        Color accent,
        out RawImage rawImg,
        out Image borderImg)
    {
        GameObject frame = new GameObject(name, typeof(RectTransform), typeof(Image));
        frame.transform.SetParent(parent, false);
        MedievalSettingsArt.SetFixedSize(frame, frameSize, frameSize);

        Image bgImg = frame.GetComponent<Image>();
        MedievalSettingsArt.ApplySprite(bgImg, MedievalSettingsArt.PanelClean, TrackDarkFill);
        bgImg.raycastTarget = false;

        GameObject rawGo = new GameObject("PortraitTex", typeof(RectTransform), typeof(RawImage));
        rawGo.transform.SetParent(frame.transform, false);
        RectTransform rRt = rawGo.GetComponent<RectTransform>();
        float pad = frameSize >= 100f ? 4f : 2f;
        rRt.anchorMin = Vector2.zero;
        rRt.anchorMax = Vector2.one;
        rRt.offsetMin = new Vector2(pad, pad);
        rRt.offsetMax = new Vector2(-pad, -pad);

        rawImg = rawGo.GetComponent<RawImage>();
        rawImg.raycastTarget = false;
        if (!string.IsNullOrEmpty(characterId))
        {
            rawImg.texture = CharacterPortraitProvider.GetPortrait(characterId, accent);
        }
        rawImg.color = Color.white;

        GameObject borderGo = new GameObject("BorderOverlay", typeof(RectTransform), typeof(Image));
        borderGo.transform.SetParent(frame.transform, false);
        FantasyUiThemeHelper.StretchFull(borderGo.GetComponent<RectTransform>());
        borderImg = borderGo.GetComponent<Image>();
        Sprite borderSprite = frameSize >= 100f ? MedievalSettingsArt.BorderStuds : MedievalSettingsArt.BorderClean;
        MedievalSettingsArt.ApplySprite(borderImg, borderSprite, accent);
        borderImg.raycastTarget = false;

        return frame;
    }

    // ==================== UI BUILDER ====================

    private void EnsureUiBuilt()
    {
        if (uiBuilt && overlayCanvas != null) return;
        uiBuilt = true;

        GameObject canvasGo = new GameObject("WarriorProfileCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        overlayCanvas = canvasGo.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 520;
        FantasyUiThemeHelper.ConfigureResponsiveCanvas(overlayCanvas);
        canvasRt = canvasGo.GetComponent<RectTransform>();

        backdropObj = new GameObject("ProfileBackdrop", typeof(RectTransform), typeof(Image));
        backdropObj.transform.SetParent(canvasGo.transform, false);
        FantasyUiThemeHelper.StretchFull(backdropObj.GetComponent<RectTransform>());
        Image bdImg = backdropObj.GetComponent<Image>();
        bdImg.color = MedievalSettingsArt.BackdropDark;
        bdImg.raycastTarget = true;

        BuildMainWindow(canvasGo.transform);

        backdropObj.SetActive(false);
        windowObj.SetActive(false);
    }

    private void BuildMainWindow(Transform canvasRoot)
    {
        windowObj = MedievalSettingsArt.CreateFramedPanel(
            canvasRoot, "WarriorProfileWindow", WindowW, WindowH,
            MedievalSettingsArt.PanelGothic, MedievalSettingsArt.FrameObsidian,
            MedievalSettingsArt.BorderGothic, MedievalSettingsArt.GoldOrnament,
            raycastTarget: true);
        windowRt = windowObj.GetComponent<RectTransform>();
        windowRt.anchorMin = new Vector2(0.5f, 0.5f);
        windowRt.anchorMax = new Vector2(0.5f, 0.5f);
        windowRt.pivot = new Vector2(0.5f, 0.5f);
        windowRt.anchoredPosition = Vector2.zero;
        windowCanvasGroup = windowObj.AddComponent<CanvasGroup>();

        BuildHeader(windowObj.transform);
        BuildTabBar(windowObj.transform);
        BuildContentArea(windowObj.transform);
    }

    private void BuildHeader(Transform root)
    {
        GameObject headerArea = new GameObject("ProfileHeader", typeof(RectTransform));
        headerArea.transform.SetParent(root, false);
        RectTransform haRt = headerArea.GetComponent<RectTransform>();
        haRt.anchorMin = new Vector2(0f, 1f);
        haRt.anchorMax = new Vector2(1f, 1f);
        haRt.pivot = new Vector2(0.5f, 1f);
        haRt.anchoredPosition = new Vector2(0f, -20f);
        haRt.sizeDelta = new Vector2(-72f, 118f);

        TMP_Text kicker = FantasyUiThemeHelper.CreateText(headerArea.transform, "Kicker", "✦  BIÊN NIÊN SỬ CHIẾN TRẬN  ✦",
            15f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.Center);
        RectTransform kRt = kicker.rectTransform;
        kRt.anchorMin = new Vector2(0.5f, 1f);
        kRt.anchorMax = new Vector2(0.5f, 1f);
        kRt.pivot = new Vector2(0.5f, 1f);
        kRt.anchoredPosition = Vector2.zero;
        kRt.sizeDelta = new Vector2(680f, 22f);
        kicker.characterSpacing = 4f;
        kicker.textWrappingMode = TextWrappingModes.NoWrap;

        GameObject titleRow = new GameObject("TitleRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        titleRow.transform.SetParent(headerArea.transform, false);
        RectTransform trRt = titleRow.GetComponent<RectTransform>();
        trRt.anchorMin = new Vector2(0.5f, 1f);
        trRt.anchorMax = new Vector2(0.5f, 1f);
        trRt.pivot = new Vector2(0.5f, 1f);
        trRt.anchoredPosition = new Vector2(0f, -22f);
        trRt.sizeDelta = new Vector2(780f, 50f);

        HorizontalLayoutGroup trHlg = titleRow.GetComponent<HorizontalLayoutGroup>();
        trHlg.spacing = 16f;
        trHlg.childAlignment = TextAnchor.MiddleCenter;
        trHlg.childControlWidth = true;
        trHlg.childControlHeight = true;
        trHlg.childForceExpandWidth = false;
        trHlg.childForceExpandHeight = false;

        GameObject leftWing = new GameObject("LeftWing", typeof(RectTransform), typeof(Image));
        leftWing.transform.SetParent(titleRow.transform, false);
        MedievalSettingsArt.SetFixedSize(leftWing, 155f, 28f);
        Image lwImg = leftWing.GetComponent<Image>();
        lwImg.sprite = MedievalSettingsArt.DividerFadeOrnate;
        lwImg.color = MedievalSettingsArt.GoldOrnament;
        lwImg.raycastTarget = false;

        TMP_Text titleTxt = FantasyUiThemeHelper.CreateText(titleRow.transform, "TitleText", "HỒ SƠ CHIẾN BINH",
            40f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        titleTxt.textWrappingMode = TextWrappingModes.NoWrap;
        titleTxt.characterSpacing = 4.5f;
        MedievalSettingsArt.ApplyTitleGoldGradient(titleTxt);
        AddShadow(titleTxt, -3f);
        MedievalSettingsArt.SetFixedSize(titleTxt.gameObject, 430f, 50f);

        GameObject rightWing = new GameObject("RightWing", typeof(RectTransform), typeof(Image));
        rightWing.transform.SetParent(titleRow.transform, false);
        MedievalSettingsArt.SetFixedSize(rightWing, 155f, 28f);
        Image rwImg = rightWing.GetComponent<Image>();
        rwImg.sprite = MedievalSettingsArt.DividerFadeOrnate;
        rwImg.color = MedievalSettingsArt.GoldOrnament;
        rwImg.raycastTarget = false;
        rightWing.transform.localScale = new Vector3(-1f, 1f, 1f);

        headerSubText = FantasyUiThemeHelper.CreateText(headerArea.transform, "SubtitleText",
            "CHIẾN BINH: VANGUARD",
            17f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Center);
        RectTransform sRt = headerSubText.rectTransform;
        sRt.anchorMin = new Vector2(0.5f, 1f);
        sRt.anchorMax = new Vector2(0.5f, 1f);
        sRt.pivot = new Vector2(0.5f, 1f);
        sRt.anchoredPosition = new Vector2(0f, -76f);
        sRt.sizeDelta = new Vector2(960f, 26f);
        headerSubText.textWrappingMode = TextWrappingModes.NoWrap;
        headerSubText.overflowMode = TextOverflowModes.Ellipsis;

        // Nút Làm Mới + Đóng góc phải Header
        GameObject actionsGroup = new GameObject("HeaderActions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        actionsGroup.transform.SetParent(headerArea.transform, false);
        RectTransform agRt = actionsGroup.GetComponent<RectTransform>();
        agRt.anchorMin = new Vector2(1f, 1f);
        agRt.anchorMax = new Vector2(1f, 1f);
        agRt.pivot = new Vector2(1f, 1f);
        agRt.anchoredPosition = new Vector2(-4f, -18f);
        agRt.sizeDelta = new Vector2(298f, 46f);

        HorizontalLayoutGroup agHlg = actionsGroup.GetComponent<HorizontalLayoutGroup>();
        agHlg.spacing = 10f;
        agHlg.childAlignment = TextAnchor.MiddleRight;
        agHlg.childControlWidth = true;
        agHlg.childControlHeight = true;
        agHlg.childForceExpandWidth = false;
        agHlg.childForceExpandHeight = false;

        refreshBtn = MedievalSettingsArt.CreateFramedButton(
            actionsGroup.transform, "BtnRefreshProfile", "LÀM MỚI", 134f, 44f, 15.5f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.BronzeFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.GoldOrnament,
            MedievalSettingsArt.BrightGoldText, null, OnRefreshClicked);

        MedievalSettingsArt.CreateFramedButton(
            actionsGroup.transform, "BtnCloseProfile", "ĐÓNG [ESC]", 152f, 44f, 15.5f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.CrimsonFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.CrimsonBorder,
            MedievalSettingsArt.CrimsonText, MedievalSettingsArt.IconCross, OnCloseClicked);

        GameObject div = MedievalSettingsArt.CreateSymmetricalDivider(
            headerArea.transform, "HeaderBottomDivider", 1588f, 12f, MedievalSettingsArt.GoldOrnament, ornate: false);
        RectTransform dRt = div.GetComponent<RectTransform>();
        dRt.anchorMin = new Vector2(0.5f, 0f);
        dRt.anchorMax = new Vector2(0.5f, 0f);
        dRt.pivot = new Vector2(0.5f, 0f);
        dRt.anchoredPosition = Vector2.zero;
    }

    private void BuildTabBar(Transform root)
    {
        GameObject bar = new GameObject("ProfileTabBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        bar.transform.SetParent(root, false);
        RectTransform bRt = bar.GetComponent<RectTransform>();
        bRt.anchorMin = new Vector2(0.5f, 1f);
        bRt.anchorMax = new Vector2(0.5f, 1f);
        bRt.pivot = new Vector2(0.5f, 1f);
        bRt.anchoredPosition = new Vector2(0f, -148f);
        bRt.sizeDelta = new Vector2(1588f, 56f);

        HorizontalLayoutGroup hlg = bar.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 20f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        tabButtons[0] = MedievalSettingsArt.CreateFramedButton(
            bar.transform, "Tab_Overview", "TỔNG QUAN", 516f, 54f, 20f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.TabActiveFill,
            MedievalSettingsArt.BorderKnotted, MedievalSettingsArt.HeroGoldBorder,
            MedievalSettingsArt.TitleGoldTop, MedievalSettingsArt.IconSystem,
            () => SelectTab(ProfileTab.Overview, true));

        tabButtons[1] = MedievalSettingsArt.CreateFramedButton(
            bar.transform, "Tab_Champions", "CHIẾN TÍCH TƯỚNG", 516f, 54f, 20f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderKnotted, MedievalSettingsArt.BronzeBorder,
            MedievalSettingsArt.IvoryText, MedievalSettingsArt.IconPlay,
            () => SelectTab(ProfileTab.Champions, true));

        tabButtons[2] = MedievalSettingsArt.CreateFramedButton(
            bar.transform, "Tab_Matches", "LỊCH SỬ ĐẤU", 516f, 54f, 20f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderKnotted, MedievalSettingsArt.BronzeBorder,
            MedievalSettingsArt.IvoryText, MedievalSettingsArt.IconControls,
            () => SelectTab(ProfileTab.Matches, true));
    }

    private void BuildContentArea(Transform root)
    {
        GameObject contentSurface = MedievalSettingsArt.CreateFramedPanel(
            root, "ProfileContentSurface", 1588f, 708f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.InnerObsidian,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.BronzeBorder);
        RectTransform csRt = contentSurface.GetComponent<RectTransform>();
        csRt.anchorMin = Vector2.zero;
        csRt.anchorMax = Vector2.one;
        csRt.offsetMin = new Vector2(36f, 28f);
        csRt.offsetMax = new Vector2(-36f, -214f);

        GameObject pagesRoot = new GameObject("PagesRoot", typeof(RectTransform));
        pagesRoot.transform.SetParent(contentSurface.transform, false);
        RectTransform prRt = pagesRoot.GetComponent<RectTransform>();
        prRt.anchorMin = Vector2.zero;
        prRt.anchorMax = Vector2.one;
        prRt.offsetMin = new Vector2(24f, 20f);
        prRt.offsetMax = new Vector2(-24f, -20f);

        tabPages[0] = BuildOverviewTab(pagesRoot.transform);
        tabPages[1] = BuildChampionsTab(pagesRoot.transform);
        tabPages[2] = BuildMatchesTab(pagesRoot.transform);

        BuildStatusOverlay(pagesRoot.transform);
    }

    private GameObject BuildOverviewTab(Transform parent)
    {
        GameObject page = new GameObject("Page_Overview", typeof(RectTransform), typeof(VerticalLayoutGroup));
        page.transform.SetParent(parent, false);
        FantasyUiThemeHelper.StretchFull(page.GetComponent<RectTransform>());

        VerticalLayoutGroup vlg = page.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 20f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // Hàng 1: 4 Thẻ KPI lớn
        GameObject kpiRow = new GameObject("KpiRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        kpiRow.transform.SetParent(page.transform, false);
        MedievalSettingsArt.SetFixedSize(kpiRow, 1540f, 172f);
        HorizontalLayoutGroup kpiHlg = kpiRow.GetComponent<HorizontalLayoutGroup>();
        kpiHlg.spacing = 16f;
        kpiHlg.childAlignment = TextAnchor.MiddleCenter;
        kpiHlg.childControlWidth = true;
        kpiHlg.childControlHeight = true;
        kpiHlg.childForceExpandWidth = true;
        kpiHlg.childForceExpandHeight = true;

        CreateKpiCard(kpiRow.transform, "KpiMatches", "TỔNG SỐ TRẬN", "0", "0 Thắng • 0 Thua", out kpiMatchesVal, out kpiMatchesSub);
        CreateKpiCard(kpiRow.transform, "KpiWinRate", "TỈ LỆ THẮNG", "0%", "Chưa có trận hoàn tất", out kpiWinRateVal, out kpiWinRateSub);
        CreateKpiCard(kpiRow.transform, "KpiKills", "QUÁI & BOSS HẠ GỤC", "0 quái", "Boss đã tiêu diệt: 0", out kpiKillsVal, out kpiKillsSub);
        CreateKpiCard(kpiRow.transform, "KpiPlayTime", "THỜI GIAN CHIẾN ĐẤU", "0 phút", "Tổng thời gian trong trận", out kpiPlayTimeVal, out kpiPlayTimeSub);

        // Hàng 2: 2 Thẻ chi tiết (Chân dung Tướng sở trường + Chỉ số chiến đấu & kinh tế)
        GameObject detailRow = new GameObject("DetailRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        detailRow.transform.SetParent(page.transform, false);
        MedievalSettingsArt.SetFixedSize(detailRow, 1540f, 472f);
        HorizontalLayoutGroup dHlg = detailRow.GetComponent<HorizontalLayoutGroup>();
        dHlg.spacing = 20f;
        dHlg.childAlignment = TextAnchor.MiddleCenter;
        dHlg.childControlWidth = true;
        dHlg.childControlHeight = true;
        dHlg.childForceExpandWidth = true;
        dHlg.childForceExpandHeight = true;

        BuildFavoriteHighlightCard(detailRow.transform);
        BuildCombatEconomyCard(detailRow.transform);

        return page;
    }

    private static void CreateKpiCard(
        Transform parent,
        string name,
        string label,
        string initialValue,
        string initialSub,
        out TMP_Text valueText,
        out TMP_Text subText)
    {
        GameObject card = MedievalSettingsArt.CreateFramedPanel(
            parent, name, 373f, 172f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.BronzeBorder);

        GameObject body = new GameObject("Body", typeof(RectTransform), typeof(VerticalLayoutGroup));
        body.transform.SetParent(card.transform, false);
        RectTransform bRt = body.GetComponent<RectTransform>();
        bRt.anchorMin = Vector2.zero;
        bRt.anchorMax = Vector2.one;
        bRt.offsetMin = new Vector2(20f, 18f);
        bRt.offsetMax = new Vector2(-20f, -18f);

        VerticalLayoutGroup vlg = body.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        TMP_Text lblTxt = FantasyUiThemeHelper.CreateText(body.transform, "Label", label,
            15.5f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Center);
        lblTxt.textWrappingMode = TextWrappingModes.NoWrap;
        lblTxt.characterSpacing = 1.6f;
        MedievalSettingsArt.SetFixedSize(lblTxt.gameObject, 333f, 24f);

        valueText = FantasyUiThemeHelper.CreateText(body.transform, "Value", initialValue,
            40f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.Center);
        valueText.textWrappingMode = TextWrappingModes.NoWrap;
        valueText.overflowMode = TextOverflowModes.Ellipsis;
        AddShadow(valueText, -2.5f);
        MedievalSettingsArt.SetFixedSize(valueText.gameObject, 333f, 54f);

        subText = FantasyUiThemeHelper.CreateText(body.transform, "Sub", initialSub,
            15f, FontStyles.Normal, MutedSlateText, TextAlignmentOptions.Center);
        subText.textWrappingMode = TextWrappingModes.NoWrap;
        subText.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(subText.gameObject, 333f, 24f);
    }

    private void BuildFavoriteHighlightCard(Transform parent)
    {
        GameObject card = MedievalSettingsArt.CreateFramedPanel(
            parent, "CardFavorites", 760f, 472f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.GoldOrnament);

        GameObject body = new GameObject("Body", typeof(RectTransform), typeof(VerticalLayoutGroup));
        body.transform.SetParent(card.transform, false);
        RectTransform bRt = body.GetComponent<RectTransform>();
        bRt.anchorMin = Vector2.zero;
        bRt.anchorMax = Vector2.one;
        bRt.offsetMin = new Vector2(26f, 22f);
        bRt.offsetMax = new Vector2(-26f, -22f);

        VerticalLayoutGroup vlg = body.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 10f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        TMP_Text k1 = FantasyUiThemeHelper.CreateText(body.transform, "FavCharKicker", "★  TƯỚNG SỞ TRƯỜNG",
            15.5f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Left);
        k1.characterSpacing = 1.8f;
        MedievalSettingsArt.SetFixedSize(k1.gameObject, 708f, 24f);

        // Khối trưng bày chân dung 3D + thông số chi tiết
        GameObject showcaseRow = new GameObject("HeroShowcaseRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        showcaseRow.transform.SetParent(body.transform, false);
        MedievalSettingsArt.SetFixedSize(showcaseRow, 708f, 192f);
        HorizontalLayoutGroup scHlg = showcaseRow.GetComponent<HorizontalLayoutGroup>();
        scHlg.spacing = 20f;
        scHlg.childAlignment = TextAnchor.MiddleLeft;
        scHlg.childControlWidth = true;
        scHlg.childControlHeight = true;
        scHlg.childForceExpandWidth = false;
        scHlg.childForceExpandHeight = false;

        CreateFramedPortrait(showcaseRow.transform, "FavHeroPortrait", 180f, null, MedievalSettingsArt.GoldOrnament,
            out favCharPortraitImg, out favCharPortraitBorder);

        GameObject infoCol = new GameObject("FavHeroInfo", typeof(RectTransform), typeof(VerticalLayoutGroup));
        infoCol.transform.SetParent(showcaseRow.transform, false);
        MedievalSettingsArt.SetFixedSize(infoCol, 508f, 192f);
        VerticalLayoutGroup infoVlg = infoCol.GetComponent<VerticalLayoutGroup>();
        infoVlg.spacing = 6f;
        infoVlg.childAlignment = TextAnchor.UpperLeft;
        infoVlg.childControlWidth = true;
        infoVlg.childControlHeight = true;
        infoVlg.childForceExpandWidth = true;
        infoVlg.childForceExpandHeight = false;

        favCharRoleText = FantasyUiThemeHelper.CreateText(infoCol.transform, "FavCharRole", "★  SỞ TRƯỜNG",
            14f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.Left);
        favCharRoleText.textWrappingMode = TextWrappingModes.NoWrap;
        favCharRoleText.characterSpacing = 1.2f;
        MedievalSettingsArt.SetFixedSize(favCharRoleText.gameObject, 508f, 22f);

        favCharNameText = FantasyUiThemeHelper.CreateText(infoCol.transform, "FavCharName", "Chưa có dữ liệu",
            32f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.Left);
        favCharNameText.textWrappingMode = TextWrappingModes.NoWrap;
        favCharNameText.overflowMode = TextOverflowModes.Ellipsis;
        AddShadow(favCharNameText, -2f);
        MedievalSettingsArt.SetFixedSize(favCharNameText.gameObject, 508f, 40f);

        favCharTitleText = FantasyUiThemeHelper.CreateText(infoCol.transform, "FavCharTitle", "Chiến Binh Tiên Phong",
            16.5f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Left);
        favCharTitleText.textWrappingMode = TextWrappingModes.NoWrap;
        favCharTitleText.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(favCharTitleText.gameObject, 508f, 24f);

        favCharSpecsText = FantasyUiThemeHelper.CreateText(infoCol.transform, "FavCharSpecs", string.Empty,
            14.5f, FontStyles.Bold, MedievalSettingsArt.IvoryText, TextAlignmentOptions.Left);
        favCharSpecsText.textWrappingMode = TextWrappingModes.NoWrap;
        favCharSpecsText.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(favCharSpecsText.gameObject, 508f, 22f);

        favCharPerkText = FantasyUiThemeHelper.CreateText(infoCol.transform, "FavCharPerk", string.Empty,
            14f, FontStyles.Normal, MutedSlateText, TextAlignmentOptions.Left);
        favCharPerkText.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(favCharPerkText.gameObject, 508f, 38f);

        favCharMetaText = FantasyUiThemeHelper.CreateText(infoCol.transform, "FavCharMeta", "Chưa có dữ liệu trận đấu",
            15f, FontStyles.Bold, MedievalSettingsArt.IvoryText, TextAlignmentOptions.Left);
        favCharMetaText.textWrappingMode = TextWrappingModes.NoWrap;
        favCharMetaText.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(favCharMetaText.gameObject, 508f, 24f);

        GameObject div = MedievalSettingsArt.CreateSymmetricalDivider(
            body.transform, "FavDivider", 690f, 14f, MedievalSettingsArt.BronzeBorder, ornate: false);
        MedievalSettingsArt.SetFixedSize(div, 708f, 24f);

        TMP_Text k2 = FantasyUiThemeHelper.CreateText(body.transform, "FavWeaponKicker", "◆  VŨ KHÍ ƯA CHUỘNG",
            15.5f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Left);
        k2.characterSpacing = 1.8f;
        MedievalSettingsArt.SetFixedSize(k2.gameObject, 708f, 24f);

        favWeaponNameText = FantasyUiThemeHelper.CreateText(body.transform, "FavWeaponName", "Chưa mua",
            26f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.Left);
        favWeaponNameText.textWrappingMode = TextWrappingModes.NoWrap;
        favWeaponNameText.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(favWeaponNameText.gameObject, 708f, 36f);

        favWeaponMetaText = FantasyUiThemeHelper.CreateText(body.transform, "FavWeaponMeta", "Chưa ghi nhận giao dịch vũ khí",
            15.5f, FontStyles.Normal, MutedSlateText, TextAlignmentOptions.Left);
        favWeaponMetaText.textWrappingMode = TextWrappingModes.NoWrap;
        favWeaponMetaText.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(favWeaponMetaText.gameObject, 708f, 24f);
    }

    private void BuildCombatEconomyCard(Transform parent)
    {
        GameObject card = MedievalSettingsArt.CreateFramedPanel(
            parent, "CardCombatEconomy", 760f, 472f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.DarkSlateFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.BronzeBorder);

        GameObject body = new GameObject("Body", typeof(RectTransform), typeof(VerticalLayoutGroup));
        body.transform.SetParent(card.transform, false);
        RectTransform bRt = body.GetComponent<RectTransform>();
        bRt.anchorMin = Vector2.zero;
        bRt.anchorMax = Vector2.one;
        bRt.offsetMin = new Vector2(26f, 22f);
        bRt.offsetMax = new Vector2(-26f, -22f);

        VerticalLayoutGroup vlg = body.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 14f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        TMP_Text title = FantasyUiThemeHelper.CreateText(body.transform, "Header", "◆  CHIẾN ĐẤU & KINH TẾ TÍCH LŨY",
            15.5f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Left);
        title.characterSpacing = 1.8f;
        MedievalSettingsArt.SetFixedSize(title.gameObject, 708f, 28f);

        statParryVal = CreateStatLineRow(body.transform, "Phản đòn hoàn hảo (Perfect Parry)", "0 pha", MedievalSettingsArt.BrightGoldText);
        statBlockVal = CreateStatLineRow(body.transform, "Đỡ đòn thành công (Block)", "0 lần", CyanAccent);
        statGoldEarnedVal = CreateStatLineRow(body.transform, "Tổng vàng thu thập", "+0 G", MedievalSettingsArt.BrightGoldText);
        statGoldSpentVal = CreateStatLineRow(body.transform, "Tổng vàng đã chi tiêu", "-0 G", MedievalSettingsArt.IvoryText);
    }

    private static TMP_Text CreateStatLineRow(Transform parent, string label, string initialValue, Color valueColor)
    {
        GameObject row = MedievalSettingsArt.CreateFramedPanel(
            parent, "StatRow_" + label, 708f, 82f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.InnerObsidian,
            MedievalSettingsArt.BorderClean, new Color(0.38f, 0.30f, 0.18f, 0.6f));

        GameObject inner = new GameObject("Inner", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        inner.transform.SetParent(row.transform, false);
        RectTransform iRt = inner.GetComponent<RectTransform>();
        iRt.anchorMin = Vector2.zero;
        iRt.anchorMax = Vector2.one;
        iRt.offsetMin = new Vector2(20f, 8f);
        iRt.offsetMax = new Vector2(-20f, -8f);

        HorizontalLayoutGroup hlg = inner.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;

        TMP_Text lbl = FantasyUiThemeHelper.CreateText(inner.transform, "Label", label,
            18f, FontStyles.Normal, MedievalSettingsArt.IvoryText, TextAlignmentOptions.MidlineLeft);
        lbl.textWrappingMode = TextWrappingModes.NoWrap;
        lbl.overflowMode = TextOverflowModes.Ellipsis;
        MedievalSettingsArt.SetFixedSize(lbl.gameObject, 468f, 56f);

        TMP_Text val = FantasyUiThemeHelper.CreateText(inner.transform, "Value", initialValue,
            22f, FontStyles.Bold, valueColor, TextAlignmentOptions.MidlineRight);
        val.textWrappingMode = TextWrappingModes.NoWrap;
        MedievalSettingsArt.SetFixedSize(val.gameObject, 188f, 56f);
        return val;
    }

    private GameObject BuildChampionsTab(Transform parent)
    {
        GameObject page = new GameObject("Page_Champions", typeof(RectTransform));
        page.transform.SetParent(parent, false);
        FantasyUiThemeHelper.StretchFull(page.GetComponent<RectTransform>());

        CreateScrollView(page.transform, "ChampionsScroll", out championsScroll, out championsContentRt, spacing: 18f);
        return page;
    }

    private GameObject BuildMatchesTab(Transform parent)
    {
        GameObject page = new GameObject("Page_Matches", typeof(RectTransform));
        page.transform.SetParent(parent, false);
        FantasyUiThemeHelper.StretchFull(page.GetComponent<RectTransform>());

        // Header cố định 8 cột cho bảng lịch sử
        GameObject headerBar = MedievalSettingsArt.CreateFramedPanel(
            page.transform, "MatchesTableHeader", 1540f, 46f,
            MedievalSettingsArt.PanelClean, MedievalSettingsArt.FrameObsidian,
            MedievalSettingsArt.BorderClean, MedievalSettingsArt.BronzeBorder);
        RectTransform hbRt = headerBar.GetComponent<RectTransform>();
        hbRt.anchorMin = new Vector2(0f, 1f);
        hbRt.anchorMax = new Vector2(1f, 1f);
        hbRt.pivot = new Vector2(0.5f, 1f);
        hbRt.anchoredPosition = Vector2.zero;
        hbRt.sizeDelta = new Vector2(0f, 46f);

        GameObject hInner = new GameObject("HeaderColumns", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        hInner.transform.SetParent(headerBar.transform, false);
        RectTransform hiRt = hInner.GetComponent<RectTransform>();
        hiRt.anchorMin = Vector2.zero;
        hiRt.anchorMax = Vector2.one;
        hiRt.offsetMin = new Vector2(16f, 4f);
        hiRt.offsetMax = new Vector2(-28f, -4f);

        HorizontalLayoutGroup hlg = hInner.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;

        CreateMatchCellText(hInner.transform, "#ID", 84f, 14f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.MidlineLeft);
        CreateMatchCellText(hInner.transform, "MÀN CHƠI", 290f, 14f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.MidlineLeft);
        CreateMatchCellText(hInner.transform, "TƯỚNG", 222f, 14f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.MidlineLeft);
        CreateMatchCellText(hInner.transform, "KẾT QUẢ", 150f, 14f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Center);
        CreateMatchCellText(hInner.transform, "QUÁI / BOSS", 170f, 14f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Center);
        CreateMatchCellText(hInner.transform, "VÀNG / PARRY", 196f, 14f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Center);
        CreateMatchCellText(hInner.transform, "THỜI LƯỢNG", 134f, 14f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.Center);
        CreateMatchCellText(hInner.transform, "THỜI GIAN", 176f, 14f, FontStyles.Bold, MedievalSettingsArt.WarmBronzeText, TextAlignmentOptions.MidlineRight);

        GameObject scrollHolder = new GameObject("MatchesScrollHolder", typeof(RectTransform));
        scrollHolder.transform.SetParent(page.transform, false);
        RectTransform shRt = scrollHolder.GetComponent<RectTransform>();
        shRt.anchorMin = Vector2.zero;
        shRt.anchorMax = Vector2.one;
        shRt.offsetMin = Vector2.zero;
        shRt.offsetMax = new Vector2(0f, -56f);

        CreateScrollView(scrollHolder.transform, "MatchesScroll", out matchesScroll, out matchesContentRt, spacing: 10f);
        return page;
    }

    private static void CreateScrollView(
        Transform parent,
        string name,
        out ScrollRect scrollRect,
        out RectTransform contentRt,
        float spacing)
    {
        GameObject scrollGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGo.transform.SetParent(parent, false);
        FantasyUiThemeHelper.StretchFull(scrollGo.GetComponent<RectTransform>());
        Image bg = scrollGo.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.01f);
        bg.raycastTarget = true;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        RectTransform vpRt = viewport.GetComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = new Vector2(-12f, 0f);

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;

        VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = spacing;
        vlg.padding = new RectOffset(0, 4, 2, 8);
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = content.GetComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect = scrollGo.GetComponent<ScrollRect>();
        scrollRect.viewport = vpRt;
        scrollRect.content = contentRt;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 42f;
    }

    private void BuildStatusOverlay(Transform parent)
    {
        statusPanelObj = new GameObject("StatusOverlay", typeof(RectTransform), typeof(VerticalLayoutGroup));
        statusPanelObj.transform.SetParent(parent, false);
        FantasyUiThemeHelper.StretchFull(statusPanelObj.GetComponent<RectTransform>());

        VerticalLayoutGroup vlg = statusPanelObj.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 22f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;

        statusMessageText = FantasyUiThemeHelper.CreateText(statusPanelObj.transform, "StatusText",
            "Đang tải Hồ Sơ Chiến Binh...", 22f, FontStyles.Bold, MedievalSettingsArt.BrightGoldText, TextAlignmentOptions.Center);
        MedievalSettingsArt.SetFixedSize(statusMessageText.gameObject, 960f, 84f);

        retryBtn = MedievalSettingsArt.CreateFramedButton(
            statusPanelObj.transform, "BtnRetryProfile", "THỬ LẠI", 220f, 50f, 18f,
            MedievalSettingsArt.PanelBevel, MedievalSettingsArt.BronzeFill,
            MedievalSettingsArt.BorderStuds, MedievalSettingsArt.GoldOrnament,
            MedievalSettingsArt.BrightGoldText, null, OnRetryClicked);

        statusPanelObj.SetActive(false);
    }

    private void SelectTab(ProfileTab tab, bool playSound)
    {
        if (playSound) GameAudioManager.PlayUiClick();
        activeTab = tab;

        for (int i = 0; i < tabButtons.Length; i++)
        {
            bool selected = (int)tab == i;
            if (tabButtons[i] != null)
            {
                MedievalSettingsArt.UpdateFramedButtonColors(
                    tabButtons[i],
                    selected ? MedievalSettingsArt.TabActiveFill : MedievalSettingsArt.DarkSlateFill,
                    selected ? MedievalSettingsArt.HeroGoldBorder : MedievalSettingsArt.BronzeBorder,
                    selected ? MedievalSettingsArt.TitleGoldTop : MedievalSettingsArt.IvoryText);
            }

            if (tabPages[i] != null)
            {
                bool showPage = selected && (statusPanelObj == null || !statusPanelObj.activeSelf);
                tabPages[i].SetActive(showPage);
            }
        }

        if (tab == ProfileTab.Champions) RebuildScroll(championsContentRt, championsScroll);
        else if (tab == ProfileTab.Matches) RebuildScroll(matchesContentRt, matchesScroll);
    }

    private void OnRefreshClicked()
    {
        GameAudioManager.PlayUiClick();
        LoadProfileData();
    }

    private void OnRetryClicked()
    {
        GameAudioManager.PlayUiClick();
        LoadProfileData();
    }

    private void OnCloseClicked()
    {
        GameAudioManager.PlayUiClick();
        CloseInternal();
    }

    // ==================== HELPERS ====================

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Destroy(parent.GetChild(i).gameObject);
        }
    }

    private static void RebuildScroll(RectTransform contentRt, ScrollRect scroll)
    {
        if (contentRt != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);
        }
        if (scroll != null)
        {
            scroll.verticalNormalizedPosition = 1f;
        }
    }

    private static Color ParseHexColor(string hex, string charId, Color fallback)
    {
        if (!string.IsNullOrWhiteSpace(hex) && ColorUtility.TryParseHtmlString(hex.Trim(), out Color parsed))
        {
            return parsed;
        }
        if (!string.IsNullOrWhiteSpace(charId) && CharacterAccentLookup.TryGetValue(charId.Trim(), out Color mapped))
        {
            return mapped;
        }
        return fallback;
    }

    private static string FormatDurationMinutes(float totalMinutes)
    {
        if (totalMinutes <= 0.05f) return "0 phút";
        int wholeMins = Mathf.RoundToInt(totalMinutes);
        if (wholeMins < 1) return "< 1 phút";
        if (wholeMins >= 60)
        {
            int hours = wholeMins / 60;
            int mins = wholeMins % 60;
            return mins > 0 ? $"{hours}g {mins}p" : $"{hours} giờ";
        }
        return $"{totalMinutes:0.#} phút";
    }

    private static string FormatLocalDate(string isoUtc, string format)
    {
        if (string.IsNullOrWhiteSpace(isoUtc)) return string.Empty;
        if (DateTime.TryParse(isoUtc.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
        {
            if (parsed.Kind == DateTimeKind.Unspecified)
            {
                parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
            return parsed.ToLocalTime().ToString(format, CultureInfo.InvariantCulture);
        }
        return string.Empty;
    }

    private static void AddShadow(TMP_Text text, float yOffset)
    {
        Shadow sh = text.gameObject.GetComponent<Shadow>() ?? text.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
        sh.effectDistance = new Vector2(0f, yOffset);
    }
}
