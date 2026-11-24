using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Điều phối lời dẫn cốt truyện nhẹ nhàng ở đáy màn hình, hướng dẫn thao tác mượt mà theo ngữ cảnh và Sổ Tay [H]
public class StoryGuideManager : MonoBehaviour
{
    public static StoryGuideManager Instance { get; private set; }
    public static bool IsAnyModalOpen => Instance != null && (Instance.isJournalOpen || Instance.isChestModalOpen);
    public static bool IsWaitingForOpeningOrWarmup => Instance != null && Instance.waitingForOpeningOrWarmup;

    private static Sprite cachedRoundedPillSprite;
    private static Sprite cachedFeatheredWhisperSprite;

    [Serializable]
    public class StoryChapterData
    {
        public int chapterIndex;
        public string areaTag;
        public string title;
        public string subtitle;
        public string body;
        public int goldReward;
    }

    private enum OnboardingPhase
    {
        DelayBeforeWhisper,
        ShowingWhisper,
        PauseBeforeWarmup,
        WarmupMove,
        WarmupAttack,
        WarmupBlock,
        LiveGameplay
    }

    private enum ContextualTipType
    {
        None,
        LockOn,
        OpenChest,
        OpenShop,
        TravelBeacon
    }

    private class ChapterCardView
    {
        public GameObject cardObj;
        public Outline cardOutline;
        public Image accentBar;
        public TMP_Text titleText;
        public TMP_Text rewardText;
        public TMP_Text bodyText;
    }

    private int currentMapLevel = 1;
    private string currentMapName = "Rừng Orc & Mỏ Vàng";
    private Vector3 stage1Anchor;
    private StoryChapterData[] currentChapters = Array.Empty<StoryChapterData>();
    private readonly HashSet<int> unlockedChapters = new HashSet<int>();

    private bool isJournalOpen;
    private bool isChestModalOpen;
    private bool waitingForOpeningOrWarmup;
    private float modalOpenedUnscaledTime;

    // Tiến trình dẫn nhập mượt mà (trễ 2.2s sau khi vào màn rồi mới hiện từ từ)
    private OnboardingPhase onboardingPhase = OnboardingPhase.DelayBeforeWhisper;
    private float phaseTimer;
    private bool whisperSkipping;
    private float whisperAlpha;

    // Tiến trình thanh hướng dẫn thao tác sát đáy màn hình
    private float toastCurrentAlpha;
    private float toastTargetAlpha;
    private bool toastVisualCompleted;
    private float warmupProgress;
    private float warmupHoldTimer;
    private bool warmupSwappingOut;
    private bool wasPlayerAttacking;
    private Color activeToastAccent = new Color(0.95f, 0.76f, 0.28f, 1f);

    // Mẹo theo ngữ cảnh khi đang chiến đấu / thám hiểm
    private ContextualTipType activeTip = ContextualTipType.None;
    private float tipTimer;
    private float tipDuration;
    private float tipHoldTimer;
    private bool tipCompletedVisual;

    private bool hasShownLockOnTip;
    private bool hasUsedLockOn;
    private bool hasShownChestTip;
    private bool hasOpenedAnyChest;
    private bool hasShownShopTip;
    private bool hasOpenedShop;

    // Gợi ý mở rương cổ mượt mà ở đáy màn hình
    private float promptAlpha;
    private float promptCurrentY = 18f;

    private Transform playerTransform;
    private PlayerController playerController;
    private PlayerAttack playerAttack;
    private StoryChestPickup nearestChest;

    // UI 1: Lời dẫn cốt truyện mờ mịn sát đáy màn hình (không chặn thao tác)
    private GameObject whisperRoot;
    private RectTransform whisperRect;
    private CanvasGroup whisperCanvasGroup;
    private TMP_Text whisperTitleText;
    private TMP_Text whisperBodyText;
    private TMP_Text whisperSkipHintText;

    // UI 2: Thanh hướng dẫn thao tác bo tròn sát đáy màn hình
    private GameObject liveToastRoot;
    private RectTransform liveToastRect;
    private CanvasGroup liveToastCanvasGroup;
    private Outline liveToastOutline;
    private Image liveKeyBadgeBg;
    private Outline liveKeyBadgeOutline;
    private TMP_Text liveKeyBadgeText;
    private TMP_Text liveStepTagText;
    private TMP_Text liveInstructionText;
    private Image liveProgressFill;

    // UI 3: Bảng Di Vật / Mảnh Cốt Truyện khi mở rương
    private GameObject chestOverlayRoot;
    private CanvasGroup chestCanvasGroup;
    private RectTransform chestCardRect;
    private TMP_Text chestSubtitleText;
    private TMP_Text chestTitleText;
    private TMP_Text chestGoldText;
    private TMP_Text chestBodyText;
    private TMP_Text chestProgressText;

    // UI 4: Sổ Tay Hành Trình [H]
    private GameObject journalOverlayRoot;
    private CanvasGroup journalCanvasGroup;
    private RectTransform journalCardRect;
    private TMP_Text journalSubText;
    private TMP_Text journalPremiseText;
    private Button journalStoryTabBtn;
    private Button journalControlsTabBtn;
    private TMP_Text journalStoryTabBtnText;
    private GameObject journalStoryContent;
    private GameObject journalControlsContent;
    private readonly List<ChapterCardView> chapterCardViews = new List<ChapterCardView>(4);

    // UI 5: Gợi ý mở rương sát đáy màn hình
    private GameObject promptRoot;
    private RectTransform promptRect;
    private CanvasGroup promptCanvasGroup;
    private TMP_Text promptText;

    private Coroutine modalAnimRoutine;

    public static StoryGuideManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        Instance = FindFirstObjectByType<StoryGuideManager>();
        if (Instance == null)
        {
            GameObject go = new GameObject("StoryGuideManager");
            Instance = go.AddComponent<StoryGuideManager>();
        }
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // Khởi tạo dữ liệu bản đồ và để người chơi tự do quan sát trước khi lời dẫn xuất hiện từ từ
    public void InitializeForMap(int mapLevel, string mapDisplayName, Vector3 stage1CenterPos)
    {
        currentMapLevel = Mathf.Clamp(mapLevel, 1, 3);
        currentMapName = string.IsNullOrEmpty(mapDisplayName) ? $"Cứ Điểm {currentMapLevel}" : mapDisplayName;
        stage1Anchor = stage1CenterPos;
        currentChapters = GetMapChapters(currentMapLevel);
        unlockedChapters.Clear();

        waitingForOpeningOrWarmup = true;
        onboardingPhase = OnboardingPhase.DelayBeforeWhisper;
        phaseTimer = 0f;
        whisperSkipping = false;
        whisperAlpha = 0f;

        toastCurrentAlpha = 0f;
        toastTargetAlpha = 0f;
        toastVisualCompleted = false;
        warmupProgress = 0f;
        warmupHoldTimer = 0f;
        warmupSwappingOut = false;
        wasPlayerAttacking = false;

        activeTip = ContextualTipType.None;
        tipTimer = 0f;
        tipDuration = 0f;
        tipHoldTimer = 0f;
        tipCompletedVisual = false;

        hasShownLockOnTip = false;
        hasUsedLockOn = false;
        hasShownChestTip = false;
        hasOpenedAnyChest = false;
        hasShownShopTip = false;
        hasOpenedShop = false;

        promptAlpha = 0f;
        promptCurrentY = 18f;

        EnsureAllUiBuilt();
        RefreshWhisperContent();
        RefreshJournalContent();

        if (whisperRoot != null) whisperRoot.SetActive(false);
        if (liveToastRoot != null) liveToastRoot.SetActive(false);
        if (promptRoot != null) promptRoot.SetActive(false);
    }

    // Gợi ý nhẹ phím khóa mục tiêu khi đợt quái đầu tiên vừa xuất hiện
    public void NotifyFirstWaveSpawned()
    {
        if (currentMapLevel == 1 && !hasShownLockOnTip && !hasUsedLockOn && activeTip == ContextualTipType.None)
        {
            hasShownLockOnTip = true;
            ShowContextualTip(
                ContextualTipType.LockOn,
                "Q / TAB",
                "✦ MẸO CHIẾN ĐẤU",
                "Nhấn <b>[Q]</b> hoặc <b>[Chuột Giữa]</b> để khóa chặt mục tiêu trước mặt, nhấn <b>[Tab]</b> đổi sang tên khác.",
                11f);
        }
    }

    // Gợi ý đi theo cột sáng vàng sau khi dọn sạch trại đầu tiên
    public void NotifyTravelToStage2Started()
    {
        if (currentMapLevel == 1 && activeTip != ContextualTipType.OpenChest)
        {
            ShowContextualTip(
                ContextualTipType.TravelBeacon,
                "CỘT SÁNG",
                "✦ DẪN ĐƯỜNG",
                "Hướng về <b>cột sáng vàng</b> phía xa — cẩn thận quân tuần tra mai phục dọc đường!",
                9f);
        }
    }

    // Sinh rương di vật cốt truyện và vàng tại vị trí vừa đánh thắng một đợt quái
    public StoryChestPickup SpawnWaveStoryChest(int chapterIndex, Vector3 worldPos)
    {
        if (currentChapters == null || currentChapters.Length == 0)
        {
            currentChapters = GetMapChapters(currentMapLevel);
        }

        int clampedIdx = Mathf.Clamp(chapterIndex, 0, currentChapters.Length - 1);
        StoryChapterData data = currentChapters[clampedIdx];
        Vector3 finalPos = StoryChestPickup.AvoidOverlapWithExistingChests(worldPos, 3.2f);

        StoryChestPickup chest = StoryChestPickup.Spawn(
            finalPos,
            currentMapLevel,
            data.chapterIndex,
            data.title,
            data.subtitle,
            data.body,
            data.goldReward);

        if (!hasOpenedAnyChest && !hasShownChestTip)
        {
            hasShownChestTip = true;
            ShowContextualTip(
                ContextualTipType.OpenChest,
                "F / E",
                "✦ CHIẾN LỢI PHẨM",
                "Rương Cổ vừa xuất hiện! Tới gần nhấn <b>[F]</b> để nhận <b>Vàng</b> và đọc mảnh ký sự.",
                13f);
        }

        return chest;
    }

    // Ghi nhận mở khóa chương và mở bảng đọc di vật cốt truyện
    public void HandleChestOpened(StoryChestPickup chest)
    {
        if (chest == null) return;

        unlockedChapters.Add(chest.chapterIndex);
        hasOpenedAnyChest = true;

        if (activeTip == ContextualTipType.OpenChest)
        {
            DismissContextualTipSmooth();
        }

        OpenChestStoryModal(chest);
    }

    private void Update()
    {
        if (GameOverManager.IsGameOver)
        {
            ForceCloseAllModals(restoreGameplayCursor: false);
            HideWhisperImmediate();
            HideToastImmediate();
            HidePromptImmediate();
            return;
        }

        if (isChestModalOpen)
        {
            HidePromptImmediate();
            if (Time.unscaledTime - modalOpenedUnscaledTime > 0.25f)
            {
                if (Input.GetKeyDown(KeyCode.Escape)
                    || Input.GetKeyDown(KeyCode.Return)
                    || Input.GetKeyDown(KeyCode.KeypadEnter)
                    || Input.GetKeyDown(KeyCode.Space)
                    || Input.GetKeyDown(KeyCode.F))
                {
                    CloseChestStoryModal();
                }
            }
            return;
        }

        if (isJournalOpen)
        {
            HidePromptImmediate();
            if (Time.unscaledTime - modalOpenedUnscaledTime > 0.25f)
            {
                if (Input.GetKeyDown(KeyCode.Escape)
                    || Input.GetKeyDown(KeyCode.H)
                    || Input.GetKeyDown(KeyCode.F1)
                    || Input.GetKeyDown(KeyCode.Return)
                    || Input.GetKeyDown(KeyCode.KeypadEnter))
                {
                    CloseJournalModal();
                }
            }
            return;
        }

        if (WeaponShopManager.Instance != null && WeaponShopManager.Instance.IsActualShopOpen)
        {
            hasOpenedShop = true;
            if (activeTip == ContextualTipType.OpenShop)
            {
                DismissContextualTipSmooth();
            }
            HidePromptImmediate();
            return;
        }

        if (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.F1))
        {
            OpenJournalModal(showControlsTab: false);
            return;
        }

        ResolvePlayer();

        if (PlayerAttack.CurrentLockedTarget != null)
        {
            hasUsedLockOn = true;
        }

        UpdateOnboardingFlow();
        UpdateLiveToastVisuals();
        UpdateChestInteractionPrompt();
    }

    private void UpdateOnboardingFlow()
    {
        // Nếu người chơi chạy thẳng đi khám phá xa quá 22m thì kết thúc khởi động luôn
        if (waitingForOpeningOrWarmup && onboardingPhase != OnboardingPhase.DelayBeforeWhisper && playerTransform != null)
        {
            Vector3 diff = playerTransform.position - stage1Anchor;
            diff.y = 0f;
            if (diff.sqrMagnitude > 22f * 22f)
            {
                FinishOnboardingSmooth();
                return;
            }
        }

        switch (onboardingPhase)
        {
            case OnboardingPhase.DelayBeforeWhisper:
                phaseTimer += Time.deltaTime;
                if (phaseTimer >= 2.2f)
                {
                    onboardingPhase = OnboardingPhase.ShowingWhisper;
                    phaseTimer = 0f;
                    whisperSkipping = false;
                    whisperAlpha = 0f;
                    if (whisperRoot != null) whisperRoot.SetActive(true);
                }
                break;

            case OnboardingPhase.ShowingWhisper:
                phaseTimer += Time.deltaTime;

                if (!whisperSkipping && phaseTimer > 0.55f && Input.GetKeyDown(KeyCode.Space))
                {
                    whisperSkipping = true;
                }

                const float fadeInDur = 0.80f;
                const float totalHoldDur = 6.40f;
                const float fadeOutDur = 0.65f;

                if (whisperSkipping)
                {
                    whisperAlpha = Mathf.MoveTowards(whisperAlpha, 0f, Time.deltaTime / 0.42f);
                    ApplyWhisperTransformAndAlpha(whisperAlpha);
                    if (whisperAlpha <= 0.005f)
                    {
                        CompleteWhisperPhase();
                    }
                }
                else if (phaseTimer < fadeInDur)
                {
                    whisperAlpha = Mathf.SmoothStep(0f, 1f, phaseTimer / fadeInDur);
                    ApplyWhisperTransformAndAlpha(whisperAlpha);
                }
                else if (phaseTimer < totalHoldDur)
                {
                    whisperAlpha = 1f;
                    ApplyWhisperTransformAndAlpha(1f);
                }
                else
                {
                    float outT = (phaseTimer - totalHoldDur) / fadeOutDur;
                    whisperAlpha = Mathf.SmoothStep(1f, 0f, outT);
                    ApplyWhisperTransformAndAlpha(whisperAlpha);
                    if (outT >= 1f)
                    {
                        CompleteWhisperPhase();
                    }
                }
                break;

            case OnboardingPhase.PauseBeforeWarmup:
                phaseTimer += Time.deltaTime;
                if (phaseTimer >= 0.55f)
                {
                    if (currentMapLevel == 1)
                    {
                        StartWarmupMove();
                    }
                    else
                    {
                        onboardingPhase = OnboardingPhase.LiveGameplay;
                        waitingForOpeningOrWarmup = false;
                    }
                }
                break;

            case OnboardingPhase.WarmupMove:
            case OnboardingPhase.WarmupAttack:
            case OnboardingPhase.WarmupBlock:
                UpdateWarmupStep();
                break;

            case OnboardingPhase.LiveGameplay:
                UpdateContextualTips();
                break;
        }
    }

    private void CompleteWhisperPhase()
    {
        HideWhisperImmediate();
        onboardingPhase = OnboardingPhase.PauseBeforeWarmup;
        phaseTimer = 0f;
    }

    private void StartWarmupMove()
    {
        onboardingPhase = OnboardingPhase.WarmupMove;
        warmupProgress = 0f;
        toastVisualCompleted = false;
        warmupSwappingOut = false;
        ShowLiveToast(
            "W A S D",
            "● ○ ○   LÀM QUEN",
            "Thử di chuyển vài bước và xoay chuột ngắm nhìn quanh bìa rừng.",
            0f,
            new Color(0.95f, 0.78f, 0.30f, 1f));
    }

    private void UpdateWarmupStep()
    {
        if (warmupSwappingOut)
        {
            if (toastCurrentAlpha <= 0.02f)
            {
                warmupSwappingOut = false;
                AdvanceWarmupStep();
            }
            return;
        }

        if (toastVisualCompleted)
        {
            warmupHoldTimer -= Time.deltaTime;
            if (warmupHoldTimer <= 0f)
            {
                warmupSwappingOut = true;
                toastTargetAlpha = 0f;
            }
            return;
        }

        switch (onboardingPhase)
        {
            case OnboardingPhase.WarmupMove:
                if (playerController != null && playerController.HasMovementInput)
                {
                    warmupProgress = Mathf.Clamp01(warmupProgress + Time.deltaTime / 0.70f);
                }
                SetLiveProgress(warmupProgress);
                if (warmupProgress >= 1f)
                {
                    TriggerWarmupStepSuccess("Tuyệt vời! Bước di chuyển rất linh hoạt.");
                }
                break;

            case OnboardingPhase.WarmupAttack:
                bool swingStarted = (Cursor.lockState == CursorLockMode.Locked && Input.GetMouseButtonDown(0))
                    || (playerAttack != null && playerAttack.IsAttacking && !wasPlayerAttacking);
                wasPlayerAttacking = playerAttack != null && playerAttack.IsAttacking;

                if (swingStarted)
                {
                    warmupProgress = Mathf.Clamp01(warmupProgress + 0.55f);
                }
                else if (playerAttack != null && playerAttack.IsAttacking)
                {
                    warmupProgress = Mathf.Clamp01(warmupProgress + Time.deltaTime * 0.65f);
                }

                SetLiveProgress(warmupProgress);
                if (warmupProgress >= 1f)
                {
                    TriggerWarmupStepSuccess("Đường kiếm rất gọn!");
                }
                break;

            case OnboardingPhase.WarmupBlock:
                if (playerController != null && playerController.IsBlocking)
                {
                    warmupProgress = Mathf.Clamp01(warmupProgress + Time.deltaTime / 0.32f);
                }
                SetLiveProgress(warmupProgress);
                if (warmupProgress >= 1f)
                {
                    TriggerWarmupStepSuccess("Chuẩn lắm! Quân trinh sát phía trước đang tiến tới!");
                }
                break;
        }
    }

    private void TriggerWarmupStepSuccess(string msg)
    {
        toastVisualCompleted = true;
        warmupHoldTimer = 0.65f;
        GameAudioManager.PlayPickup();
        ApplyLiveCompletedVisual(msg);
    }

    private void AdvanceWarmupStep()
    {
        toastVisualCompleted = false;
        warmupProgress = 0f;
        wasPlayerAttacking = false;

        if (onboardingPhase == OnboardingPhase.WarmupMove)
        {
            onboardingPhase = OnboardingPhase.WarmupAttack;
            ShowLiveToast(
                "CHUỘT TRÁI",
                "● ● ○   CHIẾN ĐẤU",
                "Nhấp <b>Chuột Trái</b> để vung kiếm — nhấp liên tiếp để nối combo 3 đòn.",
                0f,
                new Color(0.95f, 0.78f, 0.30f, 1f));
        }
        else if (onboardingPhase == OnboardingPhase.WarmupAttack)
        {
            onboardingPhase = OnboardingPhase.WarmupBlock;
            ShowLiveToast(
                "CHUỘT PHẢI",
                "● ● ●   PHÒNG THỦ",
                "Giữ <b>Chuột Phải</b> để đỡ đòn — đỡ đúng nhịp địch chém sẽ <b>Phản Đòn (Parry)</b>!",
                0f,
                new Color(0.95f, 0.78f, 0.30f, 1f));
        }
        else
        {
            onboardingPhase = OnboardingPhase.LiveGameplay;
            waitingForOpeningOrWarmup = false;
            toastTargetAlpha = 0f;
        }
    }

    private void FinishOnboardingSmooth()
    {
        HideWhisperImmediate();
        onboardingPhase = OnboardingPhase.LiveGameplay;
        waitingForOpeningOrWarmup = false;
        warmupSwappingOut = false;
        toastVisualCompleted = false;
        toastTargetAlpha = 0f;
    }

    private void UpdateContextualTips()
    {
        if (activeTip == ContextualTipType.None)
        {
            if (!hasShownShopTip
                && !hasOpenedShop
                && ScoreManager.Instance != null
                && ScoreManager.Instance.gold >= 75)
            {
                hasShownShopTip = true;
                ShowContextualTip(
                    ContextualTipType.OpenShop,
                    "PHÍM B",
                    "✦ LÒ RÈN TIỀN ĐỒN",
                    "Đã có đủ Vàng! Nhấn <b>[B]</b> bất cứ lúc nào để rèn vũ khí mới hoặc mua Bình Máu.",
                    12f);
            }
            return;
        }

        if (tipCompletedVisual)
        {
            tipHoldTimer -= Time.deltaTime;
            if (tipHoldTimer <= 0f)
            {
                DismissContextualTipSmooth();
            }
            return;
        }

        if (activeTip == ContextualTipType.LockOn && hasUsedLockOn)
        {
            CompleteContextualTip("Đã khóa chặt mục tiêu!");
            return;
        }

        if (activeTip == ContextualTipType.TravelBeacon && StageManager.Instance != null && StageManager.Instance.CurrentStage >= 2)
        {
            DismissContextualTipSmooth();
            return;
        }

        tipTimer += Time.deltaTime;
        SetLiveProgress(tipDuration > 0f ? Mathf.Clamp01(1f - tipTimer / tipDuration) : 0f);
        if (tipTimer >= tipDuration)
        {
            DismissContextualTipSmooth();
        }
    }

    private void ShowContextualTip(ContextualTipType tipType, string keyBadge, string tag, string msg, float duration)
    {
        activeTip = tipType;
        tipTimer = 0f;
        tipDuration = Mathf.Max(4f, duration);
        tipCompletedVisual = false;
        ShowLiveToast(keyBadge, tag, msg, 1f, new Color(0.46f, 0.86f, 1f, 1f));
    }

    private void CompleteContextualTip(string msg)
    {
        tipCompletedVisual = true;
        tipHoldTimer = 0.65f;
        GameAudioManager.PlayPickup();
        ApplyLiveCompletedVisual(msg);
    }

    private void DismissContextualTipSmooth()
    {
        activeTip = ContextualTipType.None;
        tipCompletedVisual = false;
        toastTargetAlpha = 0f;
    }

    private void UpdateLiveToastVisuals()
    {
        if (liveToastRoot == null) return;

        float speed = toastTargetAlpha > toastCurrentAlpha ? 2.6f : 3.4f;
        toastCurrentAlpha = Mathf.MoveTowards(toastCurrentAlpha, toastTargetAlpha, Time.deltaTime * speed);

        bool shouldBeActive = toastCurrentAlpha > 0.005f || toastTargetAlpha > 0f;
        if (liveToastRoot.activeSelf != shouldBeActive)
        {
            liveToastRoot.SetActive(shouldBeActive);
        }

        if (!shouldBeActive) return;

        float eased = Mathf.SmoothStep(0f, 1f, toastCurrentAlpha);
        if (liveToastCanvasGroup != null)
        {
            liveToastCanvasGroup.alpha = eased;
        }

        if (liveToastRect != null)
        {
            float y = Mathf.Lerp(4f, 18f, eased);
            liveToastRect.anchoredPosition = new Vector2(0f, y);
        }

        // Nhấp nháy nhẹ viền phím tắt cho thân thiện, dễ chú ý
        if (!toastVisualCompleted && !tipCompletedVisual && liveKeyBadgeOutline != null)
        {
            float pulse = 0.62f + 0.38f * Mathf.Sin(Time.unscaledTime * 3.4f);
            liveKeyBadgeOutline.effectColor = new Color(activeToastAccent.r, activeToastAccent.g, activeToastAccent.b, pulse);
        }
    }

    private void UpdateChestInteractionPrompt()
    {
        if (promptRoot == null) return;

        nearestChest = playerTransform != null ? StoryChestPickup.GetNearestInteractable(playerTransform.position) : null;

        if (nearestChest != null)
        {
            if (promptText != null)
            {
                promptText.text = $"<color=#FFD85C><b>[F]</b></color>  Mở Rương Cổ — <color=#FFE49A>{nearestChest.chapterTitle}</color>  <color=#7BE5FF>(+{nearestChest.goldReward} Vàng)</color>";
            }

            promptAlpha = Mathf.MoveTowards(promptAlpha, 1f, Time.deltaTime * 4.5f);
            if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.E))
            {
                nearestChest.OpenChest();
                HidePromptImmediate();
                return;
            }
        }
        else
        {
            promptAlpha = Mathf.MoveTowards(promptAlpha, 0f, Time.deltaTime * 5.0f);
        }

        bool active = promptAlpha > 0.005f;
        if (promptRoot.activeSelf != active)
        {
            promptRoot.SetActive(active);
        }
        if (!active) return;

        if (promptCanvasGroup != null)
        {
            promptCanvasGroup.alpha = Mathf.SmoothStep(0f, 1f, promptAlpha);
        }

        if (promptRect != null)
        {
            // Nằm sát đáy (18px) khi đứng một mình, nhích lên (92px) chỉ khi thanh hướng dẫn cũng đang hiện
            float desiredY = toastCurrentAlpha > 0.08f ? 92f : 18f;
            promptCurrentY = Mathf.MoveTowards(promptCurrentY, desiredY, Time.deltaTime * 360f);
            promptRect.anchoredPosition = new Vector2(0f, promptCurrentY);
        }
    }

    private void ApplyWhisperTransformAndAlpha(float alpha)
    {
        if (whisperCanvasGroup != null)
        {
            whisperCanvasGroup.alpha = alpha;
        }
        if (whisperRect != null)
        {
            whisperRect.anchoredPosition = new Vector2(0f, Mathf.Lerp(4f, 18f, alpha));
        }
    }

    private void HideWhisperImmediate()
    {
        whisperAlpha = 0f;
        if (whisperRoot != null && whisperRoot.activeSelf)
        {
            whisperRoot.SetActive(false);
        }
    }

    private void HideToastImmediate()
    {
        toastCurrentAlpha = 0f;
        toastTargetAlpha = 0f;
        if (liveToastRoot != null && liveToastRoot.activeSelf)
        {
            liveToastRoot.SetActive(false);
        }
    }

    private void HidePromptImmediate()
    {
        promptAlpha = 0f;
        if (promptRoot != null && promptRoot.activeSelf)
        {
            promptRoot.SetActive(false);
        }
    }

    public void OpenJournalModal(bool showControlsTab)
    {
        if (GameOverManager.IsGameOver) return;

        EnsureAllUiBuilt();
        if (journalOverlayRoot == null) return;

        if (isChestModalOpen)
        {
            isChestModalOpen = false;
            if (chestOverlayRoot != null) chestOverlayRoot.SetActive(false);
        }

        isJournalOpen = true;
        RefreshJournalContent();
        SwitchJournalTab(showControlsTab);

        journalOverlayRoot.transform.SetAsLastSibling();
        journalOverlayRoot.SetActive(true);

        PauseGameplayForModal();
        PlayModalOpenAnimation(journalCanvasGroup, journalCardRect);
    }

    public void CloseJournalModal()
    {
        if (!isJournalOpen) return;
        isJournalOpen = false;

        if (modalAnimRoutine != null)
        {
            StopCoroutine(modalAnimRoutine);
            modalAnimRoutine = null;
        }

        if (journalOverlayRoot != null)
        {
            journalOverlayRoot.SetActive(false);
        }

        GameAudioManager.PlayUiClick();
        ResumeGameplayIfNoModal();
    }

    private void OpenChestStoryModal(StoryChestPickup chest)
    {
        if (GameOverManager.IsGameOver) return;

        EnsureAllUiBuilt();
        if (chestOverlayRoot == null) return;

        if (isJournalOpen)
        {
            isJournalOpen = false;
            if (journalOverlayRoot != null) journalOverlayRoot.SetActive(false);
        }

        isChestModalOpen = true;

        int total = currentChapters != null && currentChapters.Length > 0 ? currentChapters.Length : 4;
        if (chestSubtitleText != null) chestSubtitleText.text = $"✦ DI VẬT TRONG RƯƠNG  •  {chest.chapterSubtitle} ✦";
        if (chestTitleText != null) chestTitleText.text = chest.chapterTitle;
        if (chestGoldText != null) chestGoldText.text = $"+{chest.goldReward:N0} VÀNG";
        if (chestBodyText != null) chestBodyText.text = $"“{chest.storyBody}”";
        if (chestProgressText != null)
        {
            chestProgressText.text = $"Đã ghi lại {unlockedChapters.Count}/{total} mảnh ký sự vào Sổ Tay Hành Trình [H]";
        }

        chestOverlayRoot.transform.SetAsLastSibling();
        chestOverlayRoot.SetActive(true);

        PauseGameplayForModal();
        PlayModalOpenAnimation(chestCanvasGroup, chestCardRect);
    }

    public void CloseChestStoryModal()
    {
        if (!isChestModalOpen) return;
        isChestModalOpen = false;

        if (modalAnimRoutine != null)
        {
            StopCoroutine(modalAnimRoutine);
            modalAnimRoutine = null;
        }

        if (chestOverlayRoot != null)
        {
            chestOverlayRoot.SetActive(false);
        }

        GameAudioManager.PlayUiClick();
        ResumeGameplayIfNoModal();
    }

    private void ForceCloseAllModals(bool restoreGameplayCursor)
    {
        if (!isJournalOpen && !isChestModalOpen) return;
        isJournalOpen = false;
        isChestModalOpen = false;

        if (modalAnimRoutine != null)
        {
            StopCoroutine(modalAnimRoutine);
            modalAnimRoutine = null;
        }

        if (journalOverlayRoot != null) journalOverlayRoot.SetActive(false);
        if (chestOverlayRoot != null) chestOverlayRoot.SetActive(false);

        if (restoreGameplayCursor)
        {
            ResumeGameplayIfNoModal();
        }
    }

    private void PauseGameplayForModal()
    {
        modalOpenedUnscaledTime = Time.unscaledTime;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        GameAudioManager.PlayUiClick();
    }

    private void ResumeGameplayIfNoModal()
    {
        bool shopActualOpen = WeaponShopManager.Instance != null && WeaponShopManager.Instance.IsActualShopOpen;
        if (!GameOverManager.IsGameOver && !IsAnyModalOpen && !shopActualOpen)
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void PlayModalOpenAnimation(CanvasGroup cg, RectTransform cardRt)
    {
        if (modalAnimRoutine != null) StopCoroutine(modalAnimRoutine);
        modalAnimRoutine = StartCoroutine(AnimateModalRoutine(cg, cardRt));
    }

    private IEnumerator AnimateModalRoutine(CanvasGroup cg, RectTransform cardRt)
    {
        if (cg != null) cg.alpha = 0f;
        if (cardRt != null) cardRt.localScale = Vector3.one * 0.94f;

        float t = 0f;
        const float dur = 0.22f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            float ease = 1f - Mathf.Pow(1f - k, 3f);
            if (cg != null) cg.alpha = ease;
            if (cardRt != null) cardRt.localScale = Vector3.Lerp(Vector3.one * 0.94f, Vector3.one, ease);
            yield return null;
        }

        if (cg != null) cg.alpha = 1f;
        if (cardRt != null) cardRt.localScale = Vector3.one;
        modalAnimRoutine = null;
    }

    private void ShowLiveToast(string keyBadge, string stepTag, string instruction, float initialFill, Color accent)
    {
        EnsureAllUiBuilt();
        if (liveToastRoot == null) return;

        activeToastAccent = accent;
        if (liveToastOutline != null) liveToastOutline.effectColor = new Color(accent.r, accent.g, accent.b, 0.65f);
        if (liveKeyBadgeBg != null) liveKeyBadgeBg.color = new Color(0.10f, 0.13f, 0.21f, 0.96f);
        if (liveKeyBadgeOutline != null) liveKeyBadgeOutline.effectColor = accent;
        if (liveKeyBadgeText != null)
        {
            liveKeyBadgeText.text = keyBadge;
            liveKeyBadgeText.color = accent;
        }
        if (liveStepTagText != null)
        {
            liveStepTagText.text = stepTag;
            liveStepTagText.color = accent;
        }
        if (liveInstructionText != null)
        {
            liveInstructionText.text = instruction;
            liveInstructionText.color = new Color(0.95f, 0.96f, 0.99f, 1f);
        }
        if (liveProgressFill != null)
        {
            liveProgressFill.color = accent;
        }

        SetLiveProgress(initialFill);
        toastTargetAlpha = 1f;
        if (!liveToastRoot.activeSelf)
        {
            liveToastRoot.SetActive(true);
        }
    }

    private void ApplyLiveCompletedVisual(string doneMessage)
    {
        Color emerald = new Color(0.32f, 0.95f, 0.56f, 1f);
        if (liveToastOutline != null) liveToastOutline.effectColor = emerald;
        if (liveKeyBadgeBg != null) liveKeyBadgeBg.color = new Color(0.09f, 0.26f, 0.17f, 0.96f);
        if (liveKeyBadgeOutline != null) liveKeyBadgeOutline.effectColor = emerald;
        if (liveKeyBadgeText != null)
        {
            liveKeyBadgeText.text = "✓ XONG";
            liveKeyBadgeText.color = emerald;
        }
        if (liveInstructionText != null)
        {
            liveInstructionText.text = doneMessage;
            liveInstructionText.color = emerald;
        }
        if (liveProgressFill != null)
        {
            liveProgressFill.color = emerald;
            SetLiveProgress(1f);
        }
    }

    private void SetLiveProgress(float ratio)
    {
        if (liveProgressFill == null) return;
        RectTransform rt = liveProgressFill.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void SwitchJournalTab(bool showControlsTab)
    {
        if (journalStoryContent != null) journalStoryContent.SetActive(!showControlsTab);
        if (journalControlsContent != null) journalControlsContent.SetActive(showControlsTab);

        UpdateTabButtonVisual(journalStoryTabBtn, !showControlsTab);
        UpdateTabButtonVisual(journalControlsTabBtn, showControlsTab);

        if (!showControlsTab)
        {
            RefreshStoryCards();
        }
    }

    private static void UpdateTabButtonVisual(Button btn, bool active)
    {
        if (btn == null) return;
        Image img = btn.GetComponent<Image>();
        Outline outline = btn.GetComponent<Outline>();
        TMP_Text txt = btn.GetComponentInChildren<TMP_Text>(true);

        if (img != null) img.color = active ? FantasyUiThemeHelper.GoldPrimary : new Color(0.09f, 0.12f, 0.20f, 0.92f);
        if (outline != null) outline.effectColor = active ? FantasyUiThemeHelper.GoldBright : FantasyUiThemeHelper.GoldBorder;
        if (txt != null) txt.color = active ? FantasyUiThemeHelper.DarkButtonText : FantasyUiThemeHelper.GoldBright;
    }

    private void RefreshWhisperContent()
    {
        GetMapOpeningBeat(currentMapLevel, out string kicker, out string body);
        if (whisperTitleText != null) whisperTitleText.text = kicker;
        if (whisperBodyText != null) whisperBodyText.text = body;
        if (whisperSkipHintText != null) whisperSkipHintText.text = "[Space] Bỏ qua";
    }

    private void RefreshJournalContent()
    {
        int total = currentChapters != null ? currentChapters.Length : 4;
        if (journalSubText != null)
        {
            journalSubText.text = $"{currentMapName}  •  Đã giải mã {unlockedChapters.Count}/{total} mảnh ký sự";
        }
        if (journalPremiseText != null)
        {
            GetMapOpeningBeat(currentMapLevel, out _, out string body);
            journalPremiseText.text = body;
        }
        if (journalStoryTabBtnText != null)
        {
            journalStoryTabBtnText.text = $"NHẬT KÝ HÀNH TRÌNH ({unlockedChapters.Count}/{total})";
        }

        RefreshStoryCards();
    }

    private void RefreshStoryCards()
    {
        if (currentChapters == null) return;

        for (int i = 0; i < chapterCardViews.Count; i++)
        {
            ChapterCardView view = chapterCardViews[i];
            if (i >= currentChapters.Length)
            {
                if (view.cardObj != null) view.cardObj.SetActive(false);
                continue;
            }

            if (view.cardObj != null) view.cardObj.SetActive(true);
            StoryChapterData data = currentChapters[i];
            bool unlocked = unlockedChapters.Contains(data.chapterIndex);

            if (view.accentBar != null)
            {
                view.accentBar.color = unlocked
                    ? FantasyUiThemeHelper.GoldBright
                    : new Color(0.32f, 0.38f, 0.50f, 0.75f);
            }
            if (view.cardOutline != null)
            {
                view.cardOutline.effectColor = unlocked
                    ? FantasyUiThemeHelper.GoldPrimary
                    : new Color(0.30f, 0.36f, 0.48f, 0.45f);
            }

            if (view.titleText != null)
            {
                view.titleText.text = unlocked
                    ? $"{data.areaTag}  •  {data.title}"
                    : $"[CHƯA TÌM THẤY]  {data.areaTag}";
                view.titleText.color = unlocked
                    ? FantasyUiThemeHelper.GoldBright
                    : new Color(0.68f, 0.74f, 0.85f, 0.90f);
            }

            if (view.rewardText != null)
            {
                view.rewardText.text = unlocked ? $"+{data.goldReward} Vàng" : "Bí ẩn";
                view.rewardText.color = unlocked
                    ? new Color(0.32f, 0.94f, 0.56f, 1f)
                    : FantasyUiThemeHelper.TextMuted;
            }

            if (view.bodyText != null)
            {
                view.bodyText.text = unlocked
                    ? data.body
                    : $"Đánh tan lực lượng địch tại {data.areaTag} để mở khóa chiếc rương cổ chứa mảnh ký sự này.";
                view.bodyText.color = unlocked
                    ? new Color(0.92f, 0.94f, 0.98f, 1f)
                    : FantasyUiThemeHelper.TextMuted;
            }
        }
    }

    private void EnsureAllUiBuilt()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        if (canvas.GetComponent<GraphicRaycaster>() == null)
        {
            canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        TMP_FontAsset font = canvas.GetComponentInChildren<TMP_Text>(true)?.font;

        if (whisperRoot == null) BuildBottomWhisper(canvas.transform, font);
        if (liveToastRoot == null) BuildLiveTutorialToast(canvas.transform, font);
        if (chestOverlayRoot == null) BuildChestModal(canvas.transform, font);
        if (journalOverlayRoot == null) BuildJournalModal(canvas.transform, font);
        if (promptRoot == null) BuildInteractPrompt(canvas.transform, font);
    }

    private void BuildBottomWhisper(Transform canvasTransform, TMP_FontAsset font)
    {
        whisperRoot = new GameObject("BottomStoryWhisper", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        whisperRoot.transform.SetParent(canvasTransform, false);
        whisperRect = whisperRoot.GetComponent<RectTransform>();
        whisperRect.anchorMin = new Vector2(0.5f, 0f);
        whisperRect.anchorMax = new Vector2(0.5f, 0f);
        whisperRect.pivot = new Vector2(0.5f, 0f);
        whisperRect.anchoredPosition = new Vector2(0f, 18f);
        whisperRect.sizeDelta = new Vector2(880f, 114f);

        Image bg = whisperRoot.GetComponent<Image>();
        bg.sprite = GetFeatheredWhisperSprite();
        bg.type = Image.Type.Simple;
        bg.color = new Color(0.025f, 0.036f, 0.072f, 0.88f);
        bg.raycastTarget = false;

        whisperCanvasGroup = whisperRoot.GetComponent<CanvasGroup>();
        whisperCanvasGroup.alpha = 0f;
        whisperCanvasGroup.interactable = false;
        whisperCanvasGroup.blocksRaycasts = false;

        GameObject topRule = FantasyUiThemeHelper.CreateAccentLine(whisperRoot.transform, "WhisperTopRule",
            new Vector2(0.18f, 1f), new Vector2(0.82f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(0f, 1.5f), new Color(0.88f, 0.68f, 0.28f, 0.55f));
        topRule.GetComponent<Image>().raycastTarget = false;

        whisperTitleText = FantasyUiThemeHelper.CreateText(whisperRoot.transform, "WhisperTitle", "",
            13.5f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Center, font);
        SetTopCenteredRect(whisperTitleText.rectTransform, 0f, -10f, 760f, 22f);
        whisperTitleText.characterSpacing = 1.8f;

        whisperBodyText = FantasyUiThemeHelper.CreateText(whisperRoot.transform, "WhisperBody", "",
            15f, FontStyles.Normal, new Color(0.95f, 0.96f, 0.99f, 1f), TextAlignmentOptions.Center, font);
        SetTopCenteredRect(whisperBodyText.rectTransform, 0f, -34f, 760f, 62f);
        whisperBodyText.lineSpacing = 3.5f;

        whisperSkipHintText = FantasyUiThemeHelper.CreateText(whisperRoot.transform, "SkipHint", "[Space] Bỏ qua",
            11.5f, FontStyles.Normal, new Color(0.70f, 0.76f, 0.86f, 0.72f), TextAlignmentOptions.Right, font);
        RectTransform shRt = whisperSkipHintText.rectTransform;
        shRt.anchorMin = new Vector2(1f, 0f);
        shRt.anchorMax = new Vector2(1f, 0f);
        shRt.pivot = new Vector2(1f, 0f);
        shRt.anchoredPosition = new Vector2(-68f, 8f);
        shRt.sizeDelta = new Vector2(140f, 18f);

        whisperRoot.SetActive(false);
    }

    private void BuildLiveTutorialToast(Transform canvasTransform, TMP_FontAsset font)
    {
        liveToastRoot = new GameObject("LiveTutorialToast", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        liveToastRoot.transform.SetParent(canvasTransform, false);
        liveToastRect = liveToastRoot.GetComponent<RectTransform>();
        liveToastRect.anchorMin = new Vector2(0.5f, 0f);
        liveToastRect.anchorMax = new Vector2(0.5f, 0f);
        liveToastRect.pivot = new Vector2(0.5f, 0f);
        liveToastRect.anchoredPosition = new Vector2(0f, 18f);
        liveToastRect.sizeDelta = new Vector2(580f, 66f);

        Image bg = liveToastRoot.GetComponent<Image>();
        bg.sprite = GetRoundedPillSprite();
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.040f, 0.055f, 0.100f, 0.90f);
        bg.raycastTarget = false;

        liveToastCanvasGroup = liveToastRoot.GetComponent<CanvasGroup>();
        liveToastCanvasGroup.alpha = 0f;
        liveToastCanvasGroup.blocksRaycasts = false;
        liveToastCanvasGroup.interactable = false;

        Shadow shadow = liveToastRoot.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        shadow.effectDistance = new Vector2(0f, -4f);

        liveToastOutline = liveToastRoot.AddComponent<Outline>();
        liveToastOutline.effectColor = new Color(0.86f, 0.65f, 0.25f, 0.55f);
        liveToastOutline.effectDistance = new Vector2(1.2f, -1.2f);

        // Huy hiệu phím bo tròn bên trái
        GameObject keyBadge = new GameObject("KeyBadge", typeof(RectTransform), typeof(Image));
        keyBadge.transform.SetParent(liveToastRoot.transform, false);
        RectTransform kbRt = keyBadge.GetComponent<RectTransform>();
        kbRt.anchorMin = new Vector2(0f, 0.5f);
        kbRt.anchorMax = new Vector2(0f, 0.5f);
        kbRt.pivot = new Vector2(0f, 0.5f);
        kbRt.anchoredPosition = new Vector2(14f, 1f);
        kbRt.sizeDelta = new Vector2(118f, 38f);

        liveKeyBadgeBg = keyBadge.GetComponent<Image>();
        liveKeyBadgeBg.sprite = GetRoundedPillSprite();
        liveKeyBadgeBg.type = Image.Type.Sliced;
        liveKeyBadgeBg.color = new Color(0.10f, 0.13f, 0.21f, 0.96f);
        liveKeyBadgeBg.raycastTarget = false;

        liveKeyBadgeOutline = keyBadge.AddComponent<Outline>();
        liveKeyBadgeOutline.effectColor = FantasyUiThemeHelper.GoldBright;
        liveKeyBadgeOutline.effectDistance = new Vector2(1.1f, -1.1f);

        liveKeyBadgeText = FantasyUiThemeHelper.CreateText(keyBadge.transform, "KeyLabel", "W A S D",
            13.5f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Center, font);
        FantasyUiThemeHelper.StretchFull(liveKeyBadgeText.rectTransform);
        liveKeyBadgeText.textWrappingMode = TextWrappingModes.NoWrap;

        liveStepTagText = FantasyUiThemeHelper.CreateText(liveToastRoot.transform, "StepTag", "● ○ ○   LÀM QUEN",
            11f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Left, font);
        SetTopRect(liveStepTagText.rectTransform, 146f, -8f, 416f, 16f, 0f);
        liveStepTagText.characterSpacing = 1.2f;

        liveInstructionText = FantasyUiThemeHelper.CreateText(liveToastRoot.transform, "Instruction", "",
            13.8f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft, font);
        SetTopRect(liveInstructionText.rectTransform, 146f, -24f, 418f, 34f, 0f);

        // Thanh tiến trình mỏng gọn bên trong đáy pill
        GameObject barTrack = new GameObject("ProgressTrack", typeof(RectTransform), typeof(Image));
        barTrack.transform.SetParent(liveToastRoot.transform, false);
        RectTransform btRt = barTrack.GetComponent<RectTransform>();
        btRt.anchorMin = new Vector2(0f, 0f);
        btRt.anchorMax = new Vector2(1f, 0f);
        btRt.pivot = new Vector2(0.5f, 0f);
        btRt.anchoredPosition = new Vector2(0f, 4f);
        btRt.sizeDelta = new Vector2(-36f, 3f);
        Image trackImg = barTrack.GetComponent<Image>();
        trackImg.color = new Color(0.10f, 0.13f, 0.22f, 0.85f);
        trackImg.raycastTarget = false;

        GameObject barFill = new GameObject("ProgressFill", typeof(RectTransform), typeof(Image));
        barFill.transform.SetParent(barTrack.transform, false);
        liveProgressFill = barFill.GetComponent<Image>();
        liveProgressFill.color = FantasyUiThemeHelper.GoldBright;
        liveProgressFill.raycastTarget = false;
        SetLiveProgress(0f);

        liveToastRoot.SetActive(false);
    }

    private void BuildChestModal(Transform canvasTransform, TMP_FontAsset font)
    {
        chestOverlayRoot = new GameObject("StoryChestOverlay", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        chestOverlayRoot.transform.SetParent(canvasTransform, false);
        FantasyUiThemeHelper.StretchFull(chestOverlayRoot.GetComponent<RectTransform>());
        chestOverlayRoot.GetComponent<Image>().color = new Color(0.015f, 0.025f, 0.055f, 0.84f);
        chestCanvasGroup = chestOverlayRoot.GetComponent<CanvasGroup>();

        GameObject embersObj = new GameObject("ChestEmbers", typeof(RectTransform));
        embersObj.transform.SetParent(chestOverlayRoot.transform, false);
        FantasyUiThemeHelper.StretchFull(embersObj.GetComponent<RectTransform>());
        FloatingEmbersUI embers = embersObj.AddComponent<FloatingEmbersUI>();
        embers.Initialize(20, 0.05f, 0.95f, new Color(1f, 0.84f, 0.30f, 1f), new Color(1f, 0.55f, 0.18f, 1f));

        GameObject card = new GameObject("StoryChestCard", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(chestOverlayRoot.transform, false);
        chestCardRect = card.GetComponent<RectTransform>();
        chestCardRect.anchorMin = new Vector2(0.5f, 0.5f);
        chestCardRect.anchorMax = new Vector2(0.5f, 0.5f);
        chestCardRect.pivot = new Vector2(0.5f, 0.5f);
        chestCardRect.anchoredPosition = Vector2.zero;
        chestCardRect.sizeDelta = new Vector2(740f, 436f);

        FantasyUiThemeHelper.StyleCardPanel(card, new Color(0.055f, 0.072f, 0.122f, 0.97f), FantasyUiThemeHelper.GoldPrimary);
        Image cardImg = card.GetComponent<Image>();
        cardImg.sprite = GetRoundedPillSprite();
        cardImg.type = Image.Type.Sliced;

        FantasyUiThemeHelper.CreateAccentLine(card.transform, "TopGoldBar",
            new Vector2(0.08f, 1f), new Vector2(0.92f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(0f, 3f), FantasyUiThemeHelper.GoldBright);

        chestSubtitleText = FantasyUiThemeHelper.CreateText(card.transform, "ChestSubtitle", "",
            13f, FontStyles.Bold, new Color(0.56f, 0.84f, 1f, 1f), TextAlignmentOptions.Center, font);
        SetTopCenteredRect(chestSubtitleText.rectTransform, 0f, -20f, 660f, 22f);
        chestSubtitleText.characterSpacing = 1.8f;

        chestTitleText = FantasyUiThemeHelper.CreateText(card.transform, "ChestTitle", "",
            24f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Center, font);
        SetTopCenteredRect(chestTitleText.rectTransform, 0f, -46f, 670f, 34f);

        GameObject rewardBanner = new GameObject("GoldRewardBanner", typeof(RectTransform), typeof(Image));
        rewardBanner.transform.SetParent(card.transform, false);
        SetTopCenteredRect(rewardBanner.GetComponent<RectTransform>(), 0f, -90f, 260f, 40f);
        Image rbImg = rewardBanner.GetComponent<Image>();
        rbImg.sprite = GetRoundedPillSprite();
        rbImg.type = Image.Type.Sliced;
        rbImg.color = new Color(0.14f, 0.11f, 0.04f, 0.95f);
        Outline ro = rewardBanner.AddComponent<Outline>();
        ro.effectColor = FantasyUiThemeHelper.GoldPrimary;
        ro.effectDistance = new Vector2(1.2f, -1.2f);

        chestGoldText = FantasyUiThemeHelper.CreateText(rewardBanner.transform, "GoldText", "+100 VÀNG",
            17f, FontStyles.Bold, new Color(1f, 0.88f, 0.34f, 1f), TextAlignmentOptions.Center, font);
        FantasyUiThemeHelper.StretchFull(chestGoldText.rectTransform);

        GameObject storyBox = new GameObject("StoryBodyBox", typeof(RectTransform), typeof(Image));
        storyBox.transform.SetParent(card.transform, false);
        SetTopCenteredRect(storyBox.GetComponent<RectTransform>(), 0f, -144f, 676f, 184f);
        Image sbImg = storyBox.GetComponent<Image>();
        sbImg.sprite = GetRoundedPillSprite();
        sbImg.type = Image.Type.Sliced;
        sbImg.color = new Color(0.036f, 0.050f, 0.088f, 0.96f);
        Outline sbo = storyBox.AddComponent<Outline>();
        sbo.effectColor = new Color(0.82f, 0.62f, 0.28f, 0.40f);
        sbo.effectDistance = new Vector2(1.1f, -1.1f);

        chestBodyText = FantasyUiThemeHelper.CreateText(storyBox.transform, "StoryBodyText", "",
            16f, FontStyles.Italic, new Color(0.94f, 0.95f, 0.98f, 1f), TextAlignmentOptions.MidlineLeft, font);
        RectTransform cbtRt = chestBodyText.rectTransform;
        cbtRt.anchorMin = Vector2.zero;
        cbtRt.anchorMax = Vector2.one;
        cbtRt.offsetMin = new Vector2(24f, 14f);
        cbtRt.offsetMax = new Vector2(-24f, -14f);

        chestProgressText = FantasyUiThemeHelper.CreateText(card.transform, "ProgressText", "",
            13f, FontStyles.Normal, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.Center, font);
        SetTopCenteredRect(chestProgressText.rectTransform, 0f, -340f, 660f, 22f);

        Button continueBtn = CreateStyledButton(card.transform, "ContinueCombatBtn", "CẤT VÀO HÀNH TRANG", true, 290f, 46f, 16f, font);
        RectTransform cbRt = continueBtn.GetComponent<RectTransform>();
        cbRt.anchorMin = new Vector2(0.5f, 0f);
        cbRt.anchorMax = new Vector2(0.5f, 0f);
        cbRt.pivot = new Vector2(0.5f, 0f);
        cbRt.anchoredPosition = new Vector2(0f, 20f);
        continueBtn.onClick.AddListener(CloseChestStoryModal);

        chestOverlayRoot.SetActive(false);
    }

    private void BuildJournalModal(Transform canvasTransform, TMP_FontAsset font)
    {
        journalOverlayRoot = new GameObject("JourneyJournalOverlay", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        journalOverlayRoot.transform.SetParent(canvasTransform, false);
        FantasyUiThemeHelper.StretchFull(journalOverlayRoot.GetComponent<RectTransform>());
        journalOverlayRoot.GetComponent<Image>().color = new Color(0.015f, 0.025f, 0.055f, 0.86f);
        journalCanvasGroup = journalOverlayRoot.GetComponent<CanvasGroup>();

        GameObject card = new GameObject("JournalCard", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(journalOverlayRoot.transform, false);
        journalCardRect = card.GetComponent<RectTransform>();
        journalCardRect.anchorMin = new Vector2(0.5f, 0.5f);
        journalCardRect.anchorMax = new Vector2(0.5f, 0.5f);
        journalCardRect.pivot = new Vector2(0.5f, 0.5f);
        journalCardRect.anchoredPosition = Vector2.zero;
        journalCardRect.sizeDelta = new Vector2(920f, 620f);

        FantasyUiThemeHelper.StyleCardPanel(card, new Color(0.048f, 0.068f, 0.118f, 0.97f), FantasyUiThemeHelper.GoldBorder);
        Image jcImg = card.GetComponent<Image>();
        jcImg.sprite = GetRoundedPillSprite();
        jcImg.type = Image.Type.Sliced;

        FantasyUiThemeHelper.CreateAccentLine(card.transform, "TopGoldBar",
            new Vector2(0.08f, 1f), new Vector2(0.92f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(0f, 3f), FantasyUiThemeHelper.GoldPrimary);

        TMP_Text title = FantasyUiThemeHelper.CreateText(card.transform, "JournalTitle", "SỔ TAY HÀNH TRÌNH",
            24f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Left, font);
        SetTopRect(title.rectTransform, 30f, -16f, 650f, 32f, 0f);
        title.characterSpacing = 2.5f;

        journalSubText = FantasyUiThemeHelper.CreateText(card.transform, "JournalSub", "",
            14f, FontStyles.Normal, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.Left, font);
        SetTopRect(journalSubText.rectTransform, 30f, -48f, 650f, 22f, 0f);

        Button closeBtn = CreateStyledButton(card.transform, "CloseJournalBtn", "X", false, 44f, 44f, 16f, font);
        SetTopRect(closeBtn.GetComponent<RectTransform>(), -26f, -18f, 44f, 44f, 1f);
        closeBtn.onClick.AddListener(CloseJournalModal);

        FantasyUiThemeHelper.CreateAccentLine(card.transform, "HeaderDivider",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(-60f, 2f), FantasyUiThemeHelper.GoldBorder);

        journalStoryTabBtn = CreateStyledButton(card.transform, "TabStoryBtn", "NHẬT KÝ HÀNH TRÌNH (0/4)", true, 300f, 38f, 14.5f, font);
        SetTopRect(journalStoryTabBtn.GetComponent<RectTransform>(), 30f, -86f, 300f, 38f, 0f);
        journalStoryTabBtnText = journalStoryTabBtn.GetComponentInChildren<TMP_Text>(true);
        journalStoryTabBtn.onClick.AddListener(() => { GameAudioManager.PlayUiClick(); SwitchJournalTab(false); });

        journalControlsTabBtn = CreateStyledButton(card.transform, "TabControlsBtn", "THAO TÁC CHIẾN ĐẤU", false, 240f, 38f, 14.5f, font);
        SetTopRect(journalControlsTabBtn.GetComponent<RectTransform>(), 344f, -86f, 240f, 38f, 0f);
        journalControlsTabBtn.onClick.AddListener(() => { GameAudioManager.PlayUiClick(); SwitchJournalTab(true); });

        GameObject bodyArea = new GameObject("JournalBody", typeof(RectTransform));
        bodyArea.transform.SetParent(card.transform, false);
        RectTransform bodyRt = bodyArea.GetComponent<RectTransform>();
        bodyRt.anchorMin = Vector2.zero;
        bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = new Vector2(30f, 68f);
        bodyRt.offsetMax = new Vector2(-30f, -136f);

        BuildJournalStoryTab(bodyArea.transform, font);
        BuildJournalControlsTab(bodyArea.transform, font);

        Button resumeBtn = CreateStyledButton(card.transform, "ResumeBtn", "QUAY LẠI TRẬN ĐẤU  [H]", true, 270f, 42f, 15.5f, font);
        RectTransform rRt = resumeBtn.GetComponent<RectTransform>();
        rRt.anchorMin = new Vector2(0.5f, 0f);
        rRt.anchorMax = new Vector2(0.5f, 0f);
        rRt.pivot = new Vector2(0.5f, 0f);
        rRt.anchoredPosition = new Vector2(0f, 14f);
        resumeBtn.onClick.AddListener(CloseJournalModal);

        journalOverlayRoot.SetActive(false);
    }

    private void BuildJournalStoryTab(Transform parent, TMP_FontAsset font)
    {
        journalStoryContent = new GameObject("JournalStoryContent", typeof(RectTransform));
        journalStoryContent.transform.SetParent(parent, false);
        FantasyUiThemeHelper.StretchFull(journalStoryContent.GetComponent<RectTransform>());

        GameObject premiseBox = CreateInsetBox(journalStoryContent.transform, "PremiseBox", 0f, 76f, FantasyUiThemeHelper.GoldBorder);
        journalPremiseText = FantasyUiThemeHelper.CreateText(premiseBox.transform, "PremiseText", "",
            13.5f, FontStyles.Italic, new Color(0.92f, 0.94f, 0.98f, 1f), TextAlignmentOptions.MidlineLeft, font);
        RectTransform ptRt = journalPremiseText.rectTransform;
        ptRt.anchorMin = Vector2.zero;
        ptRt.anchorMax = Vector2.one;
        ptRt.offsetMin = new Vector2(16f, 6f);
        ptRt.offsetMax = new Vector2(-16f, -6f);

        chapterCardViews.Clear();
        const float cardHeight = 76f;
        const float spacing = 8f;

        for (int i = 0; i < 4; i++)
        {
            float topY = -86f - i * (cardHeight + spacing);
            GameObject cObj = CreateInsetBox(journalStoryContent.transform, $"ChapterCard_{i}", topY, cardHeight, FantasyUiThemeHelper.GoldBorder);
            Outline outline = cObj.GetComponent<Outline>();

            GameObject bar = FantasyUiThemeHelper.CreateAccentLine(cObj.transform, "LeftAccent",
                new Vector2(0f, 0.14f), new Vector2(0f, 0.86f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(3.5f, 0f), FantasyUiThemeHelper.GoldPrimary);

            TMP_Text cTitle = FantasyUiThemeHelper.CreateText(cObj.transform, "Title", "",
                14f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Left, font);
            SetTopRect(cTitle.rectTransform, 18f, -6f, 636f, 20f, 0f);

            TMP_Text cReward = FantasyUiThemeHelper.CreateText(cObj.transform, "Reward", "",
                13f, FontStyles.Bold, new Color(0.32f, 0.94f, 0.56f, 1f), TextAlignmentOptions.Right, font);
            SetTopRect(cReward.rectTransform, -14f, -6f, 180f, 20f, 1f);

            TMP_Text cBody = FantasyUiThemeHelper.CreateText(cObj.transform, "Body", "",
                12.8f, FontStyles.Normal, new Color(0.90f, 0.93f, 0.98f, 1f), TextAlignmentOptions.TopLeft, font);
            SetTopRect(cBody.rectTransform, 18f, -28f, 826f, 44f, 0f);

            chapterCardViews.Add(new ChapterCardView
            {
                cardObj = cObj,
                cardOutline = outline,
                accentBar = bar.GetComponent<Image>(),
                titleText = cTitle,
                rewardText = cReward,
                bodyText = cBody
            });
        }
    }

    private void BuildJournalControlsTab(Transform parent, TMP_FontAsset font)
    {
        journalControlsContent = new GameObject("JournalControlsContent", typeof(RectTransform));
        journalControlsContent.transform.SetParent(parent, false);
        FantasyUiThemeHelper.StretchFull(journalControlsContent.GetComponent<RectTransform>());

        (string key, string label, string desc)[] items =
        {
            ("W A S D", "Di chuyển", "Điều khiển hướng chạy kết hợp xoay chuột để quan sát"),
            ("CHUỘT TRÁI", "Tấn công", "Vung vũ khí theo chuỗi combo 3 đòn liên hoàn"),
            ("GIỮ CHUỘT PHẢI", "Đỡ & Phản đòn", "Giảm sát thương; giơ khiên đúng lúc địch chém để làm choáng và hồi máu"),
            ("Q / CHUỘT GIỮA", "Khóa mục tiêu", "Giữ góc nhìn hướng vào một kẻ địch, nhấn [Tab] để đổi mục tiêu"),
            ("F / E", "Mở rương", "Mở Rương Cổ xuất hiện sau giao tranh để lấy Vàng và mảnh ký sự"),
            ("B / E", "Lò rèn & Lửa trại", "Rèn vũ khí mạnh hơn và mua Bình Máu ([E] khi đứng cạnh lửa trại)"),
            ("H / M", "Sổ tay & Âm thanh", "Nhấn [H] mở Sổ Tay Hành Trình; nhấn [M] bật hoặc tắt nhạc nền & hiệu ứng")
        };

        const float rowH = 52f;
        const float rowGap = 7f;
        for (int i = 0; i < items.Length; i++)
        {
            float topY = -i * (rowH + rowGap);
            GameObject row = CreateInsetBox(journalControlsContent.transform, $"CtrlRow_{i}", topY, rowH, new Color(0.82f, 0.62f, 0.28f, 0.32f));

            GameObject keyPill = new GameObject("KeyPill", typeof(RectTransform), typeof(Image));
            keyPill.transform.SetParent(row.transform, false);
            RectTransform kpRt = keyPill.GetComponent<RectTransform>();
            kpRt.anchorMin = new Vector2(0f, 0.5f);
            kpRt.anchorMax = new Vector2(0f, 0.5f);
            kpRt.pivot = new Vector2(0f, 0.5f);
            kpRt.anchoredPosition = new Vector2(12f, 0f);
            kpRt.sizeDelta = new Vector2(160f, 34f);

            Image kpImg = keyPill.GetComponent<Image>();
            kpImg.sprite = GetRoundedPillSprite();
            kpImg.type = Image.Type.Sliced;
            kpImg.color = new Color(0.11f, 0.14f, 0.22f, 1f);

            Outline kpo = keyPill.AddComponent<Outline>();
            kpo.effectColor = FantasyUiThemeHelper.GoldPrimary;
            kpo.effectDistance = new Vector2(1.0f, -1.0f);

            TMP_Text keyTxt = FantasyUiThemeHelper.CreateText(keyPill.transform, "Key", items[i].key,
                13.5f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Center, font);
            FantasyUiThemeHelper.StretchFull(keyTxt.rectTransform);

            TMP_Text nameTxt = FantasyUiThemeHelper.CreateText(row.transform, "Label", items[i].label,
                15f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, font);
            SetTopRect(nameTxt.rectTransform, 188f, -11f, 170f, 30f, 0f);

            TMP_Text descTxt = FantasyUiThemeHelper.CreateText(row.transform, "Desc", items[i].desc,
                13.8f, FontStyles.Normal, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.MidlineLeft, font);
            SetTopRect(descTxt.rectTransform, 364f, -11f, 480f, 30f, 0f);
        }

        journalControlsContent.SetActive(false);
    }

    private void BuildInteractPrompt(Transform canvasTransform, TMP_FontAsset font)
    {
        promptRoot = new GameObject("StoryChestInteractPrompt", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        promptRoot.transform.SetParent(canvasTransform, false);
        promptRect = promptRoot.GetComponent<RectTransform>();
        promptRect.anchorMin = new Vector2(0.5f, 0f);
        promptRect.anchorMax = new Vector2(0.5f, 0f);
        promptRect.pivot = new Vector2(0.5f, 0f);
        promptRect.anchoredPosition = new Vector2(0f, 18f);
        promptRect.sizeDelta = new Vector2(540f, 44f);

        Image bg = promptRoot.GetComponent<Image>();
        bg.sprite = GetRoundedPillSprite();
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.042f, 0.060f, 0.110f, 0.92f);
        bg.raycastTarget = false;

        promptCanvasGroup = promptRoot.GetComponent<CanvasGroup>();
        promptCanvasGroup.alpha = 0f;
        promptCanvasGroup.blocksRaycasts = false;
        promptCanvasGroup.interactable = false;

        Outline outline = promptRoot.AddComponent<Outline>();
        outline.effectColor = FantasyUiThemeHelper.GoldPrimary;
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        promptText = FantasyUiThemeHelper.CreateText(promptRoot.transform, "PromptText", "",
            15f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font);
        FantasyUiThemeHelper.StretchFull(promptText.rectTransform);
        promptText.textWrappingMode = TextWrappingModes.NoWrap;

        promptRoot.SetActive(false);
    }

    private static GameObject CreateInsetBox(Transform parent, string name, float topY, float height, Color borderCol)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, topY);
        rt.sizeDelta = new Vector2(0f, height);

        Image img = go.GetComponent<Image>();
        img.sprite = GetRoundedPillSprite();
        img.type = Image.Type.Sliced;
        img.color = new Color(0.068f, 0.092f, 0.155f, 0.94f);

        Outline o = go.AddComponent<Outline>();
        o.effectColor = borderCol;
        o.effectDistance = new Vector2(1.1f, -1.1f);
        return go;
    }

    private static Button CreateStyledButton(Transform parent, string name, string label, bool isPrimary, float w, float h, float fontSize, TMP_FontAsset font)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Button btn = go.GetComponent<Button>();

        TMP_Text txt = FantasyUiThemeHelper.CreateText(go.transform, "Text", label, fontSize, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font);
        FantasyUiThemeHelper.StretchFull(txt.rectTransform);
        FantasyUiThemeHelper.StyleButton(btn, label, isPrimary, w, h);
        txt.fontSize = fontSize;

        Image img = go.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = GetRoundedPillSprite();
            img.type = Image.Type.Sliced;
        }
        return btn;
    }

    private static Sprite GetRoundedPillSprite()
    {
        if (cachedRoundedPillSprite != null) return cachedRoundedPillSprite;

        const int size = 64;
        const float radius = 15f;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "StoryRoundedPillTex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[size * size];
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            float py = (y + 0.5f) - half;
            float vy = (y + 0.5f) / size;
            float glassSheen = Mathf.Lerp(0.86f, 1.0f, vy) + (vy > 0.78f ? 0.05f : 0f);
            float lum = Mathf.Clamp01(glassSheen);

            for (int x = 0; x < size; x++)
            {
                float px = (x + 0.5f) - half;
                float dx = Mathf.Abs(px) - (half - radius - 1f);
                float dy = Mathf.Abs(py) - (half - radius - 1f);
                float outX = Mathf.Max(dx, 0f);
                float outY = Mathf.Max(dy, 0f);
                float dist = Mathf.Sqrt(outX * outX + outY * outY) + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                float alpha = Mathf.Clamp01(0.5f - dist / 1.25f);
                pixels[y * size + x] = new Color(lum, lum, lum, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);
        cachedRoundedPillSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(18f, 18f, 18f, 18f));
        return cachedRoundedPillSprite;
    }

    private static Sprite GetFeatheredWhisperSprite()
    {
        if (cachedFeatheredWhisperSprite != null) return cachedFeatheredWhisperSprite;

        const int w = 128;
        const int h = 32;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = "StoryWhisperFeatheredTex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            float edgeY = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.5f - Mathf.Abs(v - 0.5f)) / 0.22f));
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float edgeX = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.5f - Mathf.Abs(u - 0.5f)) / 0.24f));
                pixels[y * w + x] = new Color(1f, 1f, 1f, edgeX * edgeY);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);
        cachedFeatheredWhisperSprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f));
        return cachedFeatheredWhisperSprite;
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

    private void ResolvePlayer()
    {
        if (playerTransform == null)
        {
            if (PlayerController.Instance != null)
            {
                playerTransform = PlayerController.Instance.transform;
            }
            else
            {
                GameObject p = GameObject.FindWithTag("Player");
                if (p != null) playerTransform = p.transform;
            }
        }

        if (playerTransform != null)
        {
            if (playerController == null) playerController = PlayerController.Instance ?? playerTransform.GetComponent<PlayerController>();
            if (playerAttack == null) playerAttack = playerTransform.GetComponent<PlayerAttack>();
        }
    }

    private static void GetMapOpeningBeat(int mapLevel, out string kicker, out string body)
    {
        switch (mapLevel)
        {
            case 2:
                kicker = "✦ CHƯƠNG II  •  CAO NGUYÊN BĂNG TUYẾT ✦";
                body = "Những bức mật thư từ Mỏ Vàng dẫn bước bạn tới Cao Nguyên Băng Tuyết. Quái Thú Frost Colossus đang phong ấn con đường huyết mạch về sào huyệt địch — hãy phá vỡ phong ấn!";
                break;

            case 3:
                kicker = "✦ CHƯƠNG III  •  PHÁO ĐÀI DUNG NHAM ✦";
                body = "Trước mắt là Ngai Vàng Dung Nham, nơi Chúa Tể Inferno Overlord điều khiển đạo quân xâm lăng. Hãy nghiền nát Quân Đoàn Tro Tàn và hạ gục Overlord để giải phóng Vương quốc Vanguard!";
                break;

            default:
                kicker = "✦ CHƯƠNG I  •  RỪNG ORC & MỎ VÀNG ✦";
                body = "Đoàn quân Orc từ phương Bắc đã chiếm Mỏ Vàng cổ và cắt đứt liên lạc với đội tuần tra. Bạn được cử đi đột phá bìa rừng, tìm dấu vết đồng đội và hạ gục Đại Tướng Orc Warlord.";
                break;
        }
    }

    private static StoryChapterData[] GetMapChapters(int mapLevel)
    {
        switch (mapLevel)
        {
            case 2:
                return new[]
                {
                    new StoryChapterData
                    {
                        chapterIndex = 0,
                        areaTag = "Trại Băng Ngoại Vi",
                        title = "Cuốn Sổ Tay Đông Cứng",
                        subtitle = "Chương II • Mảnh 1/4",
                        body = "Ngày thứ tư tuần tra: Chỉ sau một đêm, luồng hàn khí từ đỉnh Cực Quang bỗng tràn xuống đóng băng cả con suối. Lũ quái vật quanh đây không còn sợ lửa nữa — chúng như đang nghe theo một tiếng gọi từ trên đỉnh núi.",
                        goldReward = 95
                    },
                    new StoryChapterData
                    {
                        chapterIndex = 1,
                        areaTag = "Doanh Trại Tuyết",
                        title = "Ấn Ký Cổ Ngữ Băng",
                        subtitle = "Chương II • Mảnh 2/4",
                        body = "Trên giáp vai của tên thủ lĩnh băng có khắc biểu tượng của Frost Colossus. Những cỗ xe chở đầy vàng cướp được từ rừng sâu vẫn đang nối đuôi nhau đi lên vùng hồ đóng băng.",
                        goldReward = 115
                    },
                    new StoryChapterData
                    {
                        chapterIndex = 2,
                        areaTag = "Hồ Băng Tử Thần",
                        title = "Bức Tường Băng Vĩnh Cửu",
                        subtitle = "Chương II • Mảnh 3/4",
                        body = "Bọn lính gác giữ kín tấm bản đồ vẽ khe núi dẫn thẳng tới Pháo Đài Dung Nham. Cánh cổng đá khổng lồ phía trước bị đóng chặt bởi tầng băng dày không một lưỡi kiếm thường nào chém xuyên nổi.",
                        goldReward = 135
                    },
                    new StoryChapterData
                    {
                        chapterIndex = 3,
                        areaTag = "Cửa Ngõ Cực Quang",
                        title = "Tiếng Gầm Dưới Cực Quang",
                        subtitle = "Chương II • Mảnh 4/4",
                        body = "Hàng phòng ngự cuối cùng của cao nguyên đã vỡ tan. Từ Đấu Trường Cực Quang phía trước, mặt đất rung chuyển theo từng bước chân nặng nề của Quái Thú Frost Colossus.",
                        goldReward = 165
                    }
                };

            case 3:
                return new[]
                {
                    new StoryChapterData
                    {
                        chapterIndex = 0,
                        areaTag = "Cổng Pháo Đài Tro Tàn",
                        title = "Bức Thư Chưa Kịp Gửi",
                        subtitle = "Chương III • Mảnh 1/4",
                        body = "Thưa Chỉ huy, nếu ngài đọc được những dòng này thì Cổng Tro Tàn đã thất thủ. Khe nứt dung nham dưới lòng đất không tự nhiên mở ra — chính thứ cổ vật mà lũ Orc đào được đã đánh thức nó.",
                        goldReward = 110
                    },
                    new StoryChapterData
                    {
                        chapterIndex = 1,
                        areaTag = "Sân Ngoài Pháo Đài",
                        title = "Lò Luyện Giáp Hắc Ám",
                        subtitle = "Chương III • Mảnh 2/4",
                        body = "Dòng dung nham nóng chảy được dẫn thẳng vào các bễ rèn khổng lồ. Toàn bộ vàng và quặng linh lực cướp bóc từ các vùng đất ngoài đều hóa thành giáp trụ cho Quân Đoàn Tro Tàn.",
                        goldReward = 130
                    },
                    new StoryChapterData
                    {
                        chapterIndex = 2,
                        areaTag = "Hầm Ngục Chậu Lửa",
                        title = "Bia Đá Cổ Trong Hầm Ngục",
                        subtitle = "Chương III • Mảnh 3/4",
                        body = "Khắc trên phiến đá đen cháy xém: Khi cơn thịnh nộ của Inferno Overlord bùng phát, hắn sẽ triệu hồi cận vệ từ tro tàn — nhưng chính những đòn giáng mạnh nhất của hắn lại để lộ sơ hở chết người nếu bị gạt trả đúng lúc.",
                        goldReward = 150
                    },
                    new StoryChapterData
                    {
                        chapterIndex = 3,
                        areaTag = "Hành Lang Ngai Vàng",
                        title = "Cánh Cổng Cuối Cùng",
                        subtitle = "Chương III • Mảnh 4/4",
                        body = "Những tên cận vệ trung thành nhất của Huyết Ngục đã gục ngã. Phía sau cánh cổng sắt rực lửa là Ngai Vàng Dung Nham — nơi định đoạt số phận của cả Vương quốc Vanguard.",
                        goldReward = 185
                    }
                };

            default:
                return new[]
                {
                    new StoryChapterData
                    {
                        chapterIndex = 0,
                        areaTag = "Trại Rừng Ngoại Vi",
                        title = "Nhật Ký Trinh Sát Bị Bỏ Lại",
                        subtitle = "Chương I • Mảnh 1/4",
                        body = "Tờ giấy da nhàu nát còn vương vết bùn: 'Lũ Orc không chỉ cướp bóc làng mạc. Đêm nào chúng cũng áp giải tù binh vào sâu trong Mỏ Vàng cổ để đào thứ gì đó phát sáng dưới lớp đá.'",
                        goldReward = 80
                    },
                    new StoryChapterData
                    {
                        chapterIndex = 1,
                        areaTag = "Đồi Đóng Quân",
                        title = "Mật Lệnh Đóng Dấu Đỏ",
                        subtitle = "Chương I • Mảnh 2/4",
                        body = "Mật thư giấu trong giáp của tên đội trưởng Orc hé lộ: Đại Tướng Warlord đang gấp rút gom vàng chuyển lên Cao Nguyên Băng Tuyết cho một thế lực bí ẩn đứng sau cuộc xâm lăng.",
                        goldReward = 100
                    },
                    new StoryChapterData
                    {
                        chapterIndex = 2,
                        areaTag = "Thung Lũng Bẫy",
                        title = "Sơ Đồ Bẫy Gai Thung Lũng",
                        subtitle = "Chương I • Mảnh 3/4",
                        body = "Quân lính khiên Orc được lệnh rải đầy bẫy sắt dọc thung lũng nhằm cầm chân kỵ binh Vanguard, mua thời gian cho Đại Tướng Warlord hoàn tất việc khai quật bên trong hầm mỏ.",
                        goldReward = 120
                    },
                    new StoryChapterData
                    {
                        chapterIndex = 3,
                        areaTag = "Cửa Mỏ Vàng",
                        title = "Chiếc Chìa Khóa Mỏ Vàng",
                        subtitle = "Chương I • Mảnh 4/4",
                        body = "Tuyến phòng thủ cuối cùng ở Thung Lũng Bẫy đã sụp đổ! Đại Tướng Orc Warlord đang đích thân canh giữ số vàng cướp được tại Đấu Trường Mỏ Vàng ngay phía trước.",
                        goldReward = 150
                    }
                };
        }
    }
}
