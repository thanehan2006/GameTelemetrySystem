using System;
using UnityEngine;

// Tự động kiểm tra vị trí các ngón tay và khung bao mesh để căn chỉnh chuôi vũ khí vào đúng lòng bàn tay cho mọi nhân vật
public static class WeaponGripAligner
{
    private const float ReferencePalmLength = 0.144f;

    // Đưa tay cầm vũ khí vào hõm lòng bàn tay giữa ngón cái và bốn ngón còn lại, đồng thời xoay lưỡi hướng ra ngoài
    public static bool AlignWeaponToHand(Transform handBone, Transform weaponTransform, Animator animator = null)
    {
        if (handBone == null || weaponTransform == null) return false;

        if (weaponTransform.parent != handBone)
        {
            weaponTransform.SetParent(handBone, false);
        }

        if (animator == null)
        {
            animator = handBone.GetComponentInParent<Animator>();
        }

        if (!ComputeHandGripBasis(
                handBone,
                animator,
                out Vector3 gripAnchorLocal,
                out Vector3 handForward,
                out Vector3 pinkyToIndex,
                out float palmLength))
        {
            return false;
        }

        Bounds localBounds = ComputeWeaponLocalBounds(weaponTransform);
        AnalyzeWeaponGripGeometry(
            weaponTransform.name,
            localBounds,
            out Vector3 gripPointLocal,
            out float edgeSign,
            out bool invertShaft);

        // Thanh cầm đặt nghiêng nhẹ 12 độ dọc theo chiều từ ngón út sang ngón trỏ để khớp dáng nắm tay tự nhiên
        const float tiltRad = 12f * Mathf.Deg2Rad;
        Vector3 shaftDir = (pinkyToIndex * Mathf.Cos(tiltRad) + handForward * Mathf.Sin(tiltRad)).normalized;
        Vector3 edgeDir = Vector3.ProjectOnPlane(handForward, shaftDir);
        if (edgeDir.sqrMagnitude < 0.0001f)
        {
            edgeDir = handForward;
        }
        edgeDir.Normalize();

        Vector3 weaponYTarget = invertShaft ? -shaftDir : shaftDir;
        Vector3 weaponXTarget = edgeDir * edgeSign;
        Vector3 weaponZTarget = Vector3.Cross(weaponXTarget, weaponYTarget);
        if (weaponZTarget.sqrMagnitude < 0.0001f)
        {
            weaponZTarget = Vector3.forward;
        }
        weaponZTarget.Normalize();

        Quaternion gripRotation = Quaternion.LookRotation(weaponZTarget, weaponYTarget);

        Vector3 parentLossy = handBone.lossyScale;
        float handWorldScale = (Mathf.Abs(parentLossy.x) + Mathf.Abs(parentLossy.y) + Mathf.Abs(parentLossy.z)) / 3f;
        if (handWorldScale < 0.001f) handWorldScale = 1f;

        float desiredWorldScale = Mathf.Clamp((palmLength * handWorldScale) / ReferencePalmLength, 0.94f, 1.24f);
        float finalLocalScale = desiredWorldScale / handWorldScale;
        Vector3 localScaleVec = new Vector3(finalLocalScale, finalLocalScale, finalLocalScale);

        weaponTransform.localRotation = gripRotation;
        weaponTransform.localScale = localScaleVec;
        weaponTransform.localPosition = gripAnchorLocal - (gripRotation * Vector3.Scale(gripPointLocal, localScaleVec));

        if (!weaponTransform.gameObject.activeSelf)
        {
            weaponTransform.gameObject.SetActive(true);
        }

        return true;
    }

    // Kiểm tra tọa độ các khớp ngón tay (cái, trỏ, giữa, áp út, út) để xác định tâm nắm tay và hướng lòng bàn tay
    private static bool ComputeHandGripBasis(
        Transform handBone,
        Animator animator,
        out Vector3 gripAnchorLocal,
        out Vector3 handForward,
        out Vector3 pinkyToIndex,
        out float palmLength)
    {
        bool isLeftHand = handBone.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0;

        Transform thumbT = FindFingerTransform(handBone, animator, isLeftHand, FingerSlot.Thumb);
        Transform indexT = FindFingerTransform(handBone, animator, isLeftHand, FingerSlot.Index);
        Transform middleT = FindFingerTransform(handBone, animator, isLeftHand, FingerSlot.Middle);
        Transform ringT = FindFingerTransform(handBone, animator, isLeftHand, FingerSlot.Ring);
        Transform pinkyT = FindFingerTransform(handBone, animator, isLeftHand, FingerSlot.Pinky);

        bool hasThumb = TryGetLocalPosInHand(handBone, thumbT, out Vector3 thumbPos);
        bool hasIndex = TryGetLocalPosInHand(handBone, indexT, out Vector3 indexPos);
        bool hasMiddle = TryGetLocalPosInHand(handBone, middleT, out Vector3 middlePos);
        bool hasRing = TryGetLocalPosInHand(handBone, ringT, out Vector3 ringPos);
        bool hasPinky = TryGetLocalPosInHand(handBone, pinkyT, out Vector3 pinkyPos);

        Vector3 knuckleSum = Vector3.zero;
        int knuckleCount = 0;
        if (hasIndex) { knuckleSum += indexPos; knuckleCount++; }
        if (hasMiddle) { knuckleSum += middlePos; knuckleCount++; }
        if (hasRing) { knuckleSum += ringPos; knuckleCount++; }
        if (hasPinky) { knuckleSum += pinkyPos; knuckleCount++; }

        Vector3 knuckleCenter = knuckleCount > 0 ? knuckleSum / knuckleCount : new Vector3(0f, ReferencePalmLength, 0f);
        Vector3 forwardRef = hasMiddle ? middlePos : knuckleCenter;

        palmLength = forwardRef.magnitude;
        if (palmLength < 0.02f)
        {
            palmLength = ReferencePalmLength;
            handForward = Vector3.up;
        }
        else
        {
            handForward = forwardRef / palmLength;
        }

        Vector3 acrossRaw = Vector3.zero;
        if (hasIndex && hasPinky)
        {
            acrossRaw = indexPos - pinkyPos;
        }
        else if (hasIndex && hasRing)
        {
            acrossRaw = indexPos - ringPos;
        }
        else if (hasIndex && hasMiddle)
        {
            acrossRaw = indexPos - middlePos;
        }
        else if (hasMiddle && hasPinky)
        {
            acrossRaw = middlePos - pinkyPos;
        }
        else if (hasThumb)
        {
            acrossRaw = thumbPos;
        }
        else
        {
            acrossRaw = isLeftHand ? Vector3.right : Vector3.left;
        }

        pinkyToIndex = Vector3.ProjectOnPlane(acrossRaw, handForward);
        if (pinkyToIndex.sqrMagnitude < 0.00001f)
        {
            pinkyToIndex = Vector3.ProjectOnPlane(isLeftHand ? Vector3.right : Vector3.left, handForward);
        }
        pinkyToIndex.Normalize();

        Vector3 palmInward = Vector3.Cross(handForward, pinkyToIndex);
        if (palmInward.sqrMagnitude < 0.00001f)
        {
            palmInward = Vector3.forward;
        }
        palmInward.Normalize();

        if (hasThumb && Vector3.Dot(palmInward, thumbPos) < 0f)
        {
            palmInward = -palmInward;
        }

        // Đặt tâm nắm tay tại nếp gấp lòng bàn tay (80% từ cổ tay tới khớp ngón) và thụt vào hõm lòng bàn tay
        float knuckleForwardDist = knuckleCount > 0
            ? Mathf.Max(0.05f, Vector3.Dot(knuckleCenter, handForward))
            : palmLength;
        float acrossCenter = knuckleCount > 0 ? Vector3.Dot(knuckleCenter, pinkyToIndex) * 0.35f : 0f;
        float knuckleDepth = knuckleCount > 0 ? Mathf.Max(0f, Vector3.Dot(knuckleCenter, palmInward)) : 0f;

        gripAnchorLocal =
            handForward * (knuckleForwardDist * 0.80f) +
            pinkyToIndex * acrossCenter +
            palmInward * (knuckleDepth + palmLength * 0.21f);

        return true;
    }

    private enum FingerSlot
    {
        Thumb,
        Index,
        Middle,
        Ring,
        Pinky
    }

    private static Transform FindFingerTransform(Transform handBone, Animator animator, bool isLeftHand, FingerSlot slot)
    {
        if (animator != null && animator.isHuman)
        {
            HumanBodyBones bone = slot switch
            {
                FingerSlot.Thumb => isLeftHand ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal,
                FingerSlot.Index => isLeftHand ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal,
                FingerSlot.Middle => isLeftHand ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal,
                FingerSlot.Ring => isLeftHand ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal,
                _ => isLeftHand ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal
            };

            Transform mapped = animator.GetBoneTransform(bone);
            if (mapped != null && (mapped == handBone || mapped.IsChildOf(handBone)))
            {
                return mapped;
            }
        }

        string sideToken = isLeftHand ? "LeftHand" : "RightHand";
        string slotToken = slot switch
        {
            FingerSlot.Thumb => "Thumb1",
            FingerSlot.Index => "Index1",
            FingerSlot.Middle => "Middle1",
            FingerSlot.Ring => "Ring1",
            _ => "Pinky1"
        };

        Transform[] children = handBone.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform t = children[i];
            if (t == null || t == handBone) continue;
            if (t.name.IndexOf(sideToken + slotToken, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return t;
            }
        }

        string shortToken = slot switch
        {
            FingerSlot.Thumb => "Thumb",
            FingerSlot.Index => "Index",
            FingerSlot.Middle => "Middle",
            FingerSlot.Ring => "Ring",
            _ => "Pinky"
        };
        for (int i = 0; i < children.Length; i++)
        {
            Transform t = children[i];
            if (t == null || t == handBone) continue;
            if (t.name.IndexOf(shortToken, StringComparison.OrdinalIgnoreCase) >= 0 && t.name.IndexOf("1", StringComparison.Ordinal) >= 0)
            {
                return t;
            }
        }

        return null;
    }

    private static bool TryGetLocalPosInHand(Transform handBone, Transform fingerBone, out Vector3 localPos)
    {
        localPos = Vector3.zero;
        if (fingerBone == null || handBone == null) return false;

        if (fingerBone.parent == handBone)
        {
            localPos = fingerBone.localPosition;
            return true;
        }

        localPos = handBone.InverseTransformPoint(fingerBone.position);
        return true;
    }

    // Tổng hợp khung bao cục bộ của toàn bộ mesh vũ khí trong hệ quy chiếu của object gốc vũ khí
    private static Bounds ComputeWeaponLocalBounds(Transform weaponRoot)
    {
        bool hasBounds = false;
        Bounds result = new Bounds(Vector3.zero, new Vector3(0.08f, 1.0f, 0.08f));

        MeshFilter[] filters = weaponRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter mf = filters[i];
            if (mf == null || mf.sharedMesh == null) continue;
            EncapsulateTransformedBounds(weaponRoot, mf.transform, mf.sharedMesh.bounds, ref result, ref hasBounds);
        }

        SkinnedMeshRenderer[] skinned = weaponRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            SkinnedMeshRenderer smr = skinned[i];
            if (smr == null || smr.sharedMesh == null) continue;
            EncapsulateTransformedBounds(weaponRoot, smr.transform, smr.sharedMesh.bounds, ref result, ref hasBounds);
        }

        return result;
    }

    private static void EncapsulateTransformedBounds(
        Transform root,
        Transform leaf,
        Bounds leafLocalBounds,
        ref Bounds target,
        ref bool hasBounds)
    {
        Matrix4x4 rel = Matrix4x4.identity;
        Transform cur = leaf;
        while (cur != null && cur != root)
        {
            rel = Matrix4x4.TRS(cur.localPosition, cur.localRotation, cur.localScale) * rel;
            cur = cur.parent;
        }

        Vector3 c = leafLocalBounds.center;
        Vector3 e = leafLocalBounds.extents;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 corner = rel.MultiplyPoint3x4(new Vector3(c.x + sx * e.x, c.y + sy * e.y, c.z + sz * e.z));
                    if (!hasBounds)
                    {
                        target = new Bounds(corner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        target.Encapsulate(corner);
                    }
                }
            }
        }
    }

    // Xác định điểm đặt tay dọc theo cán từng dòng vũ khí và chiều hướng lưỡi vũ khí
    private static void AnalyzeWeaponGripGeometry(
        string weaponName,
        Bounds localBounds,
        out Vector3 gripPointLocal,
        out float edgeSign,
        out bool invertShaft)
    {
        float minY = localBounds.min.y;
        float maxY = localBounds.max.y;
        float spanY = Mathf.Max(0.1f, maxY - minY);

        string lower = string.IsNullOrEmpty(weaponName) ? string.Empty : weaponName.ToLowerInvariant();
        invertShaft = lower.Contains("dagger") || (maxY > 0.01f && Mathf.Abs(minY) > Mathf.Abs(maxY) * 1.6f);

        float gripU;
        if (invertShaft)
        {
            gripU = 0.84f;
        }
        else if (lower.Contains("scythe"))
        {
            gripU = 0.36f;
        }
        else if (lower.Contains("polearm") || lower.Contains("spear") || lower.Contains("halberd") || spanY > 1.45f)
        {
            gripU = 0.33f;
        }
        else if (lower.Contains("hammer") || lower.Contains("mace") || lower.Contains("axe"))
        {
            gripU = 0.28f;
        }
        else
        {
            gripU = 0.155f;
        }

        float gripY = Mathf.Lerp(minY, maxY, gripU);
        gripPointLocal = new Vector3(0f, gripY, 0f);
        if (lower.Contains("scythe") || lower.Contains("sword3_1"))
        {
            edgeSign = 1f;
        }
        else if (lower.Contains("polearm"))
        {
            edgeSign = -1f;
        }
        else
        {
            edgeSign = localBounds.center.x < -0.025f ? 1f : -1f;
        }
    }
}
