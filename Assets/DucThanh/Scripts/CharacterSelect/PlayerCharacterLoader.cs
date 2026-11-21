using UnityEngine;

/// <summary>
/// Tự động nạp nhân vật & chỉ số mà người chơi đã chọn từ CharacterSelectScene khi vào màn chơi Lv1:
/// - Áp dụng Max HP, Attack Damage, Move Speed, Knockback, Block/Parry cho Player.
/// - Nếu chọn nhân vật 2/3/4 (Maria, Vampire, Maw), tự động thay PlayerBody sang đúng model FBX,
///   gắn lại Player_AC.controller + Avatar Humanoid tương ứng và gắn vũ khí khởi đầu vào tay phải.
/// </summary>
public static class PlayerCharacterLoader
{
    public static void ApplySelectedCharacter(GameObject playerRoot)
    {
        if (playerRoot == null) return;

        CharacterProfile profile = SelectedCharacterStore.GetSelectedProfile();
        if (profile == null) return;

        // 1. Áp dụng chỉ số chiến đấu của nhân vật đã chọn
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

        PlayerAttack pa = playerRoot.GetComponent<PlayerAttack>();
        if (pa != null)
        {
            pa.attackDamage = profile.attackDamage;
            pa.knockbackForce = profile.knockbackForce;
        }

        // 2. Nếu là nhân vật mặc định (index 0 = Vanguard đang có sẵn trong Lv1) thì giữ nguyên model gốc
        int selectedIdx = SelectedCharacterStore.SelectedIndex;
        if (selectedIdx == 0 || profile.modelPrefab == null)
        {
            return;
        }

        // 3. Thay PlayerBody sang model nhân vật đã chọn (Maria / Vampire / Maw)
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

            // Tách oldBody khỏi Player trước khi tạo newBody để các Awake() chạy sau lấy đúng Animator mới
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

        // Gắn vũ khí vào tay phải của model mới
        Transform rightHand = newAnim.GetBoneTransform(HumanBodyBones.RightHand);
        if (rightHand == null)
        {
            foreach (Transform t in newBody.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "mixamorig:RightHand")
                {
                    rightHand = t;
                    break;
                }
            }
        }

        if (rightHand != null)
        {
            if (profile.startingWeaponPrefab != null)
            {
                GameObject weaponObj = Object.Instantiate(profile.startingWeaponPrefab, rightHand);
                weaponObj.name = profile.startingWeaponPrefab.name;
                weaponObj.transform.localPosition = savedWeaponPos;
                weaponObj.transform.localRotation = savedWeaponRot;
                weaponObj.transform.localScale = savedWeaponScale;

                if (oldWeaponRef != null) Object.Destroy(oldWeaponRef);
            }
            else if (oldWeaponRef != null)
            {
                oldWeaponRef.transform.SetParent(rightHand, false);
                oldWeaponRef.transform.localPosition = savedWeaponPos;
                oldWeaponRef.transform.localRotation = savedWeaponRot;
                oldWeaponRef.transform.localScale = savedWeaponScale;
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
}
