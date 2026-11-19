
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
            }
            else
            {
                Debug.LogError("Không thể tạo Session: " + message);
            }
        });
    }
}
