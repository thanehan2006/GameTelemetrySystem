using UnityEngine;

// Hỗ trợ bắn tia raycast theo hướng tâm màn hình khi nhấn chuột trái
public class PlayerShooter : MonoBehaviour
{
    [Header("Shooting Settings")]
    public Camera playerCamera;
    public float shootRange = 50f;
    public int damage = 25;
    public float fireRate = 0.25f;

    [Header("Layer Mask")]
    [SerializeField] private LayerMask hitLayers = ~0;

    private float nextFireTime;

    // Gán camera chính làm gốc phóng tia nếu chưa thiết lập sẵn
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

    // Kiểm tra thao tác chuột trái để gọi hàm bắn
    void Update()
    {

        if (GameOverManager.IsGameOver) return;

        if (Input.GetMouseButton(0) && Time.time >= nextFireTime && Cursor.lockState == CursorLockMode.Locked)
        {
            nextFireTime = Time.time + fireRate;
            Shoot();
        }
    }

    // Phát tia raycast từ giữa màn hình và gây sát thương lên EnemyHealth nếu trúng
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
