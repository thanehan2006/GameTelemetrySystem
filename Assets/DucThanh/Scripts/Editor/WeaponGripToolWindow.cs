#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor Tool: DucThanh > Weapon Grip Tool
/// Hỗ trợ gắn vũ khí vào tay Player (Mixamo RightHand/LeftHand), chỉnh Position/Rotation/Scale trực quan,
/// xem thử tư thế Idle / Atk1 và lưu vào Scene.
/// </summary>
public class WeaponGripToolWindow : EditorWindow
{
    private const string WeaponsFolder = "Assets/DucThanh/Assets/Weapons";
    private const string IdleFbxPath = "Assets/DucThanh/Assets/Player/Idle.fbx";
    private const string AtkFbxPath = "Assets/DucThanh/Assets/Player/Atk1.fbx";

    private enum HandSide { RightHand, LeftHand }
    private enum PosePreview { None, Idle, Atk1 }

    private GameObject playerRoot;
    private Animator playerAnimator;
    private Transform handBone;
    private HandSide handSide = HandSide.RightHand;

    private GameObject weaponPrefabOrObject;
    private GameObject activeWeapon;
    private List<GameObject> foundWeapons = new List<GameObject>();

    private PosePreview previewPose = PosePreview.None;
    private float previewTime = 0.25f;
    private AnimationClip idleClip;
    private AnimationClip atkClip;

    private Vector2 scrollPos;

    [MenuItem("DucThanh/Weapon Grip Tool")]
    public static void Open()
    {
        var win = GetWindow<WeaponGripToolWindow>("Weapon Grip Tool");
        win.minSize = new Vector2(360f, 520f);
        win.Show();
    }

    private void OnEnable()
    {
        RefreshReferences();
        LoadWeaponAssets();
        LoadPreviewClips();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        StopPreviewMode();
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            StopPreviewMode();
        }
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    private void OnInspectorUpdate()
    {
        if (activeWeapon != null)
        {
            Repaint();
        }
    }

    private void RefreshReferences()
    {
        if (playerRoot == null)
        {
            playerRoot = GameObject.FindWithTag("Player");
        }

        playerAnimator = playerRoot != null ? playerRoot.GetComponentInChildren<Animator>() : null;
        handBone = ResolveHandBone();

        if (handBone != null && activeWeapon == null)
        {
            // Tự nhận vũ khí đang gắn sẵn trên tay nếu có
            for (int i = 0; i < handBone.childCount; i++)
            {
                Transform child = handBone.GetChild(i);
                if (!child.name.StartsWith("mixamorig:"))
                {
                    activeWeapon = child.gameObject;
                    break;
                }
            }
        }
    }

    private Transform ResolveHandBone()
    {
        if (playerAnimator == null) return null;

        HumanBodyBones bone = handSide == HandSide.RightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand;
        Transform t = playerAnimator.GetBoneTransform(bone);
        if (t != null) return t;

        string targetName = handSide == HandSide.RightHand ? "mixamorig:RightHand" : "mixamorig:LeftHand";
        foreach (Transform child in playerAnimator.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == targetName) return child;
        }
        return null;
    }

    private void LoadWeaponAssets()
    {
        foundWeapons.Clear();
        if (!AssetDatabase.IsValidFolder(WeaponsFolder)) return;

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { WeaponsFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) foundWeapons.Add(prefab);
        }
    }

    private void LoadPreviewClips()
    {
        idleClip = LoadFirstClip(IdleFbxPath);
        atkClip = LoadFirstClip(AtkFbxPath);
    }

    private static AnimationClip LoadFirstClip(string fbxPath)
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

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.LabelField("Tool Căn Chỉnh Vũ Khí Trên Tay Player", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        DrawTargetSection();
        EditorGUILayout.Space(8);
        DrawWeaponAttachSection();
        EditorGUILayout.Space(8);
        DrawTransformSection();
        EditorGUILayout.Space(8);
        DrawPreviewSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawTargetSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("1. Nhân vật & Xương tay", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        playerRoot = (GameObject)EditorGUILayout.ObjectField("Player", playerRoot, typeof(GameObject), true);
        var newSide = (HandSide)EditorGUILayout.EnumPopup("Tay cầm vũ khí", handSide);
        if (EditorGUI.EndChangeCheck())
        {
            bool sideChanged = newSide != handSide;
            handSide = newSide;
            playerAnimator = playerRoot != null ? playerRoot.GetComponentInChildren<Animator>() : null;
            handBone = ResolveHandBone();

            if (sideChanged && activeWeapon != null && handBone != null)
            {
                Undo.SetTransformParent(activeWeapon.transform, handBone, "Đổi tay cầm vũ khí");
                MarkSceneDirty();
            }
        }

        if (handBone == null)
        {
            EditorGUILayout.HelpBox("Chưa tìm thấy xương tay (RightHand / LeftHand) trên Player.", MessageType.Warning);
            if (GUILayout.Button("Tìm lại Player trong Scene"))
            {
                playerRoot = null;
                RefreshReferences();
            }
        }
        else
        {
            EditorGUILayout.LabelField("Xương đang chọn:", handBone.name, EditorStyles.miniLabel);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawWeaponAttachSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("2. Chọn & Gắn Vũ Khí", EditorStyles.boldLabel);

        if (foundWeapons.Count > 0)
        {
            EditorGUILayout.LabelField("Vũ khí có sẵn trong Assets/DucThanh/Assets/Weapons:", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < foundWeapons.Count; i++)
            {
                GameObject w = foundWeapons[i];
                if (GUILayout.Button(w.name, GUILayout.Height(24)))
                {
                    weaponPrefabOrObject = w;
                    AttachWeapon(w);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        weaponPrefabOrObject = (GameObject)EditorGUILayout.ObjectField(
            "Prefab / Object khác", weaponPrefabOrObject, typeof(GameObject), true);

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = handBone != null && weaponPrefabOrObject != null;
        if (GUILayout.Button("Gắn vào tay", GUILayout.Height(26)))
        {
            AttachWeapon(weaponPrefabOrObject);
        }
        GUI.enabled = true;

        activeWeapon = (GameObject)EditorGUILayout.ObjectField(
            GUIContent.none, activeWeapon, typeof(GameObject), true);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawTransformSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("3. Vị trí / Góc xoay / Tỉ lệ", EditorStyles.boldLabel);

        if (activeWeapon == null)
        {
            EditorGUILayout.HelpBox("Hãy gắn hoặc chọn một vũ khí đang ở trên tay để chỉnh.", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        Transform t = activeWeapon.transform;
        Vector3 pos = t.localPosition;
        Vector3 rot = NormalizeAngles(t.localEulerAngles);
        Vector3 scale = t.localScale;

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("Position (Local)", EditorStyles.miniBoldLabel);
        pos = EditorGUILayout.Vector3Field(GUIContent.none, pos);
        pos.x = EditorGUILayout.Slider("X", pos.x, -0.5f, 0.5f);
        pos.y = EditorGUILayout.Slider("Y", pos.y, -0.5f, 0.5f);
        pos.z = EditorGUILayout.Slider("Z", pos.z, -0.5f, 0.5f);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Rotation (Local)", EditorStyles.miniBoldLabel);
        rot = EditorGUILayout.Vector3Field(GUIContent.none, rot);
        rot.x = EditorGUILayout.Slider("Rot X", rot.x, -180f, 180f);
        rot.y = EditorGUILayout.Slider("Rot Y", rot.y, -180f, 180f);
        rot.z = EditorGUILayout.Slider("Rot Z", rot.z, -180f, 180f);

        // Nút xoay nhanh +/-90 độ
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("X +90°")) rot.x = Mathf.Repeat(rot.x + 90f + 180f, 360f) - 180f;
        if (GUILayout.Button("Y +90°")) rot.y = Mathf.Repeat(rot.y + 90f + 180f, 360f) - 180f;
        if (GUILayout.Button("Z +90°")) rot.z = Mathf.Repeat(rot.z + 90f + 180f, 360f) - 180f;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Scale (Local)", EditorStyles.miniBoldLabel);
        float uniform = EditorGUILayout.Slider("Tỉ lệ đều", scale.x, 0.1f, 3f);
        if (!Mathf.Approximately(uniform, scale.x))
        {
            scale = Vector3.one * uniform;
        }
        scale = EditorGUILayout.Vector3Field(GUIContent.none, scale);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(t, "Chỉnh vị trí vũ khí");
            t.localPosition = pos;
            t.localEulerAngles = rot;
            t.localScale = scale;
            EditorUtility.SetDirty(t);
            MarkSceneDirty();
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Chọn & Focus Scene"))
        {
            Selection.activeGameObject = activeWeapon;
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }
        }
        if (GUILayout.Button("Reset Transform"))
        {
            Undo.RecordObject(t, "Reset vị trí vũ khí");
            t.localPosition = Vector3.zero;
            t.localEulerAngles = Vector3.zero;
            t.localScale = Vector3.one;
            EditorUtility.SetDirty(t);
            MarkSceneDirty();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Lưu Scene", GUILayout.Height(26)))
        {
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[WeaponGripTool] Đã lưu vị trí vũ khí vào Scene.");
        }
        if (GUILayout.Button("Xóa vũ khí", GUILayout.Height(26)))
        {
            Undo.DestroyObjectImmediate(activeWeapon);
            activeWeapon = null;
            MarkSceneDirty();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawPreviewSection()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("4. Xem thử tư thế tay (Edit Mode)", EditorStyles.boldLabel);

        if (Application.isPlaying || playerAnimator == null)
        {
            EditorGUILayout.LabelField("Chỉ dùng khi đang dừng game và đã có Animator.", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUI.BeginChangeCheck();
        var nextPose = (PosePreview)EditorGUILayout.EnumPopup("Tư thế xem thử", previewPose);
        if (nextPose != PosePreview.None)
        {
            AnimationClip clip = nextPose == PosePreview.Idle ? idleClip : atkClip;
            float maxLen = clip != null ? clip.length : 1f;
            previewTime = EditorGUILayout.Slider("Thời điểm (s)", previewTime, 0f, maxLen);
        }

        if (EditorGUI.EndChangeCheck())
        {
            previewPose = nextPose;
            ApplyPreviewPose();
        }

        if (previewPose != PosePreview.None && GUILayout.Button("Tắt xem thử (Về tư thế gốc)"))
        {
            StopPreviewMode();
        }

        EditorGUILayout.EndVertical();
    }

    private void AttachWeapon(GameObject source)
    {
        if (handBone == null || source == null) return;

        // Giữ lại local transform nếu đang thay vũ khí khác trên tay
        Vector3 prevPos = Vector3.zero;
        Quaternion prevRot = Quaternion.identity;
        Vector3 prevScale = Vector3.one;

        if (activeWeapon != null && activeWeapon != source)
        {
            prevPos = activeWeapon.transform.localPosition;
            prevRot = activeWeapon.transform.localRotation;
            prevScale = activeWeapon.transform.localScale;
            Undo.DestroyObjectImmediate(activeWeapon);
        }

        GameObject instance;
        if (PrefabUtility.IsPartOfPrefabAsset(source))
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(source, handBone);
            Undo.RegisterCreatedObjectUndo(instance, "Gắn vũ khí vào tay");
        }
        else
        {
            instance = source;
            Undo.SetTransformParent(instance.transform, handBone, "Gắn vũ khí vào tay");
        }

        instance.transform.localPosition = prevPos;
        instance.transform.localRotation = prevRot;
        instance.transform.localScale = prevScale;

        activeWeapon = instance;
        Selection.activeGameObject = activeWeapon;
        MarkSceneDirty();
    }

    private void ApplyPreviewPose()
    {
        if (previewPose == PosePreview.None)
        {
            StopPreviewMode();
            return;
        }

        AnimationClip clip = previewPose == PosePreview.Idle ? idleClip : atkClip;
        if (clip == null || playerAnimator == null) return;

        if (!AnimationMode.InAnimationMode())
        {
            AnimationMode.StartAnimationMode();
        }

        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(playerAnimator.gameObject, clip, previewTime);
        AnimationMode.EndSampling();
        SceneView.RepaintAll();
    }

    private void StopPreviewMode()
    {
        previewPose = PosePreview.None;
        if (AnimationMode.InAnimationMode())
        {
            AnimationMode.StopAnimationMode();
            SceneView.RepaintAll();
        }
    }

    private void MarkSceneDirty()
    {
        if (!Application.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
    }

    private static Vector3 NormalizeAngles(Vector3 angles)
    {
        return new Vector3(
            NormalizeAngle(angles.x),
            NormalizeAngle(angles.y),
            NormalizeAngle(angles.z)
        );
    }

    private static float NormalizeAngle(float a)
    {
        a = Mathf.Repeat(a + 180f, 360f) - 180f;
        return a;
    }
}
#endif
