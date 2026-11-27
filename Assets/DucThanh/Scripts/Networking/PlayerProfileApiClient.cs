using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class PlayerCharacterStatItem
{
    public string characterId;
    public string characterName;
    public string title;
    public string roleBadge;
    public string accentHex;
    public int matches;
    public int wins;
    public int losses;
    public float winRatePercent;
    public int totalEnemiesKilled;
    public float avgEnemiesPerMatch;
    public int bossKills;
    public float totalPlayTimeMinutes;
}

[Serializable]
public class PlayerRecentMatchItem
{
    public long sessionId;
    public int levelId;
    public string levelName;
    public string characterId;
    public string characterName;
    public string result;
    public string startTime;
    public string endTime;
    public bool hasEndTime;
    public float durationMinutes;
    public int enemiesKilled;
    public bool bossKilled;
    public int goldEarned;
    public int perfectParries;
}

[Serializable]
public class PlayerProfileResponse
{
    public int playerId;
    public string playerName;
    public string createdAt;
    public int totalMatches;
    public int wins;
    public int losses;
    public int quitOrInProgress;
    public float winRatePercent;
    public float totalPlayTimeMinutes;
    public int totalEnemiesKilled;
    public int totalBossKills;
    public int totalPerfectParries;
    public int totalBlockHits;
    public int totalGoldEarned;
    public int totalGoldSpent;
    public string favoriteCharacterId;
    public string favoriteCharacterName;
    public int favoriteCharacterMatches;
    public string favoriteWeaponName;
    public int favoriteWeaponPurchases;
    public PlayerCharacterStatItem[] characterStats;
    public PlayerRecentMatchItem[] recentMatches;
}

// Client tải trọn bộ dữ liệu Hồ Sơ Chiến Binh từ /api/Players/{id}/profile
public class PlayerProfileApiClient : MonoBehaviour
{
    [Header("Backend API")]
    [SerializeField] private string baseUrl = ApiConfig.DEFAULT_BASE_URL;
    [SerializeField] private int timeoutSeconds = 15;

    private Coroutine activeFetchRoutine;

    public bool IsBusy { get; private set; }

    public string BaseUrl
    {
        get => ApiConfig.ResolveBaseUrl(baseUrl);
        set => baseUrl = value;
    }

    private void OnDisable()
    {
        if (activeFetchRoutine != null)
        {
            StopCoroutine(activeFetchRoutine);
            activeFetchRoutine = null;
        }
        IsBusy = false;
    }

    public void FetchProfile(int playerId, Action<bool, PlayerProfileResponse, string> callback, int recentLimit = 15)
    {
        if (playerId <= 0)
        {
            callback?.Invoke(false, null, "Chưa có thông tin tài khoản hợp lệ. Vui lòng đăng nhập lại.");
            return;
        }

        if (activeFetchRoutine != null)
        {
            StopCoroutine(activeFetchRoutine);
            activeFetchRoutine = null;
        }

        int safeLimit = Mathf.Clamp(recentLimit, 1, 50);
        activeFetchRoutine = StartCoroutine(FetchProfileCoroutine(playerId, safeLimit, callback));
    }

    private IEnumerator FetchProfileCoroutine(int playerId, int recentLimit, Action<bool, PlayerProfileResponse, string> callback)
    {
        IsBusy = true;
        string url = $"{BaseUrl.TrimEnd('/')}/api/Players/{playerId}/profile?recentLimit={recentLimit}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = Mathf.Max(5, timeoutSeconds);
            ApiConfig.ConfigureRequest(request, url);

            yield return request.SendWebRequest();

            IsBusy = false;
            activeFetchRoutine = null;

            string responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

            if (request.result == UnityWebRequest.Result.Success)
            {
                PlayerProfileResponse profile = null;
                try
                {
                    profile = JsonUtility.FromJson<PlayerProfileResponse>(responseText);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PlayerProfileApiClient] Lỗi phân tích JSON hồ sơ: {ex.Message}");
                }

                if (profile == null || profile.playerId <= 0)
                {
                    callback?.Invoke(false, null, "Không đọc được dữ liệu Hồ Sơ Chiến Binh từ máy chủ.");
                    yield break;
                }

                callback?.Invoke(true, profile, "Tải hồ sơ thành công.");
            }
            else
            {
                callback?.Invoke(false, null, ParseErrorMessage(responseText, request));
            }
        }
    }

    private static string ParseErrorMessage(string responseText, UnityWebRequest request)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                PlayerApiError err = JsonUtility.FromJson<PlayerApiError>(responseText);
                if (err != null && !string.IsNullOrWhiteSpace(err.message))
                    return err.message;
                if (err != null && !string.IsNullOrWhiteSpace(err.title))
                    return err.title;
            }
            catch
            {
            }
        }

        if (request.responseCode == 404) return "Không tìm thấy hồ sơ chiến binh trên máy chủ.";
        if (request.responseCode == 400) return "Yêu cầu truy vấn hồ sơ không hợp lệ.";

        return $"Không thể kết nối máy chủ ({request.error})";
    }
}
