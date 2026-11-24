using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public enum GraphicsPreset
{
    Low = 0,
    Medium = 1,
    High = 2
}

public enum DisplayModeOption
{
    Fullscreen = 0,
    Windowed = 1,
    Borderless = 2
}

public readonly struct ResolutionOption : IEquatable<ResolutionOption>
{
    public readonly int width;
    public readonly int height;

    public ResolutionOption(int width, int height)
    {
        this.width = width;
        this.height = height;
    }

    public string Label => $"{width}x{height}";

    public bool Equals(ResolutionOption other) => width == other.width && height == other.height;
    public override bool Equals(object obj) => obj is ResolutionOption other && Equals(other);
    public override int GetHashCode() => (width * 397) ^ height;
}

/// <summary>
/// Quản lý cài đặt đồ họa (Thấp / Trung Bình / Cao), độ phân giải, chế độ hiển thị,
/// VSync, độ nhạy chuột và lưu cấu hình bền vững qua PlayerPrefs.
/// </summary>
public static class GameSettingsManager
{
    private const string GraphicsPresetPrefKey = "Settings_GraphicsPreset";
    private const string FullscreenPrefKey = "Settings_Fullscreen";
    private const string DisplayModePrefKey = "Settings_DisplayMode";
    private const string ResWidthPrefKey = "Settings_ResWidth";
    private const string ResHeightPrefKey = "Settings_ResHeight";
    private const string VSyncPrefKey = "Settings_VSync";
    private const string MouseSensitivityPrefKey = "Settings_MouseSensitivity";
    private const string ShowFpsPrefKey = "Settings_ShowFps";

    public static event Action OnSettingsChanged;

    public static GraphicsPreset CurrentGraphicsPreset { get; private set; } = GraphicsPreset.High;
    public static DisplayModeOption CurrentDisplayMode { get; private set; } = DisplayModeOption.Fullscreen;
    public static bool IsFullscreen { get; private set; } = true;
    public static int ResolutionWidth { get; private set; } = 1920;
    public static int ResolutionHeight { get; private set; } = 1080;
    public static bool VSyncEnabled { get; private set; } = true;
    public static float MouseSensitivityMultiplier { get; private set; } = 1.0f;
    public static bool ShowFpsCounter { get; private set; } = false;

    private static bool initialized;
    private static bool sceneHooked;

    private struct FeatureActiveState
    {
        public ScriptableRendererFeature feature;
        public bool active;
    }

    private static UniversalRenderPipelineAsset baselineUrpAsset;
    private static bool hasBaselineUrp;
    private static float baselineRenderScale = 1f;
    private static int baselineMsaa = 4;
    private static float baselineShadowDistance = 75f;
    private static int baselineShadowCascades = 4;
    private static int baselineMaxAdditionalLights = 8;
    private static readonly List<FeatureActiveState> BaselineAoStates = new List<FeatureActiveState>(4);
    private static bool hasUnsavedPrefs;

    private static Camera cachedCam;
    private static float cachedCamOriginalFarClip = 340f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        initialized = false;
        sceneHooked = false;
        hasBaselineUrp = false;
        baselineUrpAsset = null;
        BaselineAoStates.Clear();
        hasUnsavedPrefs = false;
        cachedCam = null;
        OnSettingsChanged = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeOnBoot()
    {
        EnsureInitialized();

        if (!sceneHooked)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.quitting += FlushPendingPrefs;
            sceneHooked = true;
#if UNITY_EDITOR
            Application.quitting += RestoreUrpBaselineInEditor;
#endif
        }
    }

    public static void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        int savedPreset = PlayerPrefs.GetInt(GraphicsPresetPrefKey, (int)GraphicsPreset.High);
        CurrentGraphicsPreset = (GraphicsPreset)Mathf.Clamp(savedPreset, 0, 2);

        if (PlayerPrefs.HasKey(DisplayModePrefKey))
        {
            CurrentDisplayMode = (DisplayModeOption)Mathf.Clamp(PlayerPrefs.GetInt(DisplayModePrefKey, 0), 0, 2);
        }
        else
        {
            bool savedFull = PlayerPrefs.GetInt(FullscreenPrefKey, 1) != 0;
            CurrentDisplayMode = savedFull ? DisplayModeOption.Fullscreen : DisplayModeOption.Windowed;
        }
        IsFullscreen = CurrentDisplayMode != DisplayModeOption.Windowed;

        GetDefaultNativeResolution(out int defW, out int defH);
        int savedW = PlayerPrefs.GetInt(ResWidthPrefKey, defW);
        int savedH = PlayerPrefs.GetInt(ResHeightPrefKey, defH);
        ResolutionWidth = savedW >= 800 ? savedW : defW;
        ResolutionHeight = savedH >= 600 ? savedH : defH;

        VSyncEnabled = PlayerPrefs.GetInt(VSyncPrefKey, 1) != 0;
        MouseSensitivityMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat(MouseSensitivityPrefKey, 1.0f), 0.5f, 2.5f);
        ShowFpsCounter = PlayerPrefs.GetInt(ShowFpsPrefKey, 0) != 0;

        CaptureUrpBaseline();
        ApplyDisplaySettings(adjustResolutionForWindow: PlayerPrefs.HasKey(ResWidthPrefKey));
        ApplyCurrentGraphicsSettings();
    }

    public static List<ResolutionOption> GetSupportedResolutions()
    {
        EnsureInitialized();

        List<ResolutionOption> list = new List<ResolutionOption>();
        HashSet<long> seen = new HashSet<long>();

        void TryAdd(int w, int h)
        {
            if (w < 1024 || h < 600) return;
            long key = ((long)w << 32) | (uint)h;
            if (seen.Add(key))
            {
                list.Add(new ResolutionOption(w, h));
            }
        }

        Resolution[] systemRes = Screen.resolutions;
        if (systemRes != null)
        {
            for (int i = 0; i < systemRes.Length; i++)
            {
                TryAdd(systemRes[i].width, systemRes[i].height);
            }
        }

        int maxW = Display.main != null ? Display.main.systemWidth : Screen.currentResolution.width;
        int maxH = Display.main != null ? Display.main.systemHeight : Screen.currentResolution.height;
        if (maxW < 1280) maxW = 1920;
        if (maxH < 720) maxH = 1080;

        int[] stdW = { 1920, 1600, 1366, 1280 };
        int[] stdH = { 1080, 900, 768, 720 };
        for (int i = 0; i < stdW.Length; i++)
        {
            if (Application.isEditor || (stdW[i] <= maxW && stdH[i] <= maxH))
            {
                TryAdd(stdW[i], stdH[i]);
            }
        }

        TryAdd(ResolutionWidth, ResolutionHeight);

        list.Sort((a, b) =>
        {
            int areaCmp = (b.width * b.height).CompareTo(a.width * a.height);
            return areaCmp != 0 ? areaCmp : b.width.CompareTo(a.width);
        });
        return list;
    }

    public static void SetGraphicsPreset(GraphicsPreset preset, bool save = true)
    {
        EnsureInitialized();
        CurrentGraphicsPreset = preset;

        if (save)
        {
            PlayerPrefs.SetInt(GraphicsPresetPrefKey, (int)preset);
            PlayerPrefs.Save();
        }

        ApplyCurrentGraphicsSettings();
        OnSettingsChanged?.Invoke();
    }

    public static void SetDisplayMode(DisplayModeOption mode, bool save = true)
    {
        EnsureInitialized();
        CurrentDisplayMode = mode;
        IsFullscreen = mode != DisplayModeOption.Windowed;

        if (save)
        {
            PlayerPrefs.SetInt(DisplayModePrefKey, (int)mode);
            PlayerPrefs.SetInt(FullscreenPrefKey, IsFullscreen ? 1 : 0);
            PlayerPrefs.Save();
        }

        ApplyDisplaySettings(adjustResolutionForWindow: true);
        OnSettingsChanged?.Invoke();
    }

    public static void SetFullscreen(bool fullscreen, bool save = true)
    {
        SetDisplayMode(fullscreen ? DisplayModeOption.Fullscreen : DisplayModeOption.Windowed, save);
    }

    public static void SetResolution(int width, int height, bool save = true)
    {
        EnsureInitialized();
        if (width < 800 || height < 600) return;

        ResolutionWidth = width;
        ResolutionHeight = height;

        if (save)
        {
            PlayerPrefs.SetInt(ResWidthPrefKey, ResolutionWidth);
            PlayerPrefs.SetInt(ResHeightPrefKey, ResolutionHeight);
            PlayerPrefs.Save();
        }

        ApplyDisplaySettings(adjustResolutionForWindow: true);
        OnSettingsChanged?.Invoke();
    }

    public static void SetVSync(bool enabled, bool save = true)
    {
        EnsureInitialized();
        VSyncEnabled = enabled;

        if (save)
        {
            PlayerPrefs.SetInt(VSyncPrefKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        ApplyVSyncSettings();
        OnSettingsChanged?.Invoke();
    }

    public static void SetMouseSensitivity(float multiplier, bool save = true)
    {
        EnsureInitialized();
        MouseSensitivityMultiplier = Mathf.Clamp(multiplier, 0.5f, 2.5f);

        if (save)
        {
            PlayerPrefs.SetFloat(MouseSensitivityPrefKey, MouseSensitivityMultiplier);
            hasUnsavedPrefs = true;
        }

        OnSettingsChanged?.Invoke();
    }

    public static void FlushPendingPrefs()
    {
        if (!hasUnsavedPrefs) return;
        hasUnsavedPrefs = false;
        PlayerPrefs.Save();
    }

    public static void SetShowFps(bool show, bool save = true)
    {
        EnsureInitialized();
        ShowFpsCounter = show;

        if (save)
        {
            PlayerPrefs.SetInt(ShowFpsPrefKey, show ? 1 : 0);
            PlayerPrefs.Save();
        }

        OnSettingsChanged?.Invoke();
    }

    public static void ApplyAndSaveAll()
    {
        EnsureInitialized();

        PlayerPrefs.SetInt(GraphicsPresetPrefKey, (int)CurrentGraphicsPreset);
        PlayerPrefs.SetInt(DisplayModePrefKey, (int)CurrentDisplayMode);
        PlayerPrefs.SetInt(FullscreenPrefKey, IsFullscreen ? 1 : 0);
        PlayerPrefs.SetInt(ResWidthPrefKey, ResolutionWidth);
        PlayerPrefs.SetInt(ResHeightPrefKey, ResolutionHeight);
        PlayerPrefs.SetInt(VSyncPrefKey, VSyncEnabled ? 1 : 0);
        PlayerPrefs.SetFloat(MouseSensitivityPrefKey, MouseSensitivityMultiplier);
        PlayerPrefs.SetInt(ShowFpsPrefKey, ShowFpsCounter ? 1 : 0);
        hasUnsavedPrefs = false;
        PlayerPrefs.Save();

        ApplyDisplaySettings(adjustResolutionForWindow: true);
        ApplyCurrentGraphicsSettings();
        OnSettingsChanged?.Invoke();
    }

    public static void ResetAllDefaults()
    {
        EnsureInitialized();

        CurrentGraphicsPreset = GraphicsPreset.High;
        CurrentDisplayMode = DisplayModeOption.Fullscreen;
        IsFullscreen = true;
        GetDefaultNativeResolution(out int defW, out int defH);
        ResolutionWidth = defW;
        ResolutionHeight = defH;
        VSyncEnabled = true;
        MouseSensitivityMultiplier = 1.0f;
        ShowFpsCounter = false;

        PlayerPrefs.SetInt(GraphicsPresetPrefKey, (int)CurrentGraphicsPreset);
        PlayerPrefs.SetInt(DisplayModePrefKey, (int)CurrentDisplayMode);
        PlayerPrefs.SetInt(FullscreenPrefKey, 1);
        PlayerPrefs.SetInt(ResWidthPrefKey, ResolutionWidth);
        PlayerPrefs.SetInt(ResHeightPrefKey, ResolutionHeight);
        PlayerPrefs.SetInt(VSyncPrefKey, 1);
        PlayerPrefs.SetFloat(MouseSensitivityPrefKey, 1.0f);
        PlayerPrefs.SetInt(ShowFpsPrefKey, 0);
        hasUnsavedPrefs = false;
        PlayerPrefs.Save();

        ApplyDisplaySettings(adjustResolutionForWindow: true);
        ApplyCurrentGraphicsSettings();
        GameAudioManager.ResetAudioDefaults();
        OnSettingsChanged?.Invoke();
    }

    public static string GetGraphicsPresetLabel(GraphicsPreset preset)
    {
        return preset switch
        {
            GraphicsPreset.Low => "THẤP",
            GraphicsPreset.Medium => "TRUNG BÌNH",
            _ => "CAO"
        };
    }

    public static string GetGraphicsPresetDescription(GraphicsPreset preset)
    {
        return preset switch
        {
            GraphicsPreset.Low => "Ưu tiên FPS cao nhất • Giảm bóng, tắt SSAO/MSAA/hậu kỳ, tối ưu địa hình cho máy yếu",
            GraphicsPreset.Medium => "Cân bằng đẹp & mượt • MSAA 2x, FXAA, tắt SSAO, bóng 48m, hậu kỳ chuẩn",
            _ => "Đồ họa sắc nét & mượt • MSAA 2x + FXAA, SSAO, bóng 2048px 3 tầng, ACES HDR"
        };
    }

    public static void ApplyCurrentGraphicsSettings()
    {
        EnsureInitialized();
        ApplyVSyncSettings();

        UniversalRenderPipelineAsset urp = GetActiveUrpAsset();
        string sceneName = SceneManager.GetActiveScene().name;
        bool isMenu2DScene = sceneName == "LoadingScene" || sceneName == "LoginScene" || sceneName == "MainMenuScene";

        Camera mainCam = Camera.main;
        if (mainCam != null && mainCam != cachedCam)
        {
            cachedCam = mainCam;
            cachedCamOriginalFarClip = mainCam.farClipPlane;
        }

        UniversalAdditionalCameraData camData = mainCam != null
            ? mainCam.GetComponent<UniversalAdditionalCameraData>()
            : null;

        Terrain terrain = Terrain.activeTerrain;

        switch (CurrentGraphicsPreset)
        {
            case GraphicsPreset.Low:
                if (urp != null)
                {
                    urp.renderScale = 0.85f;
                    urp.msaaSampleCount = 1;
                    urp.shadowDistance = 28f;
                    urp.shadowCascadeCount = 1;
                    urp.maxAdditionalLightsCount = 2;
                    SetAmbientOcclusionActive(urp, false);
                }

                QualitySettings.shadowDistance = 28f;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                QualitySettings.lodBias = 0.75f;
                QualitySettings.softParticles = false;
                QualitySettings.globalTextureMipmapLimit = 1;

                if (mainCam != null)
                {
                    mainCam.farClipPlane = Mathf.Min(cachedCamOriginalFarClip, 220f);
                }
                if (camData != null)
                {
                    camData.renderPostProcessing = false;
                    camData.antialiasing = AntialiasingMode.None;
                }

                if (terrain != null)
                {
                    terrain.drawInstanced = true;
                    terrain.heightmapPixelError = 16f;
                    terrain.treeDistance = 120f;
                    terrain.treeBillboardDistance = 32f;
                    terrain.treeMaximumFullLODCount = 20;
                    terrain.detailObjectDistance = 35f;
                    terrain.detailObjectDensity = 0.30f;
                    terrain.basemapDistance = 150f;
                    terrain.shadowCastingMode = ShadowCastingMode.Off;
                }
                break;

            case GraphicsPreset.Medium:
                if (urp != null)
                {
                    urp.renderScale = 1.0f;
                    urp.msaaSampleCount = 2;
                    urp.shadowDistance = 48f;
                    urp.shadowCascadeCount = 2;
                    urp.maxAdditionalLightsCount = 3;
                    SetAmbientOcclusionActive(urp, false);
                }

                QualitySettings.shadowDistance = 48f;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                QualitySettings.lodBias = 1.05f;
                QualitySettings.softParticles = true;
                QualitySettings.globalTextureMipmapLimit = 0;

                if (mainCam != null)
                {
                    mainCam.farClipPlane = Mathf.Min(cachedCamOriginalFarClip, 280f);
                }
                if (camData != null)
                {
                    camData.renderPostProcessing = !isMenu2DScene;
                    camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                }

                if (terrain != null)
                {
                    terrain.drawInstanced = true;
                    terrain.heightmapPixelError = 10f;
                    terrain.treeDistance = 165f;
                    terrain.treeBillboardDistance = 52f;
                    terrain.treeMaximumFullLODCount = 42;
                    terrain.detailObjectDistance = 55f;
                    terrain.detailObjectDensity = 0.50f;
                    terrain.basemapDistance = 220f;
                    terrain.shadowCastingMode = ShadowCastingMode.Off;
                }
                break;

            case GraphicsPreset.High:
            default:
                if (urp != null)
                {
                    urp.renderScale = 1.0f;
                    urp.msaaSampleCount = 2;
                    urp.shadowDistance = 65f;
                    urp.shadowCascadeCount = 3;
                    urp.maxAdditionalLightsCount = 4;
                    SetAmbientOcclusionActive(urp, !isMenu2DScene);
                }

                QualitySettings.shadowDistance = 65f;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                QualitySettings.lodBias = 1.35f;
                QualitySettings.softParticles = true;
                QualitySettings.globalTextureMipmapLimit = 0;

                if (mainCam != null)
                {
                    mainCam.farClipPlane = Mathf.Min(cachedCamOriginalFarClip, 340f);
                }
                if (camData != null)
                {
                    camData.renderPostProcessing = !isMenu2DScene;
                    camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                    camData.stopNaN = false;
                }

                if (terrain != null)
                {
                    terrain.drawInstanced = true;
                    terrain.heightmapPixelError = 7f;
                    terrain.treeDistance = 210f;
                    terrain.treeBillboardDistance = 80f;
                    terrain.treeMaximumFullLODCount = 60;
                    terrain.detailObjectDistance = 75f;
                    terrain.detailObjectDensity = 0.65f;
                    terrain.basemapDistance = 300f;
                    terrain.shadowCastingMode = ShadowCastingMode.Off;
                }
                break;
        }
    }

    private static void GetDefaultNativeResolution(out int w, out int h)
    {
        int sysW = Display.main != null ? Display.main.systemWidth : Screen.currentResolution.width;
        int sysH = Display.main != null ? Display.main.systemHeight : Screen.currentResolution.height;
        w = sysW >= 1024 ? sysW : 1920;
        h = sysH >= 600 ? sysH : 1080;
    }

    private static void ApplyVSyncSettings()
    {
        QualitySettings.vSyncCount = VSyncEnabled ? 1 : 0;
        Application.targetFrameRate = VSyncEnabled ? -1 : 120;
    }

    private static void ApplyDisplaySettings(bool adjustResolutionForWindow)
    {
        if (Application.isEditor) return;

        FullScreenMode targetMode = CurrentDisplayMode switch
        {
            DisplayModeOption.Windowed => FullScreenMode.Windowed,
            DisplayModeOption.Borderless => FullScreenMode.FullScreenWindow,
            _ => FullScreenMode.ExclusiveFullScreen
        };

        Screen.fullScreenMode = targetMode;
        Screen.fullScreen = CurrentDisplayMode != DisplayModeOption.Windowed;

        if (!adjustResolutionForWindow) return;

        int sysW = Display.main != null ? Display.main.systemWidth : Screen.currentResolution.width;
        int sysH = Display.main != null ? Display.main.systemHeight : Screen.currentResolution.height;
        int targetW = ResolutionWidth > 0 ? ResolutionWidth : (sysW > 0 ? sysW : 1920);
        int targetH = ResolutionHeight > 0 ? ResolutionHeight : (sysH > 0 ? sysH : 1080);

        if (CurrentDisplayMode == DisplayModeOption.Windowed && sysW > 0 && sysH > 0
            && (targetW >= sysW - 20 || targetH >= sysH - 20))
        {
            targetW = sysW >= 1920 ? 1600 : 1280;
            targetH = sysW >= 1920 ? 900 : 720;
        }

        Screen.SetResolution(targetW, targetH, targetMode);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        cachedCam = null;
        ApplyCurrentGraphicsSettings();
    }

    private static UniversalRenderPipelineAsset GetActiveUrpAsset()
    {
        UniversalRenderPipelineAsset urp = UniversalRenderPipeline.asset;
        if (urp == null)
        {
            urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        }
        return urp;
    }

    private static void SetAmbientOcclusionActive(UniversalRenderPipelineAsset urp, bool active)
    {
        if (urp == null) return;

        ReadOnlySpan<ScriptableRendererData> dataList = urp.rendererDataList;
        for (int i = 0; i < dataList.Length; i++)
        {
            ScriptableRendererData data = dataList[i];
            if (data == null || data.rendererFeatures == null) continue;

            bool changed = false;
            List<ScriptableRendererFeature> features = data.rendererFeatures;
            for (int f = 0; f < features.Count; f++)
            {
                ScriptableRendererFeature feature = features[f];
                if (feature == null) continue;

                string typeName = feature.GetType().Name;
                if (typeName.IndexOf("AmbientOcclusion", StringComparison.OrdinalIgnoreCase) >= 0
                    || feature.name.IndexOf("AmbientOcclusion", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (feature.isActive != active)
                    {
                        feature.SetActive(active);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                data.SetDirty();
            }
        }
    }

    private static void CaptureUrpBaseline()
    {
        if (hasBaselineUrp) return;

        UniversalRenderPipelineAsset urp = GetActiveUrpAsset();
        if (urp == null) return;

        baselineUrpAsset = urp;
        baselineRenderScale = urp.renderScale;
        baselineMsaa = urp.msaaSampleCount;
        baselineShadowDistance = urp.shadowDistance;
        baselineShadowCascades = urp.shadowCascadeCount;
        baselineMaxAdditionalLights = urp.maxAdditionalLightsCount;

        BaselineAoStates.Clear();
        ReadOnlySpan<ScriptableRendererData> dataList = urp.rendererDataList;
        for (int i = 0; i < dataList.Length; i++)
        {
            ScriptableRendererData data = dataList[i];
            if (data == null || data.rendererFeatures == null) continue;
            List<ScriptableRendererFeature> features = data.rendererFeatures;
            for (int f = 0; f < features.Count; f++)
            {
                ScriptableRendererFeature feature = features[f];
                if (feature == null) continue;
                string typeName = feature.GetType().Name;
                if (typeName.IndexOf("AmbientOcclusion", StringComparison.OrdinalIgnoreCase) >= 0
                    || feature.name.IndexOf("AmbientOcclusion", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    BaselineAoStates.Add(new FeatureActiveState
                    {
                        feature = feature,
                        active = feature.isActive
                    });
                }
            }
        }

        hasBaselineUrp = true;
    }

#if UNITY_EDITOR
    private static void RestoreUrpBaselineInEditor()
    {
        Application.quitting -= RestoreUrpBaselineInEditor;
        if (!hasBaselineUrp || baselineUrpAsset == null) return;

        baselineUrpAsset.renderScale = baselineRenderScale;
        baselineUrpAsset.msaaSampleCount = baselineMsaa;
        baselineUrpAsset.shadowDistance = baselineShadowDistance;
        baselineUrpAsset.shadowCascadeCount = baselineShadowCascades;
        baselineUrpAsset.maxAdditionalLightsCount = baselineMaxAdditionalLights;

        for (int i = 0; i < BaselineAoStates.Count; i++)
        {
            FeatureActiveState state = BaselineAoStates[i];
            if (state.feature != null && state.feature.isActive != state.active)
            {
                state.feature.SetActive(state.active);
            }
        }

        ReadOnlySpan<ScriptableRendererData> dataList = baselineUrpAsset.rendererDataList;
        for (int i = 0; i < dataList.Length; i++)
        {
            if (dataList[i] != null) dataList[i].SetDirty();
        }
        BaselineAoStates.Clear();
    }
#endif
}
