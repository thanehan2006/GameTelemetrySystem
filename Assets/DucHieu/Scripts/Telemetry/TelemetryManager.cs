
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Một Event được lưu trong hàng đợi.
// hasValue xác định Event có cần gửi trường value hay không.
[Serializable]
public class TelemetryEvent
{
    public string eventType;
    public string eventTime;
    public int value;
    public bool hasValue;
}

// Dùng để lưu hàng đợi vào PlayerPrefs.
[Serializable]
public class TelemetryEventQueue
{
    public List<TelemetryEvent> events = new List<TelemetryEvent>();
}

public class TelemetryManager : MonoBehaviour
{
    public static TelemetryManager Instance { get; private set; }

    private const string BASE_URL =
        "https://sjf1zkj0-7013.asse.devtunnels.ms";

    private const string QUEUE_KEY = "TelemetryEventQueue";

    // Phải khớp với SessionApiClient.
    private const string SESSION_KEY = "SessionId";

    public long SessionId { get; private set; }

    private readonly List<TelemetryEvent> eventQueue =
        new List<TelemetryEvent>();

    private bool isSending = false;
    private bool gameEnded = false;
    private bool gameExitTracked = false;

    private float retryTimer = 0f;
    private const float RETRY_INTERVAL = 10f;
    private const int MAX_BATCH_SIZE = 100;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadSessionId();
        LoadQueue();
    }

    private void Start()
    {
        if (SessionId > 0)
        {
            Debug.Log("Telemetry Session ID: " + SessionId);
            TryFlushQueue();
        }
        else
        {
            Debug.LogWarning(
                "Chưa có SessionId. Hãy tạo Session trước khi gửi Event.");
        }
    }

    private void Update()
    {
        if (eventQueue.Count == 0 || SessionId <= 0 || isSending)
            return;

        retryTimer += Time.deltaTime;

        if (retryTimer >= RETRY_INTERVAL)
        {
            retryTimer = 0f;
            TryFlushQueue();
        }
    }

    // Lấy SessionId do SessionApiClient lưu.
    private void LoadSessionId()
    {
        string sessionString =
            PlayerPrefs.GetString(SESSION_KEY, "");

        if (long.TryParse(sessionString, out long id))
        {
            SessionId = id;
        }
        else
        {
            SessionId = 0;
        }
    }

    // Có thể gọi hàm này sau khi SessionApiClient tạo Session thành công.
    public void SetSession(long sessionId)
    {
        if (sessionId <= 0)
        {
            Debug.LogError("SessionId không hợp lệ!");
            return;
        }

        SessionId = sessionId;
        PlayerPrefs.SetString(SESSION_KEY, sessionId.ToString());
        PlayerPrefs.Save();

        gameEnded = false;
        gameExitTracked = false;

        Debug.Log("Telemetry sử dụng Session: " + SessionId);

        TryFlushQueue();
    }

    // =========================
    // TẠO VÀ GỬI EVENT
    // =========================

    public void TrackEvent(string eventType, int? value = null)
    {
        if (SessionId <= 0)
        {
            Debug.LogWarning(
                "Không gửi Event vì chưa có SessionId: " + eventType);
            return;
        }

        if (gameEnded)
        {
            Debug.LogWarning("Session đã kết thúc!");
            return;
        }

        TelemetryEvent newEvent = new TelemetryEvent
        {
            eventType = eventType,
            eventTime = DateTime.UtcNow.ToString("o"),
            hasValue = value.HasValue,
            value = value.GetValueOrDefault()
        };

        Debug.Log("Telemetry Event: " + eventType);

        // Nếu đang gửi hoặc đã có Event tồn đọng,
        // thêm Event mới vào hàng đợi.
        if (isSending || eventQueue.Count > 0)
        {
            EnqueueEvent(newEvent);
            TryFlushQueue();
            return;
        }

        // Nếu không có Event tồn đọng, gửi trực tiếp.
        StartCoroutine(SendSingleEvent(newEvent));
    }

    // Gửi một Event lên POST /api/Events.
    private IEnumerator SendSingleEvent(TelemetryEvent telemetryEvent)
    {
        isSending = true;

        string json = BuildSingleEventJson(telemetryEvent);

        using (UnityWebRequest request = new UnityWebRequest(
            BASE_URL + "/api/Events", "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader(
                "Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log(
                    "Gửi Event thành công: " + telemetryEvent.eventType);
            }
            else
            {
                Debug.LogWarning(
                    "Gửi Event thất bại: " + request.error);

                Debug.LogWarning(
                    "Event được đưa vào hàng đợi.");

                EnqueueEvent(telemetryEvent);
            }
        }

        isSending = false;

        if (eventQueue.Count > 0)
        {
            TryFlushQueue();
        }
    }

    // =========================
    // HÀNG ĐỢI EVENT
    // =========================

    private void EnqueueEvent(TelemetryEvent telemetryEvent)
    {
        eventQueue.Add(telemetryEvent);
        SaveQueue();

        Debug.Log(
            "Event trong hàng đợi: " + eventQueue.Count);
    }

    private void SaveQueue()
    {
        TelemetryEventQueue data = new TelemetryEventQueue
        {
            events = eventQueue
        };

        string json = JsonUtility.ToJson(data);

        PlayerPrefs.SetString(QUEUE_KEY, json);
        PlayerPrefs.Save();
    }

    private void LoadQueue()
    {
        string json = PlayerPrefs.GetString(QUEUE_KEY, "");

        if (string.IsNullOrEmpty(json))
            return;

        try
        {
            TelemetryEventQueue data =
                JsonUtility.FromJson<TelemetryEventQueue>(json);

            if (data != null && data.events != null)
            {
                eventQueue.Clear();
                eventQueue.AddRange(data.events);
            }

            Debug.Log(
                "Đã khôi phục " + eventQueue.Count +
                " Event từ hàng đợi.");
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Không thể đọc hàng đợi Event: " +
                exception.Message);
        }
    }

    // =========================
    // GỬI EVENT BATCH
    // =========================

    public void TryFlushQueue()
    {
        if (SessionId <= 0 ||
            eventQueue.Count == 0 ||
            isSending)
        {
            return;
        }

        if (Application.internetReachability ==
            NetworkReachability.NotReachable)
        {
            Debug.LogWarning("Không có Internet. Giữ Event trong hàng đợi.");
            return;
        }

        StartCoroutine(SendBatchEvents());
    }

    private IEnumerator SendBatchEvents()
    {
        if (isSending || eventQueue.Count == 0)
            yield break;

        isSending = true;

        int sendingCount = Mathf.Min(
            eventQueue.Count, MAX_BATCH_SIZE);

        List<TelemetryEvent> sendingEvents =
            eventQueue.GetRange(0, sendingCount);

        string json = BuildBatchJson(sendingEvents);

        using (UnityWebRequest request = new UnityWebRequest(
            BASE_URL + "/api/Events/batch", "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader(
                "Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                // Xóa những Event đã gửi thành công.
                eventQueue.RemoveRange(0, sendingCount);

                SaveQueue();

                Debug.Log(
                    "Gửi Batch thành công: " +
                    sendingCount + " Events.");
            }
            else
            {
                Debug.LogWarning(
                    "Gửi Batch thất bại: " + request.error);

                Debug.LogWarning(
                    "Giữ nguyên Event trong hàng đợi để thử lại.");
            }
        }

        isSending = false;

        // Nếu còn Event thì tiếp tục gửi batch tiếp theo.
        if (eventQueue.Count > 0)
        {
            retryTimer = 0f;
        }
    }

    // =========================
    // TẠO JSON CHO BACKEND
    // =========================

    private string BuildSingleEventJson(
        TelemetryEvent telemetryEvent)
    {
        StringBuilder json = new StringBuilder();

        json.Append("{");
        json.Append("\"sessionId\":").Append(SessionId).Append(",");
        json.Append("\"eventType\":\"")
            .Append(EscapeJson(telemetryEvent.eventType)).Append("\",");
        json.Append("\"eventTime\":\"")
            .Append(EscapeJson(telemetryEvent.eventTime)).Append("\"");

        if (telemetryEvent.hasValue)
        {
            json.Append(",\"value\":")
                .Append(telemetryEvent.value);
        }

        json.Append("}");

        return json.ToString();
    }

    private string BuildBatchJson(
        List<TelemetryEvent> events)
    {
        StringBuilder json = new StringBuilder();

        json.Append("{");
        json.Append("\"sessionId\":").Append(SessionId).Append(",");
        json.Append("\"events\":[");

        for (int i = 0; i < events.Count; i++)
        {
            if (i > 0)
                json.Append(",");

            TelemetryEvent item = events[i];

            json.Append("{");
            json.Append("\"eventType\":\"")
                .Append(EscapeJson(item.eventType)).Append("\",");
            json.Append("\"eventTime\":\"")
                .Append(EscapeJson(item.eventTime)).Append("\"");

            if (item.hasValue)
            {
                json.Append(",\"value\":").Append(item.value);
            }

            json.Append("}");
        }

        json.Append("]}");

        return json.ToString();
    }

    private string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    // =========================
    // CÁC EVENT TRONG GAME
    // =========================

    // Gọi sau khi người chơi đăng nhập và Session đã được tạo.
    public void TrackLogin()
    {
        TrackEvent("login");
    }

    // Bắt đầu màn chơi.
    public void TrackLevelStart(int level)
    {
        TrackEvent("level_start", level);
    }

    // Hoàn thành màn chơi.
    public void TrackLevelComplete(int level)
    {
        TrackEvent("level_complete", level);
    }

    // Thất bại màn chơi.
    public void TrackLevelFailed(int level)
    {
        TrackEvent("level_failed", level);
    }

    // Khi tiêu diệt quái.
    public void TrackEnemyKilled()
    {
        TrackEvent("EnemyKilled");
    }

    // Khi người chơi chết.
    public void TrackPlayerDied()
    {
        if (gameEnded)
            return;

        TrackEvent("player_dead");
    }

    // Kết thúc lượt chơi.
    public void TrackGameEnded()
    {
        if (gameEnded)
            return;

        TrackEvent("game_end");
        gameEnded = true;
    }

    // Khi người chơi thoát game.
    public void TrackGameExit()
    {
        if (gameExitTracked || gameEnded)
            return;

        gameExitTracked = true;
        TrackEvent("game_exit");
    }

    // Khi ứng dụng chuyển về foreground.
    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            LoadSessionId();
            TryFlushQueue();
        }
    }

    // Khi ứng dụng tạm dừng hoặc quay lại.
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            SaveQueue();
        }
        else
        {
            LoadSessionId();
            TryFlushQueue();
        }
    }

    // Khi đóng ứng dụng: lưu game_exit vào hàng đợi.
    private void OnApplicationQuit()
    {
        if (!gameExitTracked && !gameEnded && SessionId > 0)
        {
            TelemetryEvent exitEvent = new TelemetryEvent
            {
                eventType = "game_exit",
                eventTime = DateTime.UtcNow.ToString("o"),
                hasValue = false
            };

            EnqueueEvent(exitEvent);
            gameExitTracked = true;
        }

        SaveQueue();
    }
}
