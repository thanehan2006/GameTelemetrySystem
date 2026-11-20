using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance;

    [Header("Score & Level Settings")]
    public int score = 0;
    public int currentLevel = 1;
    public int maxLevel = 10;
    public int baseExpToLevelUp = 200;
    public int expIncreasePerLevel = 200;

    [Header("UI")]
    public TMP_Text scoreText;
    public TMP_Text levelText;
    public Slider expSlider;
    [SerializeField] private float barSmoothSpeed = 10f;

    public event Action<int> OnLevelUp;

    private int currentExp;
    private bool hasWon;
    private float targetExpValue;

    public int RequiredExpForCurrentLevel => Mathf.Max(1, baseExpToLevelUp + (currentLevel - 1) * expIncreasePerLevel);

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

        currentLevel = Mathf.Max(1, currentLevel);
        if (maxLevel < 10) maxLevel = 10;
        baseExpToLevelUp = Mathf.Max(1, baseExpToLevelUp);
        expIncreasePerLevel = Mathf.Max(0, expIncreasePerLevel);
        currentExp = 0;
        hasWon = false;

        if (expSlider == null)
        {
            GameObject sliderObj = GameObject.Find("ExpBar");
            if (sliderObj != null) expSlider = sliderObj.GetComponent<Slider>();
        }

        if (expSlider != null)
        {
            expSlider.minValue = 0f;
            expSlider.maxValue = RequiredExpForCurrentLevel;
        }

        UpdateScoreUI(true);
    }

    private void Start()
    {
        if (LevelUpChoiceManager.Instance == null && GetComponent<LevelUpChoiceManager>() == null)
        {
            gameObject.AddComponent<LevelUpChoiceManager>();
        }
    }

    private void Update()
    {
        if (expSlider == null) return;

        if (!Mathf.Approximately(expSlider.value, targetExpValue))
        {
            expSlider.value = Mathf.Lerp(expSlider.value, targetExpValue, Time.unscaledDeltaTime * barSmoothSpeed);
            if (Mathf.Abs(expSlider.value - targetExpValue) < 0.5f)
            {
                expSlider.value = targetExpValue;
            }
        }
    }

    public void AddScore(int points)
    {
        if (hasWon || GameOverManager.IsGameOver) return;

        score += points;
        currentExp += points;
        bool leveledUp = CheckLevelUp();
        UpdateScoreUI(leveledUp && !hasWon);

        Debug.Log($"[ScoreManager] Lv.{currentLevel} | EXP: {currentExp}/{RequiredExpForCurrentLevel} | Total Score: {score}");
    }

    private bool CheckLevelUp()
    {
        bool leveledUp = false;

        while (!hasWon && currentExp >= RequiredExpForCurrentLevel)
        {
            int required = RequiredExpForCurrentLevel;
            int completedLevel = currentLevel;

            if (maxLevel > 0 && completedLevel >= maxLevel)
            {
                hasWon = true;
                currentExp = required;

                if (TelemetryManager.Instance != null)
                {
                    TelemetryManager.Instance.TrackLevelComplete(completedLevel);
                    TelemetryManager.Instance.TrackGameEnded("Win");
                }

                if (GameOverManager.Instance != null)
                {
                    GameOverManager.Instance.ShowVictory();
                }

                Debug.Log($"[ScoreManager] VICTORY! Hoàn thành tất cả {maxLevel} màn với {score} điểm!");
                break;
            }

            currentExp -= required;
            currentLevel++;
            leveledUp = true;

            if (TelemetryManager.Instance != null)
            {
                TelemetryManager.Instance.TrackLevelComplete(completedLevel);
                TelemetryManager.Instance.TrackLevelStart(currentLevel);
            }

            Debug.Log($"[ScoreManager] LEVEL UP! Hoàn thành Level {completedLevel} -> Lên Level {currentLevel} (cần {RequiredExpForCurrentLevel} điểm cho cấp tiếp theo)");
            OnLevelUp?.Invoke(currentLevel);
        }

        return leveledUp;
    }

    private void UpdateScoreUI(bool instantSlider)
    {
        int required = RequiredExpForCurrentLevel;
        int displayExp = hasWon ? required : Mathf.Clamp(currentExp, 0, required);

        targetExpValue = displayExp;

        if (expSlider != null)
        {
            expSlider.maxValue = required;
            if (instantSlider)
            {
                expSlider.value = displayExp;
            }
        }

        if (levelText != null)
        {
            levelText.text = $"Lv.{currentLevel}";
            if (scoreText != null)
            {
                scoreText.text = $"EXP: {displayExp}/{required}";
            }
        }
        else if (scoreText != null)
        {
            scoreText.text = $"Lv.{currentLevel} | EXP: {displayExp}/{required}";
        }
    }
}
