
using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    public int maxHealth = 100;
    public int currentHealth;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (currentHealth <= 0)
            return;

        currentHealth = Mathf.Max(0, currentHealth - damage);

        UnityEngine.Debug.Log(
            "Player HP: " + currentHealth + "/" + maxHealth
        );

        if (currentHealth <= 0)
        {
            UnityEngine.Debug.Log("PLAYER DEAD!");

            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
