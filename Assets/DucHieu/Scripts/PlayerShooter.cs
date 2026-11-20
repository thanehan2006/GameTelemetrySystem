
using UnityEngine;

public class PlayerShooter : MonoBehaviour
{
    public Camera playerCamera;
    public float shootRange = 50f;
    public int damage = 25;
    public float fireRate = 0.25f;

    private float nextFireTime;

    void Update()
    {
        if (Input.GetMouseButton(0) && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + fireRate;
            Shoot();
        }
    }

    void Shoot()
    {
        if (playerCamera == null)
        {
            UnityEngine.Debug.LogWarning(
                "Chua gan Player Camera trong Inspector!"
            );
            return;
        }

        Ray ray = playerCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f)
        );

        if (Physics.Raycast(ray, out RaycastHit hit, shootRange))
        {
            EnemyHealth enemy =
                hit.collider.GetComponentInParent<EnemyHealth>();

            if (enemy != null)
            {
                enemy.TakeDamage(damage);
            }
        }

        UnityEngine.Debug.DrawRay(
            ray.origin,
            ray.direction * shootRange,
            Color.red,
            0.5f
        );
    }
}
