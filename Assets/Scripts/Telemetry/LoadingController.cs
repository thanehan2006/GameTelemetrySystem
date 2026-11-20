using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class LoadingController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text loadingStatus;

    [Header("Scene")]
    [SerializeField] private string sceneToLoad = "MainScene";

    private void Start()
    {
        StartCoroutine(LoadSceneAsync());
    }

    private IEnumerator LoadSceneAsync()
    {
        if (progressBar == null ||
            progressText == null ||
            loadingStatus == null)
        {
            Debug.LogError("Chưa gán đầy đủ UI Loading!");
            yield break;
        }

        loadingStatus.text = "Đang tải dữ liệu...";

        AsyncOperation operation =
            SceneManager.LoadSceneAsync(sceneToLoad);

        if (operation == null)
        {
            Debug.LogError("Không thể tải Scene: " + sceneToLoad);
            yield break;
        }

        // Chưa cho chuyển Scene ngay lập tức
        operation.allowSceneActivation = false;

        while (operation.progress < 0.9f)
        {
            float progress = Mathf.Clamp01(
                operation.progress / 0.9f
            );

            UpdateProgress(progress);

            yield return null;
        }

        // Đảm bảo thanh tiến trình đạt 100%
        UpdateProgress(1f);

        loadingStatus.text = "Hoàn tất!";

        // Chờ một chút để người chơi nhìn thấy 100%
        yield return new WaitForSecondsRealtime(0.5f);

        operation.allowSceneActivation = true;
    }

    private void UpdateProgress(float progress)
    {
        progressBar.value = progress;

        int percentage = Mathf.RoundToInt(progress * 100f);

        progressText.text = percentage + "%";
    }
}
