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
        t.localEulerAngles = gripEulerAngles;
        t.localScale = gripScale.sqrMagnitude > 0.0001f ? gripScale : Vector3.one;
    }
}

/// <summary>
/// ScriptableObject cấu hình danh sách vũ khí trong Cửa Hàng & vị trí cầm trên tay Player.
/// </summary>
[CreateAssetMenu(fileName = "WeaponUpgradeConfig", menuName = "DucThanh/Weapon Upgrade Config", order = 1)]
public class WeaponUpgradeConfigSO : ScriptableObject
{
    [Header("Danh sách vũ khí trong Cửa Hàng / Nâng cấp")]
    public List<WeaponUpgradeEntry> weapons = new List<WeaponUpgradeEntry>();

    [Header("Vị trí cầm mặc định trên tay Player (dùng khi vũ khí chưa chỉnh vị trí riêng)")]
    public bool hasDefaultGrip = true;
    public Vector3 defaultGripPosition = new Vector3(-0.002f, 0.118f, 0.02f);
    public Vector3 defaultGripEulerAngles = new Vector3(-2.63f, -12.56f, -108.47f);
    public Vector3 defaultGripScale = new Vector3(1.0283f, 1.229127f, 1.0283f);
    [HideInInspector]
    public Quaternion defaultGripRotation = new Quaternion(-0.09637129f, -0.074628845f, -0.8076334f, 0.57695013f);

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
