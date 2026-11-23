using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class CreateGameEventRequest
{
    public long sessionId;
    public string eventType;
    public string eventTime;
    public int value;
}

[Serializable]
public class SingleEventItemRequest
{
    public string eventType;
    public string eventTime;
    public int value;
}

[Serializable]
public class CreateBatchGameEventsRequest
{
    public long sessionId;
    public List<SingleEventItemRequest> events = new List<SingleEventItemRequest>();
}

[Serializable]
public class OfflineEventStore
{
    public List<CreateGameEventRequest> pendingEvents = new List<CreateGameEventRequest>();
}

// Thu thập sự kiện hành vi người chơi trong trận, gom nhóm gửi theo lô (batch) lên API và lưu hàng đợi offline khi mất mạng
public class TelemetryManager : MonoBehaviour
{
    private const int ImmediateBatchSize = 10;

    public static TelemetryManager Instance { get; private set; }

    [Header("Backend API Configuration")]
    [SerializeField] private string baseUrl = ApiConfig.DEFAULT_BASE_URL;
    [SerializeField] private bool sendToBackend = true;
    [SerializeField] private SessionApiClient sessionApiClient;

    [Header("Level & Fallback Settings")]
    [SerializeField] private int currentLevel = 1;
    [SerializeField] private int fallbackTestPlayerId = 1;
    [SerializeField] private float retryInterval = 5f;
    [SerializeField] private float batchFlushDelay = 1.8f;

    public string SessionId { get; private set; }
    public int CurrentLevel
    {
        get => currentLevel;
        set => currentLevel = Mathf.Max(1, value);
    }

    private readonly List<CreateGameEventRequest> pendingEvents = new List<CreateGameEventRequest>(16);
    private readonly List<CreateGameEventRequest> sendBatch = new List<CreateGameEventRequest>(16);
    private readonly CreateBatchGameEventsRequest reusableBatchDto = new CreateBatchGameEventsRequest();

    private string offlineQueuePath;
    private bool hasOfflineFileOnDisk;
    private bool isSending;
    private bool isStartingSession;
    private bool gameEnded;
    private bool playerDeadTracked;
    private int startedLevel = -1;
    private int completedLevel = -1;
    private int failedLevel = -1;
    private Coroutine scheduledFlushRoutine;

    public string BaseUrl
    {
        get => string.IsNullOrEmpty(baseUrl) ? ApiConfig.GetBaseUrl() : baseUrl;
        set => baseUrl = value;
    }

    // Khởi tạo Singleton tồn tại xuyên scene và đọc hàng đợi sự kiện offline nếu có
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        offlineQueuePath = Path.Combine(Application.persistentDataPath, "telemetry_offline_queue.json");
        LoadOfflineQueue();
    }

    // Khởi tạo phiên chơi ban đầu khi vào scene gameplay
    private void Start()
    {
        StartNewSession();
    }

    // Đóng phiên cũ (nếu còn) và tạo phiên chơi mới cho lượt chơi hiện tại
    public void StartNewSession()
    {
        gameEnded = false;
        playerDeadTracked = false;
        startedLevel = -1;
        completedLevel = -1;
        failedLevel = -1;

        if (sessionApiClient == null)
        {
            sessionApiClient = FindFirstObjectByType<SessionApiClient>();
        }

        string savedSessionId = PlayerPrefs.GetString("SessionId", "");
        if (long.TryParse(savedSessionId, out long parsedSessionId) && parsedSessionId > 0)
        {
            SessionId = parsedSessionId.ToString();
        }
        else
        {
            SessionId = string.Empty;
            EnsureBackendSession();
        }

        if (PlayerPrefs.GetInt("PendingLoginEvent", 0) == 1)
        {
            PlayerPrefs.DeleteKey("PendingLoginEvent");
            PlayerPrefs.Save();
            TrackEvent("login", PlayerPrefs.GetInt("PlayerId", 0));
        }

        TrackEvent("game_start");
        TrackLevelStart(CurrentLevel);
        FlushPendingEvents();
    }

    private void EnsureBackendSession()
    {
        if (!sendToBackend || isStartingSession) return;

        if (sessionApiClient == null)
        {
            sessionApiClient = FindFirstObjectByType<SessionApiClient>();
            if (sessionApiClient == null)
            {
                Debug.LogWarning("[TelemetryManager] Không tìm thấy SessionApiClient trong Scene để tạo Session!");
                return;
            }
        }

        int playerId = PlayerPrefs.GetInt("PlayerId", 0);
        if (playerId <= 0)
        {
            playerId = fallbackTestPlayerId;
        }

        if (playerId <= 0)
        {
            Debug.LogWarning("[TelemetryManager] Không có PlayerId hợp lệ để tạo Session.");
            return;
        }

        isStartingSession = true;
        sessionApiClient.StartSession(playerId, (success, sessionId, message) =>
        {
            isStartingSession = false;

            if (success && sessionId > 0)
            {
                SessionId = sessionId.ToString();
                for (int i = 0; i < pendingEvents.Count; i++)
                {
                    if (pendingEvents[i].sessionId <= 0)
                    {
                        pendingEvents[i].sessionId = sessionId;
                    }
                }

                FlushPendingEvents();
            }
            else
            {
                Debug.LogWarning($"[TelemetryManager] Tạo Session thất bại: {message}");
                StartCoroutine(RetryStartSessionCoroutine());
            }
        });
    }

    private IEnumerator RetryStartSessionCoroutine()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(1f, retryInterval));
        if (string.IsNullOrEmpty(SessionId) && sendToBackend && !gameEnded)
        {
            EnsureBackendSession();
        }
    }

    // Ghi nhận một sự kiện telemetry vào bộ đệm chờ và tự động gửi khi đủ lô hoặc là sự kiện quan trọng
    public void TrackEvent(string eventType, int eventValue = 0)
    {
        if (string.IsNullOrWhiteSpace(eventType)) return;

        string normalizedType = eventType.Trim();
        if (gameEnded && normalizedType != "game_exit" && normalizedType != "game_end")
        {
            return;
        }

        if (!sendToBackend) return;

        long numericSessionId = long.TryParse(SessionId, out long parsed) && parsed > 0 ? parsed : 0;

        pendingEvents.Add(new CreateGameEventRequest
        {
            sessionId = numericSessionId,
            eventType = normalizedType,
            eventTime = DateTime.UtcNow.ToString("o"),
            value = eventValue
        });

#if UNITY_EDITOR
        Debug.Log($"[TelemetryManager] Tracked: {normalizedType} (value: {eventValue})");
#endif

        if (IsCriticalEvent(normalizedType) || pendingEvents.Count >= ImmediateBatchSize)
        {
            FlushPendingEvents();
        }
        else
        {
            ScheduleDebouncedFlush();
        }
    }

    private static bool IsCriticalEvent(string eventType)
    {
        return eventType == "player_dead"
            || eventType == "level_complete"
            || eventType == "level_failed"
            || eventType == "game_exit"
            || eventType == "game_end";
    }

    private void ScheduleDebouncedFlush()
    {
        if (!sendToBackend || isSending || scheduledFlushRoutine != null || !gameObject.activeInHierarchy)
        {
            return;
        }

        scheduledFlushRoutine = StartCoroutine(DebouncedFlushCoroutine());
    }

    private IEnumerator DebouncedFlushCoroutine()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.2f, batchFlushDelay));
        scheduledFlushRoutine = null;
        FlushPendingEvents();
    }

    public void TrackLevelStart(int level)
    {
        CurrentLevel = level;
        if (startedLevel == CurrentLevel) return;

        startedLevel = CurrentLevel;
        TrackEvent("level_start", CurrentLevel);
    }

    public void TrackLevelComplete(int level)
    {
        if (gameEnded || completedLevel == level) return;

        completedLevel = level;
        TrackEvent("level_complete", level);
    }

    public void TrackLevelFailed(int level)
    {
        if (failedLevel == level) return;

        failedLevel = level;
        TrackEvent("level_failed", level);
    }

    public void TrackEnemyKilled(int points = 100)
    {
        TrackEvent("enemy_killed", points);
    }

    public void TrackBossKilled(int stage)
    {
        TrackEvent("boss_killed", stage);
    }

    public void TrackItemPickup(int healOrValue)
    {
        TrackEvent("item_pickup", healOrValue);
    }

    public void TrackCheckpointReached(int stage)
    {
        TrackEvent("checkpoint_reached", stage);
    }

    public void TrackTrapHit(int damage)
    {
        TrackEvent("trap_hit", damage);
    }

    public void TrackBlockHit(int blockedDamage)
    {
        TrackEvent("block_hit", blockedDamage);
    }

    public void TrackPerfectParry(int stage)
    {
        TrackEvent("perfect_parry", stage);
    }

    public void TrackGoldCollected(int amount)
    {
        TrackEvent("gold_collected", amount);
    }

    public void TrackShopOpened(int stage)
    {
        TrackEvent("shop_opened", stage);
    }

    public void TrackWeaponPurchased(int price)
    {
        TrackEvent("weapon_purchased", price);
    }

    public void TrackPotionPurchased(int price)
    {
        TrackEvent("potion_purchased", price);
    }

    public void TrackPlayerDied()
    {
        if (playerDeadTracked || gameEnded) return;

        playerDeadTracked = true;
        TrackEvent("player_dead", CurrentLevel);
        TrackLevelFailed(CurrentLevel);
        TrackGameEnded("Lose");
    }

    public void TrackGameEnded(string sessionResult = "Quit")
    {
        if (gameEnded) return;

        TrackEvent("game_exit", CurrentLevel);
        gameEnded = true;

        if (sendToBackend && sessionApiClient != null && !string.IsNullOrEmpty(PlayerPrefs.GetString("SessionId", "")))
        {
            sessionApiClient.EndSession(sessionResult);
        }
    }

    // Đẩy toàn bộ sự kiện đang chờ trong bộ đệm qua API batch
    private void FlushPendingEvents()
    {
        if (scheduledFlushRoutine != null)
        {
            StopCoroutine(scheduledFlushRoutine);
            scheduledFlushRoutine = null;
        }

        if (!sendToBackend || isSending || !gameObject.activeInHierarchy) return;
        if (!HasSendableEvents()) return;

        StartCoroutine(ProcessPendingEventsCoroutine());
    }

    private bool HasSendableEvents()
    {
        for (int i = 0; i < pendingEvents.Count; i++)
        {
            if (pendingEvents[i].sessionId > 0) return true;
        }
        return false;
    }

    // Gửi lô sự kiện qua /api/Events/batch và tự thử lại hoặc lưu file cục bộ khi lỗi kết nối
    private IEnumerator ProcessPendingEventsCoroutine()
    {
        isSending = true;

        while (sendToBackend)
        {
            long targetSessionId = 0;
            sendBatch.Clear();

            for (int i = 0; i < pendingEvents.Count; i++)
            {
                if (pendingEvents[i].sessionId <= 0) continue;

                if (targetSessionId == 0)
                {
                    targetSessionId = pendingEvents[i].sessionId;
                }

                if (pendingEvents[i].sessionId == targetSessionId)
                {
                    sendBatch.Add(pendingEvents[i]);
                }
            }

            if (sendBatch.Count == 0) break;

            string url;
            string json;

            if (sendBatch.Count >= 2)
            {
                url = BaseUrl.TrimEnd('/') + "/api/Events/batch";
                reusableBatchDto.sessionId = targetSessionId;
                reusableBatchDto.events.Clear();
                for (int i = 0; i < sendBatch.Count; i++)
                {
                    reusableBatchDto.events.Add(new SingleEventItemRequest
                    {
                        eventType = sendBatch[i].eventType,
                        eventTime = sendBatch[i].eventTime,
                        value = sendBatch[i].value
                    });
                }
                json = JsonUtility.ToJson(reusableBatchDto);
            }
            else
            {
                url = BaseUrl.TrimEnd('/') + "/api/Events";
                json = JsonUtility.ToJson(sendBatch[0]);
            }

            bool removeBatch = false;

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] body = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 10;
                ApiConfig.ConfigureRequest(request, url);

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    removeBatch = true;
#if UNITY_EDITOR
                    Debug.Log($"[TelemetryManager] Sent {sendBatch.Count} event(s) successfully.");
#endif
                }
                else if (request.responseCode == 400 || request.responseCode == 404)
                {

                    removeBatch = true;
                    Debug.LogWarning($"[TelemetryManager] Dropped {sendBatch.Count} invalid/orphan event(s) (HTTP {request.responseCode}): {request.downloadHandler?.text}");
                }
                else
                {
                    Debug.LogWarning($"[TelemetryManager] Network/Server error ({request.error}), sẽ thử gửi lại sau {retryInterval}s.");
                }
            }

            if (removeBatch)
            {
                for (int i = 0; i < sendBatch.Count; i++)
                {
                    pendingEvents.Remove(sendBatch[i]);
                }

                if (hasOfflineFileOnDisk)
                {
                    SaveOfflineQueue();
                }
            }
            else
            {
                SaveOfflineQueue();
                yield return new WaitForSecondsRealtime(Mathf.Max(1f, retryInterval));
            }
        }

        isSending = false;
    }

    // Đọc lại các sự kiện chưa gửi được từ tệp JSON lưu trên thiết bị
    private void LoadOfflineQueue()
    {
        try
        {
            if (string.IsNullOrEmpty(offlineQueuePath) || !File.Exists(offlineQueuePath))
            {
                hasOfflineFileOnDisk = false;
                return;
            }

            hasOfflineFileOnDisk = true;
            string json = File.ReadAllText(offlineQueuePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return;

            OfflineEventStore store = JsonUtility.FromJson<OfflineEventStore>(json);
            if (store?.pendingEvents == null) return;

            for (int i = 0; i < store.pendingEvents.Count; i++)
            {
                CreateGameEventRequest evt = store.pendingEvents[i];
                if (evt != null && evt.sessionId > 0 && !string.IsNullOrWhiteSpace(evt.eventType))
                {
                    pendingEvents.Add(evt);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TelemetryManager] Lỗi đọc offline queue: {ex.Message}");
        }
    }

    // Lưu tạm hàng đợi sự kiện xuống thiết bị để tránh mất dữ liệu khi mất kết nối
    private void SaveOfflineQueue()
    {
        if (string.IsNullOrEmpty(offlineQueuePath)) return;

        try
        {
            var store = new OfflineEventStore();
            for (int i = 0; i < pendingEvents.Count; i++)
            {
                if (pendingEvents[i].sessionId > 0)
                {
                    store.pendingEvents.Add(pendingEvents[i]);
                }
            }

            if (store.pendingEvents.Count == 0)
            {
                if (hasOfflineFileOnDisk && File.Exists(offlineQueuePath))
                {
                    File.Delete(offlineQueuePath);
                }
                hasOfflineFileOnDisk = false;
                return;
            }

            string json = JsonUtility.ToJson(store);
            File.WriteAllText(offlineQueuePath, json, Encoding.UTF8);
            hasOfflineFileOnDisk = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TelemetryManager] Lỗi ghi offline queue: {ex.Message}");
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && pendingEvents.Count > 0)
        {
            SaveOfflineQueue();
        }
    }

    private void OnApplicationQuit()
    {
        if (!gameEnded)
        {
            TrackGameEnded("Quit");
        }
        SaveOfflineQueue();
    }
}
