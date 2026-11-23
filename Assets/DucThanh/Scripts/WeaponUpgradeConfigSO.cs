using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
// Thông tin một mẫu vũ khí gồm prefab, giá vàng, sát thương cộng thêm và vị trí gắn tay
public class WeaponUpgradeEntry
{

    public bool enabled = true;

    public string displayName;

    public GameObject weaponPrefab;

    public int bonusDamage = 10;

    [Range(1, 20)]
    public int minLevel = 1;

    [Header("Weapon Hand Grip")]

    public bool hasCustomGrip = false;

    public Vector3 gripPosition = new Vector3(-0.002f, 0.118f, 0.02f);

    public Vector3 gripEulerAngles = new Vector3(-2.63f, -12.56f, -108.47f);

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

// ScriptableObject lưu danh sách vũ khí nâng cấp và tọa độ cầm chuẩn trên tay nhân vật
[CreateAssetMenu(fileName = "WeaponUpgradeConfig", menuName = "DucThanh/Weapon Upgrade Config", order = 1)]
public class WeaponUpgradeConfigSO : ScriptableObject
{
    [Header("Shop Weapons")]
    public List<WeaponUpgradeEntry> weapons = new List<WeaponUpgradeEntry>();

    [Header("Default Weapon Hand Grip")]
    public bool hasDefaultGrip = true;
    public Vector3 defaultGripPosition = new Vector3(-0.002f, 0.118f, 0.02f);
    public Vector3 defaultGripEulerAngles = new Vector3(-2.63f, -12.56f, -108.47f);
    public Vector3 defaultGripScale = new Vector3(1.0283f, 1.229127f, 1.0283f);
    [HideInInspector]
    public Quaternion defaultGripRotation = new Quaternion(-0.09637129f, -0.074628845f, -0.8076334f, 0.57695013f);

    // Tìm cấu hình vũ khí khớp theo prefab hoặc tên model
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

    // Lưu lại vị trí, góc xoay và tỷ lệ cầm vũ khí đang chỉnh trong scene
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

    // Áp dụng thông số vị trí cầm hiện tại cho toàn bộ vũ khí trong danh sách
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

    // Chuẩn hóa tên vũ khí loại bỏ hậu tố Clone khi đối chiếu
    public static string CleanWeaponName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return string.Empty;
        return rawName.Replace("(Clone)", "").Trim();
    }
}
