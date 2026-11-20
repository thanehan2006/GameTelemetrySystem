using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quản lý hiệu ứng chiến đấu (Combat VFX) chuẩn URP không cần gán prefab thủ công:
/// - Phun máu (Blood Splatter + Blood Mist + Tia chém) khi Player hoặc Enemy đánh trúng nhau.
/// - Hiệu ứng chớp đỏ viền màn hình khi Player dính sát thương.
/// - Hiệu ứng hạ gục Enemy (nổ máu, khói linh hồn đen, hạt EXP vàng bay lên trời, sóng xung kích cho Boss).
/// </summary>
public class CombatVfxManager : MonoBehaviour
{
    private static CombatVfxManager instance;

    private Material softParticleMat;
    private Material dropletParticleMat;
    private Material ringParticleMat;
    private Material slashArcMat;
    private Mesh horizontalQuadMesh;

    private Image damageOverlayImage;
    private float damageFlashAlpha;

    public static CombatVfxManager EnsureInstance()
    {
        if (instance != null) return instance;

        instance = FindFirstObjectByType<CombatVfxManager>();
        if (instance == null)
        {
            GameObject go = new GameObject("CombatVfxManager");
            instance = go.AddComponent<CombatVfxManager>();
        }

        instance.EnsureMaterials();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        EnsureMaterials();
    }

    private void Update()
    {
        if (damageOverlayImage != null && damageFlashAlpha > 0.001f)
        {
            damageFlashAlpha = Mathf.MoveTowards(damageFlashAlpha, 0f, Time.unscaledDeltaTime * 1.45f);
            Color c = damageOverlayImage.color;
            c.a = damageFlashAlpha;
            damageOverlayImage.color = c;

            if (damageFlashAlpha <= 0.001f)
            {
                damageOverlayImage.enabled = false;
            }
        }
    }

    // ==================== PUBLIC API ====================

    /// <summary>
    /// Hiệu ứng máu bắn tung tóe + sương máu + tia va chạm khi chém trúng mục tiêu.
    /// </summary>
    public static void SpawnBloodHit(Vector3 position, Vector3 hitDirection, bool heavy = false)
    {
        CombatVfxManager mgr = EnsureInstance();
        mgr.SpawnBloodHitInternal(position, hitDirection, heavy);
    }

    /// <summary>
    /// Hiệu ứng khi Enemy hoặc Boss bị tiêu diệt: nổ máu + khói linh hồn + tia vàng bay lên + vòng xung kích.
    /// </summary>
    public static void SpawnEnemyDeathEffect(Vector3 position, bool isBoss = false)
    {
        CombatVfxManager mgr = EnsureInstance();
        mgr.SpawnEnemyDeathInternal(position, isBoss);
    }

    /// <summary>
    /// Hiệu ứng hạt linh hồn cuối cùng khi xác Enemy tan biến.
    /// </summary>
    public static void SpawnDissolvePuff(Vector3 position, bool isBoss = false)
    {
        CombatVfxManager mgr = EnsureInstance();
        mgr.SpawnDissolvePuffInternal(position, isBoss);
    }

    /// <summary>
    /// Chớp đỏ viền màn hình khi Player bị đánh trúng.
    /// </summary>
    public static void TriggerPlayerDamageFlash(float intensity = 0.38f)
    {
        CombatVfxManager mgr = EnsureInstance();
        mgr.TriggerDamageFlashInternal(intensity);
    }

    /// <summary>
    /// Vệt chém ánh sáng riêng theo từng chiêu thức tấn công:
    /// 0 = Atk1 (Chém chéo vàng-bạc), 1 = Atk2 (Chém ngang rộng lửa vàng), 2 = Atk3 (Xoay 360° tầm thấp + bụi đất).
    /// </summary>
    public static void SpawnSlashArc(Vector3 playerPos, Vector3 forwardDir, int attackVariant)
    {
        CombatVfxManager mgr = EnsureInstance();
        mgr.StartCoroutine(mgr.SpawnSlashArcCoroutine(playerPos, forwardDir, attackVariant));
    }

    /// <summary>
    /// Hiệu ứng luồng gió & bụi chân khi Player Né đòn (Dash).
    /// </summary>
    public static void SpawnDashEffect(Vector3 playerPos, Vector3 dashDir)
    {
        CombatVfxManager mgr = EnsureInstance();
        mgr.SpawnDashEffectInternal(playerPos, dashDir);
    }

    /// <summary>
    /// Hiệu ứng tia lửa kim loại & vòng sáng bảo vệ khi Player Đỡ Đòn (Block / Perfect Parry bằng Chuột Phải).
    /// </summary>
    public static void SpawnBlockSpark(Vector3 position, Vector3 normalDir, bool perfectParry = false)
    {
        CombatVfxManager mgr = EnsureInstance();
        mgr.SpawnBlockSparkInternal(position, normalDir, perfectParry);
    }

    public static Material GetWeaponTrailMaterial()
    {
        return EnsureInstance().softParticleMat;
    }

    // ==================== BLOOD HIT VFX ====================

    private void SpawnBloodHitInternal(Vector3 position, Vector3 hitDirection, bool heavy)
    {
        if (hitDirection.sqrMagnitude < 0.001f)
        {
            hitDirection = Vector3.up;
        }
        else
        {
            hitDirection = (hitDirection.normalized + Vector3.up * 0.35f).normalized;
        }

        GameObject root = new GameObject("VFX_BloodHit");
        root.transform.position = position;
        root.transform.rotation = Quaternion.LookRotation(hitDirection);

        // 1. Giọt máu bắn mạnh theo hướng chém (có trọng lực rơi xuống đất)
        {
            GameObject go = new GameObject("BloodDroplets");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.6f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, heavy ? 0.65f : 0.48f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(heavy ? 4.2f : 3.0f, heavy ? 8.5f : 6.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, heavy ? 0.24f : 0.17f);
            main.gravityModifier = 1.65f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 0.02f, 0.03f, 1f),
                new Color(0.92f, 0.06f, 0.06f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, heavy ? 28 : 18) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = heavy ? 42f : 34f;
            shape.radius = 0.1f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));

            var colOverLifetime = ps.colorOverLifetime;
            colOverLifetime.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.85f, 0.05f, 0.05f), 0f), new GradientColorKey(new Color(0.32f, 0.01f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.65f), new GradientAlphaKey(0f, 1f) });
            colOverLifetime.color = grad;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = dropletParticleMat;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 1.6f;

            ps.Play();
        }

        // 2. Sương máu đậm bùng nhẹ tại điểm chém
        {
            GameObject go = new GameObject("BloodMist");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.45f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, heavy ? 0.8f : 0.58f);
            main.gravityModifier = 0.25f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.62f, 0.03f, 0.04f, 0.72f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, heavy ? 9 : 6) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.18f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.45f));

            var colOverLifetime = ps.colorOverLifetime;
            colOverLifetime.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.65f, 0.03f, 0.04f), 0f), new GradientColorKey(new Color(0.25f, 0.01f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
            colOverLifetime.color = grad;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = softParticleMat;

            ps.Play();
        }

        // 3. Tia sáng va chạm sắc nét (Slash Spark)
        {
            GameObject go = new GameObject("ImpactSparks");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.25f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5.5f, 10.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.gravityModifier = 0.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.92f, 0.65f, 1f),
                new Color(1f, 0.48f, 0.18f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, heavy ? 12 : 7) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.08f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = dropletParticleMat;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.08f;
            renderer.lengthScale = 2.0f;

            ps.Play();
        }

        Destroy(root, 1.2f);
    }

    // ==================== ENEMY / BOSS DEATH VFX ====================

    private void SpawnEnemyDeathInternal(Vector3 position, bool isBoss)
    {
        float scaleMul = isBoss ? 1.85f : 1.0f;
        GameObject root = new GameObject(isBoss ? "VFX_BossDeath" : "VFX_EnemyDeath");
        root.transform.position = position;

        // 1. Vụ nổ máu đậm khi hạ gục
        {
            GameObject go = new GameObject("DeathBloodBurst");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.9f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, isBoss ? 0.95f : 0.68f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.8f * scaleMul, 7.2f * scaleMul);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f * scaleMul, 0.28f * scaleMul);
            main.gravityModifier = 1.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 0.02f, 0.03f, 1f),
                new Color(0.95f, 0.08f, 0.06f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, isBoss ? 55 : 28) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f * scaleMul;

            var colOverLifetime = ps.colorOverLifetime;
            colOverLifetime.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.85f, 0.05f, 0.05f), 0f), new GradientColorKey(new Color(0.28f, 0.01f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colOverLifetime.color = grad;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = dropletParticleMat;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.05f;
            renderer.lengthScale = 1.5f;

            ps.Play();
        }

        // 2. Khói linh hồn đen tan rã (Action AAA Soul Dissolve)
        {
            GameObject go = new GameObject("SoulSmoke");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1.4f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.75f, isBoss ? 1.6f : 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.0f * scaleMul);
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f * scaleMul, 1.4f * scaleMul);
            main.gravityModifier = -0.22f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = isBoss
                ? new Color(0.32f, 0.04f, 0.06f, 0.72f)
                : new Color(0.14f, 0.07f, 0.09f, 0.68f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, isBoss ? 24 : 14) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.45f * scaleMul;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.55f, 1f, 1.75f));

            var colOverLifetime = ps.colorOverLifetime;
            colOverLifetime.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(0.25f, 0.05f, 0.07f), 0f), new GradientColorKey(new Color(0.06f, 0.04f, 0.06f), 1f) },
                new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
            colOverLifetime.color = grad;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = softParticleMat;

            ps.Play();
        }

        // 3. Hạt ánh sáng linh hồn / EXP vàng óng bay lên trời
        {
            GameObject go = new GameObject("SoulEmbers");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1.6f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.85f, isBoss ? 2.1f : 1.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 3.8f * scaleMul);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f * scaleMul, 0.24f * scaleMul);
            main.gravityModifier = -0.55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = isBoss
                ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.25f, 1f), new Color(1f, 0.35f, 0.15f, 1f))
                : new ParticleSystem.MinMaxGradient(new Color(1f, 0.86f, 0.35f, 1f), new Color(1f, 0.55f, 0.18f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.05f, isBoss ? 50 : 22) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 38f;
            shape.radius = 0.5f * scaleMul;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.7f;
            noise.frequency = 1.5f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            var colOverLifetime = ps.colorOverLifetime;
            colOverLifetime.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.92f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.12f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colOverLifetime.color = grad;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = softParticleMat;

            ps.Play();
        }

        // 4. Vòng sóng xung kích lan rộng trên mặt đất
        {
            GameObject go = new GameObject("DeathShockwaveRing");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, -0.7f, 0f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.6f;
            main.loop = false;
            main.startLifetime = isBoss ? 0.75f : 0.45f;
            main.startSpeed = 0f;
            main.startSize = isBoss ? 7.5f : 3.2f;
            main.startColor = isBoss
                ? new Color(1f, 0.35f, 0.15f, 0.9f)
                : new Color(0.95f, 0.25f, 0.18f, 0.75f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.12f, 1f, 1f));

            var colOverLifetime = ps.colorOverLifetime;
            colOverLifetime.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.65f, 0.25f), 0f), new GradientColorKey(new Color(0.85f, 0.1f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            colOverLifetime.color = grad;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = ringParticleMat;
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = CreateHorizontalQuadMesh();

            ps.Play();
        }

        // 5. Ánh sáng chớp bùng ngắn tại điểm hạ gục
        StartCoroutine(FlashDeathLight(position, isBoss));

        if (isBoss && Camera.main != null)
        {
            ThirdPersonCamera cam = Camera.main.GetComponent<ThirdPersonCamera>();
            if (cam != null) cam.AddCameraShake(0.35f, 0.45f);
        }

        Destroy(root, isBoss ? 3.2f : 2.2f);
    }

    private void SpawnDissolvePuffInternal(Vector3 position, bool isBoss)
    {
        GameObject go = new GameObject("VFX_DissolvePuff");
        go.transform.position = position;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.8f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.15f, isBoss ? 0.5f : 0.32f);
        main.gravityModifier = -0.35f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.18f, 0.12f, 0.14f, 0.7f),
            new Color(1f, 0.78f, 0.28f, 0.85f));

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, isBoss ? 24 : 12) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = isBoss ? 1.0f : 0.55f;

        var colOverLifetime = ps.colorOverLifetime;
        colOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.5f, 0.3f, 0.2f), 1f) },
            new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
        colOverLifetime.color = grad;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = softParticleMat;

        ps.Play();
        Destroy(go, 1.4f);
    }

    private IEnumerator FlashDeathLight(Vector3 pos, bool isBoss)
    {
        GameObject lightGo = new GameObject("DeathFlashLight");
        lightGo.transform.position = pos + Vector3.up * 0.4f;
        Light lightComp = lightGo.AddComponent<Light>();
        lightComp.type = LightType.Point;
        lightComp.color = isBoss ? new Color(1f, 0.42f, 0.15f) : new Color(1f, 0.65f, 0.25f);
        lightComp.range = isBoss ? 14f : 7f;
        float startIntensity = isBoss ? 8f : 4.2f;
        lightComp.intensity = startIntensity;

        float duration = isBoss ? 0.65f : 0.35f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (lightComp != null)
            {
                lightComp.intensity = Mathf.Lerp(startIntensity, 0f, elapsed / duration);
            }
            yield return null;
        }

        if (lightGo != null) Destroy(lightGo);
    }

    // ==================== PLAYER DAMAGE SCREEN FLASH ====================

    private void TriggerDamageFlashInternal(float intensity)
    {
        EnsureDamageOverlay();
        if (damageOverlayImage == null) return;

        damageFlashAlpha = Mathf.Clamp(Mathf.Max(damageFlashAlpha, intensity), 0f, 0.62f);
        Color c = damageOverlayImage.color;
        c.a = damageFlashAlpha;
        damageOverlayImage.color = c;
        damageOverlayImage.enabled = true;
    }

    private void EnsureDamageOverlay()
    {
        if (damageOverlayImage != null) return;

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        GameObject go = new GameObject("PlayerDamageVignette", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(canvas.transform, false);
        go.transform.SetAsFirstSibling();

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        damageOverlayImage = go.GetComponent<Image>();
        damageOverlayImage.sprite = CreateVignetteSprite();
        damageOverlayImage.color = new Color(0.85f, 0.04f, 0.04f, 0f);
        damageOverlayImage.raycastTarget = false;
        damageOverlayImage.enabled = false;
    }

    // ==================== SLASH ARC & DASH VFX ====================

    private IEnumerator SpawnSlashArcCoroutine(Vector3 playerPos, Vector3 forwardDir, int attackVariant)
    {
        forwardDir.y = 0f;
        if (forwardDir.sqrMagnitude < 0.001f) forwardDir = Vector3.forward;
        forwardDir.Normalize();

        Quaternion baseRot = Quaternion.LookRotation(forwardDir);

        // Chờ một chút cho khớp pha vung vũ khí ra phía trước
        yield return new WaitForSeconds(attackVariant == 2 ? 0.14f : 0.10f);

        GameObject root = new GameObject($"VFX_SlashArc_{attackVariant}");
        float heightY = attackVariant == 2 ? 0.55f : 1.18f;
        Vector3 centerPos = playerPos + Vector3.up * heightY + (attackVariant == 2 ? Vector3.zero : forwardDir * 0.45f);
        root.transform.position = centerPos;
        root.transform.rotation = baseRot;

        // 1. Vệt chém lưỡi liềm / vòng xoay 360°
        GameObject slashQuad = new GameObject("SlashMesh");
        slashQuad.transform.SetParent(root.transform, false);
        MeshFilter mf = slashQuad.AddComponent<MeshFilter>();
        mf.sharedMesh = horizontalQuadMesh;
        MeshRenderer mr = slashQuad.AddComponent<MeshRenderer>();

        Material instMat = new Material(attackVariant == 2 ? ringParticleMat : slashArcMat);
        mr.material = instMat;

        Color startColor;
        Color endColor;
        float startYaw;
        float endYaw;
        float tiltZ;
        float startScale;
        float endScale;
        float duration;

        if (attackVariant == 2)
        {
            // Atk3: Xoay 360° tầm thấp (vòng xoáy rực đỏ-vàng + bụi đất)
            startColor = new Color(1f, 0.52f, 0.16f, 0.95f);
            endColor = new Color(0.95f, 0.16f, 0.08f, 0f);
            startYaw = -90f;
            endYaw = 290f;
            tiltZ = 0f;
            startScale = 2.6f;
            endScale = 4.6f;
            duration = 0.34f;

            SpawnLowSpinDust(playerPos + Vector3.up * 0.15f);
        }
        else if (attackVariant == 1)
        {
            // Atk2: Chém ngang rộng Horizontal Cleave
            startColor = new Color(1f, 0.78f, 0.28f, 0.95f);
            endColor = new Color(1f, 0.35f, 0.12f, 0f);
            startYaw = -75f;
            endYaw = 85f;
            tiltZ = 6f;
            startScale = 2.8f;
            endScale = 4.0f;
            duration = 0.24f;
        }
        else
        {
            // Atk1: Chém chéo sắc gọn
            startColor = new Color(1f, 0.94f, 0.72f, 0.92f);
            endColor = new Color(0.95f, 0.65f, 0.25f, 0f);
            startYaw = -65f;
            endYaw = 75f;
            tiltZ = -32f;
            startScale = 2.4f;
            endScale = 3.4f;
            duration = 0.22f;
        }

        // 2. Tia lửa bay theo đường chém
        SpawnSlashWindSparks(centerPos, forwardDir, startColor, attackVariant == 2);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easeOut = 1f - (1f - t) * (1f - t);

            float yaw = Mathf.Lerp(startYaw, endYaw, easeOut);
            float sc = Mathf.Lerp(startScale, endScale, easeOut);
            slashQuad.transform.localRotation = Quaternion.Euler(0f, yaw, tiltZ);
            slashQuad.transform.localScale = new Vector3(sc, 1f, sc);
            instMat.color = Color.Lerp(startColor, endColor, t);

            yield return null;
        }

        Destroy(instMat);
        Destroy(root);
    }

    private void SpawnSlashWindSparks(Vector3 pos, Vector3 forward, Color sparkColor, bool is360)
    {
        GameObject go = new GameObject("VFX_SlashSparks");
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(forward);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.35f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, is360 ? 8.5f : 6.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.gravityModifier = 0.3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(sparkColor, Color.white);

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, is360 ? 22 : 10) });

        var shape = ps.shape;
        shape.shapeType = is360 ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Cone;
        shape.radius = is360 ? 1.15f : 0.45f;
        if (!is360) shape.angle = 55f;
        else shape.rotation = new Vector3(90f, 0f, 0f);

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(sparkColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colOver.color = g;

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.material = dropletParticleMat;
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.07f;
        rend.lengthScale = 1.9f;

        ps.Play();
        Destroy(go, 0.9f);
    }

    private void SpawnLowSpinDust(Vector3 groundPos)
    {
        GameObject go = new GameObject("VFX_LowSpinDust");
        go.transform.position = groundPos;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.6f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 5.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.95f);
        main.gravityModifier = -0.15f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new Color(0.78f, 0.68f, 0.52f, 0.62f);

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.85f;
        shape.rotation = new Vector3(90f, 0f, 0f);

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.85f, 0.72f, 0.52f), 0f), new GradientColorKey(new Color(0.45f, 0.38f, 0.3f), 1f) },
            new[] { new GradientAlphaKey(0.65f, 0f), new GradientAlphaKey(0f, 1f) });
        colOver.color = g;

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.material = softParticleMat;

        ps.Play();
        Destroy(go, 1.2f);
    }

    private void SpawnDashEffectInternal(Vector3 playerPos, Vector3 dashDir)
    {
        dashDir.y = 0f;
        if (dashDir.sqrMagnitude < 0.001f) dashDir = Vector3.forward;
        dashDir.Normalize();

        GameObject go = new GameObject("VFX_DashWind");
        go.transform.position = playerPos + Vector3.up * 0.55f;
        go.transform.rotation = Quaternion.LookRotation(-dashDir);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.35f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.36f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 7.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.34f);
        main.gravityModifier = -0.1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.55f, 0.92f, 1f, 0.85f),
            new Color(0.95f, 0.92f, 0.75f, 0.7f));

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 26f;
        shape.radius = 0.35f;

        var colOver = ps.colorOverLifetime;
        colOver.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(new Color(0.75f, 0.95f, 1f), 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
        colOver.color = g;

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.material = dropletParticleMat;
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.08f;
        rend.lengthScale = 2.2f;

        ps.Play();
        Destroy(go, 0.9f);
    }

    private void SpawnBlockSparkInternal(Vector3 position, Vector3 normalDir, bool perfectParry)
    {
        normalDir.y = 0f;
        if (normalDir.sqrMagnitude < 0.001f) normalDir = Vector3.forward;
        normalDir.Normalize();

        GameObject root = new GameObject(perfectParry ? "VFX_PerfectParry" : "VFX_BlockSpark");
        root.transform.position = position;
        root.transform.rotation = Quaternion.LookRotation(normalDir);

        // 1. Tia lửa kim loại tóe mạnh khi vũ khí va chạm lúc đỡ đòn
        {
            GameObject go = new GameObject("MetallicSparks");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, perfectParry ? 0.42f : 0.28f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, perfectParry ? 11.5f : 8.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, perfectParry ? 0.16f : 0.11f);
            main.gravityModifier = 0.9f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = perfectParry
                ? new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.45f, 1f), new Color(1f, 0.65f, 0.15f, 1f))
                : new ParticleSystem.MinMaxGradient(new Color(0.65f, 0.92f, 1f, 1f), new Color(1f, 0.92f, 0.65f, 1f));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, perfectParry ? 32 : 18) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55f;
            shape.radius = 0.12f;

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(perfectParry ? new Color(1f, 0.72f, 0.15f) : new Color(0.45f, 0.85f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colOver.color = g;

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = dropletParticleMat;
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.08f;
            rend.lengthScale = 2.1f;

            ps.Play();
        }

        // 2. Vòng sáng khiên/chặn đòn bật ra
        {
            GameObject go = new GameObject("GuardRing");
            go.transform.SetParent(root.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.3f;
            main.loop = false;
            main.startLifetime = perfectParry ? 0.32f : 0.22f;
            main.startSpeed = 0f;
            main.startSize = perfectParry ? 2.4f : 1.45f;
            main.startColor = perfectParry
                ? new Color(1f, 0.88f, 0.32f, 0.95f)
                : new Color(0.55f, 0.88f, 1f, 0.85f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f));

            var colOver = ps.colorOverLifetime;
            colOver.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(perfectParry ? new Color(1f, 0.75f, 0.2f) : new Color(0.4f, 0.85f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
            colOver.color = g;

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.material = ringParticleMat;

            ps.Play();
        }

        StartCoroutine(FlashDeathLight(position, perfectParry));
        Destroy(root, 1.0f);
    }

    // ==================== PROCEDURAL TEXTURES & MATERIALS ====================

    private void EnsureMaterials()
    {
        if (softParticleMat != null && dropletParticleMat != null && ringParticleMat != null && slashArcMat != null && horizontalQuadMesh != null) return;

        Shader spriteShader = Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Particles/Standard Unlit");

        softParticleMat = new Material(spriteShader) { mainTexture = CreateSoftCircleTexture(64) };
        dropletParticleMat = new Material(spriteShader) { mainTexture = CreateDropletTexture(64) };
        ringParticleMat = new Material(spriteShader) { mainTexture = CreateRingTexture(128) };
        slashArcMat = new Material(spriteShader) { mainTexture = CreateSlashArcTexture(128) };
        horizontalQuadMesh = CreateHorizontalQuadMesh();
    }

    private static Texture2D CreateSlashArcTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) / half;
                float dy = (y - half + 0.5f) / half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx); // -PI..PI

                // Cung lưỡi liềm sắc nét phía trước (+Y / +X)
                float arcAngle = Mathf.Abs(angle - Mathf.PI * 0.5f);
                if (arcAngle > Mathf.PI) arcAngle = 2f * Mathf.PI - arcAngle;
                float arcFade = Mathf.Clamp01(1f - arcAngle / 1.45f);

                // Dày ở giữa cung, vuốt nhọn 2 đầu
                float bandWidth = 0.14f + 0.16f * arcFade;
                float radial = 1f - Mathf.Abs(dist - 0.74f) / bandWidth;
                float alpha = Mathf.Clamp01(radial) * Mathf.Pow(arcFade, 0.85f);
                alpha = Mathf.Clamp01(alpha * 1.25f);

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    private static Texture2D CreateSoftCircleTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) / half;
                float dy = (y - half + 0.5f) / half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - dist);
                alpha = alpha * alpha;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    private static Texture2D CreateDropletTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) / (half * 0.65f);
                float dy = (y - half + 0.5f) / half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - Mathf.SmoothStep(0.35f, 1.0f, dist));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    private static Texture2D CreateRingTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) / half;
                float dy = (y - half + 0.5f) / half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = 1f - Mathf.Abs(dist - 0.82f) / 0.16f;
                float alpha = Mathf.Clamp01(ring);
                alpha = alpha * alpha;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return tex;
    }

    private static Sprite CreateVignetteSprite()
    {
        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) / half;
                float dy = (y - half + 0.5f) / half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.SmoothStep(0.55f, 1.25f, dist);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static Mesh CreateHorizontalQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, 0.5f),
            new Vector3(-0.5f, 0f, 0.5f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        return mesh;
    }
}
