using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Điều khiển giao diện Đăng nhập / Đăng ký có mật khẩu.
/// Hỗ trợ kiểm tra hợp lệ phía client, khóa nút khi đang gọi API, và phím tắt Tab / Enter.
/// </summary>
public class AuthUIController : MonoBehaviour
{
    private const int MinUsernameLength = 3;
    private const int MaxUsernameLength = 50;
    private const int MinPasswordLength = 6;

    [Header("API & Navigation")]
    [SerializeField] private AuthApiClient authApiClient;
    [SerializeField] private string nextSceneName = "Lv1";

    [Header("Panels")]
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject registerPanel;

    [Header("Login Fields")]
    [SerializeField] private TMP_InputField loginUsernameInput;
    [SerializeField] private TMP_InputField loginPasswordInput;
    [SerializeField] private TMP_Text loginMessageText;
    [SerializeField] private Button loginButton;
    [SerializeField] private Button openRegisterButton;

    [Header("Register Fields")]
    [SerializeField] private TMP_InputField registerUsernameInput;
    [SerializeField] private TMP_InputField registerPasswordInput;
    [SerializeField] private TMP_InputField registerConfirmPasswordInput;
    [SerializeField] private TMP_Text registerMessageText;
    [SerializeField] private Button registerButton;
    [SerializeField] private Button openLoginButton;

    private static readonly Color ErrorColor = new Color(1f, 0.35f, 0.35f, 1f);
    private static readonly Color SuccessColor = new Color(0.25f, 0.95f, 0.45f, 1f);
    private static readonly Color InfoColor = new Color(1f, 0.9f, 0.4f, 1f);

    private void Awake()
    {
        if (authApiClient == null)
        {
            authApiClient = FindFirstObjectByType<AuthApiClient>();
        }

        ResolveOptionalReferences();
        ConfigurePasswordField(loginPasswordInput);
        ConfigurePasswordField(registerPasswordInput);
        ConfigurePasswordField(registerConfirmPasswordInput);
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ShowLoginPanel();
    }

    private void Update()
    {
        if (authApiClient != null && authApiClient.IsBusy) return;

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            HandleTabNavigation();
        }
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (loginPanel != null && loginPanel.activeSelf) Login();
            else if (registerPanel != null && registerPanel.activeSelf) Register();
        }
    }

    public void ShowLoginPanel()
    {
        if (loginPanel != null) loginPanel.SetActive(true);
        if (registerPanel != null) registerPanel.SetActive(false);
        ClearMessages();
        ClearPasswordInputs();

        if (loginUsernameInput != null)
        {
            loginUsernameInput.ActivateInputField();
        }
    }

    public void ShowRegisterPanel()
    {
        if (loginPanel != null) loginPanel.SetActive(false);
        if (registerPanel != null) registerPanel.SetActive(true);
        ClearMessages();
        ClearPasswordInputs();

        if (registerUsernameInput != null)
        {
            registerUsernameInput.ActivateInputField();
        }
    }

    public void Login()
    {
        if (authApiClient == null)
        {
            SetMessage(loginMessageText, "Chưa gắn AuthApiClient!", ErrorColor);
            return;
        }
        if (authApiClient.IsBusy) return;

        string playerName = loginUsernameInput != null ? loginUsernameInput.text.Trim() : string.Empty;
        string password = loginPasswordInput != null ? loginPasswordInput.text : string.Empty;

        if (string.IsNullOrEmpty(playerName))
        {
            SetMessage(loginMessageText, "Vui lòng nhập tên đăng nhập!", ErrorColor);
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            SetMessage(loginMessageText, "Vui lòng nhập mật khẩu!", ErrorColor);
            return;
        }

        SetInteractable(false);
        SetMessage(loginMessageText, "Đang đăng nhập...", InfoColor);

        authApiClient.Login(playerName, password, (success, response, message) =>
        {
            SetInteractable(true);

            if (success)
            {
                SetMessage(loginMessageText, message, SuccessColor);
                Debug.Log($"[AuthUI] Đăng nhập thành công: #{response.playerId} ({response.playerName})");
                SceneManager.LoadScene(nextSceneName);
            }
            else
            {
                SetMessage(loginMessageText, message, ErrorColor);
                if (loginPasswordInput != null)
                {
                    loginPasswordInput.text = string.Empty;
                    loginPasswordInput.ActivateInputField();
                }
            }
        });
    }

    public void Register()
    {
        if (authApiClient == null)
        {
            SetMessage(registerMessageText, "Chưa gắn AuthApiClient!", ErrorColor);
            return;
        }
        if (authApiClient.IsBusy) return;

        string playerName = registerUsernameInput != null ? registerUsernameInput.text.Trim() : string.Empty;
        string password = registerPasswordInput != null ? registerPasswordInput.text : string.Empty;

        if (playerName.Length < MinUsernameLength || playerName.Length > MaxUsernameLength)
        {
            SetMessage(registerMessageText, $"Tên đăng nhập phải từ {MinUsernameLength} đến {MaxUsernameLength} ký tự!", ErrorColor);
            return;
        }

        if (password.Length < MinPasswordLength)
        {
            SetMessage(registerMessageText, $"Mật khẩu phải có ít nhất {MinPasswordLength} ký tự!", ErrorColor);
            return;
        }

        if (registerConfirmPasswordInput != null && registerConfirmPasswordInput.text != password)
        {
            SetMessage(registerMessageText, "Mật khẩu nhập lại không khớp!", ErrorColor);
            return;
        }

        SetInteractable(false);
        SetMessage(registerMessageText, "Đang tạo tài khoản...", InfoColor);

        authApiClient.Register(playerName, password, (success, response, message) =>
        {
            SetInteractable(true);

            if (success)
            {
                Debug.Log($"[AuthUI] Đăng ký thành công: #{response.playerId} ({response.playerName})");
                if (registerUsernameInput != null) registerUsernameInput.text = string.Empty;
                ShowLoginPanel();

                if (loginUsernameInput != null) loginUsernameInput.text = playerName;
                if (loginPasswordInput != null) loginPasswordInput.ActivateInputField();
                SetMessage(loginMessageText, message, SuccessColor);
            }
            else
            {
                SetMessage(registerMessageText, message, ErrorColor);
            }
        });
    }

    private void ResolveOptionalReferences()
    {
        if (loginPanel != null)
        {
            if (loginPasswordInput == null) loginPasswordInput = FindChildComponent<TMP_InputField>(loginPanel, "PasswordInput");
            if (loginButton == null) loginButton = FindChildComponent<Button>(loginPanel, "LoginButton");
            if (openRegisterButton == null) openRegisterButton = FindChildComponent<Button>(loginPanel, "OpenRegisterButton");
        }

        if (registerPanel != null)
        {
            if (registerPasswordInput == null) registerPasswordInput = FindChildComponent<TMP_InputField>(registerPanel, "PasswordInput");
            if (registerConfirmPasswordInput == null) registerConfirmPasswordInput = FindChildComponent<TMP_InputField>(registerPanel, "ConfirmPasswordInput");
            if (registerButton == null) registerButton = FindChildComponent<Button>(registerPanel, "RegisterButton");
            if (openLoginButton == null) openLoginButton = FindChildComponent<Button>(registerPanel, "OpenLoginButton");
        }
    }

    private static T FindChildComponent<T>(GameObject parent, string childName) where T : Component
    {
        Transform child = parent.transform.Find(childName);
        return child != null ? child.GetComponent<T>() : null;
    }

    private static void ConfigurePasswordField(TMP_InputField field)
    {
        if (field == null) return;
        field.contentType = TMP_InputField.ContentType.Password;
        field.ForceLabelUpdate();
    }

    private void HandleTabNavigation()
    {
        if (loginPanel != null && loginPanel.activeSelf)
        {
            if (loginUsernameInput != null && loginUsernameInput.isFocused && loginPasswordInput != null)
                loginPasswordInput.ActivateInputField();
            else if (loginUsernameInput != null)
                loginUsernameInput.ActivateInputField();
        }
        else if (registerPanel != null && registerPanel.activeSelf)
        {
            if (registerUsernameInput != null && registerUsernameInput.isFocused && registerPasswordInput != null)
                registerPasswordInput.ActivateInputField();
            else if (registerPasswordInput != null && registerPasswordInput.isFocused && registerConfirmPasswordInput != null)
                registerConfirmPasswordInput.ActivateInputField();
            else if (registerUsernameInput != null)
                registerUsernameInput.ActivateInputField();
        }
    }

    private void SetInteractable(bool interactable)
    {
        if (loginButton != null) loginButton.interactable = interactable;
        if (openRegisterButton != null) openRegisterButton.interactable = interactable;
        if (registerButton != null) registerButton.interactable = interactable;
        if (openLoginButton != null) openLoginButton.interactable = interactable;
    }

    private void ClearMessages()
    {
        if (loginMessageText != null) loginMessageText.text = string.Empty;
        if (registerMessageText != null) registerMessageText.text = string.Empty;
    }

    private void ClearPasswordInputs()
    {
        if (loginPasswordInput != null) loginPasswordInput.text = string.Empty;
        if (registerPasswordInput != null) registerPasswordInput.text = string.Empty;
        if (registerConfirmPasswordInput != null) registerConfirmPasswordInput.text = string.Empty;
    }

    private static void SetMessage(TMP_Text target, string message, Color color)
    {
        if (target == null) return;
        target.text = message;
        target.color = color;
    }
}
