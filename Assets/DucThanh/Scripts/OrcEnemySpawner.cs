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

    private float timer;

    void Start()
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }
        }
    }

    void Update()
    {
        if (GameOverManager.IsGameOver) return;

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
            Instantiate(selectedPrefab, spawnPosition, Quaternion.identity);
        }
    }
}
