using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class StartSessionRequest
{
    public int playerId;
    public int levelId;
}

[Serializable]
public class StartSessionResponse
{
    public long sessionId;
    public int playerId;
    public int levelId;
    public string startTime;
    public string endTime;
    public string result;
}

[Serializable]
public class EndSessionRequest
{
    public string result;
    public string endTime;
}

// Client giao tiếp API quản lý bắt đầu và kết thúc phiên chơi (GameSession)
public class SessionApiClient : MonoBehaviour
{
    [Header("Backend API")]
    [SerializeField]
    private string baseUrl = ApiConfig.DEFAULT_BASE_URL;

    private readonly HashSet<long> endingSessionIds = new HashSet<long>();
    private readonly HashSet<long> endedSessionIds = new HashSet<long>();

    public string BaseUrl
    {
        get => ApiConfig.ResolveBaseUrl(baseUrl);
        set => baseUrl = value;
    }

    // Bắt đầu phiên chơi mới dựa trên PlayerId đang đăng nhập
    public void StartSession(Action<bool, long, string> callback)
    {
        int playerId = PlayerPrefs.GetInt("PlayerId", 0);
        StartSession(playerId, 0, callback);
    }

    // Bắt đầu phiên chơi mới với PlayerId chỉ định
    public void StartSession(int overridePlayerId, Action<bool, long, string> callback)
    {
        StartSession(overridePlayerId, 0, callback);
    }

    // Bắt đầu phiên chơi mới với PlayerId và LevelId (1..3)
    public void StartSession(int overridePlayerId, int levelId, Action<bool, long, string> callback)
    {
        if (overridePlayerId <= 0)
        {
            callback?.Invoke(false, 0, "Không tìm thấy PlayerId hợp lệ.");
            return;
        }

        StartCoroutine(StartSessionCoroutine(overridePlayerId, levelId, callback));
    }

    // Gửi POST tới /api/Sessions/start để lấy SessionId từ SQL Server
    private IEnumerator StartSessionCoroutine(int playerId, int levelId, Action<bool, long, string> callback)
    {
        StartSessionRequest requestData = new StartSessionRequest
        {
            playerId = playerId,
            levelId = levelId
        };
        string json = JsonUtility.ToJson(requestData);
        string url = BaseUrl.TrimEnd('/') + "/api/Sessions/start";

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 15;
            ApiConfig.ConfigureRequest(request, url);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                StartSessionResponse response = JsonUtility.FromJson<StartSessionResponse>(request.downloadHandler.text);

                if (response == null || response.sessionId <= 0)
                {
                    callback?.Invoke(false, 0, "Backend không trả về sessionId hợp lệ.");
                    yield break;
                }

                endingSessionIds.Remove(response.sessionId);
                endedSessionIds.Remove(response.sessionId);
                PlayerPrefs.SetString("SessionId", response.sessionId.ToString());
                PlayerPrefs.Save();

                Debug.Log($"[SessionApiClient] Session started: {response.sessionId} (Level {levelId})");
                callback?.Invoke(true, response.sessionId, "Tạo Session thành công.");
            }
            else
            {
                string error = request.downloadHandler?.text;
                Debug.LogError($"[SessionApiClient] Start Session Error ({request.responseCode} {request.error}) [{url}]: {error}");
                callback?.Invoke(false, 0, $"{request.error} - {error}");
            }
        }
    }

    // Kết thúc phiên chơi hiện tại trong PlayerPrefs với kết quả Win, Lose hoặc Quit
    public void EndSession(string result, Action<bool, string> callback = null)
    {
        string rawSessionId = PlayerPrefs.GetString("SessionId", "");
        if (!long.TryParse(rawSessionId, out long sessionId) || sessionId <= 0)
        {
            callback?.Invoke(false, "Không tìm thấy SessionId.");
            return;
        }

        EndSessionById(sessionId, result, DateTime.UtcNow.ToString("o"), callback);
    }

    // Kết thúc phiên chơi theo SessionId và mốc thời gian chỉ định (hỗ trợ phát lại khi có mạng lại)
    public void EndSessionById(long sessionId, string result, string endTimeIso, Action<bool, string> callback = null)
    {
        if (sessionId <= 0)
        {
            callback?.Invoke(false, "SessionId không hợp lệ.");
            return;
        }

        if (endingSessionIds.Contains(sessionId) || endedSessionIds.Contains(sessionId))
        {
            callback?.Invoke(true, "Session đã được kết thúc trước đó.");
            return;
        }

        string safeEndTime = string.IsNullOrWhiteSpace(endTimeIso)
            ? DateTime.UtcNow.ToString("o")
            : endTimeIso.Trim();

        StartCoroutine(EndSessionCoroutine(sessionId, result, safeEndTime, callback));
    }

    // Gửi PUT tới /api/Sessions/{id}/end để đóng phiên chơi trên máy chủ
    private IEnumerator EndSessionCoroutine(long sessionId, string result, string endTimeIso, Action<bool, string> callback)
    {
        endingSessionIds.Add(sessionId);
        EndSessionRequest data = new EndSessionRequest
        {
            result = string.IsNullOrWhiteSpace(result) ? "Quit" : result.Trim(),
            endTime = endTimeIso
        };
        string json = JsonUtility.ToJson(data);
        string url = BaseUrl.TrimEnd('/') + "/api/Sessions/" + sessionId + "/end";

        using (UnityWebRequest request = new UnityWebRequest(url, "PUT"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 15;
            ApiConfig.ConfigureRequest(request, url);

            yield return request.SendWebRequest();

            endingSessionIds.Remove(sessionId);

            if (request.result == UnityWebRequest.Result.Success || request.responseCode == 404)
            {
                endedSessionIds.Add(sessionId);
                Debug.Log($"[SessionApiClient] Session ended: {sessionId} ({data.result})");
                if (PlayerPrefs.GetString("SessionId", "") == sessionId.ToString())
                {
                    PlayerPrefs.DeleteKey("SessionId");
                    PlayerPrefs.Save();
                }

                callback?.Invoke(true, "Kết thúc Session thành công.");
            }
            else
            {
                string error = request.downloadHandler?.text;
                Debug.LogError($"[SessionApiClient] End Session Error ({request.responseCode} {request.error}) [{url}]: {error}");
                callback?.Invoke(false, $"{request.error} - {error}");
            }
        }
    }
}
