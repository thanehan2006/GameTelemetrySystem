using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public static class ApiConfig
{
    public const string DEFAULT_BASE_URL = "https://localhost:7013";
    public const string LOCAL_HTTP_URL = "http://localhost:5295";
    public const string DEVTUNNEL_URL = "https://sjf1zkj0-7013.asse.devtunnels.ms";

    public static string GetBaseUrl()
    {
        return PlayerPrefs.GetString("ApiBaseUrl", DEFAULT_BASE_URL);
    }

    public static void ConfigureRequest(UnityWebRequest request, string url)
    {
        if (request == null || string.IsNullOrEmpty(url)) return;

        if (url.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            request.certificateHandler = new LocalhostCertificateHandler();
        }
    }

    private sealed class LocalhostCertificateHandler : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData) => true;
    }
}

[Serializable]
public class AuthCredentialsRequest
{
    public string playerName;
    public string password;
}

[Serializable]
public class PlayerApiResponse
{
    public int playerId;
    public string playerName;
    public string createdAt;
}

[Serializable]
public class PlayerApiError
{
    public string message;
    public string title;
}

/// <summary>
/// Client kết nối API xác thực (Đăng ký / Đăng nhập kèm mật khẩu).
/// Tối ưu: dùng chung một luồng request, chống gửi trùng khi đang xử lý, tách thông báo lỗi rõ ràng.
/// </summary>
public class AuthApiClient : MonoBehaviour
{
    [Header("Backend API")]
    [SerializeField] private string baseUrl = ApiConfig.DEFAULT_BASE_URL;
    [SerializeField] private int timeoutSeconds = 15;

    public bool IsBusy { get; private set; }

    public string BaseUrl
    {
        get => string.IsNullOrEmpty(baseUrl) ? ApiConfig.GetBaseUrl() : baseUrl;
        set => baseUrl = value;
    }

    public void Register(string playerName, string password, Action<bool, PlayerApiResponse, string> callback)
    {
        SendAuthRequest("/api/Players/register", playerName, password, false, "Đăng ký thành công! Hãy đăng nhập.", callback);
    }

    public void Login(string playerName, string password, Action<bool, PlayerApiResponse, string> callback)
    {
        SendAuthRequest("/api/Players/login", playerName, password, true, "Đăng nhập thành công!", callback);
    }

    private void SendAuthRequest(
        string endpoint,
        string playerName,
        string password,
        bool savePlayerSession,
        string successMessage,
        Action<bool, PlayerApiResponse, string> callback)
    {
        if (IsBusy)
        {
            callback?.Invoke(false, null, "Đang xử lý yêu cầu, vui lòng chờ...");
            return;
        }

        var payload = new AuthCredentialsRequest
        {
            playerName = (playerName ?? string.Empty).Trim(),
            password = password ?? string.Empty
        };

        StartCoroutine(SendAuthRequestCoroutine(endpoint, payload, savePlayerSession, successMessage, callback));
    }

    private IEnumerator SendAuthRequestCoroutine(
        string endpoint,
        AuthCredentialsRequest payload,
        bool savePlayerSession,
        string successMessage,
        Action<bool, PlayerApiResponse, string> callback)
    {
        IsBusy = true;
        string url = BaseUrl.TrimEnd('/') + endpoint;
        string json = JsonUtility.ToJson(payload);

        using (UnityWebRequest request = CreatePostRequest(url, json))
        {
            yield return request.SendWebRequest();
            IsBusy = false;

            string responseText = request.downloadHandler?.text;

            if (request.result == UnityWebRequest.Result.Success)
            {
                PlayerApiResponse response = JsonUtility.FromJson<PlayerApiResponse>(responseText);
                if (response == null || response.playerId <= 0)
                {
                    callback?.Invoke(false, null, "Không đọc được dữ liệu người chơi từ máy chủ!");
                    yield break;
                }

                if (savePlayerSession)
                {
                    PlayerPrefs.SetInt("PlayerId", response.playerId);
                    PlayerPrefs.SetString("PlayerName", response.playerName);
                    PlayerPrefs.SetInt("PendingLoginEvent", 1);
                    PlayerPrefs.DeleteKey("SessionId");
                    PlayerPrefs.Save();
                }

                callback?.Invoke(true, response, successMessage);
            }
            else
            {
                callback?.Invoke(false, null, ParseErrorMessage(responseText, request));
            }
        }
    }

    private UnityWebRequest CreatePostRequest(string url, string json)
    {
        var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        byte[] body = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = timeoutSeconds;
        ApiConfig.ConfigureRequest(request, url);
        return request;
    }

    private static string ParseErrorMessage(string responseText, UnityWebRequest request)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                PlayerApiError err = JsonUtility.FromJson<PlayerApiError>(responseText);
                if (err != null && !string.IsNullOrWhiteSpace(err.message))
                    return err.message;
                if (err != null && !string.IsNullOrWhiteSpace(err.title))
                    return err.title;
            }
            catch
            {
                // Fallback sang mã trạng thái HTTP
            }
        }

        if (request.responseCode == 401) return "Sai tên đăng nhập hoặc mật khẩu!";
        if (request.responseCode == 409) return "Tên người chơi đã tồn tại!";
        if (request.responseCode == 400) return "Thông tin nhập chưa hợp lệ!";

        return $"Không thể kết nối máy chủ ({request.error})";
    }
}
