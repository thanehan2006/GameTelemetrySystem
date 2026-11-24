using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Bộ công cụ trang trí giao diện chiến đấu HUD (thanh máu, vàng, nhiệm vụ, Boss và banner) theo phong cách Dark-Fantasy
public static class CombatHudStyler
{
    public static readonly Color ObsidianDeep = new Color(0.055f, 0.070f, 0.115f, 0.91f);
    public static readonly Color CrimsonObsidian = new Color(0.105f, 0.028f, 0.045f, 0.93f);
    public static readonly Color GoldTrim = new Color(0.86f, 0.65f, 0.25f, 0.84f);
    public static readonly Color GoldBright = new Color(1.00f, 0.87f, 0.48f, 1f);
    public static readonly Color GoldDivider = new Color(0.86f, 0.65f, 0.25f, 0.42f);

    public static readonly Color HpEmeraldHealthy = new Color(0.16f, 0.86f, 0.44f, 1f);
    public static readonly Color HpAmberWarning = new Color(0.98f, 0.72f, 0.16f, 1f);
    public static readonly Color HpScarletCritical = new Color(0.98f, 0.22f, 0.22f, 1f);
    public static readonly Color HpGhostTrail = new Color(1.00f, 0.88f, 0.56f, 0.90f);

    public static readonly Color BossFillNormal = new Color(0.86f, 0.14f, 0.22f, 1f);
    public static readonly Color BossFillEnraged = new Color(1.00f, 0.28f, 0.10f, 1f);

    private static Sprite cachedBeveledPlateSprite;
    private static Sprite cachedMetallicBarSprite;
    private static Sprite cachedBannerVignetteSprite;
    private static Sprite cachedGemDiamondSprite;

    // Trang trí thanh máu người chơi cùng huy hiệu HP, thanh sát thương trễ và viền vàng
    public static void StylePlayerHealthBar(
        Slider healthBar,
        TMP_Text healthText,
        out Image mainFillImage,
        out Image ghostFillImage,
        out Image sheenImage,
        out Image crestGemImage)
    {
        mainFillImage = null;
        ghostFillImage = null;
        sheenImage = null;
        crestGemImage = null;
        if (healthBar == null) return;

        healthBar.interactable = false;
        healthBar.transition = Selectable.Transition.None;
        healthBar.direction = Slider.Direction.LeftToRight;

        RectTransform rootRt = healthBar.GetComponent<RectTransform>();
        if (rootRt != null)
        {
            rootRt.anchorMin = new Vector2(1f, 1f);
            rootRt.anchorMax = new Vector2(1f, 1f);
            rootRt.pivot = new Vector2(1f, 1f);
            rootRt.anchoredPosition = new Vector2(-22f, -20f);
            rootRt.sizeDelta = new Vector2(364f, 42f);
        }

        HideChildIfExists(healthBar.transform, "Handle Slide Area");

        Transform bgT = healthBar.transform.Find("Background");
        Image bgImg = bgT != null
            ? GetOrAddComponent<Image>(bgT.gameObject)
            : GetOrAddComponent<Image>(healthBar.gameObject);
        if (bgT != null)
        {
            bgT.SetSiblingIndex(0);
            RectTransform bgRt = bgT.GetComponent<RectTransform>();
            StretchRect(bgRt, 0f, 0f, 0f, 0f);
        }

        ApplyPlateVisual(bgImg, ObsidianDeep, GoldTrim, 1.6f, new Vector2(0f, -5f));

        const float crestWidth = 46f;
        const float padRight = 8f;
        const float padV = 6f;

        GameObject troughGo = EnsureChildPanel(healthBar.transform, "HudHpTrough", new Color(0.035f, 0.068f, 0.062f, 0.96f), out Image troughImg);
        troughGo.transform.SetSiblingIndex(bgT != null ? 1 : 0);
        RectTransform troughRt = troughGo.GetComponent<RectTransform>();
        StretchRect(troughRt, crestWidth, padV, padRight, padV);
        Outline troughOutline = EnsureOutline(troughGo, new Color(0.62f, 0.45f, 0.18f, 0.58f), 1.1f);
        if (troughOutline != null) troughOutline.enabled = true;
        if (troughImg != null && Application.isPlaying) troughImg.sprite = null;

        GameObject crestGo = EnsureChildPanel(healthBar.transform, "HudHpCrest", new Color(0f, 0f, 0f, 0f), out Image crestImg);
        crestImg.enabled = false;
        RectTransform crestRt = crestGo.GetComponent<RectTransform>();
        crestRt.anchorMin = new Vector2(0f, 0.5f);
        crestRt.anchorMax = new Vector2(0f, 0.5f);
        crestRt.pivot = new Vector2(0.5f, 0.5f);
        crestRt.anchoredPosition = new Vector2(24f, 0f);
        crestRt.sizeDelta = new Vector2(34f, 34f);

        crestGemImage = EnsureDiamondGem(crestGo.transform, "HudHpCrestGem", Vector2.zero, 21f, HpEmeraldHealthy, GoldBright);
        TMP_Text crestLabel = EnsureOrFindText(crestGo.transform, "HudHpBadgeText", "HP", 11.5f, FontStyles.Bold, new Color(1f, 0.96f, 0.86f, 1f), TextAlignmentOptions.Center, healthText != null ? healthText.font : null);
        if (crestLabel != null)
        {
            StretchRect(crestLabel.rectTransform, 0f, 0f, 0f, 0f);
            crestLabel.textWrappingMode = TextWrappingModes.NoWrap;
            EnsureDropShadow(crestLabel.gameObject, new Color(0f, 0f, 0f, 0.95f), new Vector2(1f, -1.2f));
        }

        Transform fillAreaT = healthBar.transform.Find("Fill Area");
        if (fillAreaT == null)
        {
            GameObject fa = new GameObject("Fill Area", typeof(RectTransform));
            fa.transform.SetParent(healthBar.transform, false);
            fillAreaT = fa.transform;
        }
        fillAreaT.gameObject.SetActive(true);
        fillAreaT.SetAsLastSibling();
        RectTransform fillAreaRt = fillAreaT.GetComponent<RectTransform>();
        StretchRect(fillAreaRt, crestWidth + 2f, padV + 2f, padRight + 2f, padV + 2f);

        GameObject ghostGo = EnsureChildPanel(fillAreaT, "HudGhostFill", HpGhostTrail, out ghostFillImage);
        StretchRect(ghostGo.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
        ghostGo.transform.SetAsFirstSibling();
        ApplyBarSprite(ghostFillImage);

        RectTransform fillRt = healthBar.fillRect;
        if (fillRt == null)
        {
            Transform existingFill = fillAreaT.Find("Fill");
            if (existingFill != null) fillRt = existingFill.GetComponent<RectTransform>();
        }
        if (fillRt != null)
        {
            if (fillRt.parent != fillAreaT) fillRt.SetParent(fillAreaT, false);
            fillRt.gameObject.SetActive(true);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            fillRt.anchorMin = new Vector2(fillRt.anchorMin.x, 0f);
            fillRt.anchorMax = new Vector2(fillRt.anchorMax.x, 1f);
            healthBar.fillRect = fillRt;

            mainFillImage = GetOrAddComponent<Image>(fillRt.gameObject);
            mainFillImage.enabled = true;
            mainFillImage.raycastTarget = false;
            ApplyBarSprite(mainFillImage);
            mainFillImage.color = HpEmeraldHealthy;
            fillRt.SetSiblingIndex(1);
        }

        GameObject sheenGo = EnsureChildPanel(fillAreaT, "HudBarTopSheen", new Color(1f, 1f, 1f, 0.18f), out sheenImage);
        RectTransform sheenRt = sheenGo.GetComponent<RectTransform>();
        sheenRt.anchorMin = new Vector2(0f, 0.56f);
        sheenRt.anchorMax = new Vector2(1f, 1f);
        sheenRt.offsetMin = Vector2.zero;
        sheenRt.offsetMax = Vector2.zero;
        sheenGo.transform.SetAsLastSibling();

        crestGo.transform.SetAsLastSibling();

        if (healthText != null)
        {
            healthText.transform.SetAsLastSibling();
            StretchRect(healthText.rectTransform, crestWidth + 4f, 0f, padRight + 4f, 0f);
            ApplyFantasyHeadingText(
                healthText, 17f, FontStyles.Bold, TextAlignmentOptions.Center,
                new Color(1f, 0.99f, 0.95f, 1f), new Color(0.95f, 0.85f, 0.66f, 1f),
                1.4f, true);
        }
    }

    // Chuyển thanh Exp cũ thành thẻ hiển thị Vàng và phím tắt Cửa hàng đồng bộ với HUD
    public static void StyleGoldHud(
        GameObject expBarObj,
        TMP_Text existingScoreText,
        out TMP_Text goldText,
        out RectTransform rootRect,
        out Image gemImage)
    {
        goldText = existingScoreText;
        rootRect = null;
        gemImage = null;
        if (expBarObj == null) return;

        rootRect = expBarObj.GetComponent<RectTransform>();
        if (rootRect != null)
        {
            rootRect.anchorMin = new Vector2(1f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.anchoredPosition = new Vector2(-22f, -70f);
            rootRect.sizeDelta = new Vector2(364f, 34f);
        }

        Slider slider = expBarObj.GetComponent<Slider>();
        if (slider != null)
        {
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0f;
            if (slider.fillRect != null) slider.fillRect.gameObject.SetActive(false);
        }

        HideChildIfExists(expBarObj.transform, "Fill Area");
        HideChildIfExists(expBarObj.transform, "Handle Slide Area");

        Transform bgT = expBarObj.transform.Find("Background");
        Image bgImg = bgT != null
            ? GetOrAddComponent<Image>(bgT.gameObject)
            : GetOrAddComponent<Image>(expBarObj);
        if (bgT != null)
        {
            StretchRect(bgT.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
        }

        ApplyPlateVisual(bgImg, ObsidianDeep, new Color(0.86f, 0.65f, 0.25f, 0.72f), 1.3f, new Vector2(0f, -4f));

        GameObject leftStrip = EnsureChildPanel(expBarObj.transform, "HudGoldLeftAccent", GoldTrim, out _);
        RectTransform stripRt = leftStrip.GetComponent<RectTransform>();
        stripRt.anchorMin = new Vector2(0f, 0f);
        stripRt.anchorMax = new Vector2(0f, 1f);
        stripRt.pivot = new Vector2(0f, 0.5f);
        stripRt.anchoredPosition = Vector2.zero;
        stripRt.sizeDelta = new Vector2(4f, 0f);

        gemImage = EnsureDiamondGem(
            expBarObj.transform, "HudGoldCrestGem",
            new Vector2(23f, 0f), 13f,
            new Color(1f, 0.80f, 0.24f, 1f),
            new Color(1f, 0.94f, 0.64f, 1f),
            leftCentered: true);

        if (goldText == null)
        {
            goldText = expBarObj.GetComponentInChildren<TMP_Text>(true);
        }

        if (goldText != null)
        {
            goldText.transform.SetAsLastSibling();
            StretchRect(goldText.rectTransform, 42f, 0f, 12f, 0f);
            goldText.fontStyle = FontStyles.Bold;
            goldText.fontSize = 14.5f;
            goldText.alignment = TextAlignmentOptions.MidlineLeft;
            goldText.enableVertexGradient = false;
            goldText.color = Color.white;
            goldText.characterSpacing = 0.6f;
            goldText.textWrappingMode = TextWrappingModes.NoWrap;
            goldText.raycastTarget = false;
            EnsureDropShadow(goldText.gameObject, new Color(0f, 0f, 0f, 0.92f), new Vector2(1.1f, -1.4f));
        }
    }

    // Nâng cấp bảng nhiệm vụ góc trên trái với viền vàng, thanh nhấn dọc và phần chân phím điều khiển
    public static void StyleMissionHud(
        GameObject missionPanel,
        TMP_Text titleText,
        TMP_Text objectiveText,
        TMP_Text dashText)
    {
        if (missionPanel == null) return;

        RectTransform rt = missionPanel.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(22f, -20f);
            rt.sizeDelta = new Vector2(452f, 128f);
        }

        Image bg = GetOrAddComponent<Image>(missionPanel);
        ApplyPlateVisual(bg, ObsidianDeep, GoldTrim, 1.5f, new Vector2(0f, -6f));

        GameObject leftAccent = EnsureChildPanel(missionPanel.transform, "HudLeftAccent", GoldTrim, out _);
        RectTransform leftRt = leftAccent.GetComponent<RectTransform>();
        leftRt.anchorMin = new Vector2(0f, 0f);
        leftRt.anchorMax = new Vector2(0f, 1f);
        leftRt.pivot = new Vector2(0f, 0.5f);
        leftRt.anchoredPosition = Vector2.zero;
        leftRt.sizeDelta = new Vector2(5f, 0f);

        EnsureDiamondGem(
            missionPanel.transform, "HudMissionCrest",
            new Vector2(21f, -23f), 11f,
            GoldBright,
            new Color(1f, 0.96f, 0.72f, 1f),
            leftTop: true);

        GameObject divider = EnsureChildPanel(missionPanel.transform, "HudHeaderDivider", GoldDivider, out _);
        RectTransform divRt = divider.GetComponent<RectTransform>();
        divRt.anchorMin = new Vector2(0f, 1f);
        divRt.anchorMax = new Vector2(1f, 1f);
        divRt.pivot = new Vector2(0.5f, 1f);
        divRt.anchoredPosition = new Vector2(6f, -41f);
        divRt.sizeDelta = new Vector2(-34f, 1.5f);

        GameObject footerBg = EnsureChildPanel(missionPanel.transform, "HudFooterStrip", new Color(0.024f, 0.032f, 0.058f, 0.80f), out _);
        RectTransform footRt = footerBg.GetComponent<RectTransform>();
        footRt.anchorMin = new Vector2(0f, 0f);
        footRt.anchorMax = new Vector2(1f, 0f);
        footRt.pivot = new Vector2(0.5f, 0f);
        footRt.anchoredPosition = new Vector2(2.5f, 1.5f);
        footRt.sizeDelta = new Vector2(-6f, 34f);
        footerBg.transform.SetSiblingIndex(1);

        GameObject footerRule = EnsureChildPanel(missionPanel.transform, "HudFooterRule", new Color(0.86f, 0.65f, 0.25f, 0.28f), out _);
        RectTransform fRuleRt = footerRule.GetComponent<RectTransform>();
        fRuleRt.anchorMin = new Vector2(0f, 0f);
        fRuleRt.anchorMax = new Vector2(1f, 0f);
        fRuleRt.pivot = new Vector2(0.5f, 0f);
        fRuleRt.anchoredPosition = new Vector2(2.5f, 35.5f);
        fRuleRt.sizeDelta = new Vector2(-8f, 1f);

        if (titleText != null)
        {
            titleText.transform.SetAsLastSibling();
            RectTransform trt = titleText.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(36f, -38f);
            trt.offsetMax = new Vector2(-14f, -8f);

            ApplyFantasyHeadingText(
                titleText, 16.5f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft,
                new Color(1f, 0.94f, 0.66f, 1f), new Color(0.90f, 0.64f, 0.20f, 1f),
                1.3f, true);
            titleText.enableAutoSizing = true;
            titleText.fontSizeMin = 12.5f;
            titleText.fontSizeMax = 17f;
        }

        if (objectiveText != null)
        {
            objectiveText.transform.SetAsLastSibling();
            RectTransform ort = objectiveText.rectTransform;
            ort.anchorMin = new Vector2(0f, 1f);
            ort.anchorMax = new Vector2(1f, 1f);
            ort.pivot = new Vector2(0.5f, 1f);
            ort.offsetMin = new Vector2(18f, -88f);
            ort.offsetMax = new Vector2(-14f, -44f);

            objectiveText.fontSize = 14.8f;
            objectiveText.fontStyle = FontStyles.Bold;
            objectiveText.alignment = TextAlignmentOptions.MidlineLeft;
            objectiveText.enableVertexGradient = false;
            objectiveText.color = new Color(0.95f, 0.96f, 0.99f, 1f);
            objectiveText.characterSpacing = 0.2f;
            objectiveText.textWrappingMode = TextWrappingModes.Normal;
            objectiveText.raycastTarget = false;
            EnsureDropShadow(objectiveText.gameObject, new Color(0f, 0f, 0f, 0.90f), new Vector2(1.1f, -1.4f));
        }

        if (dashText != null)
        {
            dashText.transform.SetAsLastSibling();
            RectTransform drt = dashText.rectTransform;
            drt.anchorMin = new Vector2(0f, 0f);
            drt.anchorMax = new Vector2(1f, 0f);
            drt.pivot = new Vector2(0.5f, 0f);
            drt.offsetMin = new Vector2(18f, 3f);
            drt.offsetMax = new Vector2(-14f, 32f);

            dashText.fontSize = 12.6f;
            dashText.fontStyle = FontStyles.Bold;
            dashText.alignment = TextAlignmentOptions.MidlineLeft;
            dashText.enableVertexGradient = false;
            dashText.color = Color.white;
            dashText.characterSpacing = 0.25f;
            dashText.textWrappingMode = TextWrappingModes.NoWrap;
            dashText.enableAutoSizing = true;
            dashText.fontSizeMin = 10.5f;
            dashText.fontSizeMax = 13f;
            dashText.raycastTarget = false;
            EnsureDropShadow(dashText.gameObject, new Color(0f, 0f, 0f, 0.92f), new Vector2(1f, -1.2f));
        }
    }

    // Trang trí thanh máu Boss phía trên giữa màn hình với khung đỏ vàng và thanh trễ sát thương
    public static void StyleBossHud(
        GameObject bossPanel,
        Slider bossSlider,
        TMP_Text bossNameText,
        TMP_Text bossHpValueText,
        out Image mainFillImage,
        out Image ghostFillImage,
        out Image frameImage)
    {
        mainFillImage = null;
        ghostFillImage = null;
        frameImage = null;
        if (bossPanel == null) return;

        RectTransform rt = bossPanel.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -20f);
            rt.sizeDelta = new Vector2(568f, 76f);
        }

        frameImage = GetOrAddComponent<Image>(bossPanel);
        ApplyPlateVisual(frameImage, CrimsonObsidian, new Color(0.88f, 0.56f, 0.24f, 0.84f), 1.8f, new Vector2(0f, -8f));

        GameObject topTrim = EnsureChildPanel(bossPanel.transform, "HudBossTopTrim", GoldTrim, out _);
        RectTransform ttRt = topTrim.GetComponent<RectTransform>();
        ttRt.anchorMin = new Vector2(0f, 1f);
        ttRt.anchorMax = new Vector2(1f, 1f);
        ttRt.pivot = new Vector2(0.5f, 1f);
        ttRt.anchoredPosition = Vector2.zero;
        ttRt.sizeDelta = new Vector2(0f, 2.5f);

        GameObject botTrim = EnsureChildPanel(bossPanel.transform, "HudBossBottomTrim", new Color(0.86f, 0.65f, 0.25f, 0.45f), out _);
        RectTransform btRt = botTrim.GetComponent<RectTransform>();
        btRt.anchorMin = new Vector2(0f, 0f);
        btRt.anchorMax = new Vector2(1f, 0f);
        btRt.pivot = new Vector2(0.5f, 0f);
        btRt.anchoredPosition = Vector2.zero;
        btRt.sizeDelta = new Vector2(0f, 2f);

        EnsureDiamondGem(
            bossPanel.transform, "HudBossWingLeft",
            new Vector2(19f, -21f), 11f,
            new Color(0.94f, 0.24f, 0.22f, 1f),
            GoldBright,
            leftTop: true);

        if (bossNameText != null)
        {
            bossNameText.transform.SetAsLastSibling();
            RectTransform nrt = bossNameText.rectTransform;
            nrt.anchorMin = new Vector2(0f, 1f);
            nrt.anchorMax = new Vector2(0.76f, 1f);
            nrt.pivot = new Vector2(0f, 1f);
            nrt.offsetMin = new Vector2(34f, -35f);
            nrt.offsetMax = new Vector2(-8f, -6f);

            ApplyFantasyHeadingText(
                bossNameText, 16f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft,
                new Color(1f, 0.90f, 0.62f, 1f), new Color(1f, 0.46f, 0.34f, 1f),
                1.4f, true);
            bossNameText.enableAutoSizing = true;
            bossNameText.fontSizeMin = 12f;
            bossNameText.fontSizeMax = 16.5f;
        }

        if (bossHpValueText != null)
        {
            bossHpValueText.transform.SetAsLastSibling();
            RectTransform vrt = bossHpValueText.rectTransform;
            vrt.anchorMin = new Vector2(0.76f, 1f);
            vrt.anchorMax = new Vector2(1f, 1f);
            vrt.pivot = new Vector2(1f, 1f);
            vrt.offsetMin = new Vector2(4f, -35f);
            vrt.offsetMax = new Vector2(-20f, -6f);

            ApplyFantasyHeadingText(
                bossHpValueText, 14.5f, FontStyles.Bold, TextAlignmentOptions.MidlineRight,
                new Color(1f, 0.98f, 0.94f, 1f), new Color(0.95f, 0.80f, 0.62f, 1f),
                0.8f, true);
        }

        if (bossSlider != null)
        {
            bossSlider.interactable = false;
            bossSlider.transition = Selectable.Transition.None;

            RectTransform sRt = bossSlider.GetComponent<RectTransform>();
            if (sRt != null)
            {
                sRt.anchorMin = new Vector2(0f, 0f);
                sRt.anchorMax = new Vector2(1f, 0f);
                sRt.pivot = new Vector2(0.5f, 0f);
                sRt.anchoredPosition = new Vector2(0f, 12f);
                sRt.sizeDelta = new Vector2(-40f, 20f);
            }

            Transform bgT = bossSlider.transform.Find("Bg");
            if (bgT == null) bgT = bossSlider.transform.Find("Background");
            if (bgT != null)
            {
                bgT.SetSiblingIndex(0);
                Image sliderBg = GetOrAddComponent<Image>(bgT.gameObject);
                StretchRect(bgT.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
                sliderBg.color = new Color(0.14f, 0.035f, 0.055f, 0.96f);
                if (Application.isPlaying) sliderBg.sprite = null;
                EnsureOutline(bgT.gameObject, new Color(0.75f, 0.52f, 0.22f, 0.62f), 1.2f);
            }

            Transform fillAreaT = bossSlider.transform.Find("FillArea");
            if (fillAreaT == null) fillAreaT = bossSlider.transform.Find("Fill Area");
            if (fillAreaT != null)
            {
                fillAreaT.SetAsLastSibling();
                StretchRect(fillAreaT.GetComponent<RectTransform>(), 2f, 2f, 2f, 2f);

                GameObject ghostGo = EnsureChildPanel(fillAreaT, "HudBossGhostFill", HpGhostTrail, out ghostFillImage);
                StretchRect(ghostGo.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
                ghostGo.transform.SetAsFirstSibling();
                ApplyBarSprite(ghostFillImage);

                if (bossSlider.fillRect != null)
                {
                    RectTransform bfRt = bossSlider.fillRect;
                    if (bfRt.parent != fillAreaT) bfRt.SetParent(fillAreaT, false);
                    bfRt.offsetMin = Vector2.zero;
                    bfRt.offsetMax = Vector2.zero;
                    bfRt.anchorMin = new Vector2(bfRt.anchorMin.x, 0f);
                    bfRt.anchorMax = new Vector2(bfRt.anchorMax.x, 1f);

                    mainFillImage = GetOrAddComponent<Image>(bfRt.gameObject);
                    mainFillImage.raycastTarget = false;
                    ApplyBarSprite(mainFillImage);
                    mainFillImage.color = BossFillNormal;
                    bfRt.SetSiblingIndex(1);
                }

                GameObject sheenGo = EnsureChildPanel(fillAreaT, "HudBossSheen", new Color(1f, 1f, 1f, 0.15f), out _);
                RectTransform sheenRt = sheenGo.GetComponent<RectTransform>();
                sheenRt.anchorMin = new Vector2(0f, 0.58f);
                sheenRt.anchorMax = new Vector2(1f, 1f);
                sheenRt.offsetMin = Vector2.zero;
                sheenRt.offsetMax = Vector2.zero;
                sheenGo.transform.SetAsLastSibling();
            }
        }
    }

    // Trang trí banner thông báo giữa màn hình kèm đường viền vàng và nhóm alpha mờ dần
    public static void StyleStageBanner(GameObject bannerPanel, TMP_Text bannerText, out CanvasGroup canvasGroup)
    {
        canvasGroup = null;
        if (bannerPanel == null) return;

        RectTransform rt = bannerPanel.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.78f);
            rt.anchorMax = new Vector2(0.5f, 0.78f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(740f, 98f);
        }

        canvasGroup = GetOrAddComponent<CanvasGroup>(bannerPanel);
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        Image bg = GetOrAddComponent<Image>(bannerPanel);
        bg.raycastTarget = false;
        bg.type = Image.Type.Simple;
        bg.color = new Color(0.035f, 0.050f, 0.090f, 0.90f);
        if (Application.isPlaying)
        {
            bg.sprite = GetBannerVignetteSprite();
        }
        else
        {
            bg.sprite = null;
            EnsureOutline(bannerPanel, new Color(0.86f, 0.65f, 0.25f, 0.45f), 1.2f);
        }

        GameObject topRule = EnsureChildPanel(bannerPanel.transform, "BannerTopRule", GoldTrim, out _);
        RectTransform trRt = topRule.GetComponent<RectTransform>();
        trRt.anchorMin = new Vector2(0.08f, 1f);
        trRt.anchorMax = new Vector2(0.92f, 1f);
        trRt.pivot = new Vector2(0.5f, 1f);
        trRt.anchoredPosition = new Vector2(0f, -4f);
        trRt.sizeDelta = new Vector2(0f, 2f);

        GameObject botRule = EnsureChildPanel(bannerPanel.transform, "BannerBottomRule", GoldTrim, out _);
        RectTransform brRt = botRule.GetComponent<RectTransform>();
        brRt.anchorMin = new Vector2(0.08f, 0f);
        brRt.anchorMax = new Vector2(0.92f, 0f);
        brRt.pivot = new Vector2(0.5f, 0f);
        brRt.anchoredPosition = new Vector2(0f, 4f);
        brRt.sizeDelta = new Vector2(0f, 2f);

        EnsureDiamondGem(bannerPanel.transform, "BannerTopGem", new Vector2(0f, -5f), 10f, GoldBright, Color.white, topCentered: true);
        EnsureDiamondGem(bannerPanel.transform, "BannerBottomGem", new Vector2(0f, 5f), 10f, GoldBright, Color.white, bottomCentered: true);

        if (bannerText != null)
        {
            bannerText.transform.SetAsLastSibling();
            StretchRect(bannerText.rectTransform, 44f, 12f, 44f, 12f);
            bannerText.fontSize = 21f;
            bannerText.fontStyle = FontStyles.Bold;
            bannerText.alignment = TextAlignmentOptions.Center;
            bannerText.characterSpacing = 1.2f;
            bannerText.lineSpacing = 4f;
            bannerText.enableVertexGradient = false;
            bannerText.textWrappingMode = TextWrappingModes.Normal;
            bannerText.raycastTarget = false;
            EnsureDropShadow(bannerText.gameObject, new Color(0f, 0f, 0f, 0.94f), new Vector2(1.4f, -1.8f));
        }
    }

    // Điều chỉnh tỷ lệ lấp đầy của thanh sát thương trễ theo normalizedRatio (0..1)
    public static void SetGhostFillRatio(Image ghostFill, float ratio)
    {
        if (ghostFill == null) return;
        RectTransform rt = ghostFill.rectTransform;
        float clamped = Mathf.Clamp01(ratio);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(clamped, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // Áp dụng kiểu chữ tiêu đề có gradient, giãn chữ và đổ bóng sắc nét
    private static void ApplyFantasyHeadingText(
        TMP_Text tmp,
        float fontSize,
        FontStyles style,
        TextAlignmentOptions align,
        Color topColor,
        Color bottomColor,
        float charSpacing,
        bool noWrap)
    {
        if (tmp == null) return;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.enableVertexGradient = true;
        tmp.colorGradient = new VertexGradient(topColor, topColor, bottomColor, bottomColor);
        tmp.characterSpacing = charSpacing;
        tmp.textWrappingMode = noWrap ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        EnsureDropShadow(tmp.gameObject, new Color(0f, 0f, 0f, 0.92f), new Vector2(1.2f, -1.6f));
    }

    private static void ApplyPlateVisual(Image img, Color bgColor, Color outlineColor, float outlineThickness, Vector2 shadowOffset)
    {
        if (img == null) return;
        img.color = bgColor;
        img.raycastTarget = false;
        if (Application.isPlaying)
        {
            img.sprite = GetBeveledPlateSprite();
            img.type = Image.Type.Sliced;
        }
        else
        {
            img.sprite = null;
            img.type = Image.Type.Simple;
        }

        EnsureOutline(img.gameObject, outlineColor, outlineThickness);
        EnsureDropShadow(img.gameObject, new Color(0f, 0f, 0f, 0.72f), shadowOffset);
    }

    private static void ApplyBarSprite(Image img)
    {
        if (img == null) return;
        img.raycastTarget = false;
        if (Application.isPlaying)
        {
            img.sprite = GetMetallicBarSprite();
            img.type = Image.Type.Simple;
        }
        else
        {
            img.sprite = null;
        }
    }

    private static GameObject EnsureChildPanel(Transform parent, string name, Color color, out Image img)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null
            ? existing.gameObject
            : new GameObject(name, typeof(RectTransform), typeof(Image));
        if (existing == null) go.transform.SetParent(parent, false);
        img = GetOrAddComponent<Image>(go);
        img.color = color;
        img.raycastTarget = false;
        return go;
    }

    private static Image EnsureDiamondGem(
        Transform parent,
        string name,
        Vector2 anchoredPos,
        float size,
        Color coreColor,
        Color rimColor,
        bool leftCentered = false,
        bool leftTop = false,
        bool topCentered = false,
        bool bottomCentered = false)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null
            ? existing.gameObject
            : new GameObject(name, typeof(RectTransform), typeof(Image));
        if (existing == null) go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        if (leftCentered)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
        }
        else if (leftTop)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
        }
        else if (topCentered)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
        }
        else if (bottomCentered)
        {
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
        }
        else
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
        }
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(size, size);
        rt.localRotation = Quaternion.Euler(0f, 0f, 45f);

        Image img = GetOrAddComponent<Image>(go);
        img.color = coreColor;
        img.raycastTarget = false;
        if (Application.isPlaying)
        {
            img.sprite = GetGemDiamondSprite();
        }
        else
        {
            img.sprite = null;
        }
        EnsureOutline(go, rimColor, 1.2f);
        return img;
    }

    private static TMP_Text EnsureOrFindText(
        Transform parent,
        string name,
        string content,
        float fontSize,
        FontStyles style,
        Color color,
        TextAlignmentOptions align,
        TMP_FontAsset fontAsset)
    {
        Transform existing = parent.Find(name);
        GameObject go = existing != null
            ? existing.gameObject
            : new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        if (existing == null) go.transform.SetParent(parent, false);

        TMP_Text tmp = go.GetComponent<TMP_Text>();
        if (tmp == null) tmp = go.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null && !fontAsset.name.StartsWith("LiberationSans", System.StringComparison.OrdinalIgnoreCase))
        {
            if (tmp.font != fontAsset)
            {
                tmp.font = fontAsset;
                tmp.fontSharedMaterial = fontAsset.material;
            }
        }
        else
        {
            ClassicFontRuntimeManager.ApplyFont(tmp);
        }
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Outline EnsureOutline(GameObject go, Color color, float thickness)
    {
        Outline outline = GetOrAddComponent<Outline>(go);
        outline.effectColor = color;
        outline.effectDistance = new Vector2(thickness, -thickness);
        return outline;
    }

    // Lấy component sẵn có trên GameObject hoặc tự động gắn mới theo kiểm tra null chuẩn Unity
    private static T GetOrAddComponent<T>(GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        if (comp == null) comp = go.AddComponent<T>();
        return comp;
    }

    private static void EnsureDropShadow(GameObject go, Color color, Vector2 offset)
    {
        Shadow shadow = go.GetComponent<Shadow>();
        if (shadow == null || shadow is Outline)
        {
            Shadow[] all = go.GetComponents<Shadow>();
            shadow = null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && !(all[i] is Outline))
                {
                    shadow = all[i];
                    break;
                }
            }
            if (shadow == null) shadow = go.AddComponent<Shadow>();
        }
        shadow.effectColor = color;
        shadow.effectDistance = offset;
    }

    private static void HideChildIfExists(Transform parent, string childName)
    {
        Transform t = parent.Find(childName);
        if (t != null) t.gameObject.SetActive(false);
    }

    private static void StretchRect(RectTransform rt, float left, float bottom, float right, float top)
    {
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    // Tạo sprite khung 9-slice vát cạnh kèm rãnh sáng kim loại và viền vàng mảnh
    private static Sprite GetBeveledPlateSprite()
    {
        if (cachedBeveledPlateSprite != null) return cachedBeveledPlateSprite;

        const int size = 48;
        const int border = 12;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.DontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            float vy = (float)y / (size - 1);
            int row = y * size;
            for (int x = 0; x < size; x++)
            {
                int distEdge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                int corner = Mathf.Min(x, size - 1 - x) + Mathf.Min(y, size - 1 - y);
                if (corner < 3)
                {
                    px[row + x] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float shade = 0.88f + 0.14f * vy;
                if (distEdge == 1) shade *= 1.18f;
                else if (distEdge == 2) shade *= 0.78f;

                byte c = (byte)Mathf.Clamp(Mathf.RoundToInt(shade * 255f), 0, 255);
                px[row + x] = new Color32(c, c, c, 255);
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, true);
        cachedBeveledPlateSprite = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
        cachedBeveledPlateSprite.hideFlags = HideFlags.DontSave;
        return cachedBeveledPlateSprite;
    }

    // Tạo sprite ánh kim có vệt sáng cạnh trên cho thanh máu người chơi và thanh máu Boss
    private static Sprite GetMetallicBarSprite()
    {
        if (cachedMetallicBarSprite != null) return cachedMetallicBarSprite;

        const int w = 64;
        const int h = 24;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.DontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[w * h];

        for (int y = 0; y < h; y++)
        {
            float vy = (float)y / (h - 1);
            float vertBand = vy > 0.68f
                ? Mathf.Lerp(1.04f, 1.24f, (vy - 0.68f) / 0.32f)
                : Mathf.Lerp(0.76f, 1.04f, vy / 0.68f);
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float vx = (float)x / (w - 1);
                float horiz = 0.92f + 0.12f * vx;
                byte c = (byte)Mathf.Clamp(Mathf.RoundToInt(vertBand * horiz * 255f), 0, 255);
                px[row + x] = new Color32(c, c, c, 255);
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, true);
        cachedMetallicBarSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        cachedMetallicBarSprite.hideFlags = HideFlags.DontSave;
        return cachedMetallicBarSprite;
    }

    // Tạo dải nền mờ dần về hai đầu cho banner thông báo sự kiện
    private static Sprite GetBannerVignetteSprite()
    {
        if (cachedBannerVignetteSprite != null) return cachedBannerVignetteSprite;

        const int w = 128;
        const int h = 8;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.DontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[w * h];

        for (int x = 0; x < w; x++)
        {
            float t = (float)x / (w - 1);
            float distCenter = Mathf.Abs(t - 0.5f) * 2f;
            float fade = Mathf.Clamp01(1f - Mathf.Pow(distCenter, 2.4f));
            byte a = (byte)Mathf.RoundToInt(fade * 255f);
            for (int y = 0; y < h; y++)
            {
                px[y * w + x] = new Color32(255, 255, 255, a);
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, true);
        cachedBannerVignetteSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        cachedBannerVignetteSprite.hideFlags = HideFlags.DontSave;
        return cachedBannerVignetteSprite;
    }

    // Tạo sprite viên ngọc vát khối nhẹ cho các huy hiệu góc
    private static Sprite GetGemDiamondSprite()
    {
        if (cachedGemDiamondSprite != null) return cachedGemDiamondSprite;

        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.DontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            float vy = (float)y / (size - 1);
            int row = y * size;
            for (int x = 0; x < size; x++)
            {
                float vx = (float)x / (size - 1);
                float bevel = 0.86f + 0.24f * (vy * 0.65f + (1f - vx) * 0.35f);
                byte c = (byte)Mathf.Clamp(Mathf.RoundToInt(bevel * 255f), 0, 255);
                px[row + x] = new Color32(c, c, c, 255);
            }
        }

        tex.SetPixels32(px);
        tex.Apply(false, true);
        cachedGemDiamondSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        cachedGemDiamondSprite.hideFlags = HideFlags.DontSave;
        return cachedGemDiamondSprite;
    }
}
