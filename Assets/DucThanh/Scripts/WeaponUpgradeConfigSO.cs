using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class WeaponUpgradeEntry
{
    [Tooltip("Bật để cho phép vũ khí này xuất hiện khi lên cấp")]
    public bool enabled = true;

    [Tooltip("Tên hiển thị trên nút lựa chọn (để trống sẽ tự lấy tên Prefab)")]
    public string displayName;

    [Tooltip("Prefab vũ khí gắn vào tay Player")]
    public GameObject weaponPrefab;

    [Tooltip("Sát thương cộng thêm khi đổi sang vũ khí này")]
    public int bonusDamage = 10;

    [Tooltip("Số thứ tự xuất hiện: Số thấp sẽ ra trước, lần lượt từ thấp đến cao (nếu bằng nhau sẽ lấy lần lượt từ trên xuống dưới)")]
    [Range(1, 20)]
    public int minLevel = 1;

    [Header("Vị trí cầm trên tay Player (Local Transform)")]
    [Tooltip("Khi bật, vũ khí này sẽ nằm đúng vị trí/góc xoay/tỉ lệ đã lưu bên dưới khi Player cầm")]
    public bool hasCustomGrip = false;

    [Tooltip("Vị trí Local trên xương tay Player")]
    public Vector3 gripPosition = new Vector3(-0.002f, 0.118f, 0.02f);

    [Tooltip("Góc xoay Local (Euler độ) trên xương tay Player")]
    public Vector3 gripEulerAngles = new Vector3(-2.63f, -12.56f, -108.47f);

    [Tooltip("Tỉ lệ Scale Local trên xương tay Player")]
    public Vector3 gripScale = new Vector3(1.0283f, 1.229127f, 1.0283f);

    [HideInInspector]
    public Quaternion gripRotation = new Quaternion(-0.09637129f, -0.074628845f, -0.8076334f, 0.57695013f);

    public string GetDisplayName()
    {
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
        return weaponPrefab != null ? weaponPrefab.name.Replace("_", " ").Trim() : "Vũ khí mới";
    }

    public void SaveGripFromTransform(Transform t)
    {
        if (t == null) return;
        hasCustomGrip = true;
        gripPosition = t.localPosition;
        gripEulerAngles = t.localEulerAngles;
        gripRotation = t.localRotation;
        gripScale = t.localScale;
    }

    public void ApplyGripToTransform(Transform t)
    {
        if (t == null) return;
        t.localPosition = gripPosition;
        if (gripRotation.x != 0f || gripRotation.y != 0f || gripRotation.z != 0f || gripRotation.w != 0f)
        {
            t.localEulerAngles = gripEulerAngles;
        }
        else
        {
            t.localEulerAngles = gripEulerAngles;
        }
        t.localScale = gripScale.sqrMagnitude > 0.0001f ? gripScale : Vector3.one;
    }
}

/// <summary>
/// ScriptableObject cấu hình danh sách vũ khí xuất hiện lần lượt (số thấp ra trước) khi Player lên cấp,
/// kèm lưu vị trí cầm vũ khí trên tay Player.
/// </summary>
[CreateAssetMenu(fileName = "WeaponUpgradeConfig", menuName = "DucThanh/Weapon Upgrade Config", order = 1)]
public class WeaponUpgradeConfigSO : ScriptableObject
{
    [Header("Danh sách vũ khí khi lên cấp (Ra lần lượt: Số thấp / Thứ tự trên xuống ra trước)")]
    public List<WeaponUpgradeEntry> weapons = new List<WeaponUpgradeEntry>();

    [Header("Vị trí cầm mặc định trên tay Player (dùng khi vũ khí chưa chỉnh vị trí riêng)")]
    public bool hasDefaultGrip = true;
    public Vector3 defaultGripPosition = new Vector3(-0.002f, 0.118f, 0.02f);
    public Vector3 defaultGripEulerAngles = new Vector3(-2.63f, -12.56f, -108.47f);
    public Vector3 defaultGripScale = new Vector3(1.0283f, 1.229127f, 1.0283f);
    [HideInInspector]
    public Quaternion defaultGripRotation = new Quaternion(-0.09637129f, -0.074628845f, -0.8076334f, 0.57695013f);

    private struct IndexedEntry
    {
        public WeaponUpgradeEntry entry;
        public int listIndex;
    }

    /// <summary>
    /// Trả về danh sách vũ khí đã bật (enabled = true), sắp xếp lần lượt từ số thấp đến số cao
    /// (theo minLevel tăng dần, nếu bằng nhau thì theo vị trí Element 0, 1, 2... từ trên xuống dưới).
    /// </summary>
    public List<WeaponUpgradeEntry> GetSortedEnabledWeapons()
    {
        List<WeaponUpgradeEntry> result = new List<WeaponUpgradeEntry>();
        if (weapons == null || weapons.Count == 0) return result;

        List<IndexedEntry> indexed = new List<IndexedEntry>();
        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponUpgradeEntry e = weapons[i];
            if (e == null || !e.enabled || e.weaponPrefab == null) continue;
            indexed.Add(new IndexedEntry { entry = e, listIndex = i });
        }

        indexed.Sort((a, b) =>
        {
            int cmp = a.entry.minLevel.CompareTo(b.entry.minLevel);
            if (cmp != 0) return cmp;
            return a.listIndex.CompareTo(b.listIndex);
        });

        for (int i = 0; i < indexed.Count; i++)
        {
            result.Add(indexed[i].entry);
        }

        return result;
    }

    /// <summary>
    /// Lấy vũ khí tiếp theo theo đúng thứ tự lần lượt (số thấp ra trước, không random).
    /// </summary>
    public WeaponUpgradeEntry GetSequentialWeapon(int sequenceStep, string currentEquippedPrefabName, out int resolvedStep)
    {
        resolvedStep = sequenceStep;
        List<WeaponUpgradeEntry> sorted = GetSortedEnabledWeapons();
        if (sorted.Count == 0) return null;

        int startIdx = Mathf.Clamp(sequenceStep, 0, sorted.Count - 1);

        // Tìm từ vị trí sequenceStep trở đi: lấy vũ khí có thứ tự thấp nhất tiếp theo
        for (int i = startIdx; i < sorted.Count; i++)
        {
            WeaponUpgradeEntry candidate = sorted[i];
            if (sorted.Count > 1
                && !string.IsNullOrEmpty(currentEquippedPrefabName)
                && string.Equals(candidate.weaponPrefab.name, currentEquippedPrefabName, StringComparison.OrdinalIgnoreCase)
                && i == startIdx
                && i + 1 < sorted.Count)
            {
                continue;
            }

            resolvedStep = i;
            return candidate;
        }

        // Nếu đã đi hết danh sách thì quay vòng lại từ đầu danh sách
        for (int i = 0; i < sorted.Count; i++)
        {
            WeaponUpgradeEntry candidate = sorted[i];
            if (sorted.Count > 1
                && !string.IsNullOrEmpty(currentEquippedPrefabName)
                && string.Equals(candidate.weaponPrefab.name, currentEquippedPrefabName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            resolvedStep = i;
            return candidate;
        }

        resolvedStep = startIdx;
        return sorted[startIdx];
    }

    /// <summary>
    /// Tìm cấu hình của một vũ khí theo Prefab hoặc tên Object đang cầm trên tay.
    /// </summary>
    public WeaponUpgradeEntry FindEntry(GameObject prefabOrInstance)
    {
        if (prefabOrInstance == null || weapons == null) return null;
        string cleanName = CleanWeaponName(prefabOrInstance.name);

        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponUpgradeEntry entry = weapons[i];
            if (entry == null || entry.weaponPrefab == null) continue;
            if (entry.weaponPrefab == prefabOrInstance) return entry;
            if (string.Equals(CleanWeaponName(entry.weaponPrefab.name), cleanName, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }
        return null;
    }

    public WeaponUpgradeEntry FindEntryByName(string weaponName)
    {
        if (string.IsNullOrWhiteSpace(weaponName) || weapons == null) return null;
        string cleanName = CleanWeaponName(weaponName);

        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponUpgradeEntry entry = weapons[i];
            if (entry == null || entry.weaponPrefab == null) continue;
            if (string.Equals(CleanWeaponName(entry.weaponPrefab.name), cleanName, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }
        return null;
    }

    /// <summary>
    /// Lưu vị trí cầm vũ khí trên tay Player cho vũ khí tương ứng (và cập nhật vị trí mặc định).
    /// </summary>
    public WeaponUpgradeEntry SaveGripForWeapon(Transform weaponInHand, bool alsoUpdateDefault = true)
    {
        if (weaponInHand == null) return null;

        if (alsoUpdateDefault || !hasDefaultGrip)
        {
            hasDefaultGrip = true;
            defaultGripPosition = weaponInHand.localPosition;
            defaultGripEulerAngles = weaponInHand.localEulerAngles;
            defaultGripRotation = weaponInHand.localRotation;
            defaultGripScale = weaponInHand.localScale;
        }

        WeaponUpgradeEntry entry = FindEntry(weaponInHand.gameObject);
        if (entry != null)
        {
            entry.SaveGripFromTransform(weaponInHand);
        }

        return entry;
    }

    /// <summary>
    /// Lưu vị trí cầm hiện tại cho TẤT CẢ các vũ khí trong danh sách.
    /// </summary>
    public void SaveGripForAllWeapons(Transform weaponInHand)
    {
        if (weaponInHand == null) return;

        hasDefaultGrip = true;
        defaultGripPosition = weaponInHand.localPosition;
        defaultGripEulerAngles = weaponInHand.localEulerAngles;
        defaultGripRotation = weaponInHand.localRotation;
        defaultGripScale = weaponInHand.localScale;

        if (weapons == null) return;
        for (int i = 0; i < weapons.Count; i++)
        {
            if (weapons[i] != null)
            {
                weapons[i].SaveGripFromTransform(weaponInHand);
            }
        }
    }

    /// <summary>
    /// Áp dụng vị trí đã lưu lên vũ khí mới khi gắn vào tay Player.
    /// </summary>
    public void ApplySavedGrip(WeaponUpgradeEntry entry, Transform weaponTransform, Vector3 fallbackPos, Quaternion fallbackRot, Vector3 fallbackScale)
    {
        if (weaponTransform == null) return;

        if (entry == null)
        {
            entry = FindEntry(weaponTransform.gameObject);
        }

        if (entry != null && entry.hasCustomGrip)
        {
            entry.ApplyGripToTransform(weaponTransform);
            return;
        }

        if (hasDefaultGrip)
        {
            weaponTransform.localPosition = defaultGripPosition;
            weaponTransform.localEulerAngles = defaultGripEulerAngles;
            weaponTransform.localScale = defaultGripScale.sqrMagnitude > 0.0001f ? defaultGripScale : Vector3.one;
            return;
        }

        weaponTransform.localPosition = fallbackPos;
        weaponTransform.localRotation = fallbackRot;
        weaponTransform.localScale = fallbackScale;
    }

    public static string CleanWeaponName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return string.Empty;
        return rawName.Replace("(Clone)", "").Trim();
    }
}
