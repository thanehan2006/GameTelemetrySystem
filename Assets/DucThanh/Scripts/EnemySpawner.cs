using UnityEngine;

// Bộ sinh quái đơn giản theo chu kỳ thời gian xung quanh vị trí người chơi
public class EnemySpawner : MonoBehaviour
{
    [Header("Enemy Prefab")]
    public GameObject enemyPrefab;

    [Header("Player Target")]
    public Transform player;

    [Header("Spawn Settings")]
    public float spawnInterval = 5f;
    public float spawnRadius = 10f;
    public float minimumDistance = 7f;

    private float timer;

    // Khởi tạo đồng hồ đếm và tìm mục tiêu Player
    void Start()
    {
        FindPlayerTarget();
    }

    private void FindPlayerTarget()
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

    // Đếm thời gian hồi để gọi sinh quái khi chưa đạt giới hạn tối đa
    void Update()
    {
        if (GameOverManager.IsGameOver) return;

        if (player == null)
        {
            FindPlayerTarget();
            if (player == null) return;
        }

        if (enemyPrefab == null) return;

        timer += Time.deltaTime;
        if (timer >= spawnInterval)
        {
            timer = 0f;
            SpawnEnemy();
        }
    }

    // Chọn vị trí ngẫu nhiên trên vòng tròn quanh người chơi và tạo quái
    void SpawnEnemy()
    {
        Vector2 randomCircle = Random.insideUnitCircle.normalized;
        if (randomCircle.sqrMagnitude < 0.01f)
        {
            randomCircle = Vector2.right;
        }

        float distance = Random.Range(minimumDistance, spawnRadius);
        Vector3 spawnPosition = player.position + new Vector3(randomCircle.x, 0f, randomCircle.y) * distance;
        spawnPosition.y = 0.75f;

        Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
    }
}
