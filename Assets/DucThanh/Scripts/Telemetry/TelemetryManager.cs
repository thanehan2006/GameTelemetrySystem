using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class CreateGameEventRequest
{
    public long sessionId;
    public string localSessionId;
    public string eventType;
    public string eventTime;
    public int value;
    public string clientEventId;
}

[Serializable]
public class SingleEventItemRequest
{
    public string eventType;
    public string eventTime;
    public int value;
    public string clientEventId;
}

[Serializable]
public class CreateBatchGameEventsRequest
{
    public long sessionId;
    public List<SingleEventItemRequest> events = new List<SingleEventItemRequest>();
}

[Serializable]
public class PendingOfflineSession
{
    public string localSessionId;
    public int playerId;
    public int levelId;
    public string characterId;
    public string startTime;
    public bool isEnded;
    public string endResult;
    public string endTime;
}

[Serializable]
public class PendingEndSessionRequest
{
    public long sessionId;
    public string result;
    public string endTime;
}

[Serializable]
public class OfflineEventStore
{
    public List<PendingOfflineSession> pendingOfflineSessions = new List<PendingOfflineSession>();
    public List<CreateGameEventRequest> pendingEvents = new List<CreateGameEventRequest>();
    public List<PendingEndSessionRequest> pendingEndSessions = new List<PendingEndSessionRequest>();
}

// Thu thập sự kiện hành vi người chơi trong trận, gắn ClientEventId chống trùng, gửi theo lô (batch) và lưu hàng đợi offline khi mất mạng
public class TelemetryManager : MonoBehaviour
{
    private const int ImmediateBatchSize = 10;
    private const int MaxPendingEvents = 2000;
    private const int MaxEventsPerBatch = 25;
    private const float OfflineSaveIntervalSeconds = 20f;
    private static readonly object OfflineFileLock = new object();
    private static long latestSaveGen;

    public static TelemetryManager Instance { get; private set; }

    [Header("Backend API Configuration")]
    [SerializeField] private string baseUrl = ApiConfig.DEFAULT_BASE_URL;
    [SerializeField] private bool sendToBackend = true;
    [SerializeField] private SessionApiClient sessionApiClient;
    [SerializeField] private bool verboseDebugLogs = false;

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

    private readonly List<PendingOfflineSession> pendingOfflineSessions = new List<PendingOfflineSession>(4);
    private readonly List<CreateGameEventRequest> pendingEvents = new List<CreateGameEventRequest>(32);
    private readonly List<CreateGameEventRequest> sendBatch = new List<CreateGameEventRequest>(25);
    private readonly CreateBatchGameEventsRequest reusableBatchDto = new CreateBatchGameEventsRequest();
    private readonly List<PendingEndSessionRequest> pendingEndSessions = new List<PendingEndSessionRequest>(4);
    private readonly HashSet<long> inFlightEndSessions = new HashSet<long>();
    private readonly HashSet<CreateGameEventRequest> sentBatchSet = new HashSet<CreateGameEventRequest>();

    private string offlineQueuePath;
    private bool hasOfflineFileOnDisk;
    private bool offlineQueueDirty;
    private float nextOfflineSaveUnscaledTime;
    private bool isSending;
    private bool isFlushingOfflineSessions;
    private bool isFlushingEndSessions;
    private bool isQuitting;
    private bool gameEnded;
    private bool playerDeadTracked;
    private string currentLocalSessionId;
    private int startedLevel = -1;
    private int completedLevel = -1;
    private int failedLevel = -1;
    private Coroutine scheduledFlushRoutine;
    private Coroutine retryOfflineSessionsRoutine;
    private Coroutine retryEndSessionsRoutine;
    private WaitForSecondsRealtime cachedRetryWait;
    private float cachedRetryWaitSeconds = -1f;
    private WaitForSecondsRealtime cachedBatchWait;
    private float cachedBatchWaitSeconds = -1f;

    public string BaseUrl
    {
        get => ApiConfig.ResolveBaseUrl(baseUrl);
        set => baseUrl = value;
    }

    // Khởi tạo Singleton và đọc hàng đợi sự kiện/kết thúc phiên offline nếu có
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

    // Khởi tạo phiên chơi ban đầu và gửi bù các phiên cũ đang chờ đóng
    private void Start()
    {
        StartNewSession();
        FlushPendingEndSessions();
    }

    // Tạo phiên chơi mới cho lượt chơi hiện tại
    public void StartNewSession()
    {
        gameEnded = false;
        playerDeadTracked = false;
        isQuitting = false;
        currentLocalSessionId = null;
        startedLevel = -1;
        completedLevel = -1;
        failedLevel = -1;

        if (sessionApiClient == null)
        {
            sessionApiClient = FindFirstObjectByType<SessionApiClient>();
        }

        StageManager stageManager = StageManager.Instance != null ? StageManager.Instance : FindFirstObjectByType<StageManager>();
        if (stageManager != null)
        {
            CurrentLevel = stageManager.MapLevel;
        }

        string savedSessionId = PlayerPrefs.GetString("SessionId", "");
        if (long.TryParse(savedSessionId, out long parsedSessionId) && parsedSessionId > 0)
        {
            SessionId = parsedSessionId.ToString();
        }
        else
        {
            SessionId = string.Empty;
            int playerId = ResolvePlayerId();
            if (sendToBackend && playerId > 0)
            {
                CharacterProfile selectedProfile = SelectedCharacterStore.GetSelectedProfile();
                string charId = selectedProfile != null && !string.IsNullOrWhiteSpace(selectedProfile.id)
                    ? selectedProfile.id.Trim().ToLowerInvariant()
                    : SessionApiClient.ResolveSelectedCharacterId();

                var offlineSession = new PendingOfflineSession
                {
                    localSessionId = Guid.NewGuid().ToString("N"),
                    playerId = playerId,
                    levelId = CurrentLevel,
                    characterId = charId,
                    startTime = DateTime.UtcNow.ToString("o"),
                    isEnded = false,
                    endResult = string.Empty,
                    endTime = string.Empty
                };
                pendingOfflineSessions.Add(offlineSession);
                currentLocalSessionId = offlineSession.localSessionId;
                offlineQueueDirty = true;
                SaveOfflineQueue(synchronous: false, force: true);
            }
            else if (sendToBackend)
            {
                Debug.LogWarning("[TelemetryManager] Không có PlayerId hợp lệ để tạo Session.");
            }
        }

        if (PlayerPrefs.GetInt("PendingLoginEvent", 0) == 1)
        {
            PlayerPrefs.DeleteKey("PendingLoginEvent");
            PlayerPrefs.Save();
            TrackEvent("login", PlayerPrefs.GetInt("PlayerId", 0));
        }

        TrackEvent("game_start");
        TrackLevelStart(CurrentLevel);
        FlushOfflineSessions();
        FlushPendingEvents();
        FlushPendingEndSessions();
    }

    private int ResolvePlayerId()
    {
        int playerId = PlayerPrefs.GetInt("PlayerId", 0);
        return playerId > 0 ? playerId : fallbackTestPlayerId;
    }

    private PendingOfflineSession FindOfflineSession(string localSessionId)
    {
        if (string.IsNullOrWhiteSpace(localSessionId)) return null;
        for (int i = 0; i < pendingOfflineSessions.Count; i++)
        {
            PendingOfflineSession s = pendingOfflineSessions[i];
            if (s != null && string.Equals(s.localSessionId, localSessionId, StringComparison.Ordinal))
            {
                return s;
            }
        }
        return null;
    }

    // Đồng bộ tuần tự các phiên offline (cũ đến mới) lên API để cấp SessionId và gắn lại vào sự kiện chờ
    private void FlushOfflineSessions()
    {
        if (!sendToBackend || isFlushingOfflineSessions || pendingOfflineSessions.Count == 0 || !gameObject.activeInHierarchy)
        {
            return;
        }

        if (retryOfflineSessionsRoutine != null)
        {
            StopCoroutine(retryOfflineSessionsRoutine);
            retryOfflineSessionsRoutine = null;
        }

        if (sessionApiClient == null)
        {
            sessionApiClient = FindFirstObjectByType<SessionApiClient>();
            if (sessionApiClient == null)
            {
                Debug.LogWarning("[TelemetryManager] Không tìm thấy SessionApiClient trong Scene để tạo/đồng bộ Session!");
                offlineQueueDirty = true;
                SaveOfflineQueue(synchronous: false, force: true);
                return;
            }
        }

        StartCoroutine(ProcessOfflineSessionsCoroutine());
    }

    private IEnumerator ProcessOfflineSessionsCoroutine()
    {
        isFlushingOfflineSessions = true;

        while (sendToBackend && pendingOfflineSessions.Count > 0)
        {
            PendingOfflineSession s = pendingOfflineSessions[0];
            if (s == null || string.IsNullOrWhiteSpace(s.localSessionId) || s.playerId <= 0)
            {
                pendingOfflineSessions.RemoveAt(0);
                offlineQueueDirty = true;
                continue;
            }

            bool done = false;
            bool ok = false;
            long backendSessionId = 0;
            long statusCode = 0;
            string errorMsg = null;

            bool savePref = !s.isEnded && string.Equals(s.localSessionId, currentLocalSessionId, StringComparison.Ordinal);
            sessionApiClient.StartSession(s.playerId, s.levelId, s.characterId, s.startTime, savePref, (success, sessionId, code, message) =>
            {
                ok = success;
                backendSessionId = sessionId;
                statusCode = code;
                errorMsg = message;
                done = true;
            });

            while (!done)
            {
                yield return null;
            }

            if (ok && backendSessionId > 0)
            {
                for (int i = 0; i < pendingEvents.Count; i++)
                {
                    if (string.Equals(pendingEvents[i].localSessionId, s.localSessionId, StringComparison.Ordinal))
                    {
                        pendingEvents[i].sessionId = backendSessionId;
                        pendingEvents[i].localSessionId = string.Empty;
                    }
                }

                bool isCurrent = string.Equals(s.localSessionId, currentLocalSessionId, StringComparison.Ordinal);
                if (isCurrent)
                {
                    SessionId = backendSessionId.ToString();
                    currentLocalSessionId = null;
                    if (!s.isEnded)
                    {
                        PlayerPrefs.SetString("SessionId", SessionId);
                        PlayerPrefs.Save();
                    }
                    else if (PlayerPrefs.GetString("SessionId", "") == SessionId)
                    {
                        PlayerPrefs.DeleteKey("SessionId");
                        PlayerPrefs.Save();
                    }
                }
                else if (s.isEnded && PlayerPrefs.GetString("SessionId", "") == backendSessionId.ToString())
                {
                    PlayerPrefs.DeleteKey("SessionId");
                    PlayerPrefs.Save();
                }

                if (s.isEnded)
                {
                    string safeResult = string.IsNullOrWhiteSpace(s.endResult) ? "Quit" : s.endResult.Trim();
                    EnqueueEndSession(backendSessionId, safeResult, s.endTime);
                }

                pendingOfflineSessions.Remove(s);
                offlineQueueDirty = true;
                SaveOfflineQueue(synchronous: true, force: true);

                FlushPendingEvents();
                FlushPendingEndSessions();
            }
            else if (statusCode == 400 || statusCode == 404)
            {
                Debug.LogWarning($"[TelemetryManager] Hủy phiên offline không hợp lệ (HTTP {statusCode}): {errorMsg}");
                for (int i = pendingEvents.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(pendingEvents[i].localSessionId, s.localSessionId, StringComparison.Ordinal))
                    {
                        pendingEvents.RemoveAt(i);
                    }
                }
                pendingOfflineSessions.Remove(s);
                offlineQueueDirty = true;
                SaveOfflineQueue(synchronous: true, force: true);
            }
            else
            {
                Debug.LogWarning($"[TelemetryManager] Tạo/đồng bộ Session thất bại (HTTP {statusCode}): {errorMsg}");
                offlineQueueDirty = true;
                SaveOfflineQueue(synchronous: false, force: true);
                if (!isQuitting)
                {
                    ScheduleRetryOfflineSessions();
                }
                break;
            }
        }

        isFlushingOfflineSessions = false;
    }

    private void ScheduleRetryOfflineSessions()
    {
        if (!sendToBackend || isQuitting || retryOfflineSessionsRoutine != null || !gameObject.activeInHierarchy)
        {
            return;
        }

        retryOfflineSessionsRoutine = StartCoroutine(RetryOfflineSessionsCoroutine());
    }

    private IEnumerator RetryOfflineSessionsCoroutine()
    {
        yield return GetRetryWait();
        retryOfflineSessionsRoutine = null;
        if (pendingOfflineSessions.Count > 0 && !isFlushingOfflineSessions)
        {
            FlushOfflineSessions();
        }
    }

    // Ghi nhận một sự kiện telemetry kèm ClientEventId duy nhất vào bộ đệm chờ và tự động gửi khi đủ lô hoặc là sự kiện quan trọng
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
        string localId = numericSessionId > 0 ? string.Empty : (currentLocalSessionId ?? string.Empty);

        if (pendingEvents.Count >= MaxPendingEvents)
        {
            pendingEvents.RemoveAt(0);
        }

        pendingEvents.Add(new CreateGameEventRequest
        {
            sessionId = numericSessionId,
            localSessionId = localId,
            eventType = normalizedType,
            eventTime = DateTime.UtcNow.ToString("o"),
            value = eventValue,
            clientEventId = Guid.NewGuid().ToString("N")
        });
        offlineQueueDirty = true;

#if UNITY_EDITOR
        if (verboseDebugLogs)
        {
            Debug.Log($"[TelemetryManager] Tracked: {normalizedType} (value: {eventValue})");
        }
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

    private WaitForSecondsRealtime GetBatchWait()
    {
        float delay = Mathf.Max(0.2f, batchFlushDelay);
        if (cachedBatchWait == null || !Mathf.Approximately(cachedBatchWaitSeconds, delay))
        {
            cachedBatchWaitSeconds = delay;
            cachedBatchWait = new WaitForSecondsRealtime(delay);
        }
        return cachedBatchWait;
    }

    private WaitForSecondsRealtime GetRetryWait()
    {
        float delay = Mathf.Max(1f, retryInterval);
        if (cachedRetryWait == null || !Mathf.Approximately(cachedRetryWaitSeconds, delay))
        {
            cachedRetryWaitSeconds = delay;
            cachedRetryWait = new WaitForSecondsRealtime(delay);
        }
        return cachedRetryWait;
    }

    private IEnumerator DebouncedFlushCoroutine()
    {
        yield return GetBatchWait();
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
        // Đã chuẩn hóa schema 5 bảng: bỏ qua sự kiện không còn lưu trữ
    }

    public void TrackCheckpointReached(int stage)
    {
        // Đã chuẩn hóa schema 5 bảng: bỏ qua sự kiện không còn lưu trữ
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
        // Đã chuẩn hóa schema 5 bảng: bỏ qua sự kiện không còn lưu trữ
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

        long endingSessionId = ResolveCurrentNumericSessionId();
        PlayerPrefs.DeleteKey("SessionId");
        PlayerPrefs.Save();

        if (!sendToBackend) return;

        string normalizedResult = string.IsNullOrWhiteSpace(sessionResult) ? "Quit" : sessionResult.Trim();
        if (normalizedResult.Length > 20)
        {
            normalizedResult = normalizedResult.Substring(0, 20);
        }

        if (endingSessionId > 0)
        {
            EnqueueEndSession(endingSessionId, normalizedResult, DateTime.UtcNow.ToString("o"));
            offlineQueueDirty = true;
            SaveOfflineQueue(synchronous: true, force: true);
            FlushPendingEndSessions();
        }
        else if (!string.IsNullOrEmpty(currentLocalSessionId))
        {
            PendingOfflineSession offline = FindOfflineSession(currentLocalSessionId);
            if (offline != null)
            {
                offline.isEnded = true;
                offline.endResult = normalizedResult;
                offline.endTime = DateTime.UtcNow.ToString("o");
                offlineQueueDirty = true;
                SaveOfflineQueue(synchronous: true, force: true);
                FlushOfflineSessions();
            }
        }
    }

    // Đẩy sự kiện + đóng phiên chơi với thời gian chờ ngắn trước khi thoát ứng dụng
    public void EndSessionAndQuit(string sessionResult, Action onComplete, float maxWaitSeconds = 0.8f)
    {
        if (isQuitting)
        {
            return;
        }

        isQuitting = true;

        if (!gameObject.activeInHierarchy)
        {
            TrackGameEnded(sessionResult);
            SaveOfflineQueue(synchronous: true, force: true);
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(EndSessionAndQuitCoroutine(sessionResult, onComplete, maxWaitSeconds));
    }

    private IEnumerator EndSessionAndQuitCoroutine(string sessionResult, Action onComplete, float maxWaitSeconds)
    {
        TrackGameEnded(sessionResult);
        FlushOfflineSessions();
        FlushPendingEvents();
        FlushPendingEndSessions();

        float deadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, maxWaitSeconds);
        while (Time.realtimeSinceStartup < deadline
            && (isFlushingOfflineSessions || pendingOfflineSessions.Count > 0 || isSending || HasSendableEvents() || inFlightEndSessions.Count > 0 || pendingEndSessions.Count > 0))
        {
            yield return null;
        }

        SaveOfflineQueue(synchronous: true, force: true);
        onComplete?.Invoke();
    }

    private long ResolveCurrentNumericSessionId()
    {
        if (long.TryParse(SessionId, out long parsed) && parsed > 0)
        {
            return parsed;
        }

        string saved = PlayerPrefs.GetString("SessionId", "");
        return long.TryParse(saved, out long savedParsed) && savedParsed > 0 ? savedParsed : 0;
    }

    private void EnqueueEndSession(long sessionId, string result, string endTimeIso = null)
    {
        if (sessionId <= 0) return;

        for (int i = 0; i < pendingEndSessions.Count; i++)
        {
            if (pendingEndSessions[i].sessionId == sessionId)
            {
                return;
            }
        }

        string safeResult = string.IsNullOrWhiteSpace(result) ? "Quit" : result.Trim();
        if (safeResult.Length > 20)
        {
            safeResult = safeResult.Substring(0, 20);
        }

        string safeEndTime = string.IsNullOrWhiteSpace(endTimeIso)
            ? DateTime.UtcNow.ToString("o")
            : endTimeIso.Trim();

        pendingEndSessions.Add(new PendingEndSessionRequest
        {
            sessionId = sessionId,
            result = safeResult,
            endTime = safeEndTime
        });
    }

    private void FlushPendingEndSessions()
    {
        if (!sendToBackend || pendingEndSessions.Count == 0 || !gameObject.activeInHierarchy) return;

        if (sessionApiClient == null)
        {
            sessionApiClient = FindFirstObjectByType<SessionApiClient>();
            if (sessionApiClient == null)
            {
                offlineQueueDirty = true;
                SaveOfflineQueue(synchronous: false);
                return;
            }
        }

        isFlushingEndSessions = true;
        var snapshot = new List<PendingEndSessionRequest>(pendingEndSessions);
        for (int i = 0; i < snapshot.Count; i++)
        {
            PendingEndSessionRequest req = snapshot[i];
            if (req == null || req.sessionId <= 0 || inFlightEndSessions.Contains(req.sessionId))
            {
                continue;
            }

            inFlightEndSessions.Add(req.sessionId);
            sessionApiClient.EndSessionById(req.sessionId, req.result, req.endTime, (success, message) =>
            {
                inFlightEndSessions.Remove(req.sessionId);
                if (success)
                {
                    pendingEndSessions.Remove(req);
                    offlineQueueDirty = true;
                    if (hasOfflineFileOnDisk)
                    {
                        SaveOfflineQueue(synchronous: false);
                    }
                }
                else
                {
                    offlineQueueDirty = true;
                    SaveOfflineQueue(synchronous: false);
                    ScheduleRetryEndSessions();
                }

                if (inFlightEndSessions.Count == 0)
                {
                    isFlushingEndSessions = false;
                }
            });
        }

        if (inFlightEndSessions.Count == 0)
        {
            isFlushingEndSessions = false;
        }
    }

    private void ScheduleRetryEndSessions()
    {
        if (!sendToBackend || isQuitting || retryEndSessionsRoutine != null || !gameObject.activeInHierarchy)
        {
            return;
        }

        retryEndSessionsRoutine = StartCoroutine(RetryEndSessionsCoroutine());
    }

    private IEnumerator RetryEndSessionsCoroutine()
    {
        yield return GetRetryWait();
        retryEndSessionsRoutine = null;
        if (pendingEndSessions.Count > 0 && !isFlushingEndSessions)
        {
            FlushPendingEndSessions();
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
                    if (sendBatch.Count >= MaxEventsPerBatch) break;
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
                    if (string.IsNullOrEmpty(sendBatch[i].clientEventId))
                    {
                        sendBatch[i].clientEventId = Guid.NewGuid().ToString("N");
                    }

                    reusableBatchDto.events.Add(new SingleEventItemRequest
                    {
                        eventType = sendBatch[i].eventType,
                        eventTime = sendBatch[i].eventTime,
                        value = sendBatch[i].value,
                        clientEventId = sendBatch[i].clientEventId
                    });
                }
                json = JsonUtility.ToJson(reusableBatchDto);
            }
            else
            {
                if (string.IsNullOrEmpty(sendBatch[0].clientEventId))
                {
                    sendBatch[0].clientEventId = Guid.NewGuid().ToString("N");
                }
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
                    if (verboseDebugLogs)
                    {
                        Debug.Log($"[TelemetryManager] Sent {sendBatch.Count} event(s) successfully.");
                    }
#endif
                }
                else if (request.responseCode == 400 || request.responseCode == 404)
                {
                    removeBatch = true;
#if UNITY_EDITOR
                    Debug.LogWarning($"[TelemetryManager] Dropped {sendBatch.Count} invalid/orphan event(s) (HTTP {request.responseCode}): {request.downloadHandler?.text}");
#endif
                }
                else
                {
#if UNITY_EDITOR
                    if (verboseDebugLogs)
                    {
                        Debug.LogWarning($"[TelemetryManager] Network/Server error ({request.error}), sẽ thử gửi lại sau {retryInterval}s.");
                    }
#endif
                }
            }

            if (removeBatch)
            {
                sentBatchSet.Clear();
                for (int i = 0; i < sendBatch.Count; i++)
                {
                    sentBatchSet.Add(sendBatch[i]);
                }
                for (int i = pendingEvents.Count - 1; i >= 0; i--)
                {
                    if (sentBatchSet.Contains(pendingEvents[i]))
                    {
                        pendingEvents.RemoveAt(i);
                    }
                }
                sentBatchSet.Clear();
                offlineQueueDirty = true;

                if (hasOfflineFileOnDisk)
                {
                    SaveOfflineQueue(synchronous: false);
                }
            }
            else
            {
                offlineQueueDirty = true;
                SaveOfflineQueue(synchronous: false);
                if (isQuitting) break;
                yield return GetRetryWait();
            }
        }

        isSending = false;
    }

    // Đọc lại các phiên offline, sự kiện & yêu cầu đóng phiên chưa gửi được từ tệp JSON lưu trên thiết bị
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
            if (store == null) return;

            var validLocalIds = new HashSet<string>(StringComparer.Ordinal);
            if (store.pendingOfflineSessions != null)
            {
                for (int i = 0; i < store.pendingOfflineSessions.Count; i++)
                {
                    PendingOfflineSession s = store.pendingOfflineSessions[i];
                    if (s != null && !string.IsNullOrWhiteSpace(s.localSessionId) && s.playerId > 0 && validLocalIds.Add(s.localSessionId))
                    {
                        if (string.IsNullOrWhiteSpace(s.startTime))
                        {
                            s.startTime = DateTime.UtcNow.ToString("o");
                        }
                        pendingOfflineSessions.Add(s);
                    }
                }
            }

            if (store.pendingEvents != null)
            {
                for (int i = 0; i < store.pendingEvents.Count; i++)
                {
                    CreateGameEventRequest evt = store.pendingEvents[i];
                    if (evt == null || string.IsNullOrWhiteSpace(evt.eventType)) continue;

                    bool hasBackendId = evt.sessionId > 0;
                    bool hasValidLocalId = !string.IsNullOrWhiteSpace(evt.localSessionId) && validLocalIds.Contains(evt.localSessionId);
                    if (hasBackendId || hasValidLocalId)
                    {
                        if (string.IsNullOrEmpty(evt.clientEventId))
                        {
                            evt.clientEventId = Guid.NewGuid().ToString("N");
                        }
                        pendingEvents.Add(evt);
                    }
                }
            }

            if (store.pendingEndSessions != null)
            {
                for (int i = 0; i < store.pendingEndSessions.Count; i++)
                {
                    PendingEndSessionRequest endReq = store.pendingEndSessions[i];
                    if (endReq != null && endReq.sessionId > 0)
                    {
                        pendingEndSessions.Add(endReq);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TelemetryManager] Lỗi đọc offline queue: {ex.Message}");
        }
    }

    // Lưu tạm hàng đợi phiên offline, sự kiện và phiên chờ đóng xuống thiết bị để tránh mất dữ liệu khi mất kết nối
    private void SaveOfflineQueue(bool synchronous = false, bool force = false)
    {
        if (string.IsNullOrEmpty(offlineQueuePath)) return;
        if (!synchronous && !force && (!offlineQueueDirty || Time.unscaledTime < nextOfflineSaveUnscaledTime))
        {
            return;
        }

        if (!synchronous)
        {
            nextOfflineSaveUnscaledTime = Time.unscaledTime + OfflineSaveIntervalSeconds;
        }

        try
        {
            var store = new OfflineEventStore();
            for (int i = 0; i < pendingOfflineSessions.Count; i++)
            {
                PendingOfflineSession s = pendingOfflineSessions[i];
                if (s != null && !string.IsNullOrWhiteSpace(s.localSessionId) && s.playerId > 0)
                {
                    store.pendingOfflineSessions.Add(s);
                }
            }

            for (int i = 0; i < pendingEvents.Count; i++)
            {
                CreateGameEventRequest evt = pendingEvents[i];
                if (evt.sessionId > 0 || !string.IsNullOrWhiteSpace(evt.localSessionId))
                {
                    store.pendingEvents.Add(evt);
                }
            }

            for (int i = 0; i < pendingEndSessions.Count; i++)
            {
                if (pendingEndSessions[i].sessionId > 0)
                {
                    store.pendingEndSessions.Add(pendingEndSessions[i]);
                }
            }

            offlineQueueDirty = false;
            string path = offlineQueuePath;

            if (store.pendingOfflineSessions.Count == 0 && store.pendingEvents.Count == 0 && store.pendingEndSessions.Count == 0)
            {
                bool hadFile = hasOfflineFileOnDisk;
                hasOfflineFileOnDisk = false;
                if (!hadFile) return;

                long deleteGen = Interlocked.Increment(ref latestSaveGen);
                if (synchronous)
                {
                    lock (OfflineFileLock)
                    {
                        if (File.Exists(path)) File.Delete(path);
                    }
                }
                else
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            lock (OfflineFileLock)
                            {
                                if (deleteGen != Interlocked.Read(ref latestSaveGen)) return;
                                if (File.Exists(path)) File.Delete(path);
                            }
                        }
                        catch { }
                    });
                }
                return;
            }

            string json = JsonUtility.ToJson(store);
            hasOfflineFileOnDisk = true;
            long writeGen = Interlocked.Increment(ref latestSaveGen);

            if (synchronous)
            {
                lock (OfflineFileLock)
                {
                    File.WriteAllText(path, json, Encoding.UTF8);
                }
            }
            else
            {
                Task.Run(() =>
                {
                    try
                    {
                        lock (OfflineFileLock)
                        {
                            if (writeGen != Interlocked.Read(ref latestSaveGen)) return;
                            File.WriteAllText(path, json, Encoding.UTF8);
                        }
                    }
                    catch { }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TelemetryManager] Lỗi ghi offline queue: {ex.Message}");
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && (pendingOfflineSessions.Count > 0 || pendingEvents.Count > 0 || pendingEndSessions.Count > 0))
        {
            SaveOfflineQueue(synchronous: true, force: true);
        }
    }

    private void OnApplicationQuit()
    {
        if (!gameEnded)
        {
            TrackGameEnded("Quit");
        }
        SaveOfflineQueue(synchronous: true, force: true);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            if (!gameEnded && !string.IsNullOrEmpty(currentLocalSessionId))
            {
                PendingOfflineSession currentOffline = FindOfflineSession(currentLocalSessionId);
                if (currentOffline != null && !currentOffline.isEnded)
                {
                    currentOffline.isEnded = true;
                    currentOffline.endResult = "Quit";
                    currentOffline.endTime = DateTime.UtcNow.ToString("o");
                    offlineQueueDirty = true;
                }
                currentLocalSessionId = null;
            }
            SaveOfflineQueue(synchronous: true, force: true);
            Instance = null;
        }
    }
}
