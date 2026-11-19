
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Dữ liệu gửi lên Backend
[Serializable]
public class PlayerNameRequest
{
    public string playerName;
}

// Dữ liệu Backend trả về
[Serializable]
public class PlayerApiResponse
{
    public int playerId;
    public string playerName;
    public string createdAt;
}

// Dữ liệu lỗi
[Serializable]
public class PlayerApiError
{
    public string message;
}

public class AuthApiClient : MonoBehaviour
{
    [Header("Backend API")]
    [SerializeField]
    private string baseUrl =
        "https://sjf1zkj0-7013.asse.devtunnels.ms";

    // ĐĂNG KÝ
    public void Register(
        string playerName,
        Action<bool, PlayerApiResponse, string> callback)
    {
        PlayerNameRequest data = new PlayerNameRequest
        {
            playerName = playerName
        };

        StartCoroutine(RegisterCoroutine(data, callback));
    }

    private IEnumerator RegisterCoroutine(
        PlayerNameRequest data,
        Action<bool, PlayerApiResponse, string> callback)
    {
        string json = JsonUtility.ToJson(data);

        string url = baseUrl.TrimEnd('/') + "/api/Players/register";

        using (UnityWebRequest request = CreatePostRequest(url, json))
        {
            yield return request.SendWebRequest();

            string responseText = request.downloadHandler.text;

            if (request.result == UnityWebRequest.Result.Success)
            {
                PlayerApiResponse response =
                    JsonUtility.FromJson<PlayerApiResponse>(responseText);

                callback?.Invoke(
                    response != null,
                    response,
                    response != null
                        ? "Đăng ký thành công!"
                        : "Không đọc được dữ liệu từ Backend!"
                );
            }
            else
            {
                callback?.Invoke(
                    false,
                    null,
                    GetErrorMessage(responseText, request)
                );
            }
        }
    }

    // ĐĂNG NHẬP
    public void Login(
        string playerName,
        Action<bool, PlayerApiResponse, string> callback)
    {
        PlayerNameRequest data = new PlayerNameRequest
        {
            playerName = playerName
        };

        StartCoroutine(LoginCoroutine(data, callback));
    }

    private IEnumerator LoginCoroutine(
        PlayerNameRequest data,
        Action<bool, PlayerApiResponse, string> callback)
    {
        string json = JsonUtility.ToJson(data);

        string url = baseUrl.TrimEnd('/') + "/api/Players/login";

        using (UnityWebRequest request = CreatePostRequest(url, json))
        {
            yield return request.SendWebRequest();

            string responseText = request.downloadHandler.text;

            if (request.result == UnityWebRequest.Result.Success)
            {
                PlayerApiResponse response =
                    JsonUtility.FromJson<PlayerApiResponse>(responseText);

                if (response != null)
                {
                    // Lưu thông tin người chơi để dùng ở MainScene.
                    PlayerPrefs.SetInt("PlayerId", response.playerId);
                    PlayerPrefs.SetString("PlayerName", response.playerName);
                    PlayerPrefs.Save();

                    callback?.Invoke(
                        true,
                        response,
                        "Đăng nhập thành công!"
                    );
                }
                else
                {
                    callback?.Invoke(
                        false,
                        null,
                        "Không đọc được dữ liệu người chơi!"
                    );
                }
            }
            else
            {
                callback?.Invoke(
                    false,
                    null,
                    GetErrorMessage(responseText, request)
                );
            }
        }
    }

    // Tạo request POST
    private UnityWebRequest CreatePostRequest(string url, string json)
    {
        UnityWebRequest request = new UnityWebRequest(url, "POST");

        byte[] body = Encoding.UTF8.GetBytes(json);

        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader(
            "Content-Type",
            "application/json"
        );

        request.timeout = 20;

        return request;
    }

    // Đọc thông báo lỗi từ Backend
    private string GetErrorMessage(
        string responseText,
        UnityWebRequest request)
    {
        if (!string.IsNullOrEmpty(responseText))
        {
            try
            {
                PlayerApiError error =
                    JsonUtility.FromJson<PlayerApiError>(responseText);

                if (error != null &&
                    !string.IsNullOrEmpty(error.message))
                {
                    return error.message;
                }
            }
            catch
            {
                // Dùng thông báo lỗi mặc định.
            }
        }

        return "Lỗi HTTP " + request.responseCode +
               ": " + request.error;
    }
}
