using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tự động thêm hiệu ứng rê chuột (phóng nhẹ + âm thanh) cho mọi Button chưa có hiệu ứng riêng,
/// ví dụ nút ở màn Đăng nhập, Chọn nhân vật, Chọn map, Cửa hàng, Kết thúc trận.
/// </summary>
public class UniversalButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private const float HoverScale = 1.05f;
    private const float PressScale = 0.96f;

    private Vector3 baseScale = Vector3.one;
    private bool hovered;
    private bool pressed;
    private Button button;

    private void Awake()
    {
        baseScale = transform.localScale;
        button = GetComponent<Button>();
        enabled = false;
    }

    private void OnDisable()
    {
        hovered = false;
        pressed = false;
        transform.localScale = baseScale;
    }

    private void Update()
    {
        bool ok = button == null || button.interactable;
        float target = !ok ? 1f : (pressed ? PressScale : (hovered ? HoverScale : 1f));
        float cur = transform.localScale.x / (baseScale.x == 0f ? 1f : baseScale.x);
        float k = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
        float s = Mathf.Lerp(cur, target, k);
        bool settled = Mathf.Abs(s - target) < 0.0005f;
        if (settled) s = target;
        transform.localScale = new Vector3(baseScale.x * s, baseScale.y * s, baseScale.z);
        if (settled && !hovered && !pressed) enabled = false;
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (button != null && !button.interactable) return;
        if (!hovered) GameAudioManager.PlayUiHover();
        hovered = true;
        enabled = true;
    }

    public void OnPointerExit(PointerEventData e) { hovered = false; pressed = false; enabled = true; }
    public void OnPointerDown(PointerEventData e) { pressed = true; enabled = true; }
    public void OnPointerUp(PointerEventData e) { pressed = false; enabled = true; }
}

/// <summary>Quét định kỳ (nhẹ) để gắn hiệu ứng hover cho các Button mới xuất hiện.</summary>
public class UniversalButtonHoverInstaller : MonoBehaviour
{
    private static UniversalButtonHoverInstaller instance;
    private float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("UniversalButtonHoverInstaller");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<UniversalButtonHoverInstaller>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) instance.Scan(); };
        instance.Scan();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 1.0f;
        Scan();
    }

    private void Scan()
    {
        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button b = buttons[i];
            if (b == null) continue;
            if (b.GetComponent<MedievalButtonFeedback>() != null || b.GetComponent<UniversalButtonHover>() != null) continue;

            RectTransform rt = b.transform as RectTransform;
            // Bỏ qua lớp phủ toàn màn hình (vd. lớp đóng popup) và nút không có đồ họa
            if (rt == null || b.targetGraphic == null) continue;
            if (rt.rect.width > 900f || rt.rect.height > 400f) continue;

            b.gameObject.AddComponent<UniversalButtonHover>();
        }
    }
}