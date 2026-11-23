using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Tải trước prefab, âm thanh và làm ấm shader trong lúc chạy màn hình Loading để tránh giật hình khi vào trận
public class GameAssetPreloader : MonoBehaviour
{
    private readonly struct AssetPreloadEntry
    {
        public readonly string path;
        public readonly Type assetType;
        public readonly string categoryLabel;
        public readonly bool required;
        public readonly string audioShortName;

        public AssetPreloadEntry(
            string path,
            Type assetType,
            string categoryLabel,
            bool required = true,
            string audioShortName = null)
        {
            this.path = path;
            this.assetType = assetType;
            this.categoryLabel = categoryLabel;
            this.required = required;
            this.audioShortName = audioShortName;
        }
    }

    private static readonly AssetPreloadEntry[] CoreManifest =
    {

        new AssetPreloadEntry("UI/LoadingBg", typeof(Texture2D), "giao diện & hình ảnh", required: false),
        new AssetPreloadEntry("UI/LoginBg", typeof(Texture2D), "giao diện & hình ảnh", required: true),
        new AssetPreloadEntry("UI/icon_gold", typeof(Texture2D), "giao diện & hình ảnh", required: true),
        new AssetPreloadEntry("UI/icon_potion", typeof(Texture2D), "giao diện & hình ảnh", required: true),

        new AssetPreloadEntry("CharacterRosterConfig", typeof(CharacterRosterConfig), "nhân vật & mô hình 3D", required: true),

        new AssetPreloadEntry("Rewards/SM_Rewards_Bag", typeof(GameObject), "vật phẩm & phần thưởng 3D", required: true),
        new AssetPreloadEntry("Rewards/SM_Rewards_Bottle", typeof(GameObject), "vật phẩm & phần thưởng 3D", required: true),
        new AssetPreloadEntry("Rewards/SM_Rewards_ChestGold", typeof(GameObject), "vật phẩm & phần thưởng 3D", required: true),
        new AssetPreloadEntry("Rewards/SM_Rewards_CoinCrown", typeof(GameObject), "vật phẩm & phần thưởng 3D", required: true),
        new AssetPreloadEntry("Rewards/SM_Rewards_CoinStar", typeof(GameObject), "vật phẩm & phần thưởng 3D", required: true),

        new AssetPreloadEntry("Audio/bgm_menu", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "bgm_menu"),
        new AssetPreloadEntry("Audio/bgm_forest_combat", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "bgm_forest_combat"),
        new AssetPreloadEntry("Audio/bgm_frost_combat", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "bgm_frost_combat"),
        new AssetPreloadEntry("Audio/bgm_inferno_combat", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "bgm_inferno_combat"),
        new AssetPreloadEntry("Audio/bgm_boss", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "bgm_boss"),
        new AssetPreloadEntry("Audio/sfx_boss_roar", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_boss_roar"),
        new AssetPreloadEntry("Audio/sfx_checkpoint", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_checkpoint"),
        new AssetPreloadEntry("Audio/sfx_dash", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_dash"),
        new AssetPreloadEntry("Audio/sfx_enemy_death", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_enemy_death"),
        new AssetPreloadEntry("Audio/sfx_footstep", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_footstep"),
        new AssetPreloadEntry("Audio/sfx_gameover", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_gameover"),
        new AssetPreloadEntry("Audio/sfx_level_up", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_level_up"),
        new AssetPreloadEntry("Audio/sfx_orc_attack", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_orc_attack"),
        new AssetPreloadEntry("Audio/sfx_pickup", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_pickup"),
        new AssetPreloadEntry("Audio/sfx_player_hurt", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_player_hurt"),
        new AssetPreloadEntry("Audio/sfx_stage_clear", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_stage_clear"),
        new AssetPreloadEntry("Audio/sfx_sword_hit", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_sword_hit"),
        new AssetPreloadEntry("Audio/sfx_sword_swing", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_sword_swing"),
        new AssetPreloadEntry("Audio/sfx_trap_hit", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_trap_hit"),
        new AssetPreloadEntry("Audio/sfx_ui_click", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_ui_click"),
        new AssetPreloadEntry("Audio/sfx_victory", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_victory"),
        new AssetPreloadEntry("Audio/sfx_weapon_equip", typeof(AudioClip), "âm thanh & nhạc nền", required: true, audioShortName: "sfx_weapon_equip")
    };

    private static GameAssetPreloader instance;

    public static GameAssetPreloader Instance => instance;
    public static bool IsCoreAssetsLoaded => instance != null && instance.coreAssetsLoaded;
    public static bool HasMissingCoreAssets => instance != null && instance.missingRequiredAssets.Count > 0;

    public int LoadedAssetCount { get; private set; }
    public int TotalAssetCount { get; private set; }
    public IReadOnlyList<string> MissingRequiredAssets => missingRequiredAssets;

    private readonly Dictionary<string, UnityEngine.Object> assetCache = new Dictionary<string, UnityEngine.Object>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<UnityEngine.Object> retainedDirectAssets = new HashSet<UnityEngine.Object>();
    private readonly List<string> missingRequiredAssets = new List<string>();
    private bool coreAssetsLoaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
    }

    // Tạo hoặc trả về Preloader dùng chung xuyên suốt giữa các scene
    public static GameAssetPreloader EnsureInstance()
    {
        if (instance != null) return instance;

        instance = FindFirstObjectByType<GameAssetPreloader>();
        if (instance == null)
        {
            GameObject go = new GameObject("GameAssetPreloader");
            instance = go.AddComponent<GameAssetPreloader>();
        }

        if (instance.transform.parent != null)
        {
            instance.transform.SetParent(null, false);
        }
        DontDestroyOnLoad(instance.gameObject);
        return instance;
    }

    public static T GetAsset<T>(string resourcePath) where T : UnityEngine.Object
    {
        if (instance == null || string.IsNullOrEmpty(resourcePath)) return null;

        if (instance.assetCache.TryGetValue(resourcePath, out UnityEngine.Object cached) && cached is T typed && typed != null)
        {
            return typed;
        }

        return null;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        if (transform.parent != null)
        {
            transform.SetParent(null, false);
        }
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    // Nạp bất đồng bộ tài nguyên từ Resources, đăng ký vào hệ thống cache và báo tiến độ tải
    public IEnumerator PreloadAssetsCoroutine(
        bool includeCoreAssets,
        string[] extraResourcePaths,
        UnityEngine.Object[] directPreloadAssets,
        ShaderVariantCollection[] shaderCollections,
        Action<float, string> onProgress)
    {
        List<AssetPreloadEntry> entries = new List<AssetPreloadEntry>(CoreManifest.Length + 8);
        HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (includeCoreAssets)
        {
            for (int i = 0; i < CoreManifest.Length; i++)
            {
                AssetPreloadEntry entry = CoreManifest[i];
                if (seenPaths.Add(entry.path))
                {
                    entries.Add(entry);
                }
            }
        }

        if (extraResourcePaths != null)
        {
            for (int i = 0; i < extraResourcePaths.Length; i++)
            {
                string raw = extraResourcePaths[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;

                string trimmed = raw.Trim();
                if (seenPaths.Add(trimmed))
                {
                    entries.Add(new AssetPreloadEntry(trimmed, typeof(UnityEngine.Object), "tài nguyên bổ sung", required: false));
                }
            }
        }

        if (directPreloadAssets != null)
        {
            for (int i = 0; i < directPreloadAssets.Length; i++)
            {
                UnityEngine.Object directObj = directPreloadAssets[i];
                if (directObj == null) continue;

                retainedDirectAssets.Add(directObj);
                if (directObj is GameObject go)
                {
                    WarmPrefabReferences(go);
                }
            }
        }

        List<ShaderVariantCollection> pendingShaders = new List<ShaderVariantCollection>();
        if (shaderCollections != null)
        {
            for (int i = 0; i < shaderCollections.Length; i++)
            {
                ShaderVariantCollection svc = shaderCollections[i];
                if (svc != null && !svc.isWarmedUp && !pendingShaders.Contains(svc))
                {
                    pendingShaders.Add(svc);
                }
            }
        }

        Dictionary<string, int> categoryTotals = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, int> categoryDoneCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < entries.Count; i++)
        {
            string cat = entries[i].categoryLabel;
            categoryTotals.TryGetValue(cat, out int count);
            categoryTotals[cat] = count + 1;
            if (!categoryDoneCounts.ContainsKey(cat))
            {
                categoryDoneCounts[cat] = 0;
            }
        }

        missingRequiredAssets.Clear();
        TotalAssetCount = entries.Count;
        LoadedAssetCount = 0;

        int totalSteps = 1 + entries.Count + pendingShaders.Count;
        int completedSteps = 0;

        onProgress?.Invoke(0f, "Đang khởi tạo tài nguyên giao diện...");
        FantasyUiThemeHelper.PrewarmUiSprites();
        completedSteps++;
        onProgress?.Invoke(Mathf.Clamp01((float)completedSteps / totalSteps), "Đang chuẩn bị danh sách tài nguyên...");

        float frameSliceStart = Time.realtimeSinceStartup;

        for (int i = 0; i < entries.Count; i++)
        {
            AssetPreloadEntry entry = entries[i];
            int catTotal = categoryTotals[entry.categoryLabel];
            int catCurrent = categoryDoneCounts[entry.categoryLabel] + 1;

            bool wasAlreadyCached = assetCache.TryGetValue(entry.path, out UnityEngine.Object loaded) && loaded != null;
            if (!wasAlreadyCached)
            {
                ResourceRequest req = Resources.LoadAsync(entry.path, entry.assetType);
                while (!req.isDone)
                {
                    float subProgress = Mathf.Clamp01((completedSteps + req.progress) / totalSteps);
                    onProgress?.Invoke(subProgress, $"Đang tải {entry.categoryLabel} ({catCurrent}/{catTotal})...");
                    yield return null;
                }

                loaded = req.asset;
                frameSliceStart = Time.realtimeSinceStartup;
            }

            if (loaded != null)
            {
                assetCache[entry.path] = loaded;
                LoadedAssetCount++;

                if (loaded is AudioClip clip)
                {
                    if (clip.loadState == AudioDataLoadState.Unloaded)
                    {
                        clip.LoadAudioData();
                    }

                    float audioWait = 0f;
                    while (clip.loadState == AudioDataLoadState.Loading && audioWait < 10f)
                    {
                        audioWait += Time.unscaledDeltaTime;
                        float audioProgress = Mathf.Clamp01((completedSteps + 0.85f) / totalSteps);
                        onProgress?.Invoke(audioProgress, $"Đang giải mã {entry.categoryLabel} ({catCurrent}/{catTotal})...");
                        yield return null;
                    }

                    if (clip.loadState == AudioDataLoadState.Failed)
                    {
                        Debug.LogWarning($"[GameAssetPreloader] Giải mã dữ liệu âm thanh thất bại: '{entry.path}'.");
                    }

                    if (!string.IsNullOrEmpty(entry.audioShortName))
                    {
                        GameAudioManager.EnsureInstance().RegisterPreloadedClip(entry.audioShortName, clip);
                    }
                }
                else if (loaded is CharacterRosterConfig roster)
                {
                    SelectedCharacterStore.SetPreloadedRoster(roster);
                    if (roster.characters != null)
                    {
                        for (int c = 0; c < roster.characters.Length; c++)
                        {
                            CharacterProfile profile = roster.characters[c];
                            if (profile == null) continue;

                            if (profile.modelPrefab != null)
                            {
                                retainedDirectAssets.Add(profile.modelPrefab);
                                WarmPrefabReferences(profile.modelPrefab);
                            }

                            if (profile.startingWeaponPrefab != null)
                            {
                                retainedDirectAssets.Add(profile.startingWeaponPrefab);
                                WarmPrefabReferences(profile.startingWeaponPrefab);
                            }
                        }
                    }
                }
                else if (loaded is GameObject prefab)
                {
                    WarmPrefabReferences(prefab);
                }
            }
            else
            {
                if (entry.required)
                {
                    Debug.LogError($"[GameAssetPreloader] Thiếu tài nguyên bắt buộc: Resources/{entry.path}");
                    if (!missingRequiredAssets.Contains(entry.path))
                    {
                        missingRequiredAssets.Add(entry.path);
                    }
                }
                else
                {
                    Debug.LogWarning($"[GameAssetPreloader] Không tìm thấy tài nguyên tùy chọn: Resources/{entry.path}");
                }
            }

            categoryDoneCounts[entry.categoryLabel] = catCurrent;
            completedSteps++;
            onProgress?.Invoke(
                Mathf.Clamp01((float)completedSteps / totalSteps),
                $"Đang tải {entry.categoryLabel} ({catCurrent}/{catTotal})...");

            if (!wasAlreadyCached || (Time.realtimeSinceStartup - frameSliceStart) > 0.008f)
            {
                yield return null;
                frameSliceStart = Time.realtimeSinceStartup;
            }
        }

        GoldPickup.RegisterPreloadedPrefabs(
            GetAsset<GameObject>("Rewards/SM_Rewards_CoinCrown"),
            GetAsset<GameObject>("Rewards/SM_Rewards_CoinStar"),
            GetAsset<GameObject>("Rewards/SM_Rewards_Bag"),
            GetAsset<GameObject>("Rewards/SM_Rewards_ChestGold"));
        HealthPickup.RegisterPreloadedPrefab(GetAsset<GameObject>("Rewards/SM_Rewards_Bottle"));

        for (int i = 0; i < pendingShaders.Count; i++)
        {
            ShaderVariantCollection svc = pendingShaders[i];
            if (svc == null) continue;

            while (!svc.isWarmedUp)
            {
                if (svc.variantCount == 0) break;

                bool finished = svc.WarmUpProgressively(4);
                float shaderFrac = svc.variantCount > 0
                    ? Mathf.Clamp01((float)svc.warmedUpVariantCount / svc.variantCount)
                    : 1f;
                float progress = Mathf.Clamp01((completedSteps + shaderFrac) / totalSteps);
                onProgress?.Invoke(progress, $"Đang biên dịch Shader ({svc.warmedUpVariantCount}/{svc.variantCount})...");
                if (finished) break;
                yield return null;
            }

            completedSteps++;
            onProgress?.Invoke(
                Mathf.Clamp01((float)completedSteps / totalSteps),
                $"Đang biên dịch Shader ({i + 1}/{pendingShaders.Count})...");
        }

        if (includeCoreAssets)
        {
            coreAssetsLoaded = missingRequiredAssets.Count == 0;
        }

        onProgress?.Invoke(1f, "Đã hoàn tất tải tài nguyên!");
    }

    private void WarmPrefabReferences(GameObject prefab)
    {
        if (prefab == null) return;

        MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            if (meshFilters[i] != null && meshFilters[i].sharedMesh != null)
            {
                retainedDirectAssets.Add(meshFilters[i].sharedMesh);
            }
        }

        SkinnedMeshRenderer[] skinnedMeshes = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinnedMeshes.Length; i++)
        {
            if (skinnedMeshes[i] != null && skinnedMeshes[i].sharedMesh != null)
            {
                retainedDirectAssets.Add(skinnedMeshes[i].sharedMesh);
            }
        }

        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            Material[] mats = renderers[i].sharedMaterials;
            if (mats == null) continue;
            for (int m = 0; m < mats.Length; m++)
            {
                if (mats[m] != null)
                {
                    retainedDirectAssets.Add(mats[m]);
                }
            }
        }
    }
}
