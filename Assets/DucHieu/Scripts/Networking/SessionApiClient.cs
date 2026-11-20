using System;
using System.Collections;
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

public class SessionApiClient : MonoBehaviour
{
    private const string BASE_URL =
        "https://sjf1zkj0-7013.asse.devtunnels.ms";

    public void StartSession(Action<bool, long, string> callback)
    {
        int playerId = PlayerPrefs.GetInt("PlayerId", 0);

        if (playerId <= 0)
        {
            callback?.Invoke(false, 0, "Không tìm thấy PlayerId.");
            return;
        }

        StartCoroutine(StartSessionCoroutine(playerId, callback));
    }

    private IEnumerator StartSessionCoroutine(
        int playerId,
        Action<bool, long, string> callback)
    {
        StartSessionRequest requestData =
            new StartSessionRequest { playerId = playerId };

        string json = JsonUtility.ToJson(requestData);

        using (UnityWebRequest request = new UnityWebRequest(
            BASE_URL + "/api/Sessions/start", "POST"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                StartSessionResponse response =
                    JsonUtility.FromJson<StartSessionResponse>(
                        request.downloadHandler.text);

                if (response == null || response.sessionId <= 0)
                {
                    callback?.Invoke(
                        false, 0, "Backend không trả về sessionId hợp lệ.");
                    yield break;
                }

                PlayerPrefs.SetString(
                    "SessionId", response.sessionId.ToString());

                PlayerPrefs.Save();

                Debug.Log("Session started: " + response.sessionId);

                callback?.Invoke(
                    true, response.sessionId, "Tạo Session thành công.");
            }
            else
            {
                string error = request.downloadHandler.text;

                Debug.LogError("Start Session Error: " + error);

                callback?.Invoke(
                    false, 0, request.error + " - " + error);
            }
        }
    }

    public void EndSession(
        string result,
        Action<bool, string> callback = null)
    {
        string sessionId = PlayerPrefs.GetString("SessionId", "");

        if (string.IsNullOrEmpty(sessionId))
        {
            callback?.Invoke(false, "Không tìm thấy SessionId.");
            return;
        }

        StartCoroutine(EndSessionCoroutine(sessionId, result, callback));
    }

    private IEnumerator EndSessionCoroutine(
        string sessionId,
        string result,
        Action<bool, string> callback)
    {
        EndSessionRequest data = new EndSessionRequest
        {
            result = result
        };

        string json = JsonUtility.ToJson(data);

        using (UnityWebRequest request = new UnityWebRequest(
            BASE_URL + "/api/Sessions/" + sessionId + "/end", "PUT"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Session ended: " + sessionId);

                PlayerPrefs.DeleteKey("SessionId");
                PlayerPrefs.Save();

                callback?.Invoke(true, "Kết thúc Session thành công.");
            }
            else
            {
                string error = request.downloadHandler.text;

                Debug.LogError("End Session Error: " + error);

                callback?.Invoke(
                    false, request.error + " - " + error);
            }
        }
    }
}
