
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    public GameObject enemyPrefab;
    public Transform player;

    public float spawnInterval = 5f;
    public float spawnRadius = 10f;
    public float minimumDistance = 7f;

    private float timer;

    void Update()
    {
        if (player == null || enemyPrefab == null) return;

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

        Vector3 spawnPosition = player.position
            + new Vector3(randomDirection.x, 0f, randomDirection.y) * distance;

        spawnPosition.y = 0.75f;

        Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
    }
}
