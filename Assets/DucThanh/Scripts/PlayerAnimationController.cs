using UnityEngine;

/// <summary>
/// Đồng bộ tham số "Speed" của Animator tức thì theo input digital của PlayerController.
/// Loại bỏ độ trễ khi bắt đầu chạy hoặc khi dừng lại.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerAnimationController : MonoBehaviour
{
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    [SerializeField] private float runSpeedValue = 6.5f;

    private Animator animator;
    private PlayerController playerController;
    private PlayerAttack playerAttack;

    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        animator = GetComponentInChildren<Animator>();
        playerAttack = GetComponent<PlayerAttack>();
    }

    void Update()
    {
        if (animator == null) return;

        float speed = 0f;
        if (!GameOverManager.IsGameOver
            && !LevelUpChoiceManager.IsPaused
            && (playerAttack == null || !playerAttack.IsAttacking))
        {
            bool isMoving = playerController != null
                ? playerController.HasMovementInput
                : (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A)
                   || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D)
                   || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow)
                   || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow));

            speed = isMoving ? runSpeedValue : 0f;
        }

        animator.SetFloat(SpeedHash, speed);
    }
}
