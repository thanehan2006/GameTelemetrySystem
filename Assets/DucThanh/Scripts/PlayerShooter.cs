using UnityEngine;

/// <summary>
/// Xử lý bắn súng của Player.
/// Tối ưu hóa: kiểm tra GameOverManager, fallback tự động lấy main camera nếu chưa gán trong Inspector.
/// </summary>
public class PlayerShooter : MonoBehaviour
{
    [Header("Shooting Settings")]
    public Camera playerCamera;
    public float shootRange = 50f;
    public int damage = 25;
    public float fireRate = 0.25f;

    [Header("Layer Mask")]
    [SerializeField] private LayerMask hitLayers = ~0; // Mặc định bắn trúng mọi layer

    private float nextFireTime;

    void Start()
    {
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>();
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }
        }
    }

    void Update()
    {
        // Không thể bắn khi Game Over hoặc chuột chưa khóa
        if (GameOverManager.IsGameOver) return;

        if (Input.GetMouseButton(0) && Time.time >= nextFireTime && Cursor.lockState == CursorLockMode.Locked)
        {
            nextFireTime = Time.time + fireRate;
            Shoot();
        }
    }

    void Shoot()
    {
        if (playerCamera == null)
        {
            Debug.LogWarning("[PlayerShooter] Chưa gán Player Camera!");
            return;
        }

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, shootRange, hitLayers))
        {
            EnemyHealth enemy = hit.collider.GetComponentInParent<EnemyHealth>();
            if (enemy != null)
            {
                enemy.TakeDamage(damage);
            }
        }

        Debug.DrawRay(ray.origin, ray.direction * shootRange, Color.red, 0.2f);
    }
}
