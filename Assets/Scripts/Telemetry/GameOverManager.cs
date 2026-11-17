
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameOverManager : MonoBehaviour
{
    public static GameOverManager Instance { get; private set; }

    public static bool IsGameOver { get; private set; }

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

        gameOverPanel.SetActive(true);

        Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log("GAME OVER - CAMERA PAUSED");
    }

    public void TryAgain()
    {
        Time.timeScale = 1f;
        IsGameOver = false;

        SceneManager.LoadScene("MainScene");
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;

        Application.Quit();

        Debug.Log("QUIT GAME");
    }
}
