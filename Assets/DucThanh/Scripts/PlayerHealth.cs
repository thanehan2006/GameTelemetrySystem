using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Quản lý máu, thời gian bất tử tạm thời, hồi máu và cập nhật thanh HP của người chơi
public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }

    [Header("Health Settings")]
    public int maxHealth = 190;
    public int currentHealth;
    [SerializeField] private float gameOverScreenDelay = 1.6f;

    [Header("UI")]
    public TMP_Text healthText;
    public Slider healthSlider;
    public Image healthFillImage;
    [SerializeField] private float barSmoothSpeed = 10f;

    private Image ghostFillImage;
    private Image crestGemImage;
    private float targetHpRatio = 1f;
    private float ghostHpRatio = 1f;
    private float ghostHoldTimer = 0f;
    private float hpFlashTimer = 0f;
    private Color hpFlashColor = new Color(1f, 0.36f, 0.30f, 1f);
    private Color currentTargetFillColor = CombatHudStyler.HpEmeraldHealthy;
    private Color lastAppliedFillColor = Color.clear;
    private float lowHpPulseTimer;

    private bool isDead = false;
    private float invulnerableUntil = 0f;

    public bool IsAlive => !isDead && currentHealth > 0;
    public bool IsInvulnerable => Time.time < invulnerableUntil;
    public static bool IsPlayerDead => Instance != null && Instance.isDead;

    // Kích hoạt trạng thái bất tử ngắn sau khi parry hoặc qua cửa
    public void SetInvulnerable(float duration)
    {
        if (duration <= 0f) return;
        invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + duration);
    }

    // Tự động tìm thanh HP trên HUD và hiển thị chỉ số máu ban đầu
    private void Awake()
    {
        Instance = this;
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

            CombatHudStyler.StylePlayerHealthBar(
                healthSlider,
                healthText,
                out Image styledFill,
                out ghostFillImage,
                out _,
                out crestGemImage);
            if (styledFill != null) healthFillImage = styledFill;
        }

        UpdateHealthUI(true);
    }

    // Cập nhật nội suy thanh máu, thanh sát thương trễ và hiệu ứng cảnh báo khi máu thấp
    private void Update()
    {
        if (healthSlider == null) return;

        bool sliderAtTarget = Mathf.Approximately(healthSlider.value, currentHealth);
        bool ghostAtTarget = ghostFillImage == null || Mathf.Approximately(ghostHpRatio, targetHpRatio);
        bool lowHpPulsing = !isDead && targetHpRatio > 0f && targetHpRatio <= 0.28f;

        if (sliderAtTarget && ghostAtTarget && hpFlashTimer <= 0f && !lowHpPulsing)
        {
            if (healthFillImage != null && lastAppliedFillColor != currentTargetFillColor)
            {
                lastAppliedFillColor = currentTargetFillColor;
                healthFillImage.color = currentTargetFillColor;
                if (crestGemImage != null) crestGemImage.color = currentTargetFillColor;
            }
            return;
        }

        float dt = Time.unscaledDeltaTime;

        if (!sliderAtTarget)
        {
            healthSlider.value = Mathf.Lerp(healthSlider.value, currentHealth, dt * barSmoothSpeed);
            if (Mathf.Abs(healthSlider.value - currentHealth) < 0.05f)
            {
                healthSlider.value = currentHealth;
            }
        }

        if (ghostFillImage != null)
        {
            if (ghostHpRatio > targetHpRatio)
            {
                if (ghostHoldTimer > 0f)
                {
                    ghostHoldTimer -= dt;
                }
                else
                {
                    ghostHpRatio = Mathf.MoveTowards(ghostHpRatio, targetHpRatio, dt * 0.55f);
                    CombatHudStyler.SetGhostFillRatio(ghostFillImage, ghostHpRatio);
                }
            }
            else if (ghostHpRatio < targetHpRatio)
            {
                ghostHpRatio = targetHpRatio;
                CombatHudStyler.SetGhostFillRatio(ghostFillImage, ghostHpRatio);
            }
        }

        if (healthFillImage != null)
        {
            if (hpFlashTimer > 0f)
            {
                hpFlashTimer = Mathf.Max(0f, hpFlashTimer - dt);
                Color flashCol = Color.Lerp(currentTargetFillColor, hpFlashColor, Mathf.Clamp01(hpFlashTimer / 0.16f));
                if (flashCol != lastAppliedFillColor)
                {
                    lastAppliedFillColor = flashCol;
                    healthFillImage.color = flashCol;
                    if (crestGemImage != null) crestGemImage.color = flashCol;
                }
            }
            else if (lowHpPulsing)
            {
                lowHpPulseTimer -= dt;
                if (lowHpPulseTimer <= 0f)
                {
                    lowHpPulseTimer = 0.05f;
                    float pulse = 0.84f + 0.16f * Mathf.Sin(Time.unscaledTime * 7f);
                    Color pulseCol = new Color(
                        Mathf.Clamp01(currentTargetFillColor.r * pulse),
                        Mathf.Clamp01(currentTargetFillColor.g * pulse),
                        Mathf.Clamp01(currentTargetFillColor.b * pulse),
                        1f);
                    if (pulseCol != lastAppliedFillColor)
                    {
                        lastAppliedFillColor = pulseCol;
                        healthFillImage.color = pulseCol;
                        if (crestGemImage != null) crestGemImage.color = pulseCol;
                    }
                }
            }
            else if (lastAppliedFillColor != currentTargetFillColor)
            {
                lastAppliedFillColor = currentTargetFillColor;
                healthFillImage.color = currentTargetFillColor;
                if (crestGemImage != null) crestGemImage.color = currentTargetFillColor;
            }
        }
    }

    // Trừ máu khi trúng đòn, phát flash màn hình và xử lý khi tử trận
    public void TakeDamage(int damage)
    {
        if (isDead || currentHealth <= 0 || IsInvulnerable)
            return;

        currentHealth = Mathf.Max(0, currentHealth - damage);
        UpdateHealthUI(false);

        CombatVfxManager.TriggerPlayerDamageFlash(damage >= 24 ? 0.5f : 0.35f);
        GameAudioManager.PlayPlayerHurt();

        if (ThirdPersonCamera.Instance != null)
        {
            ThirdPersonCamera.Instance.AddCameraShake(0.14f, 0.14f);
        }

        if (currentHealth <= 0 && !isDead)
        {
            Die();
        }
    }

    // Khóa điều khiển, gửi sự kiện telemetry và hiển thị màn hình thất bại
    private void Die()
    {
        isDead = true;
        UpdateHealthUI(false);

        if (TelemetryManager.Instance != null)
        {
            TelemetryManager.Instance.TrackPlayerDied();
        }

        if (GameOverManager.Instance != null)
        {
            StartCoroutine(ShowGameOverDelayed());
        }
    }

    private IEnumerator ShowGameOverDelayed()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, gameOverScreenDelay));
        if (!GameOverManager.IsGameOver && GameOverManager.Instance != null)
        {
            GameOverManager.Instance.ShowGameOver();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Hồi một lượng máu cố định và bật hiệu ứng hồi phục
    public void Heal(int amount)
    {
        if (isDead || amount <= 0) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        UpdateHealthUI(false);
    }

    // Hồi máu theo phần trăm máu tối đa (dùng cho parry và checkpoint)
    public int HealPercent(float percent)
    {
        if (isDead || percent <= 0f || maxHealth <= 0) return 0;
        int healAmount = Mathf.Max(1, Mathf.RoundToInt(maxHealth * percent));
        int healthBefore = currentHealth;
        Heal(healAmount);
        return currentHealth - healthBefore;
    }

    // Hồi đầy 100% máu cho nhân vật
    public void FullHeal()
    {
        if (isDead) return;
        currentHealth = maxHealth;
        UpdateHealthUI(false);
    }

    // Đồng bộ giá trị số HP, màu sắc và thanh sát thương trễ lên giao diện
    private void UpdateHealthUI(bool instantSlider)
    {
        float ratio = maxHealth > 0 ? Mathf.Clamp01((float)currentHealth / maxHealth) : 0f;
        float prevTargetRatio = targetHpRatio;
        targetHpRatio = ratio;

        currentTargetFillColor = ratio > 0.50f
            ? CombatHudStyler.HpEmeraldHealthy
            : ratio > 0.25f
                ? CombatHudStyler.HpAmberWarning
                : CombatHudStyler.HpScarletCritical;

        if (healthText != null)
        {
            healthText.text = $"{currentHealth} / {maxHealth}";
        }

        lastAppliedFillColor = currentTargetFillColor;
        if (healthFillImage != null)
        {
            healthFillImage.color = currentTargetFillColor;
        }
        if (crestGemImage != null)
        {
            crestGemImage.color = currentTargetFillColor;
        }

        if (healthSlider != null)
        {
            healthSlider.maxValue = maxHealth;
            if (instantSlider)
            {
                healthSlider.value = currentHealth;
            }
        }

        if (instantSlider || ratio >= prevTargetRatio)
        {
            ghostHpRatio = ratio;
            CombatHudStyler.SetGhostFillRatio(ghostFillImage, ghostHpRatio);
            if (!instantSlider && ratio > prevTargetRatio)
            {
                hpFlashColor = new Color(0.74f, 1f, 0.86f, 1f);
                hpFlashTimer = 0.16f;
            }
        }
        else if (ratio < prevTargetRatio)
        {
            ghostHoldTimer = 0.22f;
            hpFlashColor = new Color(1f, 0.36f, 0.30f, 1f);
            hpFlashTimer = 0.16f;
        }
    }
}
