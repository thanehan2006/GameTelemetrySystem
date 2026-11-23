using System;
using UnityEngine;

[Serializable]
// Dữ liệu mô tả một lớp nhân vật gồm model 3D, vũ khí và chỉ số chiến đấu
public class CharacterProfile
{
    public string id = "vanguard";
    public string displayName = "VANGUARD";
    public string title = "Chiến Binh Tiên Phong";
    public string roleBadge = "CÂN BẰNG";
    [TextArea(2, 4)]
    public string description = "Hiệp sĩ thiết giáp toàn diện với khả năng công thủ cân bằng.";
    public string signaturePerk = "Vũ khí khởi đầu: Kiếm Thiết Giáp  •  Parry hồi 6% HP";
    public Color themeColor = new Color(0.92f, 0.72f, 0.28f, 1f);

    [Header("3D Assets")]
    public GameObject modelPrefab;
    public GameObject startingWeaponPrefab;

    [Header("Gameplay Stats")]
    public int maxHealth = 190;
    public int attackDamage = 30;
    public float moveSpeed = 6.5f;
    public float knockbackForce = 7.5f;
    [Range(0f, 1f)] public float blockDamageReduction = 0.72f;
    [Range(0f, 1f)] public float perfectParryHealPercent = 0.06f;

    [Header("UI Stat Bars (0 - 100)")]
    [Range(0, 100)] public int statHealth = 78;
    [Range(0, 100)] public int statAttack = 74;
    [Range(0, 100)] public int statSpeed = 74;
    [Range(0, 100)] public int statDefense = 80;
}

// ScriptableObject lưu danh sách 4 lớp nhân vật có thể chọn trước khi vào trận
[CreateAssetMenu(fileName = "CharacterRosterConfig", menuName = "DucThanh/Character Roster Config")]
public class CharacterRosterConfig : ScriptableObject
{
    public const string ResourcePath = "CharacterRosterConfig";

    public CharacterProfile[] characters = Array.Empty<CharacterProfile>();

    public int Count => characters != null ? characters.Length : 0;

    public CharacterProfile GetProfile(int index)
    {
        if (characters == null || characters.Length == 0) return null;
        int safeIdx = Mathf.Clamp(index, 0, characters.Length - 1);
        return characters[safeIdx];
    }
}

// Lưu trữ nhân vật và bản đồ người chơi đã chọn xuyên suốt quá trình chuyển scene
public static class SelectedCharacterStore
{
    private const string PrefKey = "SelectedCharacterIndex";
    private static int cachedIndex = -1;
    private static CharacterRosterConfig cachedRoster;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        cachedIndex = -1;
        cachedRoster = null;
    }

    // Nạp sẵn cấu hình danh sách nhân vật từ quá trình preload
    public static void SetPreloadedRoster(CharacterRosterConfig roster)
    {
        if (roster != null)
        {
            cachedRoster = roster;
        }
    }

    public static int SelectedIndex
    {
        get
        {
            if (cachedIndex < 0)
            {
                cachedIndex = PlayerPrefs.GetInt(PrefKey, 0);
            }
            return Mathf.Max(0, cachedIndex);
        }
        set
        {
            cachedIndex = Mathf.Max(0, value);
            PlayerPrefs.SetInt(PrefKey, cachedIndex);
            PlayerPrefs.Save();
        }
    }

    public static CharacterRosterConfig GetRoster()
    {
        if (cachedRoster == null)
        {
            cachedRoster = GameAssetPreloader.GetAsset<CharacterRosterConfig>(CharacterRosterConfig.ResourcePath)
                ?? Resources.Load<CharacterRosterConfig>(CharacterRosterConfig.ResourcePath);
        }
        return cachedRoster;
    }

    // Trả về hồ sơ nhân vật đang được người chơi lựa chọn
    public static CharacterProfile GetSelectedProfile()
    {
        CharacterRosterConfig roster = GetRoster();
        return roster != null ? roster.GetProfile(SelectedIndex) : null;
    }
}
