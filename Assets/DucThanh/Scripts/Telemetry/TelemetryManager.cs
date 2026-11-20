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

/// <summary>
/// Quản lý và gửi sự kiện Telemetry lên Backend API (/api/Events & /api/Events/batch).
/// - Tự động kết nối SessionApiClient để khởi tạo & kết thúc phiên chơi.
/// - Lưu hàng đợi sự kiện offline xuống file JSON và tự retry khi mất mạng.
/// - Chống gửi trùng các sự kiện trạng thái (level_start, level_complete, level_failed, player_dead, game_exit).
/// </summary>
public class TelemetryManager : MonoBehaviour
{
    public static TelemetryManager Instance { get; private set; }

    [Header("Backend API Configuration")]
    [SerializeField] private string baseUrl = ApiConfig.DEVTUNNEL_URL;
    [SerializeField] private bool sendToBackend = true;
    [SerializeField] private SessionApiClient sessionApiClient;

    [Header("Level & Fallback Settings")]
    [SerializeField] private int currentLevel = 1;
    [SerializeField] private int fallbackTestPlayerId = 1;
    [SerializeField] private float retryInterval = 5f;

    public string SessionId { get; private set; }
    public int CurrentLevel
    {
        get => currentLevel;
        set => currentLevel = Mathf.Max(1, value);
    }

    private readonly List<CreateGameEventRequest> pendingEvents = new List<CreateGameEventRequest>();
    private bool isSending;
    private bool isStartingSession;
    private bool gameEnded;
    private bool playerDeadTracked;
    private int startedLevel = -1;
    private int completedLevel = -1;
    private int failedLevel = -1;

    private string OfflineQueuePath => Path.Combine(Application.persistentDataPath, "telemetry_offline_queue.json");

    public string BaseUrl
    {
        get => string.IsNullOrEmpty(baseUrl) ? ApiConfig.GetBaseUrl() : baseUrl;
        set => baseUrl = value;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        LoadOfflineQueue();
    }

    private void Start()
    {
        StartNewSession();
    }

    /// <summary>
    /// Bắt đầu phiên chơi mới: dùng SessionId đã có hoặc gọi Backend tạo Session mới,
    /// đồng thời gửi game_start và level_start.
    /// </summary>
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
            Debug.Log($"[TelemetryManager] SESSION RESUMED: {SessionId}");
        }
        else
        {
            SessionId = string.Empty;
            EnsureBackendSession();
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

                SaveOfflineQueue();
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

    /// <summary>
    /// Ghi nhận sự kiện telemetry vào hàng đợi và đẩy lên Backend (hoặc lưu offline nếu chưa có SessionId/mất mạng).
    /// </summary>
    public void TrackEvent(string eventType, int eventValue = 0)
    {
        if (string.IsNullOrWhiteSpace(eventType)) return;

        if (gameEnded && eventType != "game_exit" && eventType != "game_end")
        {
            Debug.LogWarning("[TelemetryManager] Lượt chơi đã kết thúc, bỏ qua event!");
            return;
        }

        long numericSessionId = long.TryParse(SessionId, out long parsed) && parsed > 0 ? parsed : 0;

        CreateGameEventRequest evt = new CreateGameEventRequest
        {
            sessionId = numericSessionId,
            eventType = eventType.Trim(),
            eventTime = DateTime.UtcNow.ToString("o"),
            value = eventValue
        };

        Debug.Log($"[TelemetryManager] Tracked: {evt.eventType} (value: {evt.value})");

        if (!sendToBackend) return;

        pendingEvents.Add(evt);
        SaveOfflineQueue();
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

    private void FlushPendingEvents()
    {
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

    private IEnumerator ProcessPendingEventsCoroutine()
    {
        isSending = true;

        while (sendToBackend)
        {
            long targetSessionId = 0;
            List<CreateGameEventRequest> batch = new List<CreateGameEventRequest>();

            for (int i = 0; i < pendingEvents.Count; i++)
            {
                if (pendingEvents[i].sessionId <= 0) continue;

                if (targetSessionId == 0)
                {
                    targetSessionId = pendingEvents[i].sessionId;
                }

                if (pendingEvents[i].sessionId == targetSessionId)
                {
                    batch.Add(pendingEvents[i]);
                }
            }

            if (batch.Count == 0) break;

            string url;
            string json;

            if (batch.Count >= 2)
            {
                url = BaseUrl.TrimEnd('/') + "/api/Events/batch";
                var batchDto = new CreateBatchGameEventsRequest { sessionId = targetSessionId };
                for (int i = 0; i < batch.Count; i++)
                {
                    batchDto.events.Add(new SingleEventItemRequest
                    {
                        eventType = batch[i].eventType,
                        eventTime = batch[i].eventTime,
                        value = batch[i].value
                    });
                }
                json = JsonUtility.ToJson(batchDto);
            }
            else
            {
                url = BaseUrl.TrimEnd('/') + "/api/Events";
                json = JsonUtility.ToJson(batch[0]);
            }

            bool removeBatch = false;

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] body = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 10;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    removeBatch = true;
                    Debug.Log($"[TelemetryManager] Sent {batch.Count} event(s) successfully.");
                }
                else if (request.responseCode == 400 || request.responseCode == 404)
                {
                    // Dữ liệu lỗi hoặc SessionId không tồn tại -> bỏ khỏi hàng đợi để tránh retry vô hạn
                    removeBatch = true;
                    Debug.LogWarning($"[TelemetryManager] Dropped {batch.Count} invalid/orphan event(s) (HTTP {request.responseCode}): {request.downloadHandler?.text}");
                }
                else
                {
                    Debug.LogWarning($"[TelemetryManager] Network/Server error ({request.error}), sẽ thử gửi lại sau {retryInterval}s.");
                }
            }

            if (removeBatch)
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    pendingEvents.Remove(batch[i]);
                }
                SaveOfflineQueue();
            }
            else
            {
                SaveOfflineQueue();
                yield return new WaitForSecondsRealtime(Mathf.Max(1f, retryInterval));
            }
        }

        isSending = false;
    }

    private void LoadOfflineQueue()
    {
        try
        {
            if (!File.Exists(OfflineQueuePath)) return;

            string json = File.ReadAllText(OfflineQueuePath, Encoding.UTF8);
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

    private void SaveOfflineQueue()
    {
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
                if (File.Exists(OfflineQueuePath))
                {
                    File.Delete(OfflineQueuePath);
                }
                return;
            }

            string json = JsonUtility.ToJson(store);
            File.WriteAllText(OfflineQueuePath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TelemetryManager] Lỗi ghi offline queue: {ex.Message}");
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
