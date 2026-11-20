using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Điều khiển nhân vật góc nhìn thứ 3 (Third-Person) tối ưu theo phong cách Action AAA (Black Myth: Wukong):
/// - Phản hồi WASD tức thì 0ms delay, nhả phím là dừng đanh gọn ngay lập tức (không bao giờ bị trôi/trượt quán tính).
/// - Khóa cứng tâm visualModel vào CharacterController để animation không làm trôi lệch nhân vật.
/// - Xoay mặt tốc độ cao, đổi hướng dứt khoát.
/// - Hỗ trợ bước lướt ngắn (Combat Lunge) có kiểm soát từ PlayerAttack, tự dừng khi áp sát mục tiêu.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(-20)]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings (AAA Snappy Locomotion)")]
    public float moveSpeed = 6.5f;
    public float rotationSpeed = 14f;
    public float gravity = -25f;

    [Header("Knockback & Collision")]
    public float knockbackDamping = 36f;
    public float bodyCollisionPushForce = 4.5f;

    [Header("Camera & Visual")]
    public Transform playerCamera;
    [Tooltip("Transform chứa visual/model nhân vật (PlayerBody). Nếu null sẽ xoay chính Player root.")]
    public Transform visualModel;

    public Vector3 CurrentInputDirection { get; private set; }
    public bool HasMovementInput { get; private set; }

    private CharacterController controller;
    private PlayerAttack playerAttack;
    private float verticalVelocity;
    private Vector3 knockbackVelocity;
    private Vector3 lungeVelocity;
    private int lastHorizontalDir;
    private int lastVerticalDir;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        controller.minMoveDistance = 0f;
        playerAttack = GetComponent<PlayerAttack>();

        if (visualModel == null)
        {
            Transform body = transform.Find("PlayerBody");
            if (body != null)
            {
                visualModel = body;
            }
        }
    }

    void Start()
    {
        if (playerCamera == null && Camera.main != null)
        {
            playerCamera = Camera.main.transform;
        }

        if (knockbackDamping < 30f)
        {
            knockbackDamping = 36f;
        }

        if (GetComponent<NavMeshObstacle>() == null)
        {
            var obstacle = gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.radius = controller.radius;
            obstacle.height = controller.height;
            obstacle.center = controller.center;
            obstacle.carving = false;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (GameOverManager.IsGameOver || LevelUpChoiceManager.IsPaused)
        {
            CurrentInputDirection = Vector3.zero;
            HasMovementInput = false;
            knockbackVelocity = Vector3.zero;
            lungeVelocity = Vector3.zero;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        HandleCursorLock();
        HandleMovement();
    }

    void LateUpdate()
    {
        // Khóa cứng vị trí local của PlayerBody về (0, 0, 0) để animation không bao giờ làm trôi model lệch khỏi collider
        if (visualModel != null && visualModel != transform)
        {
            Vector3 localPos = visualModel.localPosition;
            if (localPos.x != 0f || localPos.z != 0f)
            {
                visualModel.localPosition = new Vector3(0f, localPos.y, 0f);
            }
        }
    }

    /// <summary>
    /// Đọc phím trực tiếp 100% digital (không dùng Input.GetAxis để loại bỏ hoàn toàn độ trễ và hiện tượng trôi khi nhả phím).
    /// </summary>
    private Vector2 ReadDigitalMoveInput()
    {
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) lastHorizontalDir = -1;
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) lastHorizontalDir = 1;
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) lastVerticalDir = -1;
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) lastVerticalDir = 1;

        bool left = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        bool right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
        bool down = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
        bool up = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);

        float x = (left && right) ? lastHorizontalDir
                : left ? -1f
                : right ? 1f
                : 0f;

        float z = (down && up) ? lastVerticalDir
                : down ? -1f
                : up ? 1f
                : 0f;

        return new Vector2(x, z);
    }

    private Vector3 GetCameraRelativeDirection(Vector3 inputDir)
    {
        if (playerCamera == null)
        {
            if (Camera.main != null) playerCamera = Camera.main.transform;
            if (playerCamera == null) return inputDir;
        }

        Vector3 camForward = playerCamera.forward;
        Vector3 camRight = playerCamera.right;

        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        return (camForward * inputDir.z + camRight * inputDir.x).normalized;
    }

    private void HandleMovement()
    {
        Vector2 rawInput = ReadDigitalMoveInput();
        HasMovementInput = rawInput.sqrMagnitude > 0.01f;

        Vector3 inputDir = HasMovementInput ? new Vector3(rawInput.x, 0f, rawInput.y).normalized : Vector3.zero;
        CurrentInputDirection = HasMovementInput ? GetCameraRelativeDirection(inputDir) : Vector3.zero;

        bool isAttacking = playerAttack != null && playerAttack.IsAttacking;
        Vector3 moveDir = isAttacking ? Vector3.zero : CurrentInputDirection;

        // Khi không tấn công: xoay mặt cực nhanh theo hướng di chuyển
        if (!isAttacking && CurrentInputDirection.sqrMagnitude > 0.001f)
        {
            RotateVisualTowards(CurrentInputDirection, rotationSpeed);
        }

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        // Tổng hợp vận tốc: khi nhả phím và không có lunge/knockback thì vận tốc ngang = 0 tuyệt đối
        Vector3 horizontalVelocity = moveDir * moveSpeed + lungeVelocity + knockbackVelocity;
        Vector3 finalDisplacement = horizontalVelocity * Time.deltaTime;
        finalDisplacement.y = verticalVelocity * Time.deltaTime;

        controller.Move(finalDisplacement);

        // Giảm tốc nhanh để không bao giờ có cảm giác trượt băng
        if (lungeVelocity.sqrMagnitude > 0.001f)
        {
            lungeVelocity = Vector3.MoveTowards(lungeVelocity, Vector3.zero, 45f * Time.deltaTime);
        }
        else
        {
            lungeVelocity = Vector3.zero;
        }

        if (knockbackVelocity.sqrMagnitude > 0.001f)
        {
            knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDamping * Time.deltaTime);
        }
        else
        {
            knockbackVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// Xoay mặt nhân vật về hướng chỉ định (dùng cho cả di chuyển và Auto-Target khi ra đòn).
    /// </summary>
    public void RotateVisualTowards(Vector3 worldDirection, float speed, bool instant = false)
    {
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(worldDirection.normalized);
        Transform targetRotTransform = visualModel != null ? visualModel : transform;

        if (instant)
        {
            targetRotTransform.rotation = targetRotation;
        }
        else
        {
            targetRotTransform.rotation = Quaternion.Slerp(
                targetRotTransform.rotation,
                targetRotation,
                speed * Time.deltaTime
            );
        }
    }

    public Vector3 GetVisualForward()
    {
        Transform t = visualModel != null ? visualModel : transform;
        Vector3 fwd = t.forward;
        fwd.y = 0f;
        return fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
    }

    /// <summary>
    /// Đặt vận tốc bước lướt ngắn (Combat Lunge) khi vung đòn kiểu Wukong.
    /// </summary>
    public void SetCombatLunge(Vector3 velocity)
    {
        velocity.y = 0f;
        lungeVelocity = velocity;
    }

    public void StopCombatLunge()
    {
        lungeVelocity = Vector3.zero;
    }

    /// <summary>
    /// Đẩy lùi ngắn khi bị vũ khí của Quái đánh trúng (hãm dừng cực nhanh để không bị trôi).
    /// </summary>
    public void ApplyKnockback(Vector3 attackerPosition, float force)
    {
        if (force <= 0.01f) return;

        Vector3 dir = transform.position - attackerPosition;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = -GetVisualForward();
        }
        else
        {
            dir.Normalize();
        }

        // Giới hạn lực giật và dừng nhanh để Player không bị trượt trôi
        knockbackVelocity = dir * Mathf.Min(force, 6f);
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider == null) return;

        // Khi Player chạm vào thân Quái: chỉ đẩy Quái dạt ra, TUYỆT ĐỐI không đẩy ngược làm trôi Player
        OrcEnemyAI enemy = hit.collider.GetComponentInParent<OrcEnemyAI>();
        if (enemy != null)
        {
            StopCombatLunge();
            enemy.ApplyKnockback(transform.position, bodyCollisionPushForce, false);
        }
    }

    private void HandleCursorLock()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
