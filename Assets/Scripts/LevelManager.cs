
using UnityEngine;
using TMPro;

public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [Header("Level Settings")]
    [SerializeField]
    private int[] enemyTargets =
    {
        5, 10, 15, 20, 30
    };

    [Header("UI")]
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text enemyCountText;
    [SerializeField] private GameObject levelCompletePanel;
    [SerializeField] private TMP_Text levelCompleteText;

    [Header("Enemy Spawner")]
    [SerializeField] private GameObject enemySpawner;

    private int currentLevel = 1;
    private int killedEnemies = 0;
    private bool levelCompleted = false;

    public int CurrentLevel => currentLevel;
    public int KilledEnemies => killedEnemies;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        Time.timeScale = 1f;

        if (levelCompletePanel != null)
            levelCompletePanel.SetActive(false);

        UpdateUI();

        // Ghi nhận bắt đầu Level đầu tiên.
        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackLevelStart(currentLevel);
        }
    }

    // Được gọi khi một Enemy bị tiêu diệt.
    public void EnemyKilled()
    {
        if (levelCompleted)
            return;

        killedEnemies++;

        Debug.Log(
            "Level " + currentLevel +
            ": " + killedEnemies + "/" +
            enemyTargets[currentLevel - 1]);

        UpdateUI();

        if (killedEnemies >= enemyTargets[currentLevel - 1])
        {
            CompleteLevel();
        }
    }

    private void CompleteLevel()
    {
        if (levelCompleted)
            return;

        levelCompleted = true;

        // Dừng sinh Enemy.
        if (enemySpawner != null)
            enemySpawner.SetActive(false);

        // Gửi Telemetry.
        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackLevelComplete(currentLevel);
        }

        if (levelCompletePanel != null)
            levelCompletePanel.SetActive(true);

        if (currentLevel == enemyTargets.Length)
        {
            if (levelCompleteText != null)
                levelCompleteText.text = "CHUC MUNG! BAN DA HOAN THANH GAME!";
        }
        else
        {
            if (levelCompleteText != null)
                levelCompleteText.text =
                    "LEVEL " + currentLevel + " COMPLETED!";
        }

        Debug.Log("Hoàn thành Level " + currentLevel);

        Time.timeScale = 0f;
    }

    // Gắn hàm này vào nút Next Level.
    public void NextLevel()
    {
        if (!levelCompleted)
            return;

        // Đã hoàn thành toàn bộ 5 Level.
        if (currentLevel >= enemyTargets.Length)
        {
            Debug.Log("Người chơi đã hoàn thành toàn bộ game!");
            return;
        }

        currentLevel++;
        killedEnemies = 0;
        levelCompleted = false;

        Time.timeScale = 1f;

        if (levelCompletePanel != null)
            levelCompletePanel.SetActive(false);

        // Xóa Enemy còn sống của Level trước.
        EnemyHealth[] remainingEnemies =
            FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);

        foreach (EnemyHealth enemy in remainingEnemies)
        {
            if (enemy != null)
                Destroy(enemy.gameObject);
        }

        // Bật lại EnemySpawner.
        if (enemySpawner != null)
            enemySpawner.SetActive(true);

        UpdateUI();

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackLevelStart(currentLevel);
        }

        Debug.Log("Bắt đầu Level " + currentLevel);
    }

    private void UpdateUI()
    {
        if (levelText != null)
            levelText.text = "LEVEL " + currentLevel;

        if (enemyCountText != null)
        {
            enemyCountText.text =
                "ENEMY: " + killedEnemies + "/" +
                enemyTargets[currentLevel - 1];
        }
    }

    // Gọi khi người chơi thất bại.
    public void FailLevel()
    {
        if (levelCompleted)
            return;

        levelCompleted = true;

        if (enemySpawner != null)
            enemySpawner.SetActive(false);

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackLevelFailed(currentLevel);
        }

        Time.timeScale = 0f;
    }
}
