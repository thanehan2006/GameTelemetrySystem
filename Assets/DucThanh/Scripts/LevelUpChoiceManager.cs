using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Quản lý sự kiện Lên Cấp:
/// - Tự động lắng nghe ScoreManager.OnLevelUp.
/// - Tạm dừng game (Time.timeScale = 0), mở khóa chuột và hiện bảng 2 lựa chọn:
///   1) Giữ nguyên vũ khí hiện tại & hồi đầy HP.
///   2) Đổi sang vũ khí mới xuất hiện LẦN LƯỢT theo thứ tự số thấp ra trước từ WeaponUpgradeConfigSO,
///      và tự động đặt vũ khí mới vào đúng vị trí cầm đã chỉnh trên tay Player.
/// </summary>
public class LevelUpChoiceManager : MonoBehaviour
{
    public static LevelUpChoiceManager Instance { get; private set; }
    public static bool IsPaused => Instance != null && Instance.isChoosing;

    public const string DefaultConfigAssetPath = "Assets/DucThanh/Assets/Weapons/WeaponUpgradeConfig.asset";
    private const string DefaultWeaponsFolder = "Assets/DucThanh/Assets/Weapons";

    [Header("ScriptableObject Cấu Hình Vũ Khí Lên Cấp (Lần lượt số thấp ra trước)")]
    [Tooltip("File ScriptableObject chứa danh sách vũ khí và vị trí cầm trên tay Player")]
    public WeaponUpgradeConfigSO weaponConfig;

    [Tooltip("Nếu tắt (mặc định): khi chọn Hồi máu, lần lên cấp tiếp theo vẫn hiện vũ khí chưa nhận đó. Nếu bật: mỗi lần lên cấp tự chuyển sang vũ khí kế tiếp.")]
    public bool advanceSequenceOnHeal = false;

    [Header("Fallback (nếu chưa gán ScriptableObject)")]
    public List<GameObject> weaponPrefabs = new List<GameObject>();
    public int newWeaponBonusDamage = 10;

    [Header("Optional Custom UI (để trống sẽ tự tạo đẹp trên Canvas)")]
    public GameObject choicePanel;
    public TMP_Text titleText;
    public Button keepWeaponHealButton;
    public Button swapWeaponButton;
    public TMP_Text swapWeaponLabel;

    private bool isChoosing;
    private int pendingLevel;
    private int currentSequenceStep = 0;
    private int candidateSequenceStep = 0;
    private WeaponUpgradeEntry candidateEntry;
    private GameObject candidateWeaponPrefab;
    private string candidateDisplayName;
    private int candidateBonusDamage;

#if UNITY_EDITOR
    private Transform trackedHandWeapon;
    private Vector3 lastTrackedLocalPos;
    private Vector3 lastTrackedLocalEuler;
    private Vector3 lastTrackedLocalScale;
#endif

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        currentSequenceStep = 0;
        EnsureWeaponConfigLoaded();
    }

    private void Start()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnLevelUp += OnPlayerLevelUp;
        }

        if (choicePanel != null)
        {
            choicePanel.SetActive(false);
            BindButtonListeners();
        }

        SyncInitialPlayerWeaponGrip();
    }

    private void Update()
    {
#if UNITY_EDITOR
        MonitorHandWeaponGripChangesInEditor();
#endif
    }

    private void OnDestroy()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnLevelUp -= OnPlayerLevelUp;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void SyncInitialPlayerWeaponGrip()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;

        FindPlayerHandBone(player, out GameObject currentWeapon);
        if (currentWeapon == null) return;

        if (weaponConfig != null)
        {
            WeaponUpgradeEntry entry = weaponConfig.FindEntry(currentWeapon);
            if (entry != null && entry.hasCustomGrip)
            {
                entry.ApplyGripToTransform(currentWeapon.transform);
            }
            else if (!weaponConfig.hasDefaultGrip)
            {
                weaponConfig.SaveGripForWeapon(currentWeapon.transform, true);
#if UNITY_EDITOR
                EditorUtility.SetDirty(weaponConfig);
#endif
            }
        }

#if UNITY_EDITOR
        SetTrackedHandWeapon(currentWeapon.transform);
#endif
    }

#if UNITY_EDITOR
    private void SetTrackedHandWeapon(Transform weaponTransform)
    {
        trackedHandWeapon = weaponTransform;
        if (trackedHandWeapon != null)
        {
            lastTrackedLocalPos = trackedHandWeapon.localPosition;
            lastTrackedLocalEuler = trackedHandWeapon.localEulerAngles;
            lastTrackedLocalScale = trackedHandWeapon.localScale;
        }
    }

    /// <summary>
    /// Nếu bạn kéo chỉnh vị trí / góc xoay / tỉ lệ của vũ khí đang cầm trên tay Player ngay trong Scene / Inspector
    /// (kể cả khi đang Play), hệ thống tự động lưu vị trí đó vào WeaponUpgradeConfigSO để lần sau cầm đúng vị trí đã chỉnh.
    /// </summary>
    private void MonitorHandWeaponGripChangesInEditor()
    {
        if (weaponConfig == null) return;

        if (trackedHandWeapon == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                FindPlayerHandBone(player, out GameObject currentWeapon);
                if (currentWeapon != null)
                {
                    SetTrackedHandWeapon(currentWeapon.transform);
                }
            }
            return;
        }

        bool changed = (trackedHandWeapon.localPosition - lastTrackedLocalPos).sqrMagnitude > 0.0000001f
            || (trackedHandWeapon.localEulerAngles - lastTrackedLocalEuler).sqrMagnitude > 0.0001f
            || (trackedHandWeapon.localScale - lastTrackedLocalScale).sqrMagnitude > 0.0000001f;

        if (changed)
        {
            lastTrackedLocalPos = trackedHandWeapon.localPosition;
            lastTrackedLocalEuler = trackedHandWeapon.localEulerAngles;
            lastTrackedLocalScale = trackedHandWeapon.localScale;

            weaponConfig.SaveGripForWeapon(trackedHandWeapon, true);
            EditorUtility.SetDirty(weaponConfig);
        }
    }
#endif

    [ContextMenu("Lưu vị trí vũ khí đang cầm trên tay vào Config")]
    public void SaveCurrentEquippedWeaponGrip()
    {
        EnsureWeaponConfigLoaded();
        if (weaponConfig == null) return;

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;

        FindPlayerHandBone(player, out GameObject currentWeapon);
        if (currentWeapon == null)
        {
            Debug.LogWarning("[LevelUpChoice] Không tìm thấy vũ khí nào đang gắn trên tay Player để lưu vị trí!");
            return;
        }

        WeaponUpgradeEntry entry = weaponConfig.SaveGripForWeapon(currentWeapon.transform, true);
#if UNITY_EDITOR
        SetTrackedHandWeapon(currentWeapon.transform);
        EditorUtility.SetDirty(weaponConfig);
        AssetDatabase.SaveAssets();
#endif
        Debug.Log($"[LevelUpChoice] Đã lưu vị trí cầm trên tay cho '{(entry != null ? entry.GetDisplayName() : currentWeapon.name)}' " +
                  $"| Pos={currentWeapon.transform.localPosition} | Rot={currentWeapon.transform.localEulerAngles} | Scale={currentWeapon.transform.localScale}");
    }

    private void EnsureWeaponConfigLoaded()
    {
#if UNITY_EDITOR
        if (weaponConfig == null)
        {
            weaponConfig = AssetDatabase.LoadAssetAtPath<WeaponUpgradeConfigSO>(DefaultConfigAssetPath);
        }
#endif
        if (weaponConfig != null && weaponConfig.weapons != null && weaponConfig.weapons.Count > 0)
        {
            return;
        }

        if (weaponPrefabs != null && weaponPrefabs.Count > 0) return;

        weaponPrefabs = new List<GameObject>();
#if UNITY_EDITOR
        if (AssetDatabase.IsValidFolder(DefaultWeaponsFolder))
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { DefaultWeaponsFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    weaponPrefabs.Add(prefab);
                }
            }
        }
#endif
    }

    private void OnPlayerLevelUp(int newLevel)
    {
        if (GameOverManager.IsGameOver) return;

        pendingLevel = newLevel;
        PickNextWeaponCandidate();

        EnsureUI();
        UpdateChoiceTexts();

        isChoosing = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (choicePanel != null)
        {
            choicePanel.transform.SetAsLastSibling();
            choicePanel.SetActive(true);
        }
    }

    private void UpdateChoiceTexts()
    {
        if (titleText != null)
        {
            titleText.text = $"LÊN CẤP {pendingLevel}!\nChọn 1 nâng cấp";
        }

        if (swapWeaponLabel != null)
        {
            if (candidateWeaponPrefab != null)
            {
                swapWeaponLabel.text = $"Đổi vũ khí mới\n({candidateDisplayName}, +{candidateBonusDamage} DMG)";
            }
            else
            {
                swapWeaponLabel.text = "Không có vũ khí mới\nđược bật trong Config";
            }
        }

        if (swapWeaponButton != null)
        {
            swapWeaponButton.interactable = candidateWeaponPrefab != null;
        }
    }

    public void ChooseKeepWeaponAndFullHeal()
    {
        if (!isChoosing) return;

        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            if (health != null)
            {
                health.FullHeal();
            }
        }

        if (advanceSequenceOnHeal)
        {
            currentSequenceStep = candidateSequenceStep + 1;
        }

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackEvent("level_up_heal", pendingLevel);
        }

        Debug.Log($"[LevelUpChoice] Level {pendingLevel}: Giữ nguyên vũ khí và hồi đầy HP.");
        ResumeGame();
    }

    public void ChooseSwapNewWeapon()
    {
        if (!isChoosing) return;

        if (candidateWeaponPrefab != null)
        {
            EquipWeaponOnPlayer(candidateWeaponPrefab, candidateBonusDamage, candidateEntry);
            currentSequenceStep = candidateSequenceStep + 1;
        }

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackEvent("weapon_changed", pendingLevel);
        }

        Debug.Log($"[LevelUpChoice] Level {pendingLevel}: Đổi sang vũ khí '{candidateDisplayName}' (+{candidateBonusDamage} DMG). Thứ tự tiếp theo: #{currentSequenceStep + 1}");
        ResumeGame();
    }

    private void ResumeGame()
    {
        isChoosing = false;

        if (choicePanel != null)
        {
            choicePanel.SetActive(false);
        }

        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void PickNextWeaponCandidate()
    {
        candidateEntry = null;
        candidateWeaponPrefab = null;
        candidateDisplayName = "Vũ khí mới";
        candidateBonusDamage = newWeaponBonusDamage;

        EnsureWeaponConfigLoaded();
        string currentName = GetCurrentEquippedWeaponName();

        // Lấy LẦN LƯỢT từ WeaponUpgradeConfigSO (số thấp ra trước, không random)
        if (weaponConfig != null && weaponConfig.weapons != null && weaponConfig.weapons.Count > 0)
        {
            WeaponUpgradeEntry entry = weaponConfig.GetSequentialWeapon(currentSequenceStep, currentName, out candidateSequenceStep);
            if (entry != null && entry.weaponPrefab != null)
            {
                candidateEntry = entry;
                candidateWeaponPrefab = entry.weaponPrefab;
                candidateDisplayName = entry.GetDisplayName();
                candidateBonusDamage = entry.bonusDamage;
            }
            return;
        }

        // Fallback tuần tự nếu chưa có ScriptableObject
        if (weaponPrefabs == null || weaponPrefabs.Count == 0) return;

        int idx = Mathf.Clamp(currentSequenceStep, 0, weaponPrefabs.Count - 1);
        for (int i = idx; i < weaponPrefabs.Count; i++)
        {
            GameObject prefab = weaponPrefabs[i];
            if (prefab == null) continue;
            if (weaponPrefabs.Count > 1 && !string.IsNullOrEmpty(currentName) && prefab.name == currentName && i + 1 < weaponPrefabs.Count)
            {
                continue;
            }
            candidateSequenceStep = i;
            candidateWeaponPrefab = prefab;
            candidateDisplayName = FormatWeaponName(prefab.name);
            return;
        }

        candidateSequenceStep = idx;
        candidateWeaponPrefab = weaponPrefabs[idx];
        if (candidateWeaponPrefab != null)
        {
            candidateDisplayName = FormatWeaponName(candidateWeaponPrefab.name);
        }
    }

    public void EquipWeaponOnPlayer(GameObject newWeaponPrefab, int bonusDamage, WeaponUpgradeEntry entry = null)
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;

        Transform hand = FindPlayerHandBone(player, out GameObject currentWeapon);
        if (hand == null)
        {
            Debug.LogWarning("[LevelUpChoice] Không tìm thấy xương tay trên Player để gắn vũ khí mới!");
            return;
        }

        Vector3 fallbackPos = new Vector3(-0.002f, 0.118f, 0.02f);
        Quaternion fallbackRot = new Quaternion(-0.09637129f, -0.074628845f, -0.8076334f, 0.57695013f);
        Vector3 fallbackScale = new Vector3(1.0283f, 1.229127f, 1.0283f);

        if (currentWeapon != null)
        {
            fallbackPos = currentWeapon.transform.localPosition;
            fallbackRot = currentWeapon.transform.localRotation;
            fallbackScale = currentWeapon.transform.localScale;

            currentWeapon.transform.SetParent(null);
            currentWeapon.SetActive(false);
            Destroy(currentWeapon);
        }

        GameObject newWeapon = Instantiate(newWeaponPrefab, hand);
        newWeapon.name = newWeaponPrefab.name;

        if (weaponConfig != null)
        {
            weaponConfig.ApplySavedGrip(entry, newWeapon.transform, fallbackPos, fallbackRot, fallbackScale);
        }
        else if (entry != null && entry.hasCustomGrip)
        {
            entry.ApplyGripToTransform(newWeapon.transform);
        }
        else
        {
            newWeapon.transform.localPosition = fallbackPos;
            newWeapon.transform.localRotation = fallbackRot;
            newWeapon.transform.localScale = fallbackScale;
        }

#if UNITY_EDITOR
        SetTrackedHandWeapon(newWeapon.transform);
#endif

        PlayerAttack attack = player.GetComponent<PlayerAttack>();
        if (attack != null)
        {
            attack.RefreshWeapon(bonusDamage);
        }
    }

    private string GetCurrentEquippedWeaponName()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return string.Empty;

        FindPlayerHandBone(player, out GameObject currentWeapon);
        if (currentWeapon == null) return string.Empty;
        return WeaponUpgradeConfigSO.CleanWeaponName(currentWeapon.name);
    }

    public static Transform FindPlayerHandBone(GameObject player, out GameObject equippedWeapon)
    {
        equippedWeapon = null;
        if (player == null) return null;
        Animator anim = player.GetComponentInChildren<Animator>();
        if (anim == null) return null;

        Transform rightHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        Transform leftHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);

        if (rightHand == null || leftHand == null)
        {
            foreach (Transform t in anim.GetComponentsInChildren<Transform>(true))
            {
                if (rightHand == null && t.name == "mixamorig:RightHand") rightHand = t;
                if (leftHand == null && t.name == "mixamorig:LeftHand") leftHand = t;
            }
        }

        if (rightHand != null)
        {
            equippedWeapon = FindWeaponChild(rightHand);
            if (equippedWeapon != null) return rightHand;
        }

        if (leftHand != null)
        {
            equippedWeapon = FindWeaponChild(leftHand);
            if (equippedWeapon != null) return leftHand;
        }

        return rightHand != null ? rightHand : leftHand;
    }

    private static GameObject FindWeaponChild(Transform hand)
    {
        for (int i = 0; i < hand.childCount; i++)
        {
            Transform child = hand.GetChild(i);
            if (!child.name.StartsWith("mixamorig:") && child.GetComponentInChildren<Renderer>() != null)
            {
                return child.gameObject;
            }
        }
        return null;
    }

    private static string FormatWeaponName(string rawName)
    {
        return rawName.Replace("_", " ").Trim();
    }

    private void BindButtonListeners()
    {
        if (keepWeaponHealButton != null)
        {
            keepWeaponHealButton.onClick.RemoveListener(ChooseKeepWeaponAndFullHeal);
            keepWeaponHealButton.onClick.AddListener(ChooseKeepWeaponAndFullHeal);
        }

        if (swapWeaponButton != null)
        {
            swapWeaponButton.onClick.RemoveListener(ChooseSwapNewWeapon);
            swapWeaponButton.onClick.AddListener(ChooseSwapNewWeapon);
        }
    }

    private void EnsureUI()
    {
        if (choicePanel != null)
        {
            BindButtonListeners();
            return;
        }

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[LevelUpChoice] Không tìm thấy Canvas trong Scene!");
            return;
        }

        TMP_FontAsset sharedFont = ScoreManager.Instance != null && ScoreManager.Instance.scoreText != null
            ? ScoreManager.Instance.scoreText.font
            : TMP_Settings.defaultFontAsset;

        choicePanel = new GameObject("LevelUpChoicePanel", typeof(RectTransform), typeof(Image));
        choicePanel.transform.SetParent(canvas.transform, false);
        RectTransform overlayRt = choicePanel.GetComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;
        choicePanel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.1f, 0.82f);

        GameObject card = new GameObject("ChoiceCard", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(choicePanel.transform, false);
        RectTransform cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(560f, 300f);
        card.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.2f, 0.96f);

        titleText = CreateLabel(card.transform, "TitleText", "LÊN CẤP!", 30, FontStyles.Bold,
            new Color(1f, 0.85f, 0.25f, 1f), new Vector2(0f, 92f), new Vector2(500f, 80f), sharedFont);

        keepWeaponHealButton = CreateChoiceButton(
            card.transform,
            "KeepWeaponHealButton",
            "Giữ nguyên vũ khí\n& Hồi đầy 100% HP",
            new Color(0.16f, 0.62f, 0.35f, 1f),
            new Vector2(-132f, -35f),
            new Vector2(236f, 120f),
            sharedFont,
            out _
        );

        swapWeaponButton = CreateChoiceButton(
            card.transform,
            "SwapWeaponButton",
            "Đổi sang vũ khí mới",
            new Color(0.22f, 0.45f, 0.82f, 1f),
            new Vector2(132f, -35f),
            new Vector2(236f, 120f),
            sharedFont,
            out swapWeaponLabel
        );

        BindButtonListeners();
    }

    private static TMP_Text CreateLabel(
        Transform parent, string name, string text, float fontSize, FontStyles style,
        Color color, Vector2 pos, Vector2 size, TMP_FontAsset font)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }

    private static Button CreateChoiceButton(
        Transform parent, string name, string labelText, Color bgColor,
        Vector2 pos, Vector2 size, TMP_FontAsset font, out TMP_Text label)
    {
        GameObject btnGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(parent, false);
        RectTransform rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = btnGo.GetComponent<Image>();
        img.color = bgColor;

        Button btn = btnGo.GetComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        btn.colors = colors;

        label = CreateLabel(btnGo.transform, "Label", labelText, 20, FontStyles.Bold,
            Color.white, Vector2.zero, size - new Vector2(20f, 16f), font);

        return btn;
    }
}
