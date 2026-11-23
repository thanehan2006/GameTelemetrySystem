using UnityEngine;

// Thay thế model và gắn vũ khí khởi đầu cho nhân vật người chơi theo lựa chọn ở PickCharacter
public static class PlayerCharacterLoader
{
    private const string DefaultStartingSwordPath = "Assets/DucThanh/Assets/Weapons/Kiếm.prefab";

    // Khởi tạo model nhân vật đã chọn lên Player gốc và kết nối lại Animator/vũ khí
    public static void ApplySelectedCharacter(GameObject playerRoot)
    {
        if (playerRoot == null) return;

        PlayerAttack pa = playerRoot.GetComponent<PlayerAttack>();
        CharacterProfile profile = SelectedCharacterStore.GetSelectedProfile();
        if (profile == null)
        {
            AlignDefaultCharacterWeapon(playerRoot, null, pa);
            return;
        }

        PlayerHealth hp = playerRoot.GetComponent<PlayerHealth>();
        if (hp != null)
        {
            hp.maxHealth = profile.maxHealth;
            hp.currentHealth = profile.maxHealth;
        }

        PlayerController pc = playerRoot.GetComponent<PlayerController>();
        if (pc != null)
        {
            pc.moveSpeed = profile.moveSpeed;
            pc.blockDamageReduction = profile.blockDamageReduction;
            pc.perfectParryHealPercent = profile.perfectParryHealPercent;
        }

        if (pa != null)
        {
            pa.attackDamage = profile.attackDamage;
            pa.knockbackForce = profile.knockbackForce;
        }

        int selectedIdx = SelectedCharacterStore.SelectedIndex;
        if (selectedIdx == 0 || profile.modelPrefab == null)
        {
            AlignDefaultCharacterWeapon(playerRoot, profile, pa);
            return;
        }

        Transform oldBody = playerRoot.transform.Find("PlayerBody");
        RuntimeAnimatorController sharedController = null;
        Vector3 savedWeaponPos = Vector3.zero;
        Quaternion savedWeaponRot = Quaternion.identity;
        Vector3 savedWeaponScale = Vector3.one;
        GameObject oldWeaponRef = null;

        if (oldBody != null)
        {
            Animator oldAnim = oldBody.GetComponentInChildren<Animator>();
            if (oldAnim != null)
            {
                sharedController = oldAnim.runtimeAnimatorController;
            }

            WeaponShopManager.FindPlayerHandBone(playerRoot, out oldWeaponRef);
            if (oldWeaponRef != null)
            {
                savedWeaponPos = oldWeaponRef.transform.localPosition;
                savedWeaponRot = oldWeaponRef.transform.localRotation;
                savedWeaponScale = oldWeaponRef.transform.localScale;
                oldWeaponRef.transform.SetParent(null, false);
            }

            oldBody.SetParent(null, false);
            oldBody.name = "PlayerBody_Old";
            oldBody.gameObject.SetActive(false);
            Object.Destroy(oldBody.gameObject);
        }

        GameObject newBody = Object.Instantiate(profile.modelPrefab, playerRoot.transform);
        newBody.name = "PlayerBody";
        newBody.transform.localPosition = Vector3.zero;
        newBody.transform.localRotation = Quaternion.identity;
        newBody.transform.localScale = Vector3.one;

        Animator newAnim = newBody.GetComponent<Animator>();
        if (newAnim == null) newAnim = newBody.AddComponent<Animator>();
        if (sharedController != null)
        {
            newAnim.runtimeAnimatorController = sharedController;
        }
        newAnim.applyRootMotion = false;

        Transform rightHand = FindRightHandBone(newBody.transform, newAnim);
        GameObject startWeaponPrefab = ResolveStartingWeaponPrefab(profile);

        if (rightHand != null)
        {
            if (startWeaponPrefab != null)
            {
                GameObject weaponObj = Object.Instantiate(startWeaponPrefab, rightHand);
                weaponObj.name = startWeaponPrefab.name;
                if (!WeaponGripAligner.AlignWeaponToHand(rightHand, weaponObj.transform, newAnim))
                {
                    weaponObj.transform.localPosition = savedWeaponPos;
                    weaponObj.transform.localRotation = savedWeaponRot;
                    weaponObj.transform.localScale = savedWeaponScale;
                }
                WeaponElementalVfx.ApplyToWeapon(weaponObj);

                if (oldWeaponRef != null) Object.Destroy(oldWeaponRef);
            }
            else if (oldWeaponRef != null)
            {
                oldWeaponRef.transform.SetParent(rightHand, false);
                if (!WeaponGripAligner.AlignWeaponToHand(rightHand, oldWeaponRef.transform, newAnim))
                {
                    oldWeaponRef.transform.localPosition = savedWeaponPos;
                    oldWeaponRef.transform.localRotation = savedWeaponRot;
                    oldWeaponRef.transform.localScale = savedWeaponScale;
                }
                WeaponElementalVfx.ApplyToWeapon(oldWeaponRef);
            }
        }
        else if (oldWeaponRef != null)
        {
            Object.Destroy(oldWeaponRef);
        }

        if (pc != null) pc.visualModel = newBody.transform;
        if (pa != null)
        {
            pa.visualModel = newBody.transform;
            pa.RefreshWeapon(0);
        }
    }

    // Gắn đúng vũ khí khởi đầu và căn chỉnh vừa khít lòng bàn tay cho nhân vật mặc định
    private static void AlignDefaultCharacterWeapon(GameObject playerRoot, CharacterProfile profile, PlayerAttack pa)
    {
        Animator anim = playerRoot.GetComponentInChildren<Animator>(true);
        Transform hand = WeaponShopManager.FindPlayerHandBone(playerRoot, out GameObject existingWeapon);
        if (hand == null)
        {
            hand = FindRightHandBone(playerRoot.transform, anim);
        }
        if (hand == null) return;

        GameObject startWeaponPrefab = ResolveStartingWeaponPrefab(profile);
        GameObject activeWeapon = existingWeapon;
        if (startWeaponPrefab != null)
        {
            string desiredName = WeaponUpgradeConfigSO.CleanWeaponName(startWeaponPrefab.name);
            string currentName = existingWeapon != null ? WeaponUpgradeConfigSO.CleanWeaponName(existingWeapon.name) : string.Empty;
            if (activeWeapon == null || !string.Equals(currentName, desiredName, System.StringComparison.OrdinalIgnoreCase))
            {
                if (existingWeapon != null)
                {
                    existingWeapon.transform.SetParent(null, false);
                    existingWeapon.SetActive(false);
                    Object.Destroy(existingWeapon);
                }

                activeWeapon = Object.Instantiate(startWeaponPrefab, hand);
                activeWeapon.name = startWeaponPrefab.name;
            }
        }

        if (activeWeapon != null)
        {
            WeaponGripAligner.AlignWeaponToHand(hand, activeWeapon.transform, anim);
            WeaponElementalVfx.ApplyToWeapon(activeWeapon);
        }

        if (pa != null)
        {
            pa.RefreshWeapon(0);
        }
    }

    // Ưu tiên lấy đúng prefab kiếm khởi đầu mặc định dùng chung cho toàn bộ nhân vật
    private static GameObject ResolveStartingWeaponPrefab(CharacterProfile profile)
    {
        CharacterRosterConfig roster = SelectedCharacterStore.GetRoster();
        CharacterProfile defaultHero = roster != null ? roster.GetProfile(0) : null;
        if (defaultHero != null && defaultHero.startingWeaponPrefab != null)
        {
            return defaultHero.startingWeaponPrefab;
        }

        if (profile != null && profile.startingWeaponPrefab != null)
        {
            return profile.startingWeaponPrefab;
        }

#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(DefaultStartingSwordPath);
#else
        return null;
#endif
    }

    // Tìm xương bàn tay phải của nhân vật theo Animator Humanoid hoặc tên xương Mixamo
    private static Transform FindRightHandBone(Transform root, Animator anim)
    {
        Transform rightHand = anim != null ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (rightHand != null) return rightHand;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "mixamorig:RightHand")
            {
                return t;
            }
        }
        return null;
    }
}
