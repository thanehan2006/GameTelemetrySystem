#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Menu:
/// - DucThanh > Update Player Animations (Random Idle, Run, Atk):
///   Cấu hình Humanoid + Bake Into Pose cho toàn bộ clip Mixamo mới và cập nhật Player_AC.controller
///   tại chỗ (GIỮ NGUYÊN Player, vũ khí và scene Lv1 hiện tại).
/// - DucThanh > Setup Player:
///   Cấu hình lại toàn bộ animation + dựng lại Player mới trong scene Lv1.
/// </summary>
public static class PlayerSetup
{
    const string Dir = "Assets/DucThanh/Assets/Player1/";
    const string IdleFbx = Dir + "Idle.fbx";
    const string Idle2Fbx = Dir + "Standing Idle.fbx";
    const string Idle3Fbx = Dir + "Fishing Idle.fbx";
    const string RunFbx = Dir + "Fast Run.fbx";
    const string BlockFbx = Dir + "Standing Block Idle.fbx";
    const string Atk1Fbx = Dir + "Atk1.fbx";
    const string Atk2Fbx = Dir + "Standing Melee Attack Horizontal.fbx";
    const string Atk3Fbx = Dir + "Standing Melee Attack 360 Low.fbx";

    const string ControllerPath = Dir + "Player_AC.controller";
    const string MaterialPath = "Assets/DucThanh/Assets/Materials/Player.mat";
    const string ScenePath = "Assets/DucThanh/Scene/Lv1.unity";

    [MenuItem("DucThanh/Update Player Animations (Idle, Run, Block, Atk)")]
    public static void UpdateAnimationsOnly()
    {
        Avatar avatar = ConfigureAllPlayerClips();
        AnimatorController controller = BuildOrUpdateControllerInPlace();

        // Gán chắc chắn Avatar + Controller cho Player hiện tại trong scene đang mở (nếu có)
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            Animator anim = player.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.runtimeAnimatorController = controller;
                if (avatar != null) anim.avatar = avatar;
                anim.applyRootMotion = false;
                EditorUtility.SetDirty(anim);
            }
        }

        Debug.Log("[PlayerSetup] Đã cập nhật toàn bộ 8 animation (3 Idle, 1 Run, 1 Block khi giữ Chuột Phải, 3 Atk) vào Player_AC.controller!");
    }

    [MenuItem("DucThanh/Setup Player")]
    public static void Run()
    {
        Avatar avatar = ConfigureAllPlayerClips();
        AnimatorController controller = BuildOrUpdateControllerInPlace();
        SetupScene(controller, avatar);
        Debug.Log("[PlayerSetup] Xong! Nhấn Play để thử.");
    }

    static Avatar ConfigureAllPlayerClips()
    {
        // Idle.fbx (model chính có skin) luôn tạo Avatar gốc
        ConfigureImport(IdleFbx, true, null);
        Avatar baseAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(IdleFbx);

        ConfigureImport(Idle2Fbx, true, baseAvatar);
        ConfigureImport(Idle3Fbx, true, baseAvatar);
        ConfigureImport(RunFbx, true, null, isLocomotion: true);
        // Standing Block Idle.fbx là tư thế đỡ đòn giữ liên tục bằng Chuột Phải -> loop = true
        ConfigureImport(BlockFbx, true, baseAvatar);
        ConfigureImport(Atk1Fbx, false, null);
        ConfigureImport(Atk2Fbx, false, baseAvatar);
        ConfigureImport(Atk3Fbx, false, baseAvatar);

        return baseAvatar;
    }

    static void ConfigureImport(string path, bool loop, Avatar sourceAvatar, bool isLocomotion = false)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            Debug.LogWarning("[PlayerSetup] Bỏ qua (không tìm thấy file): " + path);
            return;
        }

        bool needsRigUpdate = importer.animationType != ModelImporterAnimationType.Human;
        if (sourceAvatar != null)
        {
            if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || importer.sourceAvatar != sourceAvatar)
            {
                needsRigUpdate = true;
            }
        }
        else if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            needsRigUpdate = true;
        }

        if (needsRigUpdate)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            if (sourceAvatar != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = sourceAvatar;
            }
            else
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
            importer.SaveAndReimport();
            importer = AssetImporter.GetAtPath(path) as ModelImporter;
        }

        // Cấu hình Loop + Root Transform:
        // Với clip chạy Mixamo không bật "In Place" (di chuyển theo trục Z), giữ Based Upon = Original
        // và TẮT Bake Into Pose XZ để applyRootMotion = false tự loại bỏ trôi/lắc ngang hoàn toàn.
        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

        foreach (var c in clips)
        {
            c.loopTime = loop;
            c.loopPose = loop && !isLocomotion;
            c.lockRootRotation = true;
            c.lockRootHeightY = true;
            c.lockRootPositionXZ = !isLocomotion;
            c.keepOriginalOrientation = isLocomotion;
            c.keepOriginalPositionY = false;
            c.keepOriginalPositionXZ = isLocomotion;
            c.heightFromFeet = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    static AnimationClip LoadClip(string path)
    {
        if (AssetImporter.GetAtPath(path) == null) return null;

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;

        Debug.LogWarning("[PlayerSetup] Không tìm thấy AnimationClip trong " + path);
        return null;
    }

    static AnimatorController BuildOrUpdateControllerInPlace()
    {
        // Mở lại Player_AC.controller hiện có để GIỮ NGUYÊN GUID không làm mất link trên PlayerBody
        var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ac == null)
        {
            ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        }

        // Dọn sạch parameters & state cũ trong layer 0
        while (ac.parameters.Length > 0)
        {
            ac.RemoveParameter(0);
        }

        ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ac.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        ac.AddParameter("CancelAttack", AnimatorControllerParameterType.Trigger);

        var sm = ac.layers[0].stateMachine;
        foreach (var trans in sm.anyStateTransitions) sm.RemoveAnyStateTransition(trans);
        foreach (var childState in sm.states) sm.RemoveState(childState.state);

        // --- IDLE STATES (Random giữa 3 kiểu) ---
        var idle = AddStateWithClip(sm, "Idle", IdleFbx, new Vector3(240, 20, 0));
        AddStateWithClip(sm, "Idle2", Idle2Fbx, new Vector3(240, 90, 0));
        AddStateWithClip(sm, "Idle3", Idle3Fbx, new Vector3(240, 160, 0));
        if (idle != null) sm.defaultState = idle;

        // --- RUN & BLOCK STATES ---
        var run = AddStateWithClip(sm, "Run", RunFbx, new Vector3(500, 20, 0));
        AddStateWithClip(sm, "Block", BlockFbx, new Vector3(500, 100, 0));

        // --- ATTACK STATES (Random giữa 3 chiêu thức) ---
        var atk1 = AddStateWithClip(sm, "Atk1", Atk1Fbx, new Vector3(240, -140, 0));
        var atk2 = AddStateWithClip(sm, "Atk2", Atk2Fbx, new Vector3(450, -140, 0));
        var atk3 = AddStateWithClip(sm, "Atk3", Atk3Fbx, new Vector3(660, -140, 0));

        // Giữ transition cơ bản giữa Idle <-> Run và Attack fallback
        if (idle != null && run != null)
        {
            var toRun = idle.AddTransition(run);
            toRun.hasExitTime = false;
            toRun.duration = 0.1f;
            toRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");

            var toIdle = run.AddTransition(idle);
            toIdle.hasExitTime = false;
            toIdle.duration = 0.1f;
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        }

        AddAttackExitTransitions(atk1, run, idle);
        AddAttackExitTransitions(atk2, run, idle);
        AddAttackExitTransitions(atk3, run, idle);

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        return ac;
    }

    static AnimatorState AddStateWithClip(AnimatorStateMachine sm, string stateName, string fbxPath, Vector3 pos)
    {
        AnimationClip clip = LoadClip(fbxPath);
        if (clip == null) return null;

        AnimatorState state = sm.AddState(stateName, pos);
        state.motion = clip;
        return state;
    }

    static void AddAttackExitTransitions(AnimatorState atkState, AnimatorState runState, AnimatorState idleState)
    {
        if (atkState == null) return;

        if (runState != null)
        {
            var cancelToRun = atkState.AddTransition(runState);
            cancelToRun.hasExitTime = false;
            cancelToRun.duration = 0.08f;
            cancelToRun.AddCondition(AnimatorConditionMode.If, 0f, "CancelAttack");

            var atkToRun = atkState.AddTransition(runState);
            atkToRun.hasExitTime = true;
            atkToRun.exitTime = 0.90f;
            atkToRun.duration = 0.1f;
            atkToRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        }

        if (idleState != null)
        {
            var atkToIdle = atkState.AddTransition(idleState);
            atkToIdle.hasExitTime = true;
            atkToIdle.exitTime = 0.94f;
            atkToIdle.duration = 0.1f;
        }
    }

    static void SetupScene(AnimatorController controller, Avatar avatar)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var old = GameObject.FindWithTag("Player");
        Vector3 pos = old != null ? old.transform.position : new Vector3(134f, 26f, 360f);
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

        var hp = player.AddComponent<PlayerHealth>();
        hp.maxHealth = 180;
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

            Quaternion camRot = Quaternion.Euler(12f, 0f, 0f);
            Vector3 pivot = player.transform.position + Vector3.up * tpc.height + camRot * Vector3.right * tpc.shoulderOffset;
            cam.fieldOfView = tpc.baseFov;
            cam.transform.SetPositionAndRotation(pivot - camRot * Vector3.forward * tpc.distance, camRot);
            EditorUtility.SetDirty(cam);
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
