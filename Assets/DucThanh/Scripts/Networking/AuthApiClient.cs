using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public static class ApiConfig
{
    // Có thể đổi linh hoạt localhost (chạy máy cục bộ) hoặc devtunnel URL
    public const string DEFAULT_BASE_URL = "http://localhost:5295";
    public const string DEVTUNNEL_URL = "https://sjf1zkj0-7013.asse.devtunnels.ms";

    public static string GetBaseUrl()
    {
        return PlayerPrefs.GetString("ApiBaseUrl", DEVTUNNEL_URL);
    }
}

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

/// <summary>
/// Client kết nối API xác thực (Đăng ký / Đăng nhập).
/// Tối ưu hóa: timeout an toàn, xử lý URL linh hoạt, kiểm tra kết quả đồng bộ.
/// </summary>
public class AuthApiClient : MonoBehaviour
{
    [Header("Backend API")]
    [SerializeField]
    private string baseUrl = ApiConfig.DEVTUNNEL_URL;

    public string BaseUrl
    {
        get => string.IsNullOrEmpty(baseUrl) ? ApiConfig.GetBaseUrl() : baseUrl;
        set => baseUrl = value;
    }

    // ĐĂNG KÝ
    public void Register(string playerName, Action<bool, PlayerApiResponse, string> callback)
    {
        PlayerNameRequest data = new PlayerNameRequest
        {
            playerName = playerName.Trim()
        };

        StartCoroutine(RegisterCoroutine(data, callback));
    }

    private IEnumerator RegisterCoroutine(PlayerNameRequest data, Action<bool, PlayerApiResponse, string> callback)
    {
        string json = JsonUtility.ToJson(data);
        string url = BaseUrl.TrimEnd('/') + "/api/Players/register";

        using (UnityWebRequest request = CreatePostRequest(url, json))
        {
            yield return request.SendWebRequest();
            string responseText = request.downloadHandler?.text;

            if (request.result == UnityWebRequest.Result.Success)
            {
                PlayerApiResponse response = JsonUtility.FromJson<PlayerApiResponse>(responseText);
                callback?.Invoke(
                    response != null,
                    response,
                    response != null ? "Đăng ký thành công!" : "Không đọc được dữ liệu từ Backend!"
                );
            }
            else
            {
                callback?.Invoke(false, null, GetErrorMessage(responseText, request));
            }
        }
    }

    // ĐĂNG NHẬP
    public void Login(string playerName, Action<bool, PlayerApiResponse, string> callback)
    {
        PlayerNameRequest data = new PlayerNameRequest
        {
            playerName = playerName.Trim()
        };

        StartCoroutine(LoginCoroutine(data, callback));
    }

    private IEnumerator LoginCoroutine(PlayerNameRequest data, Action<bool, PlayerApiResponse, string> callback)
    {
        string json = JsonUtility.ToJson(data);
        string url = BaseUrl.TrimEnd('/') + "/api/Players/login";

        using (UnityWebRequest request = CreatePostRequest(url, json))
        {
            yield return request.SendWebRequest();
            string responseText = request.downloadHandler?.text;

            if (request.result == UnityWebRequest.Result.Success)
            {
                PlayerApiResponse response = JsonUtility.FromJson<PlayerApiResponse>(responseText);

                if (response != null)
                {
                    PlayerPrefs.SetInt("PlayerId", response.playerId);
                    PlayerPrefs.SetString("PlayerName", response.playerName);
                    PlayerPrefs.Save();

                    callback?.Invoke(true, response, "Đăng nhập thành công!");
                }
                else
                {
                    callback?.Invoke(false, null, "Không đọc được dữ liệu người chơi!");
                }
            }
            else
            {
                callback?.Invoke(false, null, GetErrorMessage(responseText, request));
            }
        }
    }

    private UnityWebRequest CreatePostRequest(string url, string json)
    {
        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] body = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 15;
        return request;
    }

    private string GetErrorMessage(string responseText, UnityWebRequest request)
    {
        if (!string.IsNullOrEmpty(responseText))
        {
            try
            {
                PlayerApiError error = JsonUtility.FromJson<PlayerApiError>(responseText);
                if (error != null && !string.IsNullOrEmpty(error.message))
                {
                    return error.message;
                }
            }
            catch { }
        }

        return $"Lỗi kết nối ({request.responseCode}): {request.error}";
    }
}
