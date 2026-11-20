using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Điều khiển nhân vật người chơi ở góc nhìn thứ 3 (Third-Person):
/// - Di chuyển WASD theo hướng Camera nhìn (chống kẹt phím ngược hướng).
/// - Model/Nhân vật tự động xoay mặt mượt mà về hướng di chuyển.
/// - Phản hồi tức thì, không bị kẹt bởi minMoveDistance hay va chạm sàn.
/// - Tự động tìm Main Camera nếu chưa gán.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 6f;
    public float rotationSpeed = 12f; // Tốc độ xoay mặt của nhân vật
    public float gravity = -20f;

    [Header("Camera & Visual")]
    public Transform playerCamera;
    [Tooltip("Transform chứa visual/model nhân vật (nếu muốn xoay riêng model). Nếu null sẽ xoay chính Player root.")]
    public Transform visualModel;

    private CharacterController controller;
    private PlayerAttack playerAttack;
    private float verticalVelocity;
    private int lastHorizontalDir;
    private int lastVerticalDir;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        controller.minMoveDistance = 0f;
        playerAttack = GetComponent<PlayerAttack>();

        // Tự động tìm Main Camera nếu chưa gán
        if (playerCamera == null && Camera.main != null)
        {
            playerCamera = Camera.main.transform;
        }

        // Tự động tìm visual model con (PlayerBody) nếu chưa gán
        if (visualModel == null)
        {
            Transform body = transform.Find("PlayerBody");
            if (body != null)
            {
                visualModel = body;
            }
        }

        // Đảm bảo có NavMeshObstacle để Enemy né Player thay vì đi xuyên vào
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
        // Dừng toàn bộ điều khiển khi Game Over
        if (GameOverManager.IsGameOver)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        HandleCursorLock();
        HandleMovement();
    }

    private Vector2 ReadMoveInput()
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
                : Input.GetAxisRaw("Horizontal");

        float z = (down && up) ? lastVerticalDir
                : down ? -1f
                : up ? 1f
                : Input.GetAxisRaw("Vertical");

        return new Vector2(x, z);
    }

    private Vector3 GetCameraRelativeDirection(Vector3 inputDir)
    {
        if (playerCamera == null) return inputDir;

        Vector3 camForward = playerCamera.forward;
        Vector3 camRight = playerCamera.right;

        // Chỉ lấy mặt phẳng ngang X-Z
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        return (camForward * inputDir.z + camRight * inputDir.x).normalized;
    }

    private void HandleMovement()
    {
        Vector2 rawInput = ReadMoveInput();
        bool movementLocked = playerAttack != null && playerAttack.IsAttacking;

        Vector3 inputDir = new Vector3(rawInput.x, 0f, rawInput.y).normalized;
        Vector3 desiredDir = inputDir.sqrMagnitude > 0.001f ? GetCameraRelativeDirection(inputDir) : Vector3.zero;
        Vector3 moveDir = movementLocked ? Vector3.zero : desiredDir;

        if (desiredDir.sqrMagnitude > 0.001f)
        {
            // Xoay mặt nhân vật mượt về hướng phím bấm (kể cả khi đang khóa di chuyển để đánh liên hoàn)
            Quaternion targetRotation = Quaternion.LookRotation(desiredDir);
            Transform targetRotTransform = visualModel != null ? visualModel : transform;
            targetRotTransform.rotation = Quaternion.Slerp(
                targetRotTransform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        // Xử lý trọng lực bám sàn nhẹ để không ép kẹt capsule vào MeshCollider
        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 velocity = moveDir * moveSpeed;
        velocity.y = verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }

    private void HandleCursorLock()
    {
        // Nhấn ESC để mở khóa chuột
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Nhấp chuột để khóa lại
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
