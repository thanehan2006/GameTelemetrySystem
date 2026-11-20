using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Quản lý trạng thái Game Over, hiển thị UI và điều khiển dừng/tiếp tục game.
/// Tối ưu hóa: kiểm tra active scene linh hoạt (hỗ trợ cả Lv1 và MainScene).
/// </summary>
public class GameOverManager : MonoBehaviour
{
    public static GameOverManager Instance { get; private set; }
    public static bool IsGameOver { get; private set; }

    [Header("UI Reference")]
    public GameObject gameOverPanel;

    private void Awake()
    {
        Instance = this;
        IsGameOver = false;
    }

    public void ShowGameOver()
    {
        if (IsGameOver) return;
        IsGameOver = true;

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }

        Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log("[GameOverManager] GAME OVER - Game Paused");
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
