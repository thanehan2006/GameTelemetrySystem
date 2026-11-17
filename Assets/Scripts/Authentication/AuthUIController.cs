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
    [SerializeField] private TMP_InputField loginPasswordInput;
    [SerializeField] private TMP_Text loginMessageText;

    [Header("Register Fields")]
    [SerializeField] private TMP_InputField registerUsernameInput;
    [SerializeField] private TMP_InputField registerEmailInput;
    [SerializeField] private TMP_InputField registerPasswordInput;
    [SerializeField] private TMP_InputField confirmPasswordInput;
    [SerializeField] private TMP_Text registerMessageText;

    private void Start()
    {
        ShowLoginPanel();
    }

    // Hiển thị giao diện đăng nhập
    public void ShowLoginPanel()
    {
        loginPanel.SetActive(true);
        registerPanel.SetActive(false);

        ClearMessages();
    }

    // Hiển thị giao diện đăng ký
    public void ShowRegisterPanel()
    {
        loginPanel.SetActive(false);
        registerPanel.SetActive(true);

        ClearMessages();
    }

    // Xóa thông báo cũ
    private void ClearMessages()
    {
        if (loginMessageText != null)
            loginMessageText.text = "";

        if (registerMessageText != null)
            registerMessageText.text = "";
    }

    // Xử lý nút đăng nhập

    public void Login()
    {
        string username = loginUsernameInput.text.Trim();
        string password = loginPasswordInput.text;

        if (string.IsNullOrEmpty(username) ||
            string.IsNullOrEmpty(password))
        {
            loginMessageText.text = "Vui long nhap tai khoan va mat khau!";
            return;
        }

        if (authApiClient == null)
        {
            loginMessageText.text = "Chua gan AuthApiClient!";
            Debug.LogError("AuthApiClient chua duoc gan trong Inspector!");
            return;
        }

        loginMessageText.text = "Dang dang nhap...";

        authApiClient.Login(username, password, (success, response, message) =>
        {
            if (success)
            {
                loginMessageText.text = "Dang nhap thanh cong!";

                Debug.Log("User ID: " + response.userId);
                Debug.Log("Username: " + response.username);
                Debug.Log("JWT Token: " + response.token);

                // Chuyen sang Scene game
                SceneManager.LoadScene("MainScene");
            }
            else
            {
                loginMessageText.text = message;
                Debug.LogWarning("Login: " + message);
            }
        });
    }


    // Xử lý nút đăng ký
    public void Register()
    {
        string username = registerUsernameInput.text.Trim();
        string email = registerEmailInput.text.Trim();
        string password = registerPasswordInput.text;
        string confirmPassword = confirmPasswordInput.text;

        // Kiem tra du lieu
        if (string.IsNullOrEmpty(username) ||
            string.IsNullOrEmpty(email) ||
            string.IsNullOrEmpty(password) ||
            string.IsNullOrEmpty(confirmPassword))
        {
            registerMessageText.text = "Vui long nhap day du thong tin!";
            return;
        }

        if (password.Length < 6)
        {
            registerMessageText.text = "Mat khau phai co it nhat 6 ky tu!";
            return;
        }

        if (password != confirmPassword)
        {
            registerMessageText.text = "Mat khau xac nhan khong khop!";
            return;
        }

        if (authApiClient == null)
        {
            registerMessageText.text = "Chua gan AuthApiClient!";
            Debug.LogError("AuthApiClient chua duoc gan trong Inspector!");
            return;
        }

        registerMessageText.text = "Dang dang ky...";

        authApiClient.Register(username, email, password, (success, message) =>
        {
            if (success)
            {
                registerMessageText.text = "Dang ky thanh cong! Hay dang nhap.";

                // Xoa cac o nhap sau khi dang ky
                registerPasswordInput.text = "";
                confirmPasswordInput.text = "";
            }
            else
            {
                registerMessageText.text = message;
            }

            Debug.Log("Register: " + message);
        });
    }
}
