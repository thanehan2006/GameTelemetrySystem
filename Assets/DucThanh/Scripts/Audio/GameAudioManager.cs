using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Quản lý toàn bộ âm thanh & nhạc nền (BGM + SFX) xuyên suốt game (Loading -> Login -> Lv1 3 Màn):
/// - Tự động tải clip từ Assets/Resources/Audio/*.wav (có thể thay thế trực tiếp bằng file .wav/.mp3 khác).
/// - Tự chuyển nhạc nền mượt mà (Crossfade) theo Scene & giai đoạn Boss Fight.
/// - Biến thiên cao độ nhẹ (Pitch Randomization) giúp tiếng chém/bước chân tự nhiên, không bị máy móc.
/// - Phím M để bật/tắt nhanh âm thanh (Mute).
/// </summary>
public class GameAudioManager : MonoBehaviour
{
    public enum BgmTrack
    {
        None,
        Menu,
        ForestCombat,
        Boss
    }

    private static GameAudioManager instance;
    public static GameAudioManager Instance => EnsureInstance();

    [Header("Volume Settings")]
    [Range(0f, 1f)] public float bgmVolume = 0.42f;
    [Range(0f, 1f)] public float sfxVolume = 0.85f;
    public bool isMuted = false;

    private AudioSource bgmSourceA;
    private AudioSource bgmSourceB;
    private bool usingSourceA = true;
    private BgmTrack currentTrack = BgmTrack.None;
    private Coroutine crossfadeRoutine;

    private readonly List<AudioSource> sfxPool = new List<AudioSource>();
    private int sfxPoolIndex;
    private readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        EnsureInstance();
    }

    public static GameAudioManager EnsureInstance()
    {
        if (instance != null) return instance;

        instance = FindFirstObjectByType<GameAudioManager>();
        if (instance == null)
        {
            GameObject go = new GameObject("GameAudioManager");
            instance = go.AddComponent<GameAudioManager>();
        }

        instance.InitializeSources();
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
        DontDestroyOnLoad(gameObject);
        InitializeSources();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        SyncBgmForScene(SceneManager.GetActiveScene().name);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.M))
        {
            ToggleMute();
        }
    }

    private void InitializeSources()
    {
        if (bgmSourceA != null) return;

        bgmSourceA = CreateAudioSource("BGM_A", true);
        bgmSourceB = CreateAudioSource("BGM_B", true);

        for (int i = 0; i < 6; i++)
        {
            sfxPool.Add(CreateAudioSource($"SFX_{i}", false));
        }
    }

    private AudioSource CreateAudioSource(string childName, bool loop)
    {
        GameObject child = new GameObject(childName);
        child.transform.SetParent(transform, false);
        AudioSource src = child.AddComponent<AudioSource>();
        src.loop = loop;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        return src;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SyncBgmForScene(scene.name);
    }

    private void SyncBgmForScene(string sceneName)
    {
        if (sceneName == "LoadingScene" || sceneName == "LoginScene")
        {
            PlayBgmInternal(BgmTrack.Menu, 0.8f);
        }
        else if (sceneName == "Lv1")
        {
            PlayBgmInternal(BgmTrack.ForestCombat, 1.0f);
        }
    }

    public void ToggleMute()
    {
        isMuted = !isMuted;
        AudioListener.volume = isMuted ? 0f : 1f;
        Debug.Log($"[GameAudioManager] Âm thanh: {(isMuted ? "TẮT (Muted)" : "BẬT")}");
    }

    // ==================== BGM CONTROL ====================

    public static void PlayBgm(BgmTrack track, float fadeDuration = 1.0f)
    {
        EnsureInstance().PlayBgmInternal(track, fadeDuration);
    }

    public static void FadeOutBgm(float duration = 0.8f)
    {
        EnsureInstance().PlayBgmInternal(BgmTrack.None, duration);
    }

    private void PlayBgmInternal(BgmTrack track, float fadeDuration)
    {
        if (currentTrack == track && GetActiveBgmSource().isPlaying) return;
        currentTrack = track;

        AudioClip nextClip = track switch
        {
            BgmTrack.Menu => LoadClip("bgm_menu"),
            BgmTrack.ForestCombat => LoadClip("bgm_forest_combat"),
            BgmTrack.Boss => LoadClip("bgm_boss"),
            _ => null
        };

        if (crossfadeRoutine != null) StopCoroutine(crossfadeRoutine);
        crossfadeRoutine = StartCoroutine(CrossfadeBgmCoroutine(nextClip, Mathf.Max(0.05f, fadeDuration)));
    }

    private AudioSource GetActiveBgmSource() => usingSourceA ? bgmSourceA : bgmSourceB;
    private AudioSource GetInactiveBgmSource() => usingSourceA ? bgmSourceB : bgmSourceA;

    private IEnumerator CrossfadeBgmCoroutine(AudioClip nextClip, float duration)
    {
        AudioSource fadeOut = GetActiveBgmSource();
        AudioSource fadeIn = GetInactiveBgmSource();

        float startOutVol = fadeOut != null ? fadeOut.volume : 0f;

        if (nextClip != null && fadeIn != null)
        {
            fadeIn.clip = nextClip;
            fadeIn.volume = 0f;
            fadeIn.loop = true;
            fadeIn.Play();
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);

            if (fadeOut != null && fadeOut.isPlaying)
            {
                fadeOut.volume = Mathf.Lerp(startOutVol, 0f, k);
            }
            if (nextClip != null && fadeIn != null)
            {
                fadeIn.volume = Mathf.Lerp(0f, bgmVolume, k);
            }

            yield return null;
        }

        if (fadeOut != null)
        {
            fadeOut.volume = 0f;
            fadeOut.Stop();
        }

        if (nextClip != null && fadeIn != null)
        {
            fadeIn.volume = bgmVolume;
            usingSourceA = !usingSourceA;
        }

        crossfadeRoutine = null;
    }

    // ==================== SFX PLAYBACK ====================

    private void PlaySfxClip(string clipName, float volumeScale = 1f, float pitchMin = 1f, float pitchMax = 1f)
    {
        if (isMuted) return;
        AudioClip clip = LoadClip(clipName);
        if (clip == null || sfxPool.Count == 0) return;

        AudioSource src = sfxPool[sfxPoolIndex];
        sfxPoolIndex = (sfxPoolIndex + 1) % sfxPool.Count;

        src.pitch = Mathf.Approximately(pitchMin, pitchMax) ? pitchMin : Random.Range(pitchMin, pitchMax);
        src.PlayOneShot(clip, Mathf.Clamp01(sfxVolume * volumeScale));
    }

    private AudioClip LoadClip(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return null;
        if (clipCache.TryGetValue(clipName, out AudioClip cached)) return cached;

        AudioClip loaded = Resources.Load<AudioClip>("Audio/" + clipName);
        clipCache[clipName] = loaded;
        return loaded;
    }

    // ==================== STATIC SFX SHORTCUTS ====================

    public static void PlaySwordSwing() => EnsureInstance().PlaySfxClip("sfx_sword_swing", 0.72f, 0.92f, 1.08f);
    public static void PlaySwordHit(bool heavy = false) => EnsureInstance().PlaySfxClip("sfx_sword_hit", heavy ? 1.0f : 0.85f, heavy ? 0.84f : 0.94f, heavy ? 0.96f : 1.08f);
    public static void PlayPlayerHurt() => EnsureInstance().PlaySfxClip("sfx_player_hurt", 0.95f, 0.92f, 1.05f);
    public static void PlayDash() => EnsureInstance().PlaySfxClip("sfx_dash", 0.78f, 0.95f, 1.12f);
    public static void PlayFootstep() => EnsureInstance().PlaySfxClip("sfx_footstep", 0.36f, 0.88f, 1.16f);
    public static void PlayOrcAttack() => EnsureInstance().PlaySfxClip("sfx_orc_attack", 0.65f, 0.88f, 1.06f);
    public static void PlayEnemyDeath(bool isBoss = false)
    {
        if (isBoss)
        {
            EnsureInstance().PlaySfxClip("sfx_boss_roar", 1.0f, 0.82f, 0.88f);
            EnsureInstance().PlaySfxClip("sfx_enemy_death", 0.95f, 0.78f, 0.82f);
        }
        else
        {
            EnsureInstance().PlaySfxClip("sfx_enemy_death", 0.82f, 0.92f, 1.08f);
        }
    }
    public static void PlayBossRoar() => EnsureInstance().PlaySfxClip("sfx_boss_roar", 1.0f, 0.95f, 1.05f);
    public static void PlayTrapHit() => EnsureInstance().PlaySfxClip("sfx_trap_hit", 0.90f, 0.95f, 1.06f);
    public static void PlayPickup() => EnsureInstance().PlaySfxClip("sfx_pickup", 0.82f, 0.98f, 1.06f);
    public static void PlayCheckpoint() => EnsureInstance().PlaySfxClip("sfx_checkpoint", 0.88f, 1f, 1f);
    public static void PlayLevelUp() => EnsureInstance().PlaySfxClip("sfx_level_up", 0.92f, 1f, 1f);
    public static void PlayWeaponEquip() => EnsureInstance().PlaySfxClip("sfx_weapon_equip", 0.88f, 0.96f, 1.05f);
    public static void PlayStageClear() => EnsureInstance().PlaySfxClip("sfx_stage_clear", 0.92f, 1f, 1f);
    public static void PlayVictory()
    {
        FadeOutBgm(0.6f);
        EnsureInstance().PlaySfxClip("sfx_victory", 0.95f, 1f, 1f);
    }
    public static void PlayGameOver()
    {
        FadeOutBgm(0.6f);
        EnsureInstance().PlaySfxClip("sfx_gameover", 0.92f, 1f, 1f);
    }
    public static void PlayUiClick() => EnsureInstance().PlaySfxClip("sfx_ui_click", 0.75f, 0.96f, 1.06f);
}
