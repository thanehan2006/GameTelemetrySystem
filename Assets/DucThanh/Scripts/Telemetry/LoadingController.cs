
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

    [Header("Loading Settings")]
    [SerializeField] private float startDelay = 5f;
    [SerializeField] private float loadingDuration = 10f;

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

        // Bắt đầu ở 0%
        progressBar.value = 0f;
        progressText.text = "0%";
        loadingStatus.text = "Chuẩn bị tải dữ liệu...";

        // Tải Scene ở chế độ nền
        AsyncOperation operation =
            SceneManager.LoadSceneAsync(sceneToLoad);

        if (operation == null)
        {
            Debug.LogError("Không thể tải Scene: " + sceneToLoad);
            yield break;
        }

        // Chờ 5 giây trước khi chạy phần trăm
        operation.allowSceneActivation = false;

        yield return new WaitForSecondsRealtime(startDelay);

        loadingStatus.text = "Đang tải dữ liệu...";

        float elapsedTime = 0f;

        // Chạy từ 0% đến 100%
        while (elapsedTime < loadingDuration ||
               operation.progress < 0.9f)
        {
            elapsedTime += Time.unscaledDeltaTime;

            float timeProgress =
                Mathf.Clamp01(elapsedTime / loadingDuration);

            float actualProgress =
                Mathf.Clamp01(operation.progress / 0.9f);

            float displayProgress =
                Mathf.Min(timeProgress, actualProgress);

            progressBar.value = displayProgress;

            int percentage =
                Mathf.RoundToInt(displayProgress * 100f);

            progressText.text = percentage + "%";

            yield return null;
        }

        // Hoàn tất
        progressBar.value = 1f;
        progressText.text = "100%";
        loadingStatus.text = "Hoàn tất!";

        yield return new WaitForSecondsRealtime(0.5f);

        operation.allowSceneActivation = true;
    }
}
