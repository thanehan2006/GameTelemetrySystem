#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tự động tạo & bake toàn bộ Scene Chọn Nhân Vật (PickCharacter.unity) trực tiếp ở Edit Mode:
/// - Cấu hình rig Humanoid + extract textures cho 4 nhân vật (Player1..Player4).
/// - Dựng sân khấu 3D (nền đá tròn obsidian, tường đá, cột cổ ngữ, đom đóm, ánh sáng 3 điểm + Spotlight).
/// - Đặt sẵn 4 nhân vật 3D với vũ khí đặc trưng & Animator idle trong Scene (mở lên là thấy ngay, sửa trực tiếp được).
/// - Dựng toàn bộ Dark Fantasy UI Canvas & gán sẵn vào CharacterSelectController.
/// - Cập nhật Build Settings: LoadingScene -> LoginScene -> PickCharacter -> Lv1.
/// </summary>
[InitializeOnLoad]
public static class CharacterSelectSceneBuilder
{
    public const string ScenePath = "Assets/DucThanh/Scene/PickCharacter.unity";
    private const string RosterAssetPath = "Assets/Resources/CharacterRosterConfig.asset";
    private const string ShowcaseControllerPath = "Assets/DucThanh/Assets/Materials/CharSelect_Showcase_AC.controller";
    private const string MaterialsDir = "Assets/DucThanh/Assets/Materials";

    private const string Player1Fbx = "Assets/DucThanh/Assets/Player1/Idle.fbx";
    private const string Player2Fbx = "Assets/DucThanh/Assets/Player2/Unarmed Idle 01.fbx";
    private const string Player3Fbx = "Assets/DucThanh/Assets/Player3/Unarmed Idle 01 (2).fbx";
    private const string Player4Fbx = "Assets/DucThanh/Assets/Player4/Unarmed Idle 3 (1).fbx";

    private const string VanguardMatPath = "Assets/DucThanh/Assets/Materials/Player.mat";
    private const string WeaponSword1Path = "Assets/DucThanh/Assets/Weapons/Sword1_1_3.prefab";
    private const string WeaponSword2Path = "Assets/DucThanh/Assets/Weapons/Sword2_3_3.prefab";
    private const string WeaponScythePath = "Assets/DucThanh/Assets/Weapons/Scythe1_3_2.prefab";
    private const string WeaponHammerPath = "Assets/DucThanh/Assets/Weapons/Hammer1_1_3.prefab";

    private const string SkyboxPath = "Assets/LaFinca/Midgard Skybox Prime/Skyboxs/2K/Midgard Prime 08 - Pastel Lunar Billows - Moonlit Bloom 2K.mat";
    private const string StoneWallPath = "Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_StoneWall_01.prefab";
    private const string RuneStonePath = "Assets/BluBlu Games/Low Poly Forest Mini Pack/Prefabs/SM_RuneStone_02.prefab";
    private const string FireflyPath = "Assets/BluBlu Games/Low Poly Forest Mini Pack/FX/Firefly.prefab";
    private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

    private const string SessionAutoBuiltKey = "CharSelectSceneAutoBuilt_v1";

    static CharacterSelectSceneBuilder()
    {
        if (NeedsInitialBake() && !SessionState.GetBool(SessionAutoBuiltKey, false))
        {
            SessionState.SetBool(SessionAutoBuiltKey, true);
            EditorApplication.delayCall += () =>
            {
                if (NeedsInitialBake() && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    BuildSceneInternal(openInSingleMode: false);
                }
            };
        }
    }

    private static bool NeedsInitialBake()
    {
        if (!File.Exists(ScenePath) || !File.Exists(RosterAssetPath)) return true;
        string sceneContent = File.ReadAllText(ScenePath);
        return !sceneContent.Contains("CharacterSelectController");
    }

    [MenuItem("DucThanh/Build PickCharacter Scene (4 Heroes)")]
    public static void BuildAndOpenScene()
    {
        BuildSceneInternal(openInSingleMode: true);
    }

    private static void BuildSceneInternal(bool openInSingleMode)
    {
        ConfigureHeroImports();
        CharacterRosterConfig roster = BuildOrUpdateRosterAsset();
        AnimatorController showcaseAc = BuildShowcaseAnimatorController();
        CreateStageMaterials(out Material floorMat, out Material pedestalMat, out Material goldTrimMat, out Material ringGlowMat);

        Scene previousActive = EditorSceneManager.GetActiveScene();
        bool isTargetCurrentlyOpen = string.Equals(previousActive.path, ScenePath, System.StringComparison.OrdinalIgnoreCase);
        bool useSingleMode = openInSingleMode || isTargetCurrentlyOpen;

        Scene targetScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            useSingleMode ? NewSceneMode.Single : NewSceneMode.Additive);

        EditorSceneManager.SetActiveScene(targetScene);

        try
        {
            BuildSceneContents(targetScene, roster, showcaseAc, floorMat, pedestalMat, goldTrimMat, ringGlowMat);

            string dir = Path.GetDirectoryName(ScenePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            EditorSceneManager.MarkSceneDirty(targetScene);
            EditorSceneManager.SaveScene(targetScene, ScenePath);
            EnsureSceneInBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[CharacterSelectSceneBuilder] Đã dựng và lưu xong PickCharacter.unity với 4 nhân vật 3D + UI!");
        }
        finally
        {
            if (!useSingleMode)
            {
                if (previousActive.IsValid())
                {
                    EditorSceneManager.SetActiveScene(previousActive);
                }
                EditorSceneManager.CloseScene(targetScene, true);
            }
        }
    }

    // ==================== 1. CONFIGURE HERO FBXs & ROSTER ASSET ====================

    private static void ConfigureHeroImports()
    {
        ConfigureSingleHeroFbx(Player1Fbx, extractTextures: false);
        ConfigureSingleHeroFbx(Player2Fbx, extractTextures: true);
        ConfigureSingleHeroFbx(Player3Fbx, extractTextures: true);
        ConfigureSingleHeroFbx(Player4Fbx, extractTextures: true);
        AssetDatabase.Refresh();
        HeroMaterialFixer.FixAllHeroMaterials(forceUpdate: false);
    }

    private static void ConfigureSingleHeroFbx(string fbxPath, bool extractTextures)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogWarning("[CharacterSelectSceneBuilder] Không tìm thấy FBX: " + fbxPath);
            return;
        }

        bool changed = false;

        if (extractTextures)
        {
            string texDir = Path.GetDirectoryName(fbxPath)?.Replace('\\', '/') + "/Textures";
            if (!Directory.Exists(texDir))
            {
                Directory.CreateDirectory(texDir);
                importer.ExtractTextures(texDir);
                changed = true;
            }
        }

        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            changed = true;
        }

        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            changed = true;
        }

        if (changed)
        {
            importer.SaveAndReimport();
            importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        }

        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }

        if (clips != null && clips.Length > 0)
        {
            bool clipChanged = false;
            foreach (var c in clips)
            {
                if (!c.loopTime || !c.lockRootRotation || !c.lockRootHeightY || !c.lockRootPositionXZ || !c.heightFromFeet)
                {
                    c.loopTime = true;
                    c.loopPose = true;
                    c.lockRootRotation = true;
                    c.lockRootHeightY = true;
                    c.lockRootPositionXZ = true;
                    c.keepOriginalOrientation = false;
                    c.keepOriginalPositionY = false;
                    c.keepOriginalPositionXZ = false;
                    c.heightFromFeet = true;
                    clipChanged = true;
                }
            }

            if (clipChanged)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }
    }

    private static CharacterRosterConfig BuildOrUpdateRosterAsset()
    {
        if (!Directory.Exists("Assets/Resources"))
        {
            Directory.CreateDirectory("Assets/Resources");
        }

        CharacterRosterConfig roster = AssetDatabase.LoadAssetAtPath<CharacterRosterConfig>(RosterAssetPath);
        if (roster == null)
        {
            roster = ScriptableObject.CreateInstance<CharacterRosterConfig>();
            AssetDatabase.CreateAsset(roster, RosterAssetPath);
        }

        roster.characters = new[]
        {
            new CharacterProfile
            {
                id = "vanguard",
                displayName = "VANGUARD",
                title = "Chiến Binh Tiên Phong",
                roleBadge = "CÂN BẰNG",
                description = "Hiệp sĩ thiết giáp toàn diện với kiếm pháp vững chắc, phù hợp mọi tình huống giao tranh và phản đòn Parry.",
                signaturePerk = "Vũ khí: Kiếm Thiết Giáp  •  Công & Thủ cân bằng",
                themeColor = new Color(0.95f, 0.74f, 0.28f, 1f),
                modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Player1Fbx),
                startingWeaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponSword1Path),
                maxHealth = 180,
                attackDamage = 35,
                moveSpeed = 6.5f,
                knockbackForce = 7.5f,
                blockDamageReduction = 0.80f,
                perfectParryHealPercent = 0.05f,
                statHealth = 76,
                statAttack = 72,
                statSpeed = 72,
                statDefense = 78
            },
            new CharacterProfile
            {
                id = "maria",
                displayName = "MARIA",
                title = "Kiếm Sĩ Bão Tố",
                roleBadge = "TỐC ĐỘ CAO",
                description = "Nữ kiếm sĩ linh hoạt sở hữu bước chạy thần tốc và nhát chém sắc bén, dễ dàng né bẫy và áp sát mục tiêu.",
                signaturePerk = "Vũ khí: Băng Tinh Kiếm  •  Chạy nhanh 7.3 m/s",
                themeColor = new Color(0.28f, 0.84f, 0.98f, 1f),
                modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Player2Fbx),
                startingWeaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponSword2Path),
                maxHealth = 155,
                attackDamage = 38,
                moveSpeed = 7.3f,
                knockbackForce = 7.0f,
                blockDamageReduction = 0.78f,
                perfectParryHealPercent = 0.05f,
                statHealth = 64,
                statAttack = 79,
                statSpeed = 94,
                statDefense = 70
            },
            new CharacterProfile
            {
                id = "vampire",
                displayName = "VAMPIRE",
                title = "Lãnh Chúa Huyết Nguyệt",
                roleBadge = "SÁT THƯƠNG / HỒI PHỤC",
                description = "Huyết tộc mang lưỡi hái tử thần với sát thương cơ bản cao nhất và khả năng hút sinh lực vượt trội khi Parry.",
                signaturePerk = "Vũ khí: Lưỡi Hái Huyết Nguyệt  •  Parry hồi 8% HP",
                themeColor = new Color(0.95f, 0.26f, 0.36f, 1f),
                modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Player3Fbx),
                startingWeaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponScythePath),
                maxHealth = 165,
                attackDamage = 42,
                moveSpeed = 6.8f,
                knockbackForce = 7.8f,
                blockDamageReduction = 0.75f,
                perfectParryHealPercent = 0.08f,
                statHealth = 69,
                statAttack = 92,
                statSpeed = 80,
                statDefense = 66
            },
            new CharacterProfile
            {
                id = "maw",
                displayName = "MAW",
                title = "Khổng Lồ Phá Giáp",
                roleBadge = "TANKER / ĐẨY LÙI",
                description = "Chiến tướng hộ pháp với lượng sinh lực dồi dào, giáp đỡ 85% sát thương và cú nện búa hất văng cả bầy Orc.",
                signaturePerk = "Vũ khí: Đại Chùy Chiến Thần  •  230 HP & Đỡ 85%",
                themeColor = new Color(0.74f, 0.44f, 0.98f, 1f),
                modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Player4Fbx),
                startingWeaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponHammerPath),
                maxHealth = 230,
                attackDamage = 40,
                moveSpeed = 6.0f,
                knockbackForce = 9.5f,
                blockDamageReduction = 0.85f,
                perfectParryHealPercent = 0.05f,
                statHealth = 96,
                statAttack = 84,
                statSpeed = 60,
                statDefense = 94
            }
        };

        EditorUtility.SetDirty(roster);
        return roster;
    }

    private static AnimatorController BuildShowcaseAnimatorController()
    {
        if (!Directory.Exists(MaterialsDir)) Directory.CreateDirectory(MaterialsDir);

        AnimatorController ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ShowcaseControllerPath);
        if (ac == null)
        {
            ac = AnimatorController.CreateAnimatorControllerAtPath(ShowcaseControllerPath);
        }

        AnimatorStateMachine sm = ac.layers[0].stateMachine;
        foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
        foreach (var s in sm.states) sm.RemoveState(s.state);

        AnimationClip fallbackClip = LoadAnimationClip(Player1Fbx);
        string[] fbxPaths = { Player1Fbx, Player2Fbx, Player3Fbx, Player4Fbx };

        for (int i = 0; i < fbxPaths.Length; i++)
        {
            AnimationClip clip = LoadAnimationClip(fbxPaths[i]) ?? fallbackClip;
            AnimatorState st = sm.AddState($"Hero{i}_Idle", new Vector3(240f, i * 70f, 0f));
            st.motion = clip;
            if (i == 0) sm.defaultState = st;
        }

        EditorUtility.SetDirty(ac);
        return ac;
    }

    private static AnimationClip LoadAnimationClip(string fbxPath)
    {
        foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (obj is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                return clip;
            }
        }
        return null;
    }

    private static void CreateStageMaterials(out Material floorMat, out Material pedestalMat, out Material goldTrimMat, out Material ringGlowMat)
    {
        if (!Directory.Exists(MaterialsDir)) Directory.CreateDirectory(MaterialsDir);

        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        floorMat = CreateOrLoadMat(MaterialsDir + "/CharSelect_StageFloor.mat", lit, new Color(0.06f, 0.075f, 0.11f, 1f), 0.15f, 0.48f, false, Color.black);
        pedestalMat = CreateOrLoadMat(MaterialsDir + "/CharSelect_Pedestal.mat", lit, new Color(0.11f, 0.13f, 0.18f, 1f), 0.25f, 0.52f, false, Color.black);
        goldTrimMat = CreateOrLoadMat(MaterialsDir + "/CharSelect_GoldTrim.mat", lit, new Color(0.76f, 0.56f, 0.20f, 1f), 0.85f, 0.72f, false, Color.black);
        ringGlowMat = CreateOrLoadMat(MaterialsDir + "/CharSelect_RingGlow.mat", lit, new Color(0.95f, 0.74f, 0.28f, 1f), 0.1f, 0.85f, true, new Color(1.1f, 0.82f, 0.28f, 1f));
    }

    private static Material CreateOrLoadMat(string path, Shader shader, Color color, float metallic, float smoothness, bool emission, Color emissionColor)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }

        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (emission)
        {
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ==================== 2. BUILD 3D STAGE & UI DIRECTLY IN SCENE ====================

    private static void BuildSceneContents(
        Scene scene,
        CharacterRosterConfig roster,
        AnimatorController showcaseAc,
        Material floorMat,
        Material pedestalMat,
        Material goldTrimMat,
        Material ringGlowMat)
    {
        // Skybox & Lighting Environment
        Material skyMat = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
        if (skyMat != null)
        {
            RenderSettings.skybox = skyMat;
        }
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.24f, 0.30f, 0.44f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.14f, 0.16f, 0.24f, 1f);
        RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.08f, 1f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.04f, 0.06f, 0.11f, 1f);
        RenderSettings.fogDensity = 0.024f;

        // 1. Stage Environment (Floor + Trim + Stone Walls + Runes + Fireflies)
        GameObject envRoot = new GameObject("StageEnvironment");

        GameObject mainFloor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        mainFloor.name = "MainStagePlatform";
        mainFloor.transform.SetParent(envRoot.transform, false);
        mainFloor.transform.position = new Vector3(0f, -0.35f, 0.3f);
        mainFloor.transform.localScale = new Vector3(13.5f, 0.35f, 13.5f);
        mainFloor.GetComponent<Renderer>().sharedMaterial = floorMat;

        GameObject outerTrim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        outerTrim.name = "StageGoldTrim";
        outerTrim.transform.SetParent(envRoot.transform, false);
        outerTrim.transform.position = new Vector3(0f, -0.39f, 0.3f);
        outerTrim.transform.localScale = new Vector3(13.9f, 0.32f, 13.9f);
        Object.DestroyImmediate(outerTrim.GetComponent<Collider>());
        outerTrim.GetComponent<Renderer>().sharedMaterial = goldTrimMat;

        GameObject wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StoneWallPath);
        if (wallPrefab != null)
        {
            for (int i = 0; i < 7; i++)
            {
                float angle = Mathf.Lerp(-52f, 52f, i / 6f) * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(Mathf.Sin(angle) * 6.4f, -0.15f, Mathf.Cos(angle) * 5.2f + 0.6f);
                GameObject w = (GameObject)PrefabUtility.InstantiatePrefab(wallPrefab);
                w.name = $"BackdropWall_{i + 1}";
                w.transform.SetParent(envRoot.transform, false);
                w.transform.position = pos;
                w.transform.rotation = Quaternion.LookRotation(new Vector3(0f, pos.y, -1f) - pos);
                w.transform.localScale = Vector3.one * 1.35f;
            }
        }

        GameObject runePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuneStonePath);
        if (runePrefab != null)
        {
            SpawnBackdropPrefab(runePrefab, envRoot.transform, "RuneStone_Left", new Vector3(-5.1f, -0.05f, 2.4f), Quaternion.Euler(0f, 28f, 0f), Vector3.one * 1.45f);
            SpawnBackdropPrefab(runePrefab, envRoot.transform, "RuneStone_Right", new Vector3(5.1f, -0.05f, 2.4f), Quaternion.Euler(0f, -28f, 0f), Vector3.one * 1.45f);
        }

        GameObject fireflyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FireflyPath);
        if (fireflyPrefab != null)
        {
            SpawnBackdropPrefab(fireflyPrefab, envRoot.transform, "StageFireflies", new Vector3(0f, 1.4f, 1.5f), Quaternion.identity, Vector3.one);
        }

        // 2. Three-Point Dramatic Lighting + Active Hero Spotlight
        GameObject lightingRoot = new GameObject("StageLighting");

        GameObject dirLightObj = new GameObject("KeyDirectionalLight");
        dirLightObj.transform.SetParent(lightingRoot.transform, false);
        dirLightObj.transform.rotation = Quaternion.Euler(38f, -26f, 0f);
        Light dirLight = dirLightObj.AddComponent<Light>();
        dirLight.type = LightType.Directional;
        dirLight.color = new Color(1f, 0.94f, 0.86f, 1f);
        dirLight.intensity = 1.35f;
        dirLight.shadows = LightShadows.Soft;

        GameObject rimLightObj = new GameObject("BackRimPointLight");
        rimLightObj.transform.SetParent(lightingRoot.transform, false);
        rimLightObj.transform.position = new Vector3(0f, 3.6f, 3.8f);
        Light rimLight = rimLightObj.AddComponent<Light>();
        rimLight.type = LightType.Point;
        rimLight.color = new Color(0.45f, 0.72f, 1f, 1f);
        rimLight.range = 16f;
        rimLight.intensity = 7.5f;

        GameObject spotAnchorObj = new GameObject("HeroSpotlightAnchor");
        spotAnchorObj.transform.SetParent(lightingRoot.transform, false);
        spotAnchorObj.transform.position = new Vector3(-3.15f, 3.8f, -1.0f);
        Light heroSpot = spotAnchorObj.AddComponent<Light>();
        heroSpot.type = LightType.Spot;
        heroSpot.color = new Color(1f, 0.90f, 0.68f, 1f);
        heroSpot.range = 12f;
        heroSpot.spotAngle = 44f;
        heroSpot.intensity = 22f;
        spotAnchorObj.transform.rotation = Quaternion.Euler(65f, 0f, 0f);

        // 3. 4 Hero Slots (Pre-baked right in the Scene!)
        GameObject lineupRoot = new GameObject("HeroLineup");
        Vector3[] slotBasePositions =
        {
            new Vector3(-3.15f, 0f, 0.55f),
            new Vector3(-1.05f, 0f, 0.05f),
            new Vector3(1.05f, 0f, 0.05f),
            new Vector3(3.15f, 0f, 0.55f)
        };

        Material vanguardMat = AssetDatabase.LoadAssetAtPath<Material>(VanguardMatPath);
        CharacterSelectController.HeroSlotView[] slotViews = new CharacterSelectController.HeroSlotView[4];

        for (int i = 0; i < 4; i++)
        {
            CharacterProfile p = roster.GetProfile(i);
            string slotName = p != null ? $"HeroSlot_{i}_{p.displayName}" : $"HeroSlot_{i}";

            GameObject slotObj = new GameObject(slotName);
            slotObj.transform.SetParent(lineupRoot.transform, false);
            slotObj.transform.position = slotBasePositions[i] + (i == 0 ? new Vector3(0f, 0f, -0.55f) : Vector3.zero);
            slotObj.transform.localScale = Vector3.one * (i == 0 ? 1.08f : 0.94f);

            // Bệ đá
            GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "Pedestal";
            pedestal.transform.SetParent(slotObj.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, 0.14f, 0f);
            pedestal.transform.localScale = new Vector3(1.45f, 0.14f, 1.45f);
            pedestal.GetComponent<Renderer>().sharedMaterial = pedestalMat;

            // Vòng sáng màu theo nhân vật trên mặt bệ
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "PedestalGlowRing";
            ring.transform.SetParent(slotObj.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.275f, 0f);
            ring.transform.localScale = new Vector3(1.52f, 0.015f, 1.52f);
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            Renderer ringRenderer = ring.GetComponent<Renderer>();
            ringRenderer.sharedMaterial = ringGlowMat;

            // Turntable xoay nhân vật
            GameObject turntable = new GameObject("ModelTurntable");
            turntable.transform.SetParent(slotObj.transform, false);
            turntable.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            turntable.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            Animator heroAnim = null;
            if (p != null && p.modelPrefab != null)
            {
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(p.modelPrefab);
                model.name = "HeroModel";
                model.transform.SetParent(turntable.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                if (i == 0 && vanguardMat != null)
                {
                    foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                    {
                        Material[] mats = new Material[r.sharedMaterials.Length];
                        for (int m = 0; m < mats.Length; m++) mats[m] = vanguardMat;
                        r.sharedMaterials = mats;
                    }
                }

                heroAnim = model.GetComponent<Animator>();
                if (heroAnim == null) heroAnim = model.AddComponent<Animator>();
                heroAnim.runtimeAnimatorController = showcaseAc;
                heroAnim.applyRootMotion = false;

                // Gắn vũ khí đặc trưng vào tay phải để thấy ngay trong Scene
                AttachShowcaseWeapon(model, heroAnim, p.startingWeaponPrefab);
            }

            // Collider để click trực tiếp vào nhân vật trong Scene
            CapsuleCollider clickCol = slotObj.AddComponent<CapsuleCollider>();
            clickCol.isTrigger = true;
            clickCol.center = new Vector3(0f, 1.25f, 0f);
            clickCol.radius = 0.62f;
            clickCol.height = 2.2f;

            slotViews[i] = new CharacterSelectController.HeroSlotView
            {
                slotRoot = slotObj.transform,
                modelTurntable = turntable.transform,
                heroAnimator = heroAnim,
                pedestalRingRenderer = ringRenderer,
                clickCollider = clickCol,
                basePosition = slotBasePositions[i]
            };
        }

        // 4. Main Camera framed on Hero 0 (with room on left for HeroInfoPanel)
        Vector3 hero0ActivePos = slotBasePositions[0] + new Vector3(0f, 0f, -0.55f);
        GameObject camObj = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(UniversalAdditionalCameraData));
        camObj.tag = "MainCamera";
        camObj.transform.position = hero0ActivePos + new Vector3(-0.75f, 1.85f, -4.15f);
        camObj.transform.LookAt(hero0ActivePos + new Vector3(-0.55f, 1.05f, 0f));

        Camera cam = camObj.GetComponent<Camera>();
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 180f;
        cam.clearFlags = CameraClearFlags.Skybox;

        UniversalAdditionalCameraData camData = camObj.GetComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = true;

        // 5. EventSystem matching LoginScene (InputSystemUIInputModule)
        CreateEventSystem();

        // 6. Global Volume for crisp Bloom + ACES
        CreateSceneVolume();

        // 7. Controller + Complete Dark Fantasy UI Canvas
        GameObject controllerObj = new GameObject("CharacterSelectController");
        CharacterSelectController controller = controllerObj.AddComponent<CharacterSelectController>();

        BuildCanvasAndWireController(controller, roster, cam, heroSpot, spotAnchorObj.transform, slotViews);
    }

    private static void AttachShowcaseWeapon(GameObject model, Animator anim, GameObject weaponPrefab)
    {
        if (model == null || weaponPrefab == null) return;

        Transform rightHand = anim != null ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (rightHand == null)
        {
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "mixamorig:RightHand")
                {
                    rightHand = t;
                    break;
                }
            }
        }

        if (rightHand == null) return;

        GameObject weapon = (GameObject)PrefabUtility.InstantiatePrefab(weaponPrefab);
        weapon.name = weaponPrefab.name;
        weapon.transform.SetParent(rightHand, false);
        weapon.transform.localPosition = new Vector3(-0.02f, 0.08f, 0.02f);
        weapon.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        weapon.transform.localScale = Vector3.one;
    }

    private static void SpawnBackdropPrefab(GameObject prefab, Transform parent, string objName, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = objName;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = rot;
        go.transform.localScale = scale;
    }

    private static void CreateEventSystem()
    {
        GameObject esObj = new GameObject("EventSystem");
        esObj.AddComponent<EventSystem>();
        InputSystemUIInputModule inputModule = esObj.AddComponent<InputSystemUIInputModule>();
        inputModule.AssignDefaultActions();
    }

    private static void CreateSceneVolume()
    {
        GameObject volObj = new GameObject("CharacterSelectVolume");
        Volume vol = volObj.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 1f;

        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
        if (profile != null)
        {
            vol.sharedProfile = profile;
        }
    }

    // ==================== 3. BUILD DARK FANTASY UI CANVAS IN SCENE ====================

    private static void BuildCanvasAndWireController(
        CharacterSelectController controller,
        CharacterRosterConfig roster,
        Camera showcaseCam,
        Light heroSpot,
        Transform spotAnchor,
        CharacterSelectController.HeroSlotView[] slotViews)
    {
        TMP_FontAsset sharedFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        GameObject canvasObj = new GameObject("CharacterSelectCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        FantasyUiThemeHelper.ConfigureResponsiveCanvas(canvas);

        // --- Header Bar ---
        GameObject backBtnGo = CreateStyledButton("BackButton", canvas.transform, "← QUAY LẠI", false, 180f, 46f, sharedFont);
        RectTransform backRt = backBtnGo.GetComponent<RectTransform>();
        backRt.anchorMin = new Vector2(0f, 1f);
        backRt.anchorMax = new Vector2(0f, 1f);
        backRt.pivot = new Vector2(0f, 1f);
        backRt.anchoredPosition = new Vector2(44f, -34f);

        GameObject headerGroup = new GameObject("HeaderTitleGroup", typeof(RectTransform));
        headerGroup.transform.SetParent(canvas.transform, false);
        RectTransform headerRt = headerGroup.GetComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0.5f, 1f);
        headerRt.anchorMax = new Vector2(0.5f, 1f);
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.anchoredPosition = new Vector2(0f, -26f);
        headerRt.sizeDelta = new Vector2(680f, 92f);

        TMP_Text mainTitle = FantasyUiThemeHelper.CreateText(headerGroup.transform, "MainTitle", "CHỌN ANH HÙNG",
            36f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, sharedFont);
        mainTitle.characterSpacing = 8f;
        SetRect(mainTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(660f, 44f));

        FantasyUiThemeHelper.CreateAccentLine(headerGroup.transform, "HeaderDivider",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -49f), new Vector2(210f, 2.5f), FantasyUiThemeHelper.GoldPrimary);

        TMP_Text subTitle = FantasyUiThemeHelper.CreateText(headerGroup.transform, "SubTitle", "VANGUARD  •  RAID OF THE ORC WOODS",
            14f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Center, sharedFont);
        subTitle.characterSpacing = 5f;
        SetRect(subTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(660f, 24f));

        // --- Left Hero Info Panel ---
        GameObject infoPanel = new GameObject("HeroInfoPanel", typeof(RectTransform), typeof(Image));
        infoPanel.transform.SetParent(canvas.transform, false);
        SetRect(infoPanel.GetComponent<RectTransform>(), new Vector2(0f, 0.54f), new Vector2(0f, 0.54f), new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(455f, 560f));
        FantasyUiThemeHelper.StyleCardPanel(infoPanel, FantasyUiThemeHelper.ObsidianCard, FantasyUiThemeHelper.GoldBorder);

        // Role Badge Pill
        GameObject badgeGo = new GameObject("RoleBadge", typeof(RectTransform), typeof(Image));
        badgeGo.transform.SetParent(infoPanel.transform, false);
        SetRect(badgeGo.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -26f), new Vector2(210f, 28f));
        Image roleBadgeBg = badgeGo.GetComponent<Image>();
        roleBadgeBg.color = new Color(0.95f, 0.74f, 0.28f, 0.24f);
        TMP_Text roleBadgeText = FantasyUiThemeHelper.CreateText(badgeGo.transform, "RoleBadgeText", "CÂN BẰNG",
            13f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Center, sharedFont);
        roleBadgeText.characterSpacing = 3f;
        FantasyUiThemeHelper.StretchFull(roleBadgeText.rectTransform);

        // Hero Name + Title
        TMP_Text heroNameText = FantasyUiThemeHelper.CreateText(infoPanel.transform, "HeroNameText", "VANGUARD",
            40f, FontStyles.Bold, Color.white, TextAlignmentOptions.Left, sharedFont);
        heroNameText.characterSpacing = 4f;
        SetRect(heroNameText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(-56f, 48f));

        TMP_Text heroTitleText = FantasyUiThemeHelper.CreateText(infoPanel.transform, "HeroTitleText", "Chiến Binh Tiên Phong",
            18f, FontStyles.Italic, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Left, sharedFont);
        SetRect(heroTitleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -114f), new Vector2(-56f, 26f));

        GameObject accentLineGo = FantasyUiThemeHelper.CreateAccentLine(infoPanel.transform, "InfoAccentLine",
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(28f, -148f), new Vector2(150f, 3f), FantasyUiThemeHelper.GoldPrimary);
        Image infoAccentLine = accentLineGo.GetComponent<Image>();

        TMP_Text heroDescText = FantasyUiThemeHelper.CreateText(infoPanel.transform, "HeroDescText",
            "Hiệp sĩ thiết giáp toàn diện với kiếm pháp vững chắc.",
            15f, FontStyles.Normal, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.TopLeft, sharedFont);
        SetRect(heroDescText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -164f), new Vector2(-56f, 76f));

        // 4 Stat Bars
        CreateStatBarRow(infoPanel.transform, "StatRow_HP", "SINH LỰC", -260f, new Color(0.28f, 0.92f, 0.52f, 1f), sharedFont, out Slider hpSlider, out TMP_Text hpValText);
        CreateStatBarRow(infoPanel.transform, "StatRow_ATK", "TẤN CÔNG", -312f, new Color(0.96f, 0.42f, 0.30f, 1f), sharedFont, out Slider atkSlider, out TMP_Text atkValText);
        CreateStatBarRow(infoPanel.transform, "StatRow_SPD", "TỐC ĐỘ", -364f, new Color(0.32f, 0.82f, 0.98f, 1f), sharedFont, out Slider spdSlider, out TMP_Text spdValText);
        CreateStatBarRow(infoPanel.transform, "StatRow_DEF", "PHÒNG THỦ", -416f, new Color(0.96f, 0.78f, 0.32f, 1f), sharedFont, out Slider defSlider, out TMP_Text defValText);

        // Signature Perk Box at Bottom of Info Panel
        GameObject perkBox = new GameObject("SignaturePerkBox", typeof(RectTransform), typeof(Image));
        perkBox.transform.SetParent(infoPanel.transform, false);
        SetRect(perkBox.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(-56f, 54f));
        FantasyUiThemeHelper.StyleCardPanel(perkBox, new Color(0.08f, 0.11f, 0.18f, 0.95f), new Color(0.82f, 0.62f, 0.28f, 0.40f));
        TMP_Text heroPerkText = FantasyUiThemeHelper.CreateText(perkBox.transform, "HeroPerkText", "Vũ khí: Kiếm Thiết Giáp",
            14f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Center, sharedFont);
        FantasyUiThemeHelper.StretchFull(heroPerkText.rectTransform);
        heroPerkText.rectTransform.offsetMin = new Vector2(12f, 4f);
        heroPerkText.rectTransform.offsetMax = new Vector2(-12f, -4f);

        // --- Bottom Hero Selector Bar (4 Cards + Prev/Next) ---
        GameObject selectorBar = new GameObject("HeroSelectorBar", typeof(RectTransform));
        selectorBar.transform.SetParent(canvas.transform, false);
        SetRect(selectorBar.GetComponent<RectTransform>(), new Vector2(0.36f, 0f), new Vector2(0.36f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(1080f, 108f));

        GameObject prevBtnGo = CreateStyledButton("PrevHeroButton", selectorBar.transform, "<", false, 52f, 92f, sharedFont);
        SetRect(prevBtnGo.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(52f, 92f));

        GameObject nextBtnGo = CreateStyledButton("NextHeroButton", selectorBar.transform, ">", false, 52f, 92f, sharedFont);
        SetRect(nextBtnGo.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(52f, 92f));

        CharacterSelectController.HeroCardView[] cardViews = new CharacterSelectController.HeroCardView[4];
        float startCardX = -345f;
        float cardStepX = 230f;

        for (int i = 0; i < 4; i++)
        {
            CharacterProfile p = roster.GetProfile(i);
            cardViews[i] = CreateHeroCard(selectorBar.transform, i, p, new Vector2(startCardX + i * cardStepX, 0f), sharedFont);
        }

        // --- Bottom-Right Play CTA + Control Hint ---
        GameObject ctaGroup = new GameObject("PlayActionGroup", typeof(RectTransform));
        ctaGroup.transform.SetParent(canvas.transform, false);
        SetRect(ctaGroup.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-44f, 30f), new Vector2(330f, 110f));

        GameObject playBtnGo = CreateStyledButton("PlayGameButton", ctaGroup.transform, "VÀO CHIẾN ĐẤU", true, 310f, 66f, sharedFont);
        SetRect(playBtnGo.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(310f, 66f));

        TMP_Text hintText = FantasyUiThemeHelper.CreateText(ctaGroup.transform, "ControlHintText",
            "Kéo chuột xoay 360°  •  [1-4] / [A-D] đổi tướng",
            13f, FontStyles.Normal, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.Center, sharedFont);
        SetRect(hintText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 4f), new Vector2(310f, 26f));

        // --- Wire All References to CharacterSelectController ---
        SerializedObject so = new SerializedObject(controller);
        so.FindProperty("rosterConfig").objectReferenceValue = roster;
        so.FindProperty("gameplaySceneName").stringValue = "Lv1";
        so.FindProperty("backSceneName").stringValue = "LoginScene";
        so.FindProperty("showcaseCamera").objectReferenceValue = showcaseCam;
        so.FindProperty("heroSpotlight").objectReferenceValue = heroSpot;
        so.FindProperty("spotlightAnchor").objectReferenceValue = spotAnchor;

        SerializedProperty slotsProp = so.FindProperty("heroSlots");
        slotsProp.arraySize = slotViews.Length;
        for (int i = 0; i < slotViews.Length; i++)
        {
            SerializedProperty el = slotsProp.GetArrayElementAtIndex(i);
            el.FindPropertyRelative("slotRoot").objectReferenceValue = slotViews[i].slotRoot;
            el.FindPropertyRelative("modelTurntable").objectReferenceValue = slotViews[i].modelTurntable;
            el.FindPropertyRelative("heroAnimator").objectReferenceValue = slotViews[i].heroAnimator;
            el.FindPropertyRelative("pedestalRingRenderer").objectReferenceValue = slotViews[i].pedestalRingRenderer;
            el.FindPropertyRelative("clickCollider").objectReferenceValue = slotViews[i].clickCollider;
            el.FindPropertyRelative("basePosition").vector3Value = slotViews[i].basePosition;
        }

        SerializedProperty cardsProp = so.FindProperty("heroCards");
        cardsProp.arraySize = cardViews.Length;
        for (int i = 0; i < cardViews.Length; i++)
        {
            SerializedProperty el = cardsProp.GetArrayElementAtIndex(i);
            el.FindPropertyRelative("button").objectReferenceValue = cardViews[i].button;
            el.FindPropertyRelative("background").objectReferenceValue = cardViews[i].background;
            el.FindPropertyRelative("accentStrip").objectReferenceValue = cardViews[i].accentStrip;
            el.FindPropertyRelative("borderOutline").objectReferenceValue = cardViews[i].borderOutline;
            el.FindPropertyRelative("nameText").objectReferenceValue = cardViews[i].nameText;
            el.FindPropertyRelative("roleText").objectReferenceValue = cardViews[i].roleText;
        }

        so.FindProperty("roleBadgeBg").objectReferenceValue = roleBadgeBg;
        so.FindProperty("roleBadgeText").objectReferenceValue = roleBadgeText;
        so.FindProperty("heroNameText").objectReferenceValue = heroNameText;
        so.FindProperty("heroTitleText").objectReferenceValue = heroTitleText;
        so.FindProperty("heroDescText").objectReferenceValue = heroDescText;
        so.FindProperty("heroPerkText").objectReferenceValue = heroPerkText;
        so.FindProperty("infoAccentLine").objectReferenceValue = infoAccentLine;

        so.FindProperty("hpSlider").objectReferenceValue = hpSlider;
        so.FindProperty("hpValueText").objectReferenceValue = hpValText;
        so.FindProperty("atkSlider").objectReferenceValue = atkSlider;
        so.FindProperty("atkValueText").objectReferenceValue = atkValText;
        so.FindProperty("spdSlider").objectReferenceValue = spdSlider;
        so.FindProperty("spdValueText").objectReferenceValue = spdValText;
        so.FindProperty("defSlider").objectReferenceValue = defSlider;
        so.FindProperty("defValueText").objectReferenceValue = defValText;

        so.FindProperty("playButton").objectReferenceValue = playBtnGo.GetComponent<Button>();
        so.FindProperty("backButton").objectReferenceValue = backBtnGo.GetComponent<Button>();
        so.FindProperty("prevButton").objectReferenceValue = prevBtnGo.GetComponent<Button>();
        so.FindProperty("nextButton").objectReferenceValue = nextBtnGo.GetComponent<Button>();
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static CharacterSelectController.HeroCardView CreateHeroCard(
        Transform parent,
        int index,
        CharacterProfile profile,
        Vector2 anchoredPos,
        TMP_FontAsset font)
    {
        Color accent = profile != null ? profile.themeColor : FantasyUiThemeHelper.GoldBright;
        string heroName = profile != null ? profile.displayName : $"HERO {index + 1}";
        string roleName = profile != null ? profile.roleBadge : "CHIẾN BINH";

        GameObject cardGo = new GameObject($"HeroCard_{index}", typeof(RectTransform), typeof(Image), typeof(Button));
        cardGo.transform.SetParent(parent, false);
        SetRect(cardGo.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPos, new Vector2(214f, 96f));

        Image bg = cardGo.GetComponent<Image>();
        bg.color = index == 0
            ? new Color(0.11f, 0.14f, 0.22f, 0.96f)
            : new Color(0.05f, 0.07f, 0.12f, 0.84f);

        Outline outline = cardGo.AddComponent<Outline>();
        outline.effectColor = index == 0 ? accent : new Color(0.45f, 0.52f, 0.65f, 0.30f);
        outline.effectDistance = index == 0 ? new Vector2(2.2f, -2.2f) : new Vector2(1.2f, -1.2f);

        GameObject stripGo = FantasyUiThemeHelper.CreateAccentLine(cardGo.transform, "AccentStrip",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, 0f), new Vector2(0f, 4f), accent);
        Image stripImg = stripGo.GetComponent<Image>();

        TMP_Text idxText = FantasyUiThemeHelper.CreateText(cardGo.transform, "KeyBadge", $"[{index + 1}]",
            12f, FontStyles.Bold, FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.TopRight, font);
        SetRect(idxText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -10f), new Vector2(40f, 20f));

        TMP_Text nameText = FantasyUiThemeHelper.CreateText(cardGo.transform, "NameText", heroName,
            20f, FontStyles.Bold, index == 0 ? Color.white : new Color(0.76f, 0.80f, 0.88f, 0.9f), TextAlignmentOptions.Left, font);
        SetRect(nameText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(-28f, 30f));

        TMP_Text roleText = FantasyUiThemeHelper.CreateText(cardGo.transform, "RoleText", roleName,
            12f, FontStyles.Bold, index == 0 ? accent : FantasyUiThemeHelper.TextMuted, TextAlignmentOptions.Left, font);
        SetRect(roleText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(-28f, 22f));

        Button btn = cardGo.GetComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.pressedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        btn.colors = cb;

        return new CharacterSelectController.HeroCardView
        {
            button = btn,
            background = bg,
            accentStrip = stripImg,
            borderOutline = outline,
            nameText = nameText,
            roleText = roleText
        };
    }

    private static void CreateStatBarRow(
        Transform parent,
        string rowName,
        string label,
        float posY,
        Color fillColor,
        TMP_FontAsset font,
        out Slider slider,
        out TMP_Text valueText)
    {
        GameObject row = new GameObject(rowName, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        SetRect(row.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, posY), new Vector2(-56f, 40f));

        TMP_Text lbl = FantasyUiThemeHelper.CreateText(row.transform, "Label", label,
            13f, FontStyles.Bold, FantasyUiThemeHelper.TextPrimary, TextAlignmentOptions.Left, font);
        lbl.characterSpacing = 2f;
        SetRect(lbl.rectTransform, new Vector2(0f, 1f), new Vector2(0.6f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(0f, 20f));

        valueText = FantasyUiThemeHelper.CreateText(row.transform, "ValueText", "100",
            14f, FontStyles.Bold, FantasyUiThemeHelper.GoldBright, TextAlignmentOptions.Right, font);
        SetRect(valueText.rectTransform, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, 20f));

        GameObject sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(row.transform, false);
        SetRect(sliderGo.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(0f, 11f));

        GameObject bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
        bgGo.transform.SetParent(sliderGo.transform, false);
        FantasyUiThemeHelper.StretchFull(bgGo.GetComponent<RectTransform>());
        bgGo.GetComponent<Image>().color = new Color(0.10f, 0.14f, 0.22f, 0.95f);

        GameObject fillArea = new GameObject("FillArea", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        FantasyUiThemeHelper.StretchFull(fillArea.GetComponent<RectTransform>());

        GameObject fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(fillArea.transform, false);
        FantasyUiThemeHelper.StretchFull(fillGo.GetComponent<RectTransform>());
        fillGo.GetComponent<Image>().color = fillColor;

        slider = sliderGo.GetComponent<Slider>();
        slider.interactable = false;
        slider.transition = Selectable.Transition.None;
        slider.fillRect = fillGo.GetComponent<RectTransform>();
        slider.minValue = 0f;
        slider.maxValue = 100f;
        slider.value = 75f;
    }

    private static GameObject CreateStyledButton(string name, Transform parent, string label, bool isPrimary, float width, float height, TMP_FontAsset font)
    {
        GameObject btnGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(parent, false);

        GameObject txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        txtGo.transform.SetParent(btnGo.transform, false);
        TextMeshProUGUI tmp = txtGo.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.raycastTarget = false;

        Button btn = btnGo.GetComponent<Button>();
        FantasyUiThemeHelper.StyleButton(btn, label, isPrimary, width, height);
        return btnGo;
    }

    private static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
    }

    // ==================== 4. BUILD SETTINGS ORDER ====================

    private static void EnsureSceneInBuildSettings()
    {
        var desiredOrder = new[]
        {
            "Assets/DucThanh/Scene/LoadingScene.unity",
            "Assets/DucThanh/Scene/LoginScene.unity",
            ScenePath,
            "Assets/DucThanh/Scene/Lv1.unity"
        };

        var result = new List<EditorBuildSettingsScene>();
        var seen = new HashSet<string>();

        foreach (string path in desiredOrder)
        {
            if (File.Exists(path) && seen.Add(path))
            {
                result.Add(new EditorBuildSettingsScene(path, true));
            }
        }

        foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
        {
            if (!string.IsNullOrEmpty(existing.path) && seen.Add(existing.path))
            {
                result.Add(existing);
            }
        }

        EditorBuildSettings.scenes = result.ToArray();
    }
}
#endif
