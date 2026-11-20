using UnityEngine;

public class OrcWeaponHitbox : MonoBehaviour
{
    private OrcEnemyAI ownerAI;
    private int damage = 10;
    private bool hitboxActive = false;
    private bool hasDealtDamageInCurrentSwing = false;

    public void Initialize(OrcEnemyAI owner, int attackDamage)
    {
        ownerAI = owner;
        damage = attackDamage;
    }

    public void SetDamage(int attackDamage)
    {
        damage = attackDamage;
    }

    public void EnableHitbox()
    {
        hitboxActive = true;
        hasDealtDamageInCurrentSwing = false;
    }

    public void DisableHitbox()
    {
        hitboxActive = false;
        hasDealtDamageInCurrentSwing = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        CheckHit(other);
    }

    private void OnTriggerStay(Collider other)
    {
        CheckHit(other);
    }

    private void CheckHit(Collider other)
    {
        if (!hitboxActive || hasDealtDamageInCurrentSwing) return;

        // Bỏ qua chính bản thân enemy hoặc các quái vật khác
        if (ownerAI != null && (other.transform == ownerAI.transform || other.transform.IsChildOf(ownerAI.transform)))
        {
            return;
        }

        // Kiểm tra xem có trúng Player hoặc PlayerHealth không
        PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth == null)
        {
            playerHealth = other.GetComponentInParent<PlayerHealth>();
        }

        if (playerHealth != null && playerHealth.currentHealth > 0 && !playerHealth.IsInvulnerable)
        {
            hasDealtDamageInCurrentSwing = true;

            Vector3 sourcePos = ownerAI != null ? ownerAI.transform.position : transform.position;
            Vector3 hitPoint = playerHealth.transform.position + Vector3.up * 1.15f;
            PlayerController playerController = playerHealth.GetComponent<PlayerController>();

            // Xử lý Đỡ Đòn (Block / Perfect Parry) khi Player đang giữ Chuột Phải
            if (playerController != null && playerController.IsBlocking)
            {
                bool perfectParry = playerController.IsPerfectParryWindow;
                Vector3 guardDir = (sourcePos - playerHealth.transform.position).normalized;
                Vector3 guardPos = hitPoint + guardDir * 0.45f;

                CombatVfxManager.SpawnBlockSpark(guardPos, guardDir, perfectParry);
                GameAudioManager.PlayWeaponEquip();

                if (Camera.main != null)
                {
                    ThirdPersonCamera cam = Camera.main.GetComponent<ThirdPersonCamera>();
                    if (cam != null) cam.AddCameraShake(perfectParry ? 0.14f : 0.08f, 0.09f);
                }

                if (perfectParry)
                {
                    // Perfect Parry: 0 sát thương + đẩy choáng ngược lại Orc!
                    if (ownerAI != null)
                    {
                        ownerAI.ApplyKnockback(playerHealth.transform.position, 9.5f, true);
                    }

                    if (TelemetryManager.Instance != null)
                    {
                        TelemetryManager.Instance.TrackPerfectParry(TelemetryManager.Instance.CurrentLevel);
                    }

                    if (StageManager.Instance != null)
                    {
                        StageManager.Instance.ShowBanner("ĐỠ ĐÒN HOÀN HẢO! (PARRY)", new Color(1f, 0.86f, 0.25f), 1.4f);
                    }

                    Debug.Log($"[OrcWeaponHitbox] Player PERFECT PARRY đòn đánh của {(ownerAI != null ? ownerAI.gameObject.name : gameObject.name)}!");
                }
                else
                {
                    // Block thường: giảm 80% sát thương & giảm mạnh lực đẩy lùi, không phun máu
                    int reducedDamage = Mathf.Max(1, Mathf.RoundToInt(damage * (1f - playerController.blockDamageReduction)));
                    int blockedAmount = Mathf.Max(0, damage - reducedDamage);
                    playerHealth.TakeDamage(reducedDamage);

                    float kbForce = (ownerAI != null ? ownerAI.playerKnockbackForce : 6.5f) * 0.25f;
                    playerController.ApplyKnockback(sourcePos, kbForce);

                    if (TelemetryManager.Instance != null)
                    {
                        TelemetryManager.Instance.TrackBlockHit(blockedAmount);
                    }

                    Debug.Log($"[OrcWeaponHitbox] Player ĐỠ ĐÒN! Chặn {blockedAmount} DMG, chỉ nhận {reducedDamage} DMG.");
                }

                return;
            }

            playerHealth.TakeDamage(damage);
            CombatVfxManager.SpawnBloodHit(hitPoint, playerHealth.transform.position - sourcePos, damage >= 24);

            if (playerController != null)
            {
                float kbForce = ownerAI != null ? ownerAI.playerKnockbackForce : 6.5f;
                playerController.ApplyKnockback(sourcePos, kbForce);
            }

            Debug.Log($"[OrcWeaponHitbox] Vũ khí của {(ownerAI != null ? ownerAI.gameObject.name : gameObject.name)} chém TRÚNG Player! Gây {damage} damage.");
        }
    }
}
