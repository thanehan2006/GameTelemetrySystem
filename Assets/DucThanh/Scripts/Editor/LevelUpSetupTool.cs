#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu:
/// - DucThanh > Open Weapon Upgrade Config (ScriptableObject): Mở file ScriptableObject cấu hình thứ tự vũ khí & vị trí cầm.
/// - DucThanh > Save Current Player Weapon Grip: Lưu vị trí vũ khí đang nằm trên tay Player vào ScriptableObject.
/// - DucThanh > Setup LevelUp Choice Manager: Gắn LevelUpChoiceManager + gán sẵn file ScriptableObject vào ScoreManager.
/// </summary>
public static class LevelUpSetupTool
{
    private const string WeaponsFolder = "Assets/DucThanh/Assets/Weapons";
    private const string ConfigAssetPath = LevelUpChoiceManager.DefaultConfigAssetPath;

    [MenuItem("DucThanh/Open Weapon Upgrade Config (ScriptableObject)")]
    public static void OpenOrCreateWeaponConfig()
    {
        WeaponUpgradeConfigSO config = GetOrCreateConfigAsset();
        if (config != null)
        {
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
            Debug.Log($"[LevelUpSetupTool] Đã mở '{ConfigAssetPath}'. Các vũ khí sẽ xuất hiện LẦN LƯỢT từ số thấp đến số cao (theo Min Level và thứ tự từ trên xuống dưới)!");
        }
    }

    [MenuItem("DucThanh/Save Current Player Weapon Grip (Lưu vị trí vũ khí đang cầm)")]
    public static void SaveCurrentPlayerWeaponGrip()
    {
        WeaponUpgradeConfigSO config = GetOrCreateConfigAsset();
        if (config == null) return;

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("[LevelUpSetupTool] Không tìm thấy Player trong Scene!");
            return;
        }

        LevelUpChoiceManager.FindPlayerHandBone(player, out GameObject equippedWeapon);
        if (equippedWeapon == null)
        {
            Debug.LogWarning("[LevelUpSetupTool] Không tìm thấy vũ khí nào đang gắn trên tay Player!");
            return;
        }

        Undo.RecordObject(config, "Lưu vị trí vũ khí trên tay Player");
        WeaponUpgradeEntry entry = config.SaveGripForWeapon(equippedWeapon.transform, true);
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();

        string displayName = entry != null ? entry.GetDisplayName() : equippedWeapon.name;
        Debug.Log($"[LevelUpSetupTool] Đã lưu vị trí cầm trên tay cho '{displayName}' -> Pos={equippedWeapon.transform.localPosition}, Rot={equippedWeapon.transform.localEulerAngles}, Scale={equippedWeapon.transform.localScale}");
    }

    [MenuItem("DucThanh/Setup LevelUp Choice Manager")]
    public static void SetupInOpenScene()
    {
        WeaponUpgradeConfigSO config = GetOrCreateConfigAsset();

        // Lưu luôn vị trí vũ khí đang cầm trên tay Player làm chuẩn nếu có
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null && config != null)
        {
            LevelUpChoiceManager.FindPlayerHandBone(player, out GameObject equippedWeapon);
            if (equippedWeapon != null)
            {
                config.SaveGripForWeapon(equippedWeapon.transform, true);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
        }

        ScoreManager scoreManager = Object.FindFirstObjectByType<ScoreManager>();
        if (scoreManager == null)
        {
            Debug.LogError("[LevelUpSetupTool] Không tìm thấy ScoreManager trong Scene đang mở!");
            return;
        }

        Undo.RecordObject(scoreManager, "Đặt giới hạn 10 cấp Player");
        scoreManager.maxLevel = 10;
        EditorUtility.SetDirty(scoreManager);

        LevelUpChoiceManager manager = scoreManager.GetComponent<LevelUpChoiceManager>();
        if (manager == null)
        {
            manager = Undo.AddComponent<LevelUpChoiceManager>(scoreManager.gameObject);
        }

        Undo.RecordObject(manager, "Gán WeaponUpgradeConfigSO cho LevelUpChoiceManager");
        manager.weaponConfig = config;
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scoreManager.gameObject.scene);

        if (config != null)
        {
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        Debug.Log($"[LevelUpSetupTool] Đã gắn WeaponUpgradeConfigSO ({(config != null ? config.weapons.Count : 0)} vũ khí, ra lần lượt số thấp trước) và đặt maxLevel = 10.");
    }

    public static WeaponUpgradeConfigSO GetOrCreateConfigAsset()
    {
        WeaponUpgradeConfigSO config = AssetDatabase.LoadAssetAtPath<WeaponUpgradeConfigSO>(ConfigAssetPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<WeaponUpgradeConfigSO>();
            PopulateInitialWeapons(config);
            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LevelUpSetupTool] Đã tạo mới ScriptableObject tại '{ConfigAssetPath}'.");
        }
        else if (config.weapons == null || config.weapons.Count == 0)
        {
            if (PopulateInitialWeapons(config))
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
        }

        return config;
    }

    private static bool PopulateInitialWeapons(WeaponUpgradeConfigSO config)
    {
        if (!AssetDatabase.IsValidFolder(WeaponsFolder)) return false;

        if (config.weapons == null)
        {
            config.weapons = new List<WeaponUpgradeEntry>();
        }

        if (config.weapons.Count > 0) return false;

        bool changed = false;
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { WeaponsFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            config.weapons.Add(new WeaponUpgradeEntry
            {
                enabled = true,
                displayName = prefab.name.Replace("_", " ").Trim(),
                weaponPrefab = prefab,
                bonusDamage = 10,
                minLevel = i + 1
            });
            changed = true;
        }

        return changed;
    }
}
#endif
