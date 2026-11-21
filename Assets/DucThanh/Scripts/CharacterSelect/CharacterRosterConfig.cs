using System;
using UnityEngine;

/// <summary>
/// Hồ sơ dữ liệu của từng nhân vật có thể chọn (Vanguard, Maria, Vampire, Maw):
/// Dùng chung cho cả màn hình Chọn Nhân Vật (CharacterSelectScene) và màn chơi thực tế (Lv1).
/// </summary>
[Serializable]
public class CharacterProfile
{
    public string id = "vanguard";
    public string displayName = "VANGUARD";
    public string title = "Chiến Binh Tiên Phong";
    public string roleBadge = "CÂN BẰNG";
    [TextArea(2, 4)]
    public string description = "Hiệp sĩ thiết giáp toàn diện với khả năng công thủ cân bằng.";
    public string signaturePerk = "Vũ khí khởi đầu: Kiếm Thiết Giáp • Parry hồi 5% HP";
    public Color themeColor = new Color(0.92f, 0.72f, 0.28f, 1f);

    [Header("3D Assets")]
    public GameObject modelPrefab;
    public GameObject startingWeaponPrefab;

    [Header("Gameplay Stats (áp dụng thật khi vào Lv1)")]
    public int maxHealth = 180;
    public int attackDamage = 35;
    public float moveSpeed = 6.5f;
    public float knockbackForce = 7.5f;
    [Range(0f, 1f)] public float blockDamageReduction = 0.80f;
    [Range(0f, 1f)] public float perfectParryHealPercent = 0.05f;

    [Header("UI Stat Bars (0 - 100)")]
    [Range(0, 100)] public int statHealth = 75;
    [Range(0, 100)] public int statAttack = 72;
    [Range(0, 100)] public int statSpeed = 72;
    [Range(0, 100)] public int statDefense = 78;
}

/// <summary>
/// ScriptableObject lưu danh sách 4 nhân vật (Assets/Resources/CharacterRosterConfig.asset).
/// </summary>
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

/// <summary>
/// Lưu & đọc nhân vật người chơi đã chọn xuyên suốt các Scene (CharacterSelectScene -> Lv1).
/// </summary>
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

    public static CharacterProfile GetSelectedProfile()
    {
        CharacterRosterConfig roster = GetRoster();
        return roster != null ? roster.GetProfile(SelectedIndex) : null;
    }
}
