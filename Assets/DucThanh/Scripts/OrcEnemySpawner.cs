using UnityEngine;

// Quản lý sinh quái Orc theo đợt và tăng dần độ khó theo cấp độ màn
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
    public int maxAliveEnemies = 12;

    [Header("Level Scaling")]
    public float spawnIntervalStepPerLevel = 0.5f;
    public float minSpawnInterval = 1.5f;
    public int bonusHealthPerLevel = 20;
    public float bonusSpeedPerLevel = 0.3f;

    private int currentLevel = 1;
    private float timer;

    // Tự động tìm Player nếu chưa được gán sẵn trong Inspector
    void Start()
    {

        if (StageManager.Instance != null)
        {
            enabled = false;
            return;
        }

        if (player == null)
        {
            ResolvePlayer();
        }

        if (ScoreManager.Instance != null)
        {
            currentLevel = ScoreManager.Instance.currentLevel;
        }
    }

    // Kiểm tra số lượng quái còn sống và sinh bổ sung theo chu kỳ
    void Update()
    {
        if (GameOverManager.IsGameOver || WeaponShopManager.IsShopOpen) return;

        if (player == null)
        {
            ResolvePlayer();
            if (player == null) return;
        }

        if (enemyPrefabs == null || enemyPrefabs.Length == 0) return;

        if (ScoreManager.Instance != null)
        {
            currentLevel = ScoreManager.Instance.currentLevel;
        }

        if (OrcEnemyAI.ActiveEnemyCount >= maxAliveEnemies) return;

        float effectiveInterval = Mathf.Max(
            minSpawnInterval,
            spawnInterval - Mathf.Max(0, currentLevel - 1) * spawnIntervalStepPerLevel);

        timer += Time.deltaTime;

        if (timer >= effectiveInterval)
        {
            timer = 0f;
            SpawnEnemy();
        }
    }

    private void ResolvePlayer()
    {
        if (PlayerController.Instance != null)
        {
            player = PlayerController.Instance.transform;
            return;
        }

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }
    }

    // Chọn prefab quái ngẫu nhiên, đặt lên NavMesh quanh người chơi và nhân chỉ số
    void SpawnEnemy()
    {
        Vector2 randomDirection = Random.insideUnitCircle.normalized;
        if (randomDirection.sqrMagnitude < 0.01f)
        {
            randomDirection = Vector2.right;
        }

        float distance = Random.Range(minimumDistance, spawnRadius);
        Vector3 spawnPosition = player.position + new Vector3(randomDirection.x, 0f, randomDirection.y) * distance;

        spawnPosition.y = player.position.y;

        if (Physics.Raycast(new Vector3(spawnPosition.x, player.position.y + 50f, spawnPosition.z), Vector3.down, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
        {
            spawnPosition.y = hit.point.y;
        }

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
