using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class AuthUIController : MonoBehaviour
{
    [Header("API")]
    [SerializeField] private AuthApiClient authApiClient;

    [Header("Panels")]
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject registerPanel;

    [Header("Login Fields")]
    [SerializeField] private TMP_InputField loginUsernameInput;
    [SerializeField] private TMP_Text loginMessageText;

    [Header("Register Fields")]
    [SerializeField] private TMP_InputField registerUsernameInput;
    [SerializeField] private TMP_Text registerMessageText;

    private void Start()
    {
        ShowLoginPanel();
    }

    public void ShowLoginPanel()
    {
        loginPanel.SetActive(true);
        registerPanel.SetActive(false);
        ClearMessages();
    }

    public void ShowRegisterPanel()
    {
        loginPanel.SetActive(false);
        registerPanel.SetActive(true);
        ClearMessages();
    }

    private void ClearMessages()
    {
        if (loginMessageText != null)
            loginMessageText.text = "";

        if (registerMessageText != null)
            registerMessageText.text = "";
    }

    // ĐĂNG NHẬP
    public void Login()
    {
        string playerName = loginUsernameInput.text.Trim();

        if (string.IsNullOrEmpty(playerName))
        {
            loginMessageText.text = "Vui lòng nhập tên người chơi!";
            return;
        }

        if (authApiClient == null)
        {
            loginMessageText.text = "Chưa gắn AuthApiClient!";
            return;
        }

        loginMessageText.text = "Đang đăng nhập...";

        authApiClient.Login(
            playerName,
            (success, response, message) =>
            {
                if (success)
                {
                    loginMessageText.text = "Đăng nhập thành công!";

                    Debug.Log("Player ID: " + response.playerId);
                    Debug.Log("Player Name: " + response.playerName);

                    SceneManager.LoadScene("LoadingScene");
                }
                else
                {
                    loginMessageText.text = message;
                    Debug.LogWarning("Login: " + message);
                }
            }
        );
    }

    // ĐĂNG KÝ
    public void Register()
    {
        string playerName = registerUsernameInput.text.Trim();

        if (string.IsNullOrEmpty(playerName))
        {
            registerMessageText.text = "Vui lòng nhập tên người chơi!";
            return;
        }

        if (authApiClient == null)
        {
            registerMessageText.text = "Chưa gắn AuthApiClient!";
            return;
        }

        registerMessageText.text = "Đang đăng ký...";

        authApiClient.Register(
            playerName,
            (success, response, message) =>
            {
                if (success)
                {
                    registerMessageText.text =
                        "Đăng ký thành công! Hãy đăng nhập.";

                    loginUsernameInput.text = playerName;
                    registerUsernameInput.text = "";

                    Debug.Log("Registered Player ID: " +
                              response.playerId);
                }
                else
                {
                    registerMessageText.text = message;
                    Debug.LogWarning("Register: " + message);
                }
            }
        );
    }
}
