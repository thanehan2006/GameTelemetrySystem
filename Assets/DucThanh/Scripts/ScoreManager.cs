using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Lưu trữ điểm số, lượng vàng hiện có của người chơi và cập nhật hiển thị lên HUD
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance;

    [Header("Score & Gold Settings")]
    public int score = 0;
    public int gold = 0;
    public int totalGoldEarned = 0;

    [HideInInspector] public int currentLevel = 1;
    [HideInInspector] public int maxLevel = 10;
    [HideInInspector] public int baseExpToLevelUp = 200;
    [HideInInspector] public int expIncreasePerLevel = 150;
    [HideInInspector] public bool manageStageTelemetry = false;

    [Header("UI")]
    public TMP_Text scoreText;
    public TMP_Text levelText;
    public Slider expSlider;

    public event Action<int> OnGoldChanged;

    private RectTransform goldHudRect;
    private Image goldGemImage;
    private float goldPopTimer;

    // Khởi tạo Singleton và tìm text hiển thị vàng trên giao diện
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (expSlider == null)
        {
            GameObject sliderObj = GameObject.Find("ExpBar");
            if (sliderObj != null) expSlider = sliderObj.GetComponent<Slider>();
        }

        StyleExpBarAsGoldHud();
        UpdateGoldHud();
    }

    // Đồng bộ giao diện thanh vàng ban đầu
    private void Start()
    {

        if (WeaponShopManager.Instance == null && GetComponent<WeaponShopManager>() == null)
        {
            gameObject.AddComponent<WeaponShopManager>();
        }
    }

    // Cộng điểm chiến công khi hạ gục kẻ địch
    public void AddScore(int points)
    {
        if (GameOverManager.IsGameOver) return;
        score += Mathf.Max(0, points);
    }

    // Cộng vàng nhặt được và làm mới HUD
    public void AddGold(int amount)
    {
        if (GameOverManager.IsGameOver || amount <= 0) return;

        gold += amount;
        totalGoldEarned += amount;
        goldPopTimer = 0.24f;
        UpdateGoldHud();
        OnGoldChanged?.Invoke(gold);
    }

    // Tạo hiệu ứng nảy nhẹ khung hiển thị vàng khi vừa nhặt thưởng
    private void Update()
    {
        if (goldPopTimer <= 0f || goldHudRect == null) return;

        goldPopTimer = Mathf.Max(0f, goldPopTimer - Time.unscaledDeltaTime);
        float t = goldPopTimer / 0.24f;
        float bump = 1f + Mathf.Sin(t * Mathf.PI) * 0.045f;
        goldHudRect.localScale = new Vector3(bump, bump, 1f);

        if (goldGemImage != null)
        {
            goldGemImage.color = Color.Lerp(new Color(1f, 0.80f, 0.24f, 1f), Color.white, t * 0.85f);
        }
    }

    // Kiểm tra và trừ vàng khi người chơi mua vũ khí hoặc bình máu
    public bool TrySpendGold(int cost)
    {
        if (cost < 0 || gold < cost) return false;

        gold -= cost;
        UpdateGoldHud();
        OnGoldChanged?.Invoke(gold);
        return true;
    }

    // Áp dụng giao diện Dark-Fantasy đồng bộ cho thẻ hiển thị vàng và phím mở cửa hàng
    private void StyleExpBarAsGoldHud()
    {
        if (expSlider == null) return;

        CombatHudStyler.StyleGoldHud(
            expSlider.gameObject,
            scoreText,
            out TMP_Text styledGoldText,
            out goldHudRect,
            out goldGemImage);

        if (scoreText == null && styledGoldText != null)
        {
            scoreText = styledGoldText;
        }
    }

    // Cập nhật số vàng hiện tại và gợi ý mở cửa hàng lên màn hình
    private void UpdateGoldHud()
    {
        if (scoreText != null)
        {
            scoreText.text = $"<color=#FFD85C><b>{gold:N0} G</b></color>   <color=#5C6A85>•</color>   <color=#E8C56E><b>[B]</b></color> <color=#DDE5F5>CỬA HÀNG</color>";
            scoreText.color = Color.white;
        }

        if (levelText != null)
        {
            levelText.text = $"{gold:N0} G";
        }
    }
}
