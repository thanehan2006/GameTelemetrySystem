#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu: DucThanh > Setup Gameplay 3 Man (De 9)
/// Tự động kiểm tra, gán đủ Prefabs/Skyboxes cho StageManager, cân bằng HP Player (180)
/// và tách bạch EXP Level khỏi Stage 1-3 trong scene Lv1.unity.
/// </summary>
public static class StageGameplaySetupTool
{
    private const string ScenePath = "Assets/DucThanh/Scene/Lv1.unity";

    [MenuItem("DucThanh/Setup Gameplay 3 Man (De 9)")]
    public static void SetupLv1Gameplay()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        StageManager stageManager = Object.FindFirstObjectByType<StageManager>();
        if (stageManager == null)
        {
            GameObject go = new GameObject("StageManager");
            stageManager = go.AddComponent<StageManager>();
        }

        stageManager.scoutOrcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DucThanh/Prefabs/Orc_Enemy_Combined1.prefab");
        stageManager.axeOrcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DucThanh/Prefabs/Orc_Enemy_Combined2.prefab");
        stageManager.shieldTankPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DucThanh/Prefabs/Orc_Enemy_Combined3.prefab");
        stageManager.bossOrcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DucThanh/Prefabs/Orc_Enemy_Combined3.prefab");

        stageManager.spikeTrapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_Spikes_01.prefab");
        stageManager.mushroomPickupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HealthPickup.PotionAssetPath);
        stageManager.runeStonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_RuneStone_02.prefab");
        stageManager.goldMinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PolyNext/Low Poly Orc Warrior/Environment/Prefabs/Gold_Mine.prefab");
        stageManager.trolleyGoldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PolyNext/Low Poly Orc Warrior/Environment/Prefabs/Trolley_Gold.prefab");
        stageManager.stoneWallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_StoneWall_01.prefab");
        stageManager.fireflyFxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BluBlu Games/Low Poly Forest Mini Pack/FX/Firefly.prefab");

        stageManager.skyboxStage1Dawn = AssetDatabase.LoadAssetAtPath<Material>("Assets/LaFinca/Midgard Skybox Prime/Skyboxs/2K/Midgard Prime 03 - Peach Dawn Expanse - First Light Rays 2K.mat");
        stageManager.skyboxStage2Sunset = AssetDatabase.LoadAssetAtPath<Material>("Assets/LaFinca/Midgard Skybox Prime/Skyboxs/2K/Midgard Prime 05 - Celestial Sunset Drift - Golden Ray Drift 2K.mat");
        stageManager.skyboxStage3Lunar = AssetDatabase.LoadAssetAtPath<Material>("Assets/LaFinca/Midgard Skybox Prime/Skyboxs/2K/Midgard Prime 08 - Pastel Lunar Billows - Moonlit Bloom 2K.mat");

        EditorUtility.SetDirty(stageManager);

        PlayerHealth playerHealth = Object.FindFirstObjectByType<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.maxHealth = 180;
            EditorUtility.SetDirty(playerHealth);
        }

        ScoreManager scoreManager = Object.FindFirstObjectByType<ScoreManager>();
        if (scoreManager != null)
        {
            scoreManager.manageStageTelemetry = false;
            scoreManager.maxLevel = 10;
            scoreManager.baseExpToLevelUp = 200;
            scoreManager.expIncreasePerLevel = 150;
            EditorUtility.SetDirty(scoreManager);
        }

        if (stageManager.skyboxStage1Dawn != null)
        {
            RenderSettings.skybox = stageManager.skyboxStage1Dawn;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[StageGameplaySetupTool] Đã cấu hình xong kịch bản Gameplay 3 Màn chuẩn Đề 9 cho Lv1.unity!");
    }
}
#endif
