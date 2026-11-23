using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Bộ hàm tiện ích dựng thành phần UI, nút bấm, ô nhập liệu và sprite gradient dùng chung
public static class FantasyUiThemeHelper
{
    public static readonly Color ObsidianCard = new Color(0.045f, 0.065f, 0.11f, 0.90f);
    public static readonly Color InputBg = new Color(0.08f, 0.115f, 0.185f, 0.95f);
    public static readonly Color GoldPrimary = new Color(0.86f, 0.64f, 0.24f, 1f);
    public static readonly Color GoldBright = new Color(1f, 0.84f, 0.42f, 1f);
    public static readonly Color GoldBorder = new Color(0.82f, 0.62f, 0.28f, 0.60f);
    public static readonly Color GhostButtonBg = new Color(0.10f, 0.14f, 0.22f, 0.82f);
    public static readonly Color TextPrimary = new Color(0.96f, 0.97f, 0.99f, 1f);
    public static readonly Color TextMuted = new Color(0.68f, 0.74f, 0.84f, 0.92f);
    public static readonly Color DarkButtonText = new Color(0.05f, 0.06f, 0.09f, 1f);

    private static Sprite cachedSoftDotSprite;
    private static Sprite cachedVerticalGradientSprite;
    private static Sprite cachedHorizontalGradientSprite;

    // Cấu hình CanvasScaler tự co giãn theo độ phân giải chuẩn 1920x1080
    public static void ConfigureResponsiveCanvas(Canvas canvas)
    {
        if (canvas == null) return;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    // Tạo ảnh nền toàn màn hình từ thư mục Resources
    public static RawImage CreateFullscreenKeyArt(Transform parent, string resourcePath, string objName)
    {
        GameObject bgObj = new GameObject(objName, typeof(RectTransform), typeof(RawImage));
        bgObj.transform.SetParent(parent, false);
        bgObj.transform.SetAsFirstSibling();

        RectTransform rt = bgObj.GetComponent<RectTransform>();
        StretchFull(rt);

        RawImage raw = bgObj.GetComponent<RawImage>();
        Texture2D tex = GameAssetPreloader.GetAsset<Texture2D>(resourcePath) ?? Resources.Load<Texture2D>(resourcePath);
        if (tex != null)
        {
            raw.texture = tex;
            raw.color = Color.white;
        }
        else
        {
            raw.color = new Color(0.05f, 0.07f, 0.12f, 1f);
        }
        raw.raycastTarget = false;

        AspectRatioFitter fitter = bgObj.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = tex != null && tex.height > 0 ? (float)tex.width / tex.height : 16f / 9f;

        return raw;
    }

    public static Image CreateGradientOverlay(Transform parent, string objName, bool horizontal, Color tint)
    {
        GameObject obj = new GameObject(objName, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        StretchFull(obj.GetComponent<RectTransform>());

        Image img = obj.GetComponent<Image>();
        img.sprite = horizontal ? GetHorizontalGradientSprite() : GetVerticalGradientSprite();
        img.color = tint;
        img.raycastTarget = false;
        return img;
    }

    public static GameObject CreateAccentLine(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return go;
    }

    // Tạo đối tượng văn bản TextMeshPro với font, màu sắc và căn lề chỉ định
    public static TMP_Text CreateText(
        Transform parent,
        string name,
        string content,
        float fontSize,
        FontStyles style,
        Color color,
        TextAlignmentOptions align,
        TMP_FontAsset fontAsset = null)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null) tmp.font = fontAsset;
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }

    // Áp dụng khung nền tối và viền vàng cho thẻ giao diện
    public static void StyleCardPanel(GameObject panel, Color bgColor, Color borderColor)
    {
        if (panel == null) return;
        Image img = panel.GetComponent<Image>();
        if (img == null) img = panel.AddComponent<Image>();
        img.sprite = null;
        img.color = bgColor;

        Outline outline = panel.GetComponent<Outline>();
        if (outline == null) outline = panel.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        Shadow shadow = panel.GetComponent<Shadow>();
        if (shadow == null) shadow = panel.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        shadow.effectDistance = new Vector2(0f, -10f);
    }

    // Trang trí ô nhập liệu và hiệu ứng viền khi focus
    public static void StyleInputField(TMP_InputField input, string placeholderText, float width, float height)
    {
        if (input == null) return;

        RectTransform rt = input.GetComponent<RectTransform>();
        if (rt != null) rt.sizeDelta = new Vector2(width, height);

        Image bg = input.GetComponent<Image>();
        if (bg != null)
        {
            bg.sprite = null;
            bg.color = InputBg;
        }

        Outline outline = input.GetComponent<Outline>();
        if (outline == null) outline = input.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.42f, 0.50f, 0.65f, 0.45f);
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        ColorBlock cb = input.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.08f, 1.05f, 0.98f, 1f);
        cb.selectedColor = new Color(1.15f, 1.08f, 0.92f, 1f);
        cb.pressedColor = Color.white;
        input.colors = cb;

        input.caretColor = GoldBright;
        input.selectionColor = new Color(0.86f, 0.64f, 0.24f, 0.35f);

        if (input.textViewport != null)
        {
            input.textViewport.anchorMin = Vector2.zero;
            input.textViewport.anchorMax = Vector2.one;
            input.textViewport.offsetMin = new Vector2(18f, 6f);
            input.textViewport.offsetMax = new Vector2(-18f, -6f);
        }

        if (input.textComponent != null)
        {
            input.textComponent.color = TextPrimary;
            input.textComponent.fontSize = 20f;
            input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
        }

        if (input.placeholder is TMP_Text ph)
        {
            if (!string.IsNullOrEmpty(placeholderText)) ph.text = placeholderText;
            ph.color = new Color(0.55f, 0.62f, 0.74f, 0.75f);
            ph.fontSize = 19f;
            ph.fontStyle = FontStyles.Normal;
            ph.alignment = TextAlignmentOptions.MidlineLeft;
        }
    }

    // Trang trí nút bấm chính/phụ kèm hiệu ứng chuyển màu khi di chuột
    public static void StyleButton(Button btn, string label, bool isPrimary, float width, float height)
    {
        if (btn == null) return;

        RectTransform rt = btn.GetComponent<RectTransform>();
        if (rt != null) rt.sizeDelta = new Vector2(width, height);

        Image img = btn.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = null;
            img.color = isPrimary ? GoldPrimary : GhostButtonBg;
        }

        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = isPrimary
            ? new Color(1.14f, 1.08f, 0.95f, 1f)
            : new Color(1.35f, 1.25f, 1.08f, 1f);
        cb.pressedColor = isPrimary
            ? new Color(0.82f, 0.78f, 0.72f, 1f)
            : new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        btn.colors = cb;

        Outline outline = btn.GetComponent<Outline>();
        if (outline == null) outline = btn.gameObject.AddComponent<Outline>();
        outline.effectColor = isPrimary ? new Color(1f, 0.88f, 0.55f, 0.75f) : GoldBorder;
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        TMP_Text txt = btn.GetComponentInChildren<TMP_Text>(true);
        if (txt != null)
        {
            if (!string.IsNullOrEmpty(label)) txt.text = label;
            txt.color = isPrimary ? DarkButtonText : GoldBright;
            txt.fontSize = isPrimary ? 21f : 17f;
            txt.fontStyle = FontStyles.Bold;
            txt.alignment = TextAlignmentOptions.Center;

            RectTransform trt = txt.GetComponent<RectTransform>();
            if (trt != null) StretchFull(trt);
        }
    }

    public static void StretchFull(RectTransform rt)
    {
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public static void PrewarmUiSprites()
    {
        GetSoftDotSprite();
        GetVerticalGradientSprite();
        GetHorizontalGradientSprite();
    }

    public static Sprite GetSoftDotSprite()
    {
        if (cachedSoftDotSprite != null) return cachedSoftDotSprite;

        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;
        float invHalf = 1f / half;

        for (int y = 0; y < size; y++)
        {
            int row = y * size;
            float dy = (y - half + 0.5f) * invHalf;
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) * invHalf;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[row + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        cachedSoftDotSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return cachedSoftDotSprite;
    }

    private static Sprite GetVerticalGradientSprite()
    {
        if (cachedVerticalGradientSprite != null) return cachedVerticalGradientSprite;

        const int h = 128;
        Texture2D tex = new Texture2D(2, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[2 * h];

        for (int y = 0; y < h; y++)
        {
            float t = (float)y / (h - 1);

            float bottomDark = Mathf.Pow(Mathf.Clamp01(1f - t / 0.45f), 1.6f) * 0.94f;
            float topDark = Mathf.Pow(Mathf.Clamp01((t - 0.72f) / 0.28f), 1.8f) * 0.48f;
            byte a = (byte)(Mathf.Clamp01(bottomDark + topDark) * 255f);
            Color32 c = new Color32(255, 255, 255, a);
            pixels[y * 2] = c;
            pixels[y * 2 + 1] = c;
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        cachedVerticalGradientSprite = Sprite.Create(tex, new Rect(0, 0, 2, h), new Vector2(0.5f, 0.5f));
        return cachedVerticalGradientSprite;
    }

    private static Sprite GetHorizontalGradientSprite()
    {
        if (cachedHorizontalGradientSprite != null) return cachedHorizontalGradientSprite;

        const int w = 128;
        Texture2D tex = new Texture2D(w, 2, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[w * 2];

        for (int x = 0; x < w; x++)
        {
            float t = (float)x / (w - 1);

            byte a = (byte)(Mathf.Pow(Mathf.Clamp01((t - 0.35f) / 0.65f), 1.35f) * 0.76f * 255f);
            Color32 c = new Color32(255, 255, 255, a);
            pixels[x] = c;
            pixels[w + x] = c;
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        cachedHorizontalGradientSprite = Sprite.Create(tex, new Rect(0, 0, w, 2), new Vector2(0.5f, 0.5f));
        return cachedHorizontalGradientSprite;
    }
}

// Hiệu ứng đốm lửa bay nhẹ trên nền UI các màn hình menu
public class FloatingEmbersUI : MonoBehaviour
{
    private struct Ember
    {
        public RectTransform rt;
        public Image img;
        public float speedY;
        public float driftAmp;
        public float driftFreq;
        public float phase;
        public float baseX;
        public float alphaMax;
    }

    private readonly List<Ember> embers = new List<Ember>();
    private float minXNormalized = 0f;
    private float maxXNormalized = 1f;

    public void Initialize(int count, float minXNorm, float maxXNorm, Color warmColorA, Color warmColorB)
    {
        minXNormalized = minXNorm;
        maxXNormalized = maxXNorm;

        Sprite dot = FantasyUiThemeHelper.GetSoftDotSprite();
        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject($"Ember_{i}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            float size = Random.Range(6f, 18f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);

            float startX = Mathf.Lerp(-960f, 960f, Random.Range(minXNormalized, maxXNormalized));
            float startY = Random.Range(-560f, 560f);
            rt.anchoredPosition = new Vector2(startX, startY);

            Image img = go.GetComponent<Image>();
            img.sprite = dot;
            img.color = Color.Lerp(warmColorA, warmColorB, Random.value);
            img.raycastTarget = false;

            embers.Add(new Ember
            {
                rt = rt,
                img = img,
                speedY = Random.Range(28f, 82f),
                driftAmp = Random.Range(14f, 44f),
                driftFreq = Random.Range(0.7f, 1.8f),
                phase = Random.Range(0f, Mathf.PI * 2f),
                baseX = startX,
                alphaMax = Random.Range(0.35f, 0.85f)
            });
        }
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float t = Time.unscaledTime;

        for (int i = 0; i < embers.Count; i++)
        {
            Ember e = embers[i];
            if (e.rt == null) continue;

            Vector2 pos = e.rt.anchoredPosition;
            pos.y += e.speedY * dt;
            pos.x = e.baseX + Mathf.Sin(t * e.driftFreq + e.phase) * e.driftAmp;

            if (pos.y > 580f)
            {
                pos.y = -580f;
                e.baseX = Mathf.Lerp(-960f, 960f, Random.Range(minXNormalized, maxXNormalized));
                pos.x = e.baseX;
                embers[i] = e;
            }

            e.rt.anchoredPosition = pos;

            float normY = Mathf.InverseLerp(-560f, 560f, pos.y);
            float fade = Mathf.Sin(normY * Mathf.PI);
            float flicker = 0.78f + 0.22f * Mathf.Sin(t * 4.5f + e.phase);
            Color c = e.img.color;
            c.a = e.alphaMax * fade * flicker;
            e.img.color = c;
        }
    }
}
