
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class RegisterApiRequest
{
    public string username;
    public string email;
    public string password;
}

[Serializable]
public class LoginApiRequest
{
    public string usernameOrEmail;
    public string password;
}

[Serializable]
public class RegisterApiResponse
{
    public bool success;
    public string message;
    public int userId;
    public string username;
}

[Serializable]
public class LoginApiResponse
{
    public bool success;
    public string message;
    public int userId;
    public string username;
    public string token;
}

[Serializable]
public class ApiErrorResponse
{
    public bool success;
    public string message;
}

public class AuthApiClient : MonoBehaviour
{
    [Header("Backend API")]
    [SerializeField]
    private string baseUrl = "http://localhost:5176/api/auth/";

    public void Register(
        string username,
        string email,
        string password,
        Action<bool, string> callback)
    {
        RegisterApiRequest data = new RegisterApiRequest
        {
            username = username,
            email = email,
            password = password
        };

        StartCoroutine(RegisterCoroutine(data, callback));
    }

    private IEnumerator RegisterCoroutine(
        RegisterApiRequest data,
        Action<bool, string> callback)
    {
        string json = JsonUtility.ToJson(data);

        using (UnityWebRequest request = CreatePostRequest(
            baseUrl + "register", json))
        {
            yield return request.SendWebRequest();

            string responseText = request.downloadHandler.text;

            if (request.result == UnityWebRequest.Result.Success)
            {
                RegisterApiResponse response =
                    JsonUtility.FromJson<RegisterApiResponse>(responseText);

                callback?.Invoke(
                    response != null && response.success,
                    response != null ? response.message : "Dang ky thanh cong!"
                );
            }
            else
            {
                callback?.Invoke(false, GetErrorMessage(responseText, request));
            }
        }
    }

    public void Login(
        string usernameOrEmail,
        string password,
        Action<bool, LoginApiResponse, string> callback)
    {
        LoginApiRequest data = new LoginApiRequest
        {
            usernameOrEmail = usernameOrEmail,
            password = password
        };

        StartCoroutine(LoginCoroutine(data, callback));
    }

    private IEnumerator LoginCoroutine(
        LoginApiRequest data,
        Action<bool, LoginApiResponse, string> callback)
    {
        string json = JsonUtility.ToJson(data);

        using (UnityWebRequest request = CreatePostRequest(
            baseUrl + "login", json))
        {
            yield return request.SendWebRequest();

            string responseText = request.downloadHandler.text;

            if (request.result == UnityWebRequest.Result.Success)
            {
                LoginApiResponse response =
                    JsonUtility.FromJson<LoginApiResponse>(responseText);

                if (response != null && response.success)
                {
                    // Luu thong tin dang nhap de su dung o cac Scene sau.
                    PlayerPrefs.SetInt("UserId", response.userId);
                    PlayerPrefs.SetString("Username", response.username);
                    PlayerPrefs.SetString("AuthToken", response.token);
                    PlayerPrefs.Save();

                    callback?.Invoke(true, response, response.message);
                }
                else
                {
                    callback?.Invoke(
                        false, null, "Khong doc duoc phan hoi dang nhap!"
                    );
                }
            }
            else
            {
                callback?.Invoke(
                    false, null, GetErrorMessage(responseText, request)
                );
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

    private string GetErrorMessage(
        string responseText,
        UnityWebRequest request)
    {
        if (!string.IsNullOrEmpty(responseText))
        {
            try
            {
                ApiErrorResponse error =
                    JsonUtility.FromJson<ApiErrorResponse>(responseText);

                if (error != null && !string.IsNullOrEmpty(error.message))
                    return error.message;
            }
            catch
            {
                // Su dung thong bao loi mac dinh ben duoi.
            }
        }

        return request.error;
    }
}
