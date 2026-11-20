#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Menu: DucThanh > Setup Player
/// 1. Chuyển Idle.fbx, Run.fbx và Atk1.fbx Mixamo sang Humanoid, bake into pose.
/// 2. Tạo Player_AC (Idle / Run theo Speed, Atk1 theo Trigger Attack).
/// 3. Tạo Player trong scene Lv1 + camera góc nhìn thứ ba.
/// </summary>
public static class PlayerSetup
{
    const string Dir = "Assets/DucThanh/Assets/Player/";
    const string IdleFbx = Dir + "Idle.fbx";
    const string RunFbx = Dir + "Run.fbx";
    const string AtkFbx = Dir + "Atk1.fbx";
    const string ControllerPath = Dir + "Player_AC.controller";
    const string MaterialPath = "Assets/DucThanh/Assets/Materials/Player.mat";
    const string ScenePath = "Assets/DucThanh/Scene/Lv1.unity";

    [MenuItem("DucThanh/Setup Player")]
    public static void Run()
    {
        ConfigureImport(IdleFbx, true);
        ConfigureImport(RunFbx, true);
        ConfigureImport(AtkFbx, false);

        Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(IdleFbx);
        AnimatorController controller = BuildController();
        SetupScene(controller, avatar);
        Debug.Log("[PlayerSetup] Xong! Nhấn Play để thử.");
    }

    static void ConfigureImport(string path, bool loop)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) { Debug.LogError("[PlayerSetup] Không tìm thấy " + path); return; }

        // Bước 1: chuyển sang Humanoid + CreateFromThisModel rồi reimport để Unity tự sinh humanDescription
        if (importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
            importer = AssetImporter.GetAtPath(path) as ModelImporter;
        }

        // Bước 2: cấu hình Loop + Bake Into Pose cho clip
        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

        foreach (var c in clips)
        {
            c.loopTime = loop;
            c.loopPose = loop;
            c.lockRootRotation = true;
            c.lockRootHeightY = true;
            c.lockRootPositionXZ = true;
            c.keepOriginalOrientation = false;
            c.keepOriginalPositionY = false;
            c.keepOriginalPositionXZ = false;
            c.heightFromFeet = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    static AnimationClip LoadClip(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
        Debug.LogError("[PlayerSetup] Không có clip trong " + path);
        return null;
    }

    static AnimatorController BuildController()
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ac.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        ac.AddParameter("CancelAttack", AnimatorControllerParameterType.Trigger);

        var sm = ac.layers[0].stateMachine;
        var idle = sm.AddState("Idle");
        idle.motion = LoadClip(IdleFbx);
        var run = sm.AddState("Run");
        run.motion = LoadClip(RunFbx);
        var atk1 = sm.AddState("Atk1");
        atk1.motion = LoadClip(AtkFbx);
        sm.defaultState = idle;

        var toRun = idle.AddTransition(run);
        toRun.hasExitTime = false;
        toRun.duration = 0.1f;
        toRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");

        var toIdle = run.AddTransition(idle);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.1f;
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        var anyToAtk = sm.AddAnyStateTransition(atk1);
        anyToAtk.hasExitTime = false;
        anyToAtk.duration = 0.08f;
        anyToAtk.canTransitionToSelf = false;
        anyToAtk.AddCondition(AnimatorConditionMode.If, 0f, "Attack");

        var cancelToRun = atk1.AddTransition(run);
        cancelToRun.hasExitTime = false;
        cancelToRun.duration = 0.08f;
        cancelToRun.AddCondition(AnimatorConditionMode.If, 0f, "CancelAttack");

        var atkToRun = atk1.AddTransition(run);
        atkToRun.hasExitTime = true;
        atkToRun.exitTime = 0.85f;
        atkToRun.duration = 0.1f;
        atkToRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");

        var atkToIdle = atk1.AddTransition(idle);
        atkToIdle.hasExitTime = true;
        atkToIdle.exitTime = 0.9f;
        atkToIdle.duration = 0.1f;

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        return ac;
    }

    static void SetupScene(AnimatorController controller, Avatar avatar)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Xoá Player cũ nếu có
        var old = GameObject.FindWithTag("Player");
        Vector3 pos = old != null ? old.transform.position : new Vector3(1.895f, 0f, 17.17f);
        if (old != null) Object.DestroyImmediate(old);

        var player = new GameObject("Player") { tag = "Player" };
        player.transform.position = pos;

        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.stepOffset = 0.3f;

        var obstacle = player.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Capsule;
        obstacle.radius = cc.radius;
        obstacle.height = cc.height;
        obstacle.center = cc.center;
        obstacle.carving = false;

        player.AddComponent<PlayerHealth>();
        var pc = player.AddComponent<PlayerController>();
        player.AddComponent<PlayerAnimationController>();
        var pa = player.AddComponent<PlayerAttack>();

        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(IdleFbx), player.transform);
        model.name = "PlayerBody";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        ApplyMaterial(model);

        var animator = model.GetComponent<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.avatar = avatar;
        animator.applyRootMotion = false;

        pc.visualModel = model.transform;
        pa.visualModel = model.transform;

        var cam = Camera.main;
        if (cam != null)
        {
            pc.playerCamera = cam.transform;
            var tpc = cam.GetComponent<ThirdPersonCamera>();
            if (tpc == null) tpc = cam.gameObject.AddComponent<ThirdPersonCamera>();
            tpc.target = player.transform;

            Quaternion camRot = Quaternion.Euler(15f, 0f, 0f);
            Vector3 pivot = player.transform.position + Vector3.up * tpc.height;
            cam.transform.SetPositionAndRotation(pivot - camRot * Vector3.forward * tpc.distance, camRot);
            EditorUtility.SetDirty(cam.transform);
            EditorUtility.SetDirty(tpc);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void ApplyMaterial(GameObject model)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null) { Debug.LogWarning("[PlayerSetup] Không tìm thấy material: " + MaterialPath); return; }

        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            EditorUtility.SetDirty(r);
        }
    }
}
#endif
