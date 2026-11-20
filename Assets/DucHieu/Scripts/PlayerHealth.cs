
using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    public int maxHealth = 100;
    public int currentHealth;

    private bool isDead = false;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (isDead || currentHealth <= 0)
            return;

        currentHealth = Mathf.Max(0, currentHealth - damage);

        Debug.Log("Player HP: " + currentHealth + "/" + maxHealth);

        if (currentHealth <= 0)
            if (currentHealth <= 0)
            {
                Debug.Log("PLAYER DEAD!");

                if (GameOverManager.Instance != null)
                {
                    GameOverManager.Instance.ShowGameOver();
                }
            }
    }
}
