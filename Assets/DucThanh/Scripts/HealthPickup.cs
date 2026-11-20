using UnityEngine;

/// <summary>
/// Vật phẩm hồi máu (Nấm hồi máu đặt sẵn trong màn hoặc rơi từ quái).
/// Tự xoay nhẹ + phát sáng dịu, khi Player chạm vào sẽ hồi HP và gửi sự kiện Telemetry "item_pickup".
/// </summary>
public class HealthPickup : MonoBehaviour
{
    [Header("Pickup Settings")]
    public int healAmount = 45;
    public float pickupRadius = 1.75f;
    public float rotateSpeed = 70f;
    public float bobAmplitude = 0.12f;
    public float bobFrequency = 2.6f;

    private Transform playerTransform;
    private PlayerHealth playerHealth;
    private Vector3 basePosition;
    private bool collected;

    void Start()
    {
        basePosition = transform.position;

        foreach (Collider col in GetComponentsInChildren<Collider>(true))
        {
            col.isTrigger = true;
        }

        // Thêm ánh sáng xanh dịu giúp người chơi dễ thấy vật phẩm hồi máu trên địa hình
        if (GetComponentInChildren<Light>() == null)
        {
            GameObject lightObj = new GameObject("PickupGlow");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = Vector3.up * 0.6f;
            Light glow = lightObj.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.25f, 1f, 0.5f);
            glow.range = 4.5f;
            glow.intensity = 2.0f;
        }

        ResolvePlayer();
    }

    void Update()
    {
        if (collected || GameOverManager.IsGameOver || LevelUpChoiceManager.IsPaused) return;

        transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
        float yOffset = Mathf.Sin(Time.time * bobFrequency) * bobAmplitude;
        transform.position = new Vector3(basePosition.x, basePosition.y + yOffset, basePosition.z);

        if (playerTransform == null || playerHealth == null)
        {
            ResolvePlayer();
            if (playerTransform == null || playerHealth == null) return;
        }

        if (!playerHealth.IsAlive) return;

        Vector3 diff = playerTransform.position - basePosition;
        float verticalDiff = Mathf.Abs(diff.y);
        diff.y = 0f;

        if (verticalDiff <= 2.5f && diff.sqrMagnitude <= pickupRadius * pickupRadius)
        {
            Collect();
        }
    }

    private void Collect()
    {
        collected = true;
        playerHealth.Heal(healAmount);
        GameAudioManager.PlayPickup();

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackItemPickup(healAmount);
        }

        if (StageManager.Instance != null)
        {
            StageManager.Instance.ShowBanner($"+{healAmount} HP (NHẶT VẬT PHẨM)", new Color(0.25f, 0.95f, 0.5f), 1.8f);
        }

        Debug.Log($"[HealthPickup] Player nhặt vật phẩm hồi +{healAmount} HP.");
        Destroy(gameObject);
    }

    private void ResolvePlayer()
    {
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            playerHealth = playerObj.GetComponent<PlayerHealth>();
        }
    }
}
