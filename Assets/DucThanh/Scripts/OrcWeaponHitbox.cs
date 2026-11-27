using UnityEngine;

// Xử lý vùng va chạm vũ khí của quái, tính giảm sát thương khi đỡ đòn và kích hoạt Perfect Parry
public class OrcWeaponHitbox : MonoBehaviour
{
    private OrcEnemyAI ownerAI;
    private Transform ownerTransform;
    private int damage = 10;
    private bool hitboxActive = false;
    private bool hasDealtDamageInCurrentSwing = false;

    // Gán chủ thể quái sở hữu hitbox và mức sát thương của đòn đánh
    public void Initialize(OrcEnemyAI owner, int attackDamage)
    {
        ownerAI = owner;
        ownerTransform = owner != null ? owner.transform : null;
        damage = attackDamage;
    }

    public void SetDamage(int attackDamage)
    {
        damage = attackDamage;
    }

    // Bật collider vũ khí trong pha chém chính để bắt va chạm
    public void EnableHitbox()
    {
        hitboxActive = true;
        hasDealtDamageInCurrentSwing = false;
    }

    // Tắt collider vũ khí khi kết thúc đòn đánh
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

    // Kiểm tra va chạm với Player để xét Perfect Parry, Block thường hoặc trừ máu trực tiếp
    private void CheckHit(Collider other)
    {
        if (!hitboxActive || hasDealtDamageInCurrentSwing || other == null || other is TerrainCollider) return;

        Transform selfRoot = ownerTransform != null ? ownerTransform : (ownerAI != null ? ownerAI.transform : null);
        if (selfRoot != null && (other.transform == selfRoot || other.transform.IsChildOf(selfRoot)))
        {
            return;
        }

        PlayerHealth playerHealth = PlayerHealth.Instance;
        if (playerHealth != null)
        {
            Transform pt = playerHealth.transform;
            if (other.transform != pt && !other.transform.IsChildOf(pt)) return;
        }
        else
        {
            playerHealth = other.GetComponent<PlayerHealth>();
            if (playerHealth == null)
            {
                playerHealth = other.GetComponentInParent<PlayerHealth>();
            }
        }

        if (playerHealth != null && playerHealth.currentHealth > 0 && !playerHealth.IsInvulnerable)
        {
            hasDealtDamageInCurrentSwing = true;

            Vector3 sourcePos = selfRoot != null ? selfRoot.position : transform.position;
            Vector3 hitPoint = playerHealth.transform.position + Vector3.up * 1.15f;
            PlayerController playerController = PlayerController.Instance != null && PlayerController.Instance.transform == playerHealth.transform
                ? PlayerController.Instance
                : playerHealth.GetComponent<PlayerController>();

            if (playerController != null && playerController.IsBlocking)
            {
                bool perfectParry = playerController.IsPerfectParryWindow;
                Vector3 guardDir = (sourcePos - playerHealth.transform.position).normalized;
                Vector3 guardPos = hitPoint + guardDir * 0.45f;

                CombatVfxManager.SpawnBlockSpark(guardPos, guardDir, perfectParry);
                GameAudioManager.PlayWeaponEquip();

                if (ThirdPersonCamera.Instance != null)
                {
                    ThirdPersonCamera.Instance.AddCameraShake(perfectParry ? 0.14f : 0.08f, 0.09f);
                }

                if (perfectParry)
                {

                    int healedHp = playerHealth.HealPercent(playerController.perfectParryHealPercent);

                    CombatVfxManager.SpawnFloatingText(guardPos + Vector3.up * 0.55f, "PARRY!", new Color(1f, 0.9f, 0.3f), 1.2f, 1.0f);
                    CombatVfxManager.SpawnHealEffect(playerHealth.transform, healedHp, true);

                    if (ownerAI != null)
                    {
                        ownerAI.ApplyParryStun(playerHealth.transform.position, 9.5f);
                        CombatVfxManager.SpawnFloatingText(ownerAI.transform.position + Vector3.up * 2.45f, "CHOÁNG!", new Color(1f, 0.92f, 0.35f), 1.05f, 1.2f);
                    }

                    if (TelemetryManager.Instance != null)
                    {
                        TelemetryManager.Instance.TrackPerfectParry(TelemetryManager.Instance.CurrentLevel);
                    }

                    if (StageManager.Instance != null)
                    {
                        string bannerText = healedHp > 0
                            ? $"ĐỠ ĐÒN HOÀN HẢO! (PARRY +{healedHp} HP)"
                            : "ĐỠ ĐÒN HOÀN HẢO! (PARRY)";
                        StageManager.Instance.ShowBanner(bannerText, new Color(1f, 0.86f, 0.25f), 1.4f);
                    }
                }
                else
                {

                    int reducedDamage = Mathf.Max(1, Mathf.RoundToInt(damage * (1f - playerController.blockDamageReduction)));
                    int blockedAmount = Mathf.Max(0, damage - reducedDamage);
                    playerHealth.TakeDamage(reducedDamage);

                    CombatVfxManager.SpawnFloatingText(guardPos + Vector3.up * 0.45f, $"ĐỠ -{reducedDamage}", new Color(0.55f, 0.92f, 1f), 0.92f, 0.85f);

                    float kbForce = (ownerAI != null ? ownerAI.playerKnockbackForce : 6.5f) * 0.25f;
                    playerController.ApplyKnockback(sourcePos, kbForce);

                    if (TelemetryManager.Instance != null)
                    {
                        TelemetryManager.Instance.TrackBlockHit(blockedAmount);
                    }
                }

                return;
            }

            playerHealth.TakeDamage(damage);
            CombatVfxManager.SpawnBloodHit(hitPoint, playerHealth.transform.position - sourcePos, damage >= 24);
            CombatVfxManager.SpawnFloatingText(hitPoint + Vector3.up * 0.75f, $"-{damage}", new Color(1f, 0.3f, 0.26f), damage >= 24 ? 1.05f : 0.9f, 0.85f);

            if (playerController != null)
            {
                float kbForce = ownerAI != null ? ownerAI.playerKnockbackForce : 6.5f;
                playerController.ApplyKnockback(sourcePos, kbForce);
            }
        }
    }
}
