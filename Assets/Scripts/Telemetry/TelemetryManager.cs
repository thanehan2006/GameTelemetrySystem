
using System;
using UnityEngine;

[Serializable]
public class TelemetryEvent
{
    public string eventId;
    public string sessionId;
    public string eventType;
    public string timestamp;
}

public class TelemetryManager : MonoBehaviour
{
    public static TelemetryManager Instance { get; private set; }

    public string SessionId { get; private set; }

    private bool gameEnded = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        StartNewSession();
    }

    // Tạo lượt chơi mới
    public void StartNewSession()
    {
        SessionId = Guid.NewGuid().ToString("N");
        gameEnded = false;

        Debug.Log("SESSION STARTED: " + SessionId);

        TrackEvent("GameStarted");
    }

    // Ghi nhận sự kiện
    public void TrackEvent(string eventType)
    {
        if (string.IsNullOrEmpty(SessionId))
        {
            Debug.LogWarning("Chưa có SessionId!");
            return;
        }

        if (gameEnded && eventType != "GameEnded")
        {
            Debug.LogWarning("Lượt chơi đã kết thúc!");
            return;
        }

        TelemetryEvent telemetryEvent = new TelemetryEvent
        {
            eventId = Guid.NewGuid().ToString("N"),
            sessionId = SessionId,
            eventType = eventType,
            timestamp = DateTime.UtcNow.ToString("O")
        };

        string json = JsonUtility.ToJson(telemetryEvent);

        Debug.Log("TELEMETRY EVENT: " + json);
    }

    // Khi tiêu diệt quái
    public void TrackEnemyKilled()
    {
        TrackEvent("EnemyKilled");
    }

    // Khi người chơi chết
    public void TrackPlayerDied()
    {
        if (gameEnded)
            return;

        TrackEvent("PlayerDied");
        TrackGameEnded();
    }

    // Khi lượt chơi kết thúc
    public void TrackGameEnded()
    {
        if (gameEnded)
            return;

        TrackEvent("GameEnded");
        gameEnded = true;
    }
}
