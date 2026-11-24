using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Nạp bộ Sprite Dark Fantasy CC0 chính chủ từ Kenney (Fantasy UI Borders + Game Icons)
/// trong Resources/UI/FantasyPack, kèm helper dựng khung viền 2 lớp, nút bấm chuẩn kích thước
/// và hiệu ứng tương tác mượt mà.
/// </summary>
public static class MedievalSettingsArt
{
    // Bảng màu Dark Fantasy (Vanguard — Dark Souls / Elden Ring inspired)
    public static readonly Color TitleGoldTop = new Color(1f, 0.95f, 0.74f, 1f);
    public static readonly Color TitleGoldBottom = new Color(0.86f, 0.60f, 0.20f, 1f);
    public static readonly Color IvoryText = new Color(0.94f, 0.91f, 0.85f, 1f);
    public static readonly Color WarmBronzeText = new Color(0.76f, 0.66f, 0.52f, 1f);
    public static readonly Color BrightGoldText = new Color(1f, 0.84f, 0.44f, 1f);
    public static readonly Color DarkInkText = new Color(0.08f, 0.045f, 0.015f, 1f);
    public static readonly Color EmeraldText = new Color(0.90f, 1f, 0.90f, 1f);
    public static readonly Color CrimsonText = new Color(1f, 0.86f, 0.82f, 1f);
    public static readonly Color BackdropDark = new Color(0.015f, 0.013f, 0.012f, 0.86f);

    public static readonly Color FrameObsidian = new Color(0.048f, 0.043f, 0.042f, 0.95f);
    public static readonly Color InnerObsidian = new Color(0.082f, 0.073f, 0.070f, 0.92f);
    public static readonly Color GoldOrnament = new Color(0.86f, 0.67f, 0.30f, 0.96f);
    public static readonly Color BronzeBorder = new Color(0.54f, 0.41f, 0.22f, 0.86f);

    public static readonly Color HeroGoldFill = new Color(0.86f, 0.61f, 0.16f, 0.98f);
    public static readonly Color HeroGoldBorder = new Color(1f, 0.88f, 0.54f, 1f);
    public static readonly Color BronzeFill = new Color(0.17f, 0.125f, 0.088f, 0.95f);
    public static readonly Color DarkSlateFill = new Color(0.105f, 0.095f, 0.092f, 0.94f);
    public static readonly Color TabActiveFill = new Color(0.28f, 0.17f, 0.07f, 0.98f);
    public static readonly Color ActiveGoldFill = new Color(0.84f, 0.58f, 0.15f, 0.98f);
    public static readonly Color EmeraldFill = new Color(0.12f, 0.34f, 0.17f, 0.98f);
    public static readonly Color EmeraldBorder = new Color(0.46f, 0.88f, 0.52f, 0.95f);
    public static readonly Color CrimsonFill = new Color(0.36f, 0.10f, 0.10f, 0.98f);
    public static readonly Color CrimsonBorder = new Color(0.86f, 0.40f, 0.32f, 0.92f);

    private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();
    private static Sprite fallbackWhiteSprite;

    // 9-slice Kenney Fantasy UI Borders (đã đo chính xác pixel cap)
    public static Sprite PanelGothic => GetSlicedSprite("panel_gothic", new Vector4(28f, 28f, 28f, 28f));
    public static Sprite BorderGothic => GetSlicedSprite("border_gothic", new Vector4(24f, 24f, 24f, 24f));
    public static Sprite PanelBevel => GetSlicedSprite("panel_bevel", new Vector4(16f, 16f, 16f, 16f));
    public static Sprite BorderStuds => GetSlicedSprite("border_studs", new Vector4(16f, 16f, 16f, 16f));
    public static Sprite PanelClean => GetSlicedSprite("panel_clean", new Vector4(8f, 8f, 8f, 8f));
    public static Sprite BorderClean => GetSlicedSprite("border_clean", new Vector4(4f, 4f, 4f, 4f));
    public static Sprite BorderBrackets => GetSlicedSprite("border_brackets", new Vector4(16f, 16f, 16f, 16f));
    public static Sprite BorderKnotted => GetSlicedSprite("border_knotted", new Vector4(20f, 20f, 20f, 20f));
    public static Sprite DividerFadeOrnate => GetSimpleSprite("divider_fade_ornate");
    public static Sprite DividerFadeSlim => GetSimpleSprite("divider_fade_slim");

    // Kenney Game Icons (White 2x PNG sắc nét)
    public static Sprite IconGear => GetSimpleSprite("icon_gear");
    public static Sprite IconAudioOn => GetSimpleSprite("icon_audio_on");
    public static Sprite IconAudioOff => GetSimpleSprite("icon_audio_off");
    public static Sprite IconMusic => GetSimpleSprite("icon_music");
    public static Sprite IconSfx => GetSimpleSprite("icon_sfx");
    public static Sprite IconControls => GetSimpleSprite("icon_controls");
    public static Sprite IconSystem => GetSimpleSprite("icon_system");
    public static Sprite IconDown => GetSimpleSprite("icon_down");
    public static Sprite IconCheck => GetSimpleSprite("icon_check");
    public static Sprite IconCross => GetSimpleSprite("icon_cross");
    public static Sprite IconReturn => GetSimpleSprite("icon_return");
    public static Sprite IconPower => GetSimpleSprite("icon_power");
    public static Sprite IconPlay => GetSimpleSprite("icon_play");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        SpriteCache.Clear();
        fallbackWhiteSprite = null;
    }

    public static void SetFixedSize(GameObject go, float width, float height)
    {
        if (go == null) return;
        RectTransform rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.sizeDelta = new Vector2(width, height);
        }

        LayoutElement le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.preferredWidth = width;
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;
    }

    public static void ApplySprite(Image img, Sprite sprite, Color? tint = null)
    {
        if (img == null) return;
        img.sprite = sprite != null ? sprite : GetFallbackSprite();
        img.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        if (tint.HasValue)
        {
            img.color = tint.Value;
        }
    }

    public static void ApplyTitleGoldGradient(TMP_Text text)
    {
        if (text == null) return;
        text.color = Color.white;
        text.enableVertexGradient = true;
        text.colorGradient = new VertexGradient(TitleGoldTop, TitleGoldTop, TitleGoldBottom, TitleGoldBottom);
    }

    public static GameObject CreateFramedPanel(
        Transform parent,
        string name,
        float width,
        float height,
        Sprite fillSprite,
        Color fillColor,
        Sprite borderSprite,
        Color borderColor,
        bool raycastTarget = false)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        SetFixedSize(go, width, height);

        Image fillImg = go.GetComponent<Image>();
        ApplySprite(fillImg, fillSprite, fillColor);
        fillImg.raycastTarget = raycastTarget;

        GameObject borderGo = new GameObject("BorderOverlay", typeof(RectTransform), typeof(Image));
        borderGo.transform.SetParent(go.transform, false);
        FantasyUiThemeHelper.StretchFull(borderGo.GetComponent<RectTransform>());
        Image borderImg = borderGo.GetComponent<Image>();
        ApplySprite(borderImg, borderSprite, borderColor);
        borderImg.raycastTarget = false;
        LayoutElement borderLe = borderGo.AddComponent<LayoutElement>();
        borderLe.ignoreLayout = true;

        return go;
    }

    public static Button CreateFramedButton(
        Transform parent,
        string name,
        string label,
        float width,
        float height,
        float fontSize,
        Sprite fillSprite,
        Color fillColor,
        Sprite borderSprite,
        Color borderColor,
        Color textColor,
        Sprite iconSprite = null,
        UnityEngine.Events.UnityAction onClick = null,
        bool alignLeft = false)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        SetFixedSize(go, width, height);

        Image fillImg = go.GetComponent<Image>();
        ApplySprite(fillImg, fillSprite, fillColor);

        GameObject borderGo = new GameObject("BorderOverlay", typeof(RectTransform), typeof(Image));
        borderGo.transform.SetParent(go.transform, false);
        FantasyUiThemeHelper.StretchFull(borderGo.GetComponent<RectTransform>());
        Image borderImg = borderGo.GetComponent<Image>();
        ApplySprite(borderImg, borderSprite, borderColor);
        borderImg.raycastTarget = false;

        float textLeftPad = 12f;
        if (iconSprite != null)
        {
            GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            RectTransform iRt = iconGo.GetComponent<RectTransform>();
            iRt.anchorMin = new Vector2(0f, 0.5f);
            iRt.anchorMax = new Vector2(0f, 0.5f);
            iRt.pivot = new Vector2(0f, 0.5f);
            float iconSize = Mathf.Clamp(height * 0.44f, 18f, 26f);
            iRt.anchoredPosition = new Vector2(16f, 0f);
            iRt.sizeDelta = new Vector2(iconSize, iconSize);

            Image iImg = iconGo.GetComponent<Image>();
            iImg.sprite = iconSprite;
            iImg.color = textColor;
            iImg.preserveAspect = true;
            iImg.raycastTarget = false;
            textLeftPad = 16f + iconSize + 10f;
        }

        TMP_Text txt = FantasyUiThemeHelper.CreateText(go.transform, "Text", label,
            fontSize, FontStyles.Bold, textColor,
            alignLeft ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
        RectTransform trt = txt.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(alignLeft ? textLeftPad : (iconSprite != null ? textLeftPad - 4f : 10f), 0f);
        trt.offsetMax = new Vector2(alignLeft ? -12f : (iconSprite != null ? -12f : -10f), 0f);
        txt.textWrappingMode = TextWrappingModes.NoWrap;
        txt.overflowMode = TextOverflowModes.Ellipsis;
        txt.characterSpacing = 1.2f;

        if (textColor != DarkInkText)
        {
            Shadow sh = txt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
            sh.effectDistance = new Vector2(0f, -1.8f);
        }

        Button btn = go.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        AttachButtonFeedback(btn, fillColor);
        if (onClick != null) btn.onClick.AddListener(onClick);
        return btn;
    }

    public static void UpdateFramedButtonColors(Button btn, Color fillColor, Color borderColor, Color textColor)
    {
        if (btn == null) return;

        Image fillImg = btn.GetComponent<Image>();
        if (fillImg != null) fillImg.color = fillColor;

        Transform borderTf = btn.transform.Find("BorderOverlay");
        if (borderTf != null && borderTf.TryGetComponent(out Image borderImg))
        {
            borderImg.color = borderColor;
        }

        Transform iconTf = btn.transform.Find("Icon");
        if (iconTf != null && iconTf.TryGetComponent(out Image iconImg))
        {
            iconImg.color = textColor;
        }

        TMP_Text txt = btn.transform.Find("Text")?.GetComponent<TMP_Text>() ?? btn.GetComponentInChildren<TMP_Text>(true);
        if (txt != null) txt.color = textColor;

        MedievalButtonFeedback fb = btn.GetComponent<MedievalButtonFeedback>();
        if (fb != null) { fb.SetBaseColor(fillColor); fb.SetBorderBaseColor(borderColor); }
    }

    public static GameObject CreateSymmetricalDivider(
        Transform parent,
        string name,
        float width,
        float height,
        Color tint,
        bool ornate = false)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        SetFixedSize(root, width, height);

        Sprite divSprite = ornate ? DividerFadeOrnate : DividerFadeSlim;

        GameObject left = new GameObject("LeftWing", typeof(RectTransform), typeof(Image));
        left.transform.SetParent(root.transform, false);
        RectTransform lRt = left.GetComponent<RectTransform>();
        lRt.anchorMin = new Vector2(0f, 0f);
        lRt.anchorMax = new Vector2(0.5f, 1f);
        lRt.offsetMin = Vector2.zero;
        lRt.offsetMax = Vector2.zero;
        Image lImg = left.GetComponent<Image>();
        lImg.sprite = divSprite;
        lImg.color = tint;
        lImg.raycastTarget = false;

        GameObject right = new GameObject("RightWing", typeof(RectTransform), typeof(Image));
        right.transform.SetParent(root.transform, false);
        RectTransform rRt = right.GetComponent<RectTransform>();
        rRt.anchorMin = new Vector2(0.5f, 0f);
        rRt.anchorMax = new Vector2(1f, 1f);
        rRt.offsetMin = Vector2.zero;
        rRt.offsetMax = Vector2.zero;
        rRt.localScale = new Vector3(-1f, 1f, 1f);
        Image rImg = right.GetComponent<Image>();
        rImg.sprite = divSprite;
        rImg.color = tint;
        rImg.raycastTarget = false;

        return root;
    }

    public static MedievalButtonFeedback AttachButtonFeedback(Button btn, Color baseFillColor, float hoverScale = 1.045f, float pressScale = 0.96f)
    {
        if (btn == null) return null;
        MedievalButtonFeedback fb = btn.GetComponent<MedievalButtonFeedback>() ?? btn.gameObject.AddComponent<MedievalButtonFeedback>();
        fb.Configure(hoverScale, pressScale, btn.targetGraphic, baseFillColor);
        return fb;
    }

    public static MedievalSliderFeedback AttachSliderFeedback(Slider slider)
    {
        if (slider == null) return null;
        MedievalSliderFeedback fb = slider.GetComponent<MedievalSliderFeedback>() ?? slider.gameObject.AddComponent<MedievalSliderFeedback>();
        fb.Configure(slider.handleRect, slider.targetGraphic);
        return fb;
    }

    private static Sprite GetSimpleSprite(string assetName) => GetSlicedSprite(assetName, Vector4.zero);

    private static Sprite GetSlicedSprite(string assetName, Vector4 border)
    {
        string key = $"{assetName}_{(int)border.x}_{(int)border.y}_{(int)border.z}_{(int)border.w}";
        if (SpriteCache.TryGetValue(key, out Sprite cached) && cached != null)
        {
            return cached;
        }

        string resPath = "UI/FantasyPack/" + assetName;
        Texture2D tex = GameAssetPreloader.GetAsset<Texture2D>(resPath) ?? Resources.Load<Texture2D>(resPath);
        if (tex == null)
        {
            return GetFallbackSprite();
        }

        tex.wrapMode = TextureWrapMode.Clamp;

        Vector4 clampedBorder = new Vector4(
            Mathf.Min(border.x, tex.width * 0.45f),
            Mathf.Min(border.y, tex.height * 0.45f),
            Mathf.Min(border.z, tex.width * 0.45f),
            Mathf.Min(border.w, tex.height * 0.45f));

        Sprite sprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            clampedBorder);
        sprite.name = assetName + "_Sprite";
        SpriteCache[key] = sprite;
        return sprite;
    }

    private static Sprite GetFallbackSprite()
    {
        if (fallbackWhiteSprite != null) return fallbackWhiteSprite;

        Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color32[] cols = new Color32[16];
        for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(cols);
        tex.Apply(false, true);
        fallbackWhiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        return fallbackWhiteSprite;
    }
}

/// <summary>
/// Hiệu ứng phản hồi phóng nhẹ và sáng viền khi rê chuột hoặc nhấn nút.
/// </summary>
public class MedievalButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private float hoverScale = 1.045f;
    private float pressScale = 0.96f;
    private Graphic targetGraphic;
    private Graphic borderGraphic;
    private Color borderBaseColor = Color.white;
    private bool borderCaptured;
    private static readonly Color HoverBorderColor = new Color(1f, 0.86f, 0.45f, 1f);
    private Color baseColor = Color.white;
    private bool hasBaseColor;
    private bool isHovered;
    private bool isPressed;
    private Selectable selectable;

    public void Configure(float hover, float press, Graphic graphic, Color baseCol)
    {
        hoverScale = hover;
        pressScale = press;
        targetGraphic = graphic;
        baseColor = baseCol;
        hasBaseColor = true;
        selectable = GetComponent<Selectable>();
        CaptureBorder();
        Wake();
    }

    private void CaptureBorder()
    {
        Transform b = transform.Find("BorderOverlay");
        if (b != null && b.TryGetComponent(out Image img))
        {
            borderGraphic = img;
            borderBaseColor = img.color;
            borderCaptured = true;
        }
    }

    public void SetBorderBaseColor(Color c)
    {
        borderBaseColor = c;
        borderCaptured = borderGraphic != null;
        Wake();
    }

    public void SetBaseColor(Color baseCol)
    {
        baseColor = baseCol;
        hasBaseColor = true;
        Wake();
    }

    private void Wake()
    {
        if (!enabled) enabled = true;
    }

    private void OnEnable()
    {
        isHovered = false;
        isPressed = false;
        transform.localScale = Vector3.one;
        if (targetGraphic != null && hasBaseColor) targetGraphic.color = baseColor;
        Wake();
    }

    private void OnDisable()
    {
        isHovered = false;
        isPressed = false;
        transform.localScale = Vector3.one;
        if (targetGraphic != null && hasBaseColor) targetGraphic.color = baseColor;
        if (borderGraphic != null && borderCaptured) borderGraphic.color = borderBaseColor;
    }

    private void Update()
    {
        bool interactable = selectable == null || selectable.interactable;
        float desiredScale = !interactable ? 1f : (isPressed ? pressScale : (isHovered ? hoverScale : 1f));
        float k = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
        float s = Mathf.Lerp(transform.localScale.x, desiredScale, k);
        bool scaleSettled = Mathf.Abs(s - desiredScale) < 0.0005f;
        if (scaleSettled) s = desiredScale;
        transform.localScale = new Vector3(s, s, 1f);

        bool colorSettled = true;
        if (targetGraphic != null && hasBaseColor)
        {
            Color targetCol = !interactable
                ? new Color(baseColor.r * 0.65f, baseColor.g * 0.65f, baseColor.b * 0.65f, baseColor.a * 0.8f)
                : (isPressed
                    ? new Color(baseColor.r * 0.85f, baseColor.g * 0.85f, baseColor.b * 0.85f, baseColor.a)
                    : (isHovered
                        ? new Color(Mathf.Min(1f, baseColor.r + 0.12f), Mathf.Min(1f, baseColor.g + 0.10f), Mathf.Min(1f, baseColor.b + 0.06f), Mathf.Min(1f, baseColor.a + 0.05f))
                        : baseColor));
            Color nextCol = Color.Lerp(targetGraphic.color, targetCol, k);
            colorSettled = Mathf.Abs(nextCol.r - targetCol.r) < 0.002f
                && Mathf.Abs(nextCol.g - targetCol.g) < 0.002f
                && Mathf.Abs(nextCol.b - targetCol.b) < 0.002f
                && Mathf.Abs(nextCol.a - targetCol.a) < 0.002f;
            targetGraphic.color = colorSettled ? targetCol : nextCol;
        }

        if (borderGraphic != null && borderCaptured)
        {
            Color bt = !interactable ? borderBaseColor : ((isHovered || isPressed) ? HoverBorderColor : borderBaseColor);
            Color nb = Color.Lerp(borderGraphic.color, bt, k);
            bool bs = Mathf.Abs(nb.r - bt.r) < 0.002f && Mathf.Abs(nb.g - bt.g) < 0.002f && Mathf.Abs(nb.b - bt.b) < 0.002f && Mathf.Abs(nb.a - bt.a) < 0.002f;
            borderGraphic.color = bs ? bt : nb;
            colorSettled &= bs;
        }

        if (scaleSettled && colorSettled && !isHovered && !isPressed)
        {
            enabled = false;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isHovered && (selectable == null || selectable.interactable)) GameAudioManager.PlayUiHover();
        isHovered = true;
        Wake();
    }
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        isPressed = false;
        Wake();
    }

    public void OnPointerDown(PointerEventData eventData) { isPressed = true; Wake(); }
    public void OnPointerUp(PointerEventData eventData) { isPressed = false; Wake(); }
}

/// <summary>
/// Tạo phản hồi phóng nhẹ và sáng viền cho tay cầm Slider khi rê chuột hoặc kéo.
/// </summary>
public class MedievalSliderFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private RectTransform handleRect;
    private Graphic handleGraphic;
    private bool isHovered;
    private bool isDragging;

    public void Configure(RectTransform handle, Graphic graphic)
    {
        handleRect = handle;
        handleGraphic = graphic;
        Wake();
    }

    private void Wake()
    {
        if (!enabled) enabled = true;
    }

    private void OnEnable()
    {
        isHovered = false;
        isDragging = false;
        if (handleRect != null) handleRect.localScale = Vector3.one;
        Wake();
    }

    private void OnDisable()
    {
        isHovered = false;
        isDragging = false;
        if (handleRect != null) handleRect.localScale = Vector3.one;
    }

    private void Update()
    {
        if (handleRect == null)
        {
            enabled = false;
            return;
        }

        float desired = isDragging ? 1.22f : (isHovered ? 1.12f : 1f);
        float k = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
        float s = Mathf.Lerp(handleRect.localScale.x, desired, k);
        bool settled = Mathf.Abs(s - desired) < 0.0005f;
        if (settled) s = desired;
        handleRect.localScale = new Vector3(s, s, 1f);

        if (settled && !isHovered && !isDragging)
        {
            enabled = false;
        }
    }

    public void OnPointerEnter(PointerEventData eventData) { isHovered = true; Wake(); }
    public void OnPointerExit(PointerEventData eventData) { isHovered = false; Wake(); }
    public void OnPointerDown(PointerEventData eventData) { isDragging = true; Wake(); }
    public void OnPointerUp(PointerEventData eventData) { isDragging = false; Wake(); }
}
