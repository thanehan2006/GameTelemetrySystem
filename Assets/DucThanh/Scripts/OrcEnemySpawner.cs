using UnityEngine;

public class OrcEnemySpawner : MonoBehaviour
{
    [Header("Enemy Prefabs to Spawn")]
    public GameObject[] enemyPrefabs;

    [Header("Player Reference")]
    public Transform player;

    [Header("Spawn Settings")]
    public float spawnInterval = 4f;
    public float spawnRadius = 12f;
    public float minimumDistance = 6f;

    [Header("Level Scaling")]
    public float spawnIntervalStepPerLevel = 0.5f;
    public float minSpawnInterval = 1.5f;
    public int bonusHealthPerLevel = 20;
    public float bonusSpeedPerLevel = 0.3f;

    private float baseSpawnInterval;
    private int currentLevel = 1;
    private float timer;

    void Start()
    {
        baseSpawnInterval = spawnInterval;

        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }
        }

        if (ScoreManager.Instance != null)
        {
            currentLevel = ScoreManager.Instance.currentLevel;
            ScoreManager.Instance.OnLevelUp += HandleLevelUp;
        }
    }

    private void OnDestroy()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnLevelUp -= HandleLevelUp;
        }
    }

    private void HandleLevelUp(int newLevel)
    {
        currentLevel = newLevel;
        spawnInterval = Mathf.Max(minSpawnInterval, baseSpawnInterval - (newLevel - 1) * spawnIntervalStepPerLevel);
        Debug.Log($"[OrcEnemySpawner] Tăng độ khó Level {newLevel}: spawnInterval = {spawnInterval:0.0}s");
    }

    void Update()
    {
        if (GameOverManager.IsGameOver || LevelUpChoiceManager.IsPaused) return;

        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }
            else return;
        }

        if (enemyPrefabs == null || enemyPrefabs.Length == 0) return;

        timer += Time.deltaTime;

        if (timer >= spawnInterval)
        {
            timer = 0f;
            SpawnEnemy();
        }
    }

    void SpawnEnemy()
    {
        Vector2 randomDirection = Random.insideUnitCircle.normalized;
        if (randomDirection.sqrMagnitude < 0.01f)
        {
            randomDirection = Vector2.right;
        }

        float distance = Random.Range(minimumDistance, spawnRadius);
        Vector3 spawnPosition = player.position + new Vector3(randomDirection.x, 0f, randomDirection.y) * distance;

        // Đặt độ cao chân chạm đúng mặt đất (Ground y = 0)
        spawnPosition.y = 0f;

        // Bắn Raycast kiểm tra bề mặt sàn để đảm bảo chính xác vị trí chân chạm đất
        if (Physics.Raycast(new Vector3(spawnPosition.x, 10f, spawnPosition.z), Vector3.down, out RaycastHit hit, 20f))
        {
            spawnPosition.y = hit.point.y;
        }

        // Chọn ngẫu nhiên một prefab trong danh sách enemyPrefabs
        int randomIndex = Random.Range(0, enemyPrefabs.Length);
        GameObject selectedPrefab = enemyPrefabs[randomIndex];

        if (selectedPrefab != null)
        {
            GameObject enemy = Instantiate(selectedPrefab, spawnPosition, Quaternion.identity);
            ApplyLevelScaling(enemy);
        }
    }

    private void ApplyLevelScaling(GameObject enemy)
    {
        int extraLevels = Mathf.Max(0, currentLevel - 1);
        if (extraLevels == 0) return;

        EnemyHealth health = enemy.GetComponent<EnemyHealth>();
        if (health != null)
        {
            health.SetMaxHealth(health.maxHealth + extraLevels * bonusHealthPerLevel);
        }

        OrcEnemyAI ai = enemy.GetComponent<OrcEnemyAI>();
        if (ai != null)
        {
            ai.moveSpeed += extraLevels * bonusSpeedPerLevel;
        }
    }
}
