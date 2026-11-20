using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Quản lý máu của Player, hiển thị thanh máu (Slider) + chữ HP mượt mà,
/// kích hoạt TelemetryManager và GameOverManager khi chết.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("Health Settings")]
    public int maxHealth = 100;
    public int currentHealth;

    [Header("UI")]
    public TMP_Text healthText;
    public Slider healthSlider;
    public Image healthFillImage;
    [SerializeField] private float barSmoothSpeed = 10f;

    private bool isDead = false;
    private float invulnerableUntil = 0f;

    public bool IsAlive => !isDead && currentHealth > 0;
    public bool IsInvulnerable => Time.time < invulnerableUntil;

    public void SetInvulnerable(float duration)
    {
        if (duration <= 0f) return;
        invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + duration);
    }

    private void Awake()
    {
        currentHealth = maxHealth;
        isDead = false;

        if (healthText == null)
        {
            GameObject obj = GameObject.Find("HealthText");
            if (obj != null) healthText = obj.GetComponent<TMP_Text>();
        }

        if (healthSlider == null)
        {
            GameObject sliderObj = GameObject.Find("HealthBar");
            if (sliderObj != null) healthSlider = sliderObj.GetComponent<Slider>();
        }

        if (healthSlider != null)
        {
            healthSlider.minValue = 0f;
            healthSlider.maxValue = maxHealth;
            healthSlider.value = currentHealth;

            if (healthFillImage == null && healthSlider.fillRect != null)
            {
                healthFillImage = healthSlider.fillRect.GetComponent<Image>();
            }
        }

        UpdateHealthUI(true);
    }

    private void Update()
    {
        if (healthSlider == null) return;

        if (!Mathf.Approximately(healthSlider.value, currentHealth))
        {
            healthSlider.value = Mathf.Lerp(healthSlider.value, currentHealth, Time.unscaledDeltaTime * barSmoothSpeed);
            if (Mathf.Abs(healthSlider.value - currentHealth) < 0.05f)
            {
                healthSlider.value = currentHealth;
            }
        }
    }

    public void TakeDamage(int damage)
    {
        if (isDead || currentHealth <= 0 || IsInvulnerable)
            return;

        currentHealth = Mathf.Max(0, currentHealth - damage);
        UpdateHealthUI(false);

        CombatVfxManager.TriggerPlayerDamageFlash(damage >= 24 ? 0.5f : 0.35f);
        GameAudioManager.PlayPlayerHurt();

        if (Camera.main != null)
        {
            ThirdPersonCamera cam = Camera.main.GetComponent<ThirdPersonCamera>();
            if (cam != null) cam.AddCameraShake(0.14f, 0.14f);
        }

        Debug.Log($"Player HP: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0 && !isDead)
        {
            Die();
        }
    }

    private void Die()
    {
        isDead = true;
        UpdateHealthUI(false);
        Debug.Log("PLAYER DEAD!");

        // Ghi nhận sự kiện telemetry khi player hy sinh
        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackPlayerDied();
        }

        // Kích hoạt panel Game Over
        if (GameOverManager.Instance != null)
        {
            GameOverManager.Instance.ShowGameOver();
        }
    }

    public void Heal(int amount)
    {
        if (isDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        UpdateHealthUI(false);
    }

    public void FullHeal()
    {
        if (isDead) return;
        currentHealth = maxHealth;
        UpdateHealthUI(false);
    }

    private void UpdateHealthUI(bool instantSlider)
    {
        float ratio = maxHealth > 0 ? (float)currentHealth / maxHealth : 0f;
        Color hpColor = ratio > 0.5f
            ? new Color(0.2f, 0.9f, 0.45f, 1f)
            : ratio > 0.25f
                ? new Color(1f, 0.8f, 0.2f, 1f)
                : new Color(1f, 0.28f, 0.28f, 1f);

        if (healthText != null)
        {
            healthText.text = $"HP: {currentHealth}/{maxHealth}";
            healthText.color = Color.white;
        }

        if (healthFillImage != null)
        {
            healthFillImage.color = hpColor;
        }

        if (healthSlider != null)
        {
            healthSlider.maxValue = maxHealth;
            if (instantSlider)
            {
                healthSlider.value = currentHealth;
            }
        }
    }
}
