using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Quản lý animation cho Player:
/// - Idle: tự đổi ngẫu nhiên mượt giữa Idle, Idle2 (Standing Idle), Idle3 (Fishing Idle).
/// - Run: phát animation chạy chuẩn (Run).
/// - Block (giữ Chuột Phải): phát animation đỡ đòn (Standing Block Idle -> state Block).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerAnimationController : MonoBehaviour
{
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int RunHash = Animator.StringToHash("Run");
    private static readonly int BlockHash = Animator.StringToHash("Block");
    private static readonly string[] IdleCandidateNames = { "Idle", "Idle2", "Idle3" };

    [SerializeField] private float runSpeedValue = 6.5f;
    [SerializeField] private float minIdleSwitchTime = 5.5f;
    [SerializeField] private float maxIdleSwitchTime = 9.0f;

    private Animator animator;
    private PlayerController playerController;
    private PlayerAttack playerAttack;

    private readonly List<int> availableIdleHashes = new List<int>();
    private bool hasBlockState;
    private int currentIdleIndex = -1;
    private bool wasMoving;
    private bool wasAttacking;
    private bool wasBlocking;
    private float nextIdleSwitchTime;

    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        animator = GetComponentInChildren<Animator>();
        playerAttack = GetComponent<PlayerAttack>();
    }

    void Start()
    {
        CacheAvailableStates();
        ScheduleNextIdleSwitch();
    }

    void Update()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null) return;
            CacheAvailableStates();
        }

        if (GameOverManager.IsGameOver || LevelUpChoiceManager.IsPaused)
        {
            animator.SetFloat(SpeedHash, 0f);
            return;
        }

        bool isBlocking = playerController != null && playerController.IsBlocking;
        if (isBlocking)
        {
            animator.SetFloat(SpeedHash, 0f);
            animator.speed = 1f;

            if (!wasBlocking)
            {
                PlayBlockAnimation();
            }

            wasBlocking = true;
            wasAttacking = false;
            wasMoving = false;
            return;
        }

        bool isAttacking = playerAttack != null && playerAttack.IsAttacking;
        if (isAttacking)
        {
            wasAttacking = true;
            wasBlocking = false;
            animator.SetFloat(SpeedHash, 0f);
            return;
        }

        bool isMoving = playerController != null
            ? playerController.HasMovementInput
            : (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A)
               || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D)
               || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow)
               || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow));

        animator.SetFloat(SpeedHash, isMoving ? runSpeedValue : 0f);
        animator.speed = 1f;

        if (isMoving && (!wasMoving || wasAttacking || wasBlocking))
        {
            PlayRunAnimation(0.09f);
        }
        else if (!isMoving && (wasMoving || wasAttacking || wasBlocking))
        {
            PlayRandomIdleAnimation(0.12f);
            ScheduleNextIdleSwitch();
        }
        else if (!isMoving && availableIdleHashes.Count > 1 && Time.time >= nextIdleSwitchTime)
        {
            PlayRandomIdleAnimation(0.32f);
            ScheduleNextIdleSwitch();
        }

        wasMoving = isMoving;
        wasAttacking = false;
        wasBlocking = false;
    }

    public void ForceReturnToLocomotion()
    {
        if (animator == null) return;

        animator.speed = 1f;

        if (playerController != null && playerController.IsBlocking)
        {
            PlayBlockAnimation();
            wasBlocking = true;
            wasAttacking = false;
            wasMoving = false;
            return;
        }

        bool isMoving = playerController != null && playerController.HasMovementInput;
        if (isMoving)
        {
            PlayRunAnimation(0.08f);
        }
        else
        {
            PlayRandomIdleAnimation(0.12f);
            ScheduleNextIdleSwitch();
        }

        wasMoving = isMoving;
        wasAttacking = false;
        wasBlocking = false;
    }

    private void CacheAvailableStates()
    {
        availableIdleHashes.Clear();
        hasBlockState = false;
        if (animator == null) return;

        for (int i = 0; i < IdleCandidateNames.Length; i++)
        {
            int h = Animator.StringToHash(IdleCandidateNames[i]);
            if (animator.HasState(0, h)) availableIdleHashes.Add(h);
        }

        hasBlockState = animator.HasState(0, BlockHash);
    }

    private void PlayRandomIdleAnimation(float fadeDuration)
    {
        if (availableIdleHashes.Count == 0) return;

        int nextIdx = PickRandomDifferentIndex(availableIdleHashes.Count, currentIdleIndex);
        currentIdleIndex = nextIdx;
        animator.CrossFadeInFixedTime(availableIdleHashes[nextIdx], fadeDuration, 0);
    }

    private void PlayRunAnimation(float fadeDuration)
    {
        if (animator.HasState(0, RunHash))
        {
            animator.CrossFadeInFixedTime(RunHash, fadeDuration, 0);
        }
    }

    private void PlayBlockAnimation()
    {
        if (!hasBlockState) hasBlockState = animator.HasState(0, BlockHash);
        if (hasBlockState)
        {
            animator.CrossFadeInFixedTime(BlockHash, 0.08f, 0, 0f);
        }
    }

    private static int PickRandomDifferentIndex(int count, int lastIndex)
    {
        if (count <= 1) return 0;
        int idx = Random.Range(0, count);
        if (idx == lastIndex)
        {
            idx = (idx + Random.Range(1, count)) % count;
        }
        return idx;
    }

    private void ScheduleNextIdleSwitch()
    {
        nextIdleSwitchTime = Time.time + Random.Range(minIdleSwitchTime, maxIdleSwitchTime);
    }
}
