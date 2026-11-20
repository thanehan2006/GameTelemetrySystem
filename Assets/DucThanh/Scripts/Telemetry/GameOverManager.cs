using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Quản lý trạng thái Game Over & Victory, hiển thị UI và điều khiển dừng/tiếp tục game.
/// </summary>
public class GameOverManager : MonoBehaviour
{
    public static GameOverManager Instance { get; private set; }
    public static bool IsGameOver { get; private set; }

    [Header("UI Reference")]
    public GameObject gameOverPanel;
    public TMP_Text titleText;

    private void Awake()
    {
        Instance = this;
        IsGameOver = false;
        ResolveTitleText();
    }

    private void ResolveTitleText()
    {
        if (titleText != null || gameOverPanel == null) return;

        Transform found = gameOverPanel.transform.Find("GameOverText");
        if (found != null)
        {
            titleText = found.GetComponent<TMP_Text>();
        }
    }

    public void ShowGameOver()
    {
        ShowEndScreen("GAME OVER", new Color(1f, 0.28f, 0.28f, 1f));
        Debug.Log("[GameOverManager] GAME OVER - Game Paused");
    }

    public void ShowVictory()
    {
        ShowEndScreen("VICTORY!", new Color(0.2f, 0.95f, 0.45f, 1f));
        Debug.Log("[GameOverManager] VICTORY - Game Paused");
    }

    private void ShowEndScreen(string title, Color titleColor)
    {
        if (IsGameOver) return;
        IsGameOver = true;

        ResolveTitleText();
        if (titleText != null)
        {
            titleText.text = title;
            titleText.color = titleColor;
        }

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }

        Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void TryAgain()
    {
        Time.timeScale = 1f;
        IsGameOver = false;

        PlayerPrefs.DeleteKey("SessionId");
        PlayerPrefs.Save();

        // Load lại chính scene đang hoạt động
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }

    public void QuitGame()
    {
        TelemetryManager.Instance?.TrackGameEnded("Quit");
        Time.timeScale = 1f;
        Application.Quit();
        Debug.Log("[GameOverManager] QUIT GAME");
    }
}
