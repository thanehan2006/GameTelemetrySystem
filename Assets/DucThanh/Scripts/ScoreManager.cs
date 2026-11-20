using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Quản lý Điểm (Score) và Vàng (Gold) của Player trong trận đấu.
/// Tái sử dụng thanh ExpBar ở góc trên-phải thành khung hiển thị Vàng & phím mở Cửa Hàng [B].
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance;

    [Header("Score & Gold Settings")]
    public int score = 0;
    public int gold = 0;
    public int totalGoldEarned = 0;

    // Giữ lại các trường cũ để tương thích scene/inspector, không còn bật popup lên cấp chọn đồ
    [HideInInspector] public int currentLevel = 1;
    [HideInInspector] public int maxLevel = 10;
    [HideInInspector] public int baseExpToLevelUp = 200;
    [HideInInspector] public int expIncreasePerLevel = 150;
    [HideInInspector] public bool manageStageTelemetry = false;

    [Header("UI")]
    public TMP_Text scoreText;
    public TMP_Text levelText;
    public Slider expSlider;
    [SerializeField] private float barSmoothSpeed = 10f;

    public event Action<int> OnLevelUp;
    public event Action<int> OnGoldChanged;

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

    private void Start()
    {
        // Tự khởi tạo Cửa Hàng Vũ Khí (thay thế hoàn toàn LevelUpChoiceManager)
        if (WeaponShopManager.Instance == null && GetComponent<WeaponShopManager>() == null)
        {
            gameObject.AddComponent<WeaponShopManager>();
        }
    }

    public void AddScore(int points)
    {
        if (GameOverManager.IsGameOver) return;
        score += Mathf.Max(0, points);
    }

    public void AddGold(int amount)
    {
        if (GameOverManager.IsGameOver || amount <= 0) return;

        gold += amount;
        totalGoldEarned += amount;
        UpdateGoldHud();
        OnGoldChanged?.Invoke(gold);
        Debug.Log($"[ScoreManager] +{amount} Gold | Hiện có: {gold} G (Tổng: {totalGoldEarned} G)");
    }

    public bool TrySpendGold(int cost)
    {
        if (cost < 0 || gold < cost) return false;

        gold -= cost;
        UpdateGoldHud();
        OnGoldChanged?.Invoke(gold);
        return true;
    }

    private void StyleExpBarAsGoldHud()
    {
        if (expSlider == null) return;

        expSlider.interactable = false;
        expSlider.minValue = 0f;
        expSlider.maxValue = 1f;
        expSlider.value = 0f;

        // Ẩn thanh fill xanh cũ để biến khung này thành thẻ hiển thị Vàng gọn đẹp
        if (expSlider.fillRect != null)
        {
            expSlider.fillRect.gameObject.SetActive(false);
        }

        Transform bg = expSlider.transform.Find("Background");
        if (bg != null)
        {
            Image bgImg = bg.GetComponent<Image>();
            if (bgImg != null) bgImg.color = new Color(0.06f, 0.08f, 0.13f, 0.88f);

            Outline outline = bg.GetComponent<Outline>();
            if (outline == null) outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.90f, 0.70f, 0.25f, 0.65f);
            outline.effectDistance = new Vector2(1.2f, -1.2f);
        }
    }

    private void UpdateGoldHud()
    {
        if (scoreText != null)
        {
            scoreText.text = $"VÀNG: {gold:N0} G   •   [B] CỬA HÀNG";
            scoreText.color = new Color(1f, 0.86f, 0.34f, 1f);
            scoreText.fontSize = 15f;
        }

        if (levelText != null)
        {
            levelText.text = $"{gold:N0} G";
        }
    }
}
