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

        if (playerHealth != null && playerHealth.currentHealth > 0)
        {
            hasDealtDamageInCurrentSwing = true;
            playerHealth.TakeDamage(damage);
            Debug.Log($"[OrcWeaponHitbox] Vũ khí của {(ownerAI != null ? ownerAI.gameObject.name : gameObject.name)} chém TRÚNG Player! Gây {damage} damage.");
        }
    }
}
