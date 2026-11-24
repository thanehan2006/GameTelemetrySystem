using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Quản lý phát nhạc nền BGM chuyển cảnh mượt (crossfade) và âm thanh hiệu ứng SFX toàn game
public class GameAudioManager : MonoBehaviour
{
    public enum BgmTrack
    {
        None,
        Menu,
        ForestCombat,
        FrostCombat,
        InfernoCombat,
        Boss,
        BossForest,
        BossFrost,
        BossInferno
    }

    private static GameAudioManager instance;
    public static GameAudioManager Instance => EnsureInstance();

    private const string MasterMutedPrefKey = "Settings_MasterMuted";
    private const string MasterVolumePrefKey = "Settings_MasterVolume";
    private const string BgmMutedPrefKey = "Settings_BgmMuted";
    private const string BgmVolumePrefKey = "Settings_BgmVolume";
    private const string SfxMutedPrefKey = "Settings_SfxMuted";
    private const string SfxVolumePrefKey = "Settings_SfxVolume";

    public static event System.Action OnAudioSettingsChanged;

    [Header("Volume Settings")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float bgmVolume = 0.42f;
    [Range(0f, 1f)] public float sfxVolume = 0.85f;
    public bool isMuted = false;
    public bool isBgmMuted = false;
    public bool isSfxMuted = false;

    private bool settingsLoaded;
    private bool hasPendingPrefsSave;
    private AudioSource bgmSourceA;
    private AudioSource bgmSourceB;
    private bool usingSourceA = true;
    private BgmTrack currentTrack = BgmTrack.None;
    private Coroutine crossfadeRoutine;

    private readonly AudioSource[] sfxPool = new AudioSource[6];
    private int sfxPoolIndex;
    private readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();

    public static bool IsMasterMuted => EnsureInstance().isMuted;
    public static float MasterVolume => EnsureInstance().masterVolume;
    public static bool IsBgmMuted => EnsureInstance().isBgmMuted;
    public static float BgmVolume => EnsureInstance().bgmVolume;
    public static bool IsSfxMuted => EnsureInstance().isSfxMuted;
    public static float SfxVolume => EnsureInstance().sfxVolume;

    private float EffectiveBgmVolume => isBgmMuted ? 0f : Mathf.Clamp01(bgmVolume);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
        OnAudioSettingsChanged = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    // Tự động khởi tạo trình quản lý âm thanh trước khi scene đầu tiên chạy
    private static void AutoBootstrap()
    {
        EnsureInstance();
    }

    // Lấy hoặc tạo instance GameAudioManager tồn tại xuyên suốt các scene
    public static GameAudioManager EnsureInstance()
    {
        if (instance != null) return instance;

        instance = FindFirstObjectByType<GameAudioManager>();
        if (instance == null)
        {
            GameObject go = new GameObject("GameAudioManager");
            instance = go.AddComponent<GameAudioManager>();
        }

        instance.LoadSavedAudioSettings();
        instance.InitializeSources();
        instance.ApplyAudioListenerState();
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
        LoadSavedAudioSettings();
        InitializeSources();
        ApplyAudioListenerState();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        FlushPendingPrefs();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) FlushPendingPrefs();
    }

    private void OnApplicationQuit()
    {
        FlushPendingPrefs();
    }

    public static void FlushPendingPrefs()
    {
        if (instance != null && instance.hasPendingPrefsSave)
        {
            instance.hasPendingPrefsSave = false;
            PlayerPrefs.Save();
        }
    }

    private void Start()
    {
        SyncBgmForScene(SceneManager.GetActiveScene().name);
    }

    private void Update()
    {
        if (!IsTypingInTextField() && Input.GetKeyDown(KeyCode.M))
        {
            ToggleMute();
        }
    }

    private void LoadSavedAudioSettings()
    {
        if (settingsLoaded) return;
        settingsLoaded = true;

        isMuted = PlayerPrefs.GetInt(MasterMutedPrefKey, 0) != 0;
        masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumePrefKey, 1f));
        isBgmMuted = PlayerPrefs.GetInt(BgmMutedPrefKey, 0) != 0;
        bgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BgmVolumePrefKey, 0.42f));
        isSfxMuted = PlayerPrefs.GetInt(SfxMutedPrefKey, 0) != 0;
        sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumePrefKey, 0.85f));
    }

    private void ApplyAudioListenerState()
    {
        AudioListener.pause = false;
        AudioListener.volume = isMuted ? 0f : Mathf.Clamp01(masterVolume);
    }

    private void ApplyBgmVolumeImmediate()
    {
        if (isBgmMuted)
        {
            if (bgmSourceA != null) bgmSourceA.volume = 0f;
            if (bgmSourceB != null) bgmSourceB.volume = 0f;
            return;
        }

        if (crossfadeRoutine == null)
        {
            AudioSource active = GetActiveBgmSource();
            if (active != null && active.isPlaying)
            {
                active.volume = EffectiveBgmVolume;
            }
        }
    }

    public static void SetMasterMuted(bool muted)
    {
        GameAudioManager mgr = EnsureInstance();
        mgr.isMuted = muted;
        PlayerPrefs.SetInt(MasterMutedPrefKey, muted ? 1 : 0);
        mgr.hasPendingPrefsSave = false;
        PlayerPrefs.Save();
        mgr.ApplyAudioListenerState();
        OnAudioSettingsChanged?.Invoke();
    }

    public static void SetMasterVolume(float volume)
    {
        GameAudioManager mgr = EnsureInstance();
        mgr.masterVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(MasterVolumePrefKey, mgr.masterVolume);
        mgr.hasPendingPrefsSave = true;
        mgr.ApplyAudioListenerState();
        OnAudioSettingsChanged?.Invoke();
    }

    public static void SetBgmMuted(bool muted)
    {
        GameAudioManager mgr = EnsureInstance();
        mgr.isBgmMuted = muted;
        PlayerPrefs.SetInt(BgmMutedPrefKey, muted ? 1 : 0);
        mgr.hasPendingPrefsSave = false;
        PlayerPrefs.Save();
        mgr.ApplyBgmVolumeImmediate();
        OnAudioSettingsChanged?.Invoke();
    }

    public static void SetBgmVolume(float volume)
    {
        GameAudioManager mgr = EnsureInstance();
        mgr.bgmVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(BgmVolumePrefKey, mgr.bgmVolume);
        mgr.hasPendingPrefsSave = true;
        mgr.ApplyBgmVolumeImmediate();
        OnAudioSettingsChanged?.Invoke();
    }

    public static void SetSfxMuted(bool muted)
    {
        GameAudioManager mgr = EnsureInstance();
        mgr.isSfxMuted = muted;
        PlayerPrefs.SetInt(SfxMutedPrefKey, muted ? 1 : 0);
        mgr.hasPendingPrefsSave = false;
        PlayerPrefs.Save();
        OnAudioSettingsChanged?.Invoke();
    }

    public static void SetSfxVolume(float volume)
    {
        GameAudioManager mgr = EnsureInstance();
        mgr.sfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(SfxVolumePrefKey, mgr.sfxVolume);
        mgr.hasPendingPrefsSave = true;
        OnAudioSettingsChanged?.Invoke();
    }

    public static void ResetAudioDefaults()
    {
        GameAudioManager mgr = EnsureInstance();
        mgr.isMuted = false;
        mgr.masterVolume = 1f;
        mgr.isBgmMuted = false;
        mgr.bgmVolume = 0.42f;
        mgr.isSfxMuted = false;
        mgr.sfxVolume = 0.85f;

        PlayerPrefs.SetInt(MasterMutedPrefKey, 0);
        PlayerPrefs.SetFloat(MasterVolumePrefKey, 1f);
        PlayerPrefs.SetInt(BgmMutedPrefKey, 0);
        PlayerPrefs.SetFloat(BgmVolumePrefKey, 0.42f);
        PlayerPrefs.SetInt(SfxMutedPrefKey, 0);
        PlayerPrefs.SetFloat(SfxVolumePrefKey, 0.85f);
        mgr.hasPendingPrefsSave = false;
        PlayerPrefs.Save();

        mgr.ApplyAudioListenerState();
        mgr.ApplyBgmVolumeImmediate();
        OnAudioSettingsChanged?.Invoke();
    }

    private static bool IsTypingInTextField()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return false;

        GameObject selected = eventSystem.currentSelectedGameObject;
        if (selected == null) return false;

        if (selected.TryGetComponent(out TMP_InputField tmpInput) && tmpInput.isFocused)
        {
            return true;
        }

        if (selected.TryGetComponent(out InputField legacyInput) && legacyInput.isFocused)
        {
            return true;
        }

        return false;
    }

    private void InitializeSources()
    {
        if (bgmSourceA != null) return;

        bgmSourceA = CreateAudioSource("BGM_A", true);
        bgmSourceB = CreateAudioSource("BGM_B", true);

        for (int i = 0; i < sfxPool.Length; i++)
        {
            sfxPool[i] = CreateAudioSource($"SFX_{i}", false);
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

    // Tự động chọn và phát nhạc nền phù hợp theo từng scene hiện tại
    private void SyncBgmForScene(string sceneName)
    {
        if (sceneName == "LoadingScene" || sceneName == "LoginScene" || sceneName == "MainMenuScene" || sceneName == "PickCharacter" || sceneName == "PickMap")
        {
            PlayBgmInternal(BgmTrack.Menu, 0.8f);
        }
        else if (sceneName == "Lv1")
        {
            PlayBgmInternal(BgmTrack.ForestCombat, 1.0f);
        }
        else if (sceneName == "Lv2")
        {
            PlayBgmInternal(BgmTrack.FrostCombat, 1.0f);
        }
        else if (sceneName == "Lv3")
        {
            PlayBgmInternal(BgmTrack.InfernoCombat, 1.0f);
        }
    }

    // Bật/tắt toàn bộ âm thanh game bằng phím tắt
    public void ToggleMute()
    {
        SetMasterMuted(!isMuted);
        Debug.Log($"[GameAudioManager] Âm thanh: {(isMuted ? "TẮT (Muted - nhấn M để bật lại)" : "BẬT")}");
    }

    // Chọn track nhạc nền Boss tương ứng theo chỉ số bản đồ
    public static BgmTrack GetBossTrackForMap(int mapLevel)
    {
        return mapLevel switch
        {
            1 => BgmTrack.BossForest,
            2 => BgmTrack.BossFrost,
            3 => BgmTrack.BossInferno,
            _ => BgmTrack.Boss
        };
    }

    // Chọn track nhạc nền Boss tương ứng theo tên scene
    public static BgmTrack GetBossTrackForScene(string sceneName)
    {
        return sceneName switch
        {
            "Lv1" => BgmTrack.BossForest,
            "Lv2" => BgmTrack.BossFrost,
            "Lv3" => BgmTrack.BossInferno,
            _ => BgmTrack.Boss
        };
    }

    // Phát nhạc nền Boss riêng theo từng bản đồ
    public static void PlayBossBgmForMap(int mapLevel, float fadeDuration = 1.2f)
    {
        PlayBgm(GetBossTrackForMap(mapLevel), fadeDuration);
    }

    // Phát bản nhạc nền chỉ định với thời gian chuyển mượt
    public static void PlayBgm(BgmTrack track, float fadeDuration = 1.0f)
    {
        EnsureInstance().PlayBgmInternal(track, fadeDuration);
    }

    // Giảm dần âm lượng nhạc nền hiện tại
    public static void FadeOutBgm(float duration = 0.8f)
    {
        EnsureInstance().PlayBgmInternal(BgmTrack.None, duration);
    }

    private void PlayBgmInternal(BgmTrack track, float fadeDuration)
    {
        InitializeSources();

        if (track == BgmTrack.Boss)
        {
            track = GetBossTrackForScene(SceneManager.GetActiveScene().name);
        }

        if (currentTrack == track && track != BgmTrack.None)
        {
            AudioSource active = GetActiveBgmSource();
            if ((active != null && active.isPlaying) || crossfadeRoutine != null)
            {
                return;
            }
        }

        currentTrack = track;

        AudioClip nextClip = track switch
        {
            BgmTrack.Menu => LoadClip("bgm_menu"),
            BgmTrack.ForestCombat => LoadClip("bgm_forest_combat"),
            BgmTrack.FrostCombat => LoadClip("bgm_frost_combat"),
            BgmTrack.InfernoCombat => LoadClip("bgm_inferno_combat"),
            BgmTrack.BossForest => LoadClip("bgm_boss_forest"),
            BgmTrack.BossFrost => LoadClip("bgm_boss_frost"),
            BgmTrack.BossInferno => LoadClip("bgm_boss_inferno"),
            BgmTrack.Boss => LoadClip("bgm_boss"),
            _ => null
        };

        if (crossfadeRoutine != null) StopCoroutine(crossfadeRoutine);
        crossfadeRoutine = StartCoroutine(CrossfadeBgmCoroutine(nextClip, Mathf.Max(0.05f, fadeDuration)));
    }

    private AudioSource GetActiveBgmSource() => usingSourceA ? bgmSourceA : bgmSourceB;
    private AudioSource GetInactiveBgmSource() => usingSourceA ? bgmSourceB : bgmSourceA;

    // Chuyển âm lượng giữa kênh nhạc cũ và kênh nhạc mới
    private IEnumerator CrossfadeBgmCoroutine(AudioClip nextClip, float duration)
    {
        if (nextClip != null)
        {
            if (nextClip.loadState == AudioDataLoadState.Unloaded)
            {
                nextClip.LoadAudioData();
            }

            float waitElapsed = 0f;
            while (nextClip.loadState == AudioDataLoadState.Loading && waitElapsed < 5f)
            {
                waitElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (nextClip.loadState == AudioDataLoadState.Failed)
            {
                Debug.LogWarning($"[GameAudioManager] Không thể giải mã AudioClip '{nextClip.name}'.");
                nextClip = null;
            }
        }

        if (nextClip == null)
        {
            float startVolA = bgmSourceA != null && bgmSourceA.isPlaying ? bgmSourceA.volume : 0f;
            float startVolB = bgmSourceB != null && bgmSourceB.isPlaying ? bgmSourceB.volume : 0f;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / duration);

                if (bgmSourceA != null && bgmSourceA.isPlaying)
                {
                    bgmSourceA.volume = Mathf.Lerp(startVolA, 0f, k);
                }
                if (bgmSourceB != null && bgmSourceB.isPlaying)
                {
                    bgmSourceB.volume = Mathf.Lerp(startVolB, 0f, k);
                }

                yield return null;
            }

            if (bgmSourceA != null)
            {
                bgmSourceA.volume = 0f;
                bgmSourceA.Stop();
            }
            if (bgmSourceB != null)
            {
                bgmSourceB.volume = 0f;
                bgmSourceB.Stop();
            }

            crossfadeRoutine = null;
            yield break;
        }

        AudioSource fadeOut = GetActiveBgmSource();
        AudioSource fadeIn = GetInactiveBgmSource();
        usingSourceA = !usingSourceA;

        float startOutVol = fadeOut != null && fadeOut.isPlaying ? fadeOut.volume : 0f;

        if (fadeIn != null)
        {
            fadeIn.Stop();
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
            if (fadeIn != null)
            {
                fadeIn.volume = Mathf.Lerp(0f, EffectiveBgmVolume, k);
            }

            yield return null;
        }

        if (fadeOut != null)
        {
            fadeOut.volume = 0f;
            fadeOut.Stop();
        }

        if (fadeIn != null)
        {
            fadeIn.volume = EffectiveBgmVolume;
        }

        crossfadeRoutine = null;
    }

    private void PlaySfxClip(string clipName, float volumeScale = 1f, float pitchMin = 1f, float pitchMax = 1f)
    {
        if (isMuted || isSfxMuted) return;
        InitializeSources();

        AudioClip clip = LoadClip(clipName);
        if (clip == null || sfxPool.Length == 0) return;

        if (clip.loadState == AudioDataLoadState.Unloaded)
        {
            clip.LoadAudioData();
        }

        if (clip.loadState == AudioDataLoadState.Loading)
        {
            StartCoroutine(PlaySfxWhenLoadedCoroutine(clip, volumeScale, pitchMin, pitchMax));
            return;
        }

        if (clip.loadState == AudioDataLoadState.Loaded)
        {
            PlaySfxClipNow(clip, volumeScale, pitchMin, pitchMax);
        }
    }

    private IEnumerator PlaySfxWhenLoadedCoroutine(AudioClip clip, float volumeScale, float pitchMin, float pitchMax)
    {
        float waitElapsed = 0f;
        while (clip != null && clip.loadState == AudioDataLoadState.Loading && waitElapsed < 2f)
        {
            waitElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (clip != null && clip.loadState == AudioDataLoadState.Loaded && !isMuted && !isSfxMuted)
        {
            PlaySfxClipNow(clip, volumeScale, pitchMin, pitchMax);
        }
    }

    private void PlaySfxClipNow(AudioClip clip, float volumeScale, float pitchMin, float pitchMax)
    {
        if (clip == null || sfxPool.Length == 0) return;

        AudioSource src = sfxPool[sfxPoolIndex];
        sfxPoolIndex = (sfxPoolIndex + 1) % sfxPool.Length;
        if (src == null) return;

        src.pitch = Mathf.Approximately(pitchMin, pitchMax) ? pitchMin : Random.Range(pitchMin, pitchMax);
        src.PlayOneShot(clip, Mathf.Clamp01(sfxVolume * volumeScale));
    }

    public void RegisterPreloadedClip(string clipName, AudioClip clip)
    {
        if (string.IsNullOrEmpty(clipName) || clip == null) return;
        clipCache[clipName] = clip;
    }

    public bool HasCachedClip(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return false;
        return clipCache.TryGetValue(clipName, out AudioClip cached) && cached != null;
    }

    private AudioClip LoadClip(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return null;

        if (!clipCache.TryGetValue(clipName, out AudioClip clip) || clip == null)
        {
            clip = GameAssetPreloader.GetAsset<AudioClip>("Audio/" + clipName);
            if (clip == null)
            {
                clip = Resources.Load<AudioClip>("Audio/" + clipName);
            }

            if (clip != null)
            {
                clipCache[clipName] = clip;
            }
        }

        if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
        {
            clip.LoadAudioData();
        }

        return clip;
    }

    public static void PlaySwordSwing() => EnsureInstance().PlaySfxClip("sfx_sword_swing", 0.72f, 0.92f, 1.08f);
    public static void PlaySwordHit(bool heavy = false) => EnsureInstance().PlaySfxClip("sfx_sword_hit", heavy ? 1.0f : 0.85f, heavy ? 0.84f : 0.94f, heavy ? 0.96f : 1.08f);
    public static void PlayPlayerHurt() => EnsureInstance().PlaySfxClip("sfx_player_hurt", 0.95f, 0.92f, 1.05f);
    public static void PlayFootstep() => EnsureInstance().PlaySfxClip("sfx_footstep", 0.36f, 0.88f, 1.16f);
    public static void PlayOrcAttack() => EnsureInstance().PlaySfxClip("sfx_orc_attack", 0.65f, 0.88f, 1.06f);
    // Phát hiệu ứng âm thanh khi tiêu diệt quái thường hoặc Boss
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
    public static void PlayWeaponEquip() => EnsureInstance().PlaySfxClip("sfx_weapon_equip", 0.88f, 0.96f, 1.05f);
    public static void PlayStageClear() => EnsureInstance().PlaySfxClip("sfx_stage_clear", 0.92f, 1f, 1f);
    // Phát âm thanh chúc mừng chiến thắng
    public static void PlayVictory()
    {
        FadeOutBgm(0.6f);
        EnsureInstance().PlaySfxClip("sfx_victory", 0.95f, 1f, 1f);
    }
    // Phát âm thanh thông báo thất bại
    public static void PlayGameOver()
    {
        FadeOutBgm(0.6f);
        EnsureInstance().PlaySfxClip("sfx_gameover", 0.92f, 1f, 1f);
    }
    public static void PlayUiClick() => EnsureInstance().PlaySfxClip("sfx_ui_click", 0.75f, 0.96f, 1.06f);
public static void PlayUiHover() => EnsureInstance().PlaySfxClip("sfx_ui_click", 0.18f, 1.3f, 1.4f);
}
