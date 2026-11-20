
using UnityEngine;

public class SessionStarter : MonoBehaviour
{
    [SerializeField] private SessionApiClient sessionApiClient;

    private void Start()
    {
        if (sessionApiClient == null)
        {
            Debug.LogError("Chưa gán SessionApiClient!");
            return;
        }

        sessionApiClient.StartSession((success, sessionId, message) =>
        {
            if (success)
            {
                Debug.Log("Đã tạo Session: " + sessionId);

                if (TelemetryManager.Instance != null)
                {
                    TelemetryManager.Instance.SetSession(sessionId);
                    TelemetryManager.Instance.TrackLogin();
                }
            }
            else
            {
                Debug.LogError("Không thể tạo Session: " + message);
            }
        });
    }
}
