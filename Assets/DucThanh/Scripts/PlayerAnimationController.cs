using UnityEngine;

/// <summary>
/// Đồng bộ tham số "Speed" của Animator theo vận tốc ngang của CharacterController.
/// Speed ~ 0 => Idle, Speed > 0.1 => Run.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerAnimationController : MonoBehaviour
{
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    [SerializeField] private float runSpeedFallback = 6f;

    private Animator animator;
    private CharacterController controller;
    private PlayerAttack playerAttack;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        playerAttack = GetComponent<PlayerAttack>();
    }

    void Update()
    {
        if (animator == null) return;

        float speed;
        if (GameOverManager.IsGameOver || (playerAttack != null && playerAttack.IsAttacking))
        {
            speed = 0f;
        }
        else
        {
            Vector3 v = controller.velocity;
            v.y = 0f;
            speed = v.magnitude;

            // Dự phòng: velocity có thể = 0 trong frame vừa Move, dùng thêm input
            if (speed < 0.05f)
            {
                bool hasInput = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A)
                             || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D)
                             || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow)
                             || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow)
                             || Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f
                             || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f;
                if (hasInput) speed = runSpeedFallback;
            }
        }

        animator.SetFloat(SpeedHash, speed);
    }
}
