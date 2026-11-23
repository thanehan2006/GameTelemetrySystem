using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class StartSessionRequest
{
    public int playerId;
}

[Serializable]
public class StartSessionResponse
{
    public long sessionId;
    public int playerId;
    public string startTime;
    public string endTime;
    public string result;
}

[Serializable]
public class EndSessionRequest
{
    public string result;
}

// Client giao tiếp API quản lý bắt đầu và kết thúc phiên chơi (GameSession)
public class SessionApiClient : MonoBehaviour
{
    [Header("Backend API")]
    [SerializeField]
    private string baseUrl = ApiConfig.DEFAULT_BASE_URL;

    private bool isEndingSession;
    private string endedSessionId;

    public string BaseUrl
    {
        get => string.IsNullOrEmpty(baseUrl) ? ApiConfig.GetBaseUrl() : baseUrl;
        set => baseUrl = value;
    }

    // Bắt đầu phiên chơi mới dựa trên PlayerId đang đăng nhập
    public void StartSession(Action<bool, long, string> callback)
    {
        int playerId = PlayerPrefs.GetInt("PlayerId", 0);
        StartSession(playerId, callback);
    }

    // Bắt đầu phiên chơi mới với PlayerId chỉ định
    public void StartSession(int overridePlayerId, Action<bool, long, string> callback)
    {
        if (overridePlayerId <= 0)
        {
            callback?.Invoke(false, 0, "Không tìm thấy PlayerId hợp lệ.");
            return;
        }

        StartCoroutine(StartSessionCoroutine(overridePlayerId, callback));
    }

    // Gửi POST tới /api/Sessions/start để lấy SessionId từ SQL Server
    private IEnumerator StartSessionCoroutine(int playerId, Action<bool, long, string> callback)
    {
        StartSessionRequest requestData = new StartSessionRequest { playerId = playerId };
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

                endedSessionId = null;
                isEndingSession = false;
                PlayerPrefs.SetString("SessionId", response.sessionId.ToString());
                PlayerPrefs.Save();

                Debug.Log($"[SessionApiClient] Session started: {response.sessionId}");
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

    // Kết thúc phiên chơi hiện tại với kết quả Win, Lose hoặc Quit
    public void EndSession(string result, Action<bool, string> callback = null)
    {
        string sessionId = PlayerPrefs.GetString("SessionId", "");

        if (string.IsNullOrEmpty(sessionId))
        {
            callback?.Invoke(false, "Không tìm thấy SessionId.");
            return;
        }

        if (isEndingSession || sessionId == endedSessionId)
        {
            callback?.Invoke(true, "Session đã được kết thúc trước đó.");
            return;
        }

        StartCoroutine(EndSessionCoroutine(sessionId, result, callback));
    }

    // Gửi PUT tới /api/Sessions/{id}/end để đóng phiên chơi trên máy chủ
    private IEnumerator EndSessionCoroutine(string sessionId, string result, Action<bool, string> callback)
    {
        isEndingSession = true;
        EndSessionRequest data = new EndSessionRequest { result = result };
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

            isEndingSession = false;

            if (request.result == UnityWebRequest.Result.Success || request.responseCode == 404)
            {
                endedSessionId = sessionId;
                Debug.Log($"[SessionApiClient] Session ended: {sessionId} ({result})");
                PlayerPrefs.DeleteKey("SessionId");
                PlayerPrefs.Save();

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
