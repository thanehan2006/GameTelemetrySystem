
using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    public int maxHealth = 100;
    public int scoreValue = 100;

    private int currentHealth;
    private bool isDead = false;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (isDead) return;

        currentHealth -= damage;

        Debug.Log("Enemy HP: " + Mathf.Max(0, currentHealth));

        if (currentHealth <= 0)
        {
            isDead = true;

            if (ScoreManager.Instance != null)
            {
                ScoreManager.Instance.AddScore(scoreValue);
            }
            else
            {
                Debug.LogWarning("Chua co ScoreManager trong Scene!");
            }

            Destroy(gameObject);
        }
    }
}
