using UnityEngine;

/// <summary>
/// Điểm lưu Checkpoint tại các Trại Lửa (SM_Campfire).
/// Khi Player bước vào vùng trại lần đầu sẽ hồi nhẹ máu, thông báo trên HUD và gửi sự kiện "checkpoint_reached".
/// </summary>
public class CampfireCheckpoint : MonoBehaviour
{
    [Header("Checkpoint Settings")]
    public int stageNumber = 1;
    public string checkpointName = "Trại Rừng Ngoại Vi";
    public float activationRadius = 5.5f;
    public int healBonus = 40;

    private Transform playerTransform;
    private PlayerHealth playerHealth;
    private bool activated;

    void Start()
    {
        ResolvePlayer();
    }

    void Update()
    {
        if (activated || GameOverManager.IsGameOver || LevelUpChoiceManager.IsPaused) return;

        if (playerTransform == null || playerHealth == null)
        {
            ResolvePlayer();
            if (playerTransform == null || playerHealth == null) return;
        }

        if (!playerHealth.IsAlive) return;

        Vector3 diff = playerTransform.position - transform.position;
        diff.y = 0f;

        if (diff.sqrMagnitude <= activationRadius * activationRadius)
        {
            ActivateCheckpoint();
        }
    }

    private void ActivateCheckpoint()
    {
        activated = true;
        GameAudioManager.PlayCheckpoint();

        if (healBonus > 0 && playerHealth.currentHealth < playerHealth.maxHealth)
        {
            playerHealth.Heal(healBonus);
        }

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackCheckpointReached(stageNumber);
        }

        if (StageManager.Instance != null)
        {
            StageManager.Instance.ShowBanner($"CHECKPOINT: {checkpointName.ToUpper()} (+{healBonus} HP)", new Color(1f, 0.78f, 0.25f), 2.5f);
        }

        Debug.Log($"[CampfireCheckpoint] Đã kích hoạt Checkpoint Màn {stageNumber}: {checkpointName}");
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
