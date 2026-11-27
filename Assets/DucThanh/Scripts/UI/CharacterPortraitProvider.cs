using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Tạo và cache ảnh chân dung 3D từ modelPrefab của 4 lớp nhân vật trong CharacterRosterConfig
/// (hoặc dùng ảnh 2D tùy chọn trong Resources/UI/Portraits/{id} nếu có).
/// </summary>
public static class CharacterPortraitProvider
{
    private const int PortraitSize = 320;
    private const int PortraitLayer = 31;

    private static readonly Dictionary<string, Texture2D> PortraitCache =
        new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<Texture2D> RuntimeGeneratedTextures = new HashSet<Texture2D>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        foreach (Texture2D tex in RuntimeGeneratedTextures)
        {
            if (tex != null)
            {
                UnityEngine.Object.Destroy(tex);
            }
        }
        RuntimeGeneratedTextures.Clear();
        PortraitCache.Clear();
    }

    public static CharacterProfile FindRosterProfile(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId)) return null;

        CharacterRosterConfig roster = SelectedCharacterStore.GetRoster();
        if (roster == null || roster.characters == null) return null;

        string target = characterId.Trim();
        for (int i = 0; i < roster.characters.Length; i++)
        {
            CharacterProfile p = roster.characters[i];
            if (p != null && string.Equals(p.id, target, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }
        return null;
    }

    public static Texture2D GetPortrait(string characterId, Color accentColor)
    {
        string key = string.IsNullOrWhiteSpace(characterId)
            ? "unknown"
            : characterId.Trim().ToLowerInvariant();

        if (PortraitCache.TryGetValue(key, out Texture2D cached) && cached != null)
        {
            return cached;
        }

        // Cho phép ghi đè bằng ảnh 2D đặt trong Resources/UI/Portraits/{id} nếu cần
        Texture2D customTex = Resources.Load<Texture2D>("UI/Portraits/" + key);
        if (customTex != null)
        {
            PortraitCache[key] = customTex;
            return customTex;
        }

        CharacterProfile profile = FindRosterProfile(key);
        Texture2D generated = null;

        if (profile != null && profile.modelPrefab != null)
        {
            generated = CaptureStudioPortrait(profile);
        }

        if (generated == null)
        {
            generated = CreateCrestFallbackTexture(accentColor, key == "unknown");
        }

        if (generated != null)
        {
            RuntimeGeneratedTextures.Add(generated);
            PortraitCache[key] = generated;
        }
        return generated;
    }

    private static Texture2D CaptureStudioPortrait(CharacterProfile profile)
    {
        Vector3 studioPos = new Vector3(0f, -450f, 0f);
        List<Light> disabledSceneLights = new List<Light>();
        List<GameObject> tempObjects = new List<GameObject>();
        RenderTexture rt = null;
        RenderTexture prevActive = RenderTexture.active;
        Camera cam = null;

        bool prevFog = RenderSettings.fog;
        AmbientMode prevAmbientMode = RenderSettings.ambientMode;
        Color prevAmbientLight = RenderSettings.ambientLight;

        try
        {
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.26f, 0.26f, 0.30f, 1f);

            // Tắt tạm các đèn ngoài scene để studio 3 điểm giữ ánh sáng nhất quán trên mọi màn
            Light[] existingLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < existingLights.Length; i++)
            {
                Light l = existingLights[i];
                if (l != null && l.enabled)
                {
                    l.enabled = false;
                    disabledSceneLights.Add(l);
                }
            }

            GameObject model = UnityEngine.Object.Instantiate(profile.modelPrefab, studioPos, Quaternion.identity);
            tempObjects.Add(model);
            model.name = "PortraitStudioModel_" + profile.id;
            model.SetActive(true);

            PrepareStudioModel(model);

            Transform headBone = FindHeadBone(model);
            float modelHeight = 1.85f;
            Bounds totalBounds = default;
            bool hasHeadHeight = false;

            if (headBone != null)
            {
                float headHeight = headBone.position.y - studioPos.y;
                if (headHeight > 0.35f && headHeight < 4.0f)
                {
                    modelHeight = Mathf.Clamp(headHeight / 0.84f, 1.0f, 3.5f);
                    hasHeadHeight = true;
                }
            }

            if (!hasHeadHeight)
            {
                totalBounds = CalculateRendererBounds(model, studioPos);
                modelHeight = totalBounds.size.y < 0.25f
                    ? 1.85f
                    : Mathf.Clamp(totalBounds.size.y, 1.0f, 4.0f);
            }

            Vector3 focus = headBone != null
                ? headBone.position - Vector3.up * (modelHeight * 0.055f)
                : new Vector3(totalBounds.center.x, totalBounds.min.y + modelHeight * 0.80f, totalBounds.center.z);

            GameObject camGo = new GameObject("PortraitStudioCamera", typeof(Camera));
            tempObjects.Add(camGo);
            cam = camGo.GetComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(
                Mathf.Lerp(0.055f, profile.themeColor.r, 0.16f),
                Mathf.Lerp(0.050f, profile.themeColor.g, 0.16f),
                Mathf.Lerp(0.058f, profile.themeColor.b, 0.16f),
                1f);
            cam.fieldOfView = 28f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            cam.cullingMask = 1 << PortraitLayer;
            cam.useOcclusionCulling = false;
            cam.forceIntoRenderTexture = true;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            UniversalAdditionalCameraData urpCam = camGo.GetComponent<UniversalAdditionalCameraData>()
                ?? camGo.AddComponent<UniversalAdditionalCameraData>();
            urpCam.renderType = CameraRenderType.Base;
            urpCam.renderPostProcessing = false;
            urpCam.renderShadows = false;
            urpCam.antialiasing = AntialiasingMode.None;
            urpCam.requiresDepthOption = CameraOverrideOption.Off;
            urpCam.requiresColorOption = CameraOverrideOption.Off;
            urpCam.volumeLayerMask = 0;
            urpCam.stopNaN = false;
            urpCam.dithering = false;

            camGo.transform.position = focus + new Vector3(modelHeight * 0.14f, modelHeight * 0.03f, modelHeight * 0.56f);
            camGo.transform.LookAt(focus, Vector3.up);

            // Studio 3 điểm: Key (ấm), Fill (lạnh nhẹ), Rim (viền màu tướng)
            tempObjects.Add(CreateStudioLight("StudioKeyLight", new Vector3(22f, 158f, 0f), new Color(1f, 0.94f, 0.84f), 1.9f));
            tempObjects.Add(CreateStudioLight("StudioFillLight", new Vector3(12f, -145f, 0f), new Color(0.70f, 0.82f, 1f), 0.78f));
            Color rimCol = Color.Lerp(Color.white, profile.themeColor, 0.75f);
            tempObjects.Add(CreateStudioLight("StudioRimLight", new Vector3(18f, 25f, 0f), rimCol, 1.65f));

            RenderTextureDescriptor desc = new RenderTextureDescriptor(PortraitSize, PortraitSize, RenderTextureFormat.ARGB32, 24)
            {
                sRGB = true,
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            rt = RenderTexture.GetTemporary(desc);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(PortraitSize, PortraitSize, TextureFormat.RGBA32, false, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.ReadPixels(new Rect(0, 0, PortraitSize, PortraitSize), 0, 0, false);

            ApplyBottomVignette(tex, profile.themeColor);
            tex.Apply(false, true);
            return tex;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CharacterPortraitProvider] Không thể chụp chân dung 3D cho '{profile.id}': {ex.Message}");
            return null;
        }
        finally
        {
            if (cam != null)
            {
                cam.targetTexture = null;
            }
            RenderTexture.active = prevActive;
            if (rt != null)
            {
                RenderTexture.ReleaseTemporary(rt);
            }

            RenderSettings.fog = prevFog;
            RenderSettings.ambientMode = prevAmbientMode;
            RenderSettings.ambientLight = prevAmbientLight;

            for (int i = 0; i < tempObjects.Count; i++)
            {
                if (tempObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(tempObjects[i]);
                }
            }

            for (int i = 0; i < disabledSceneLights.Count; i++)
            {
                if (disabledSceneLights[i] != null)
                {
                    disabledSceneLights[i].enabled = true;
                }
            }
        }
    }

    private static void PrepareStudioModel(GameObject root)
    {
        Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allTransforms.Length; i++)
        {
            if (allTransforms[i] != null)
            {
                allTransforms[i].gameObject.layer = PortraitLayer;
            }
        }

        Renderer[] allRenderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < allRenderers.Length; i++)
        {
            Renderer r = allRenderers[i];
            if (r == null) continue;
            r.enabled = true;
            r.forceRenderingOff = false;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.renderingLayerMask = 1u;
        }

        SkinnedMeshRenderer[] skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            if (skinned[i] != null)
            {
                skinned[i].updateWhenOffscreen = true;
                skinned[i].forceMatrixRecalculationPerRender = true;
            }
        }

        Collider[] cols = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null) cols[i].enabled = false;
        }

        Rigidbody[] rbs = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rbs.Length; i++)
        {
            if (rbs[i] != null) rbs[i].isKinematic = true;
        }

        AudioSource[] audio = root.GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < audio.Length; i++)
        {
            if (audio[i] != null) audio[i].enabled = false;
        }

        Light[] embeddedLights = root.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < embeddedLights.Length; i++)
        {
            if (embeddedLights[i] != null) embeddedLights[i].enabled = false;
        }

        Camera[] embeddedCams = root.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < embeddedCams.Length; i++)
        {
            if (embeddedCams[i] != null) embeddedCams[i].enabled = false;
        }

        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null) behaviours[i].enabled = false;
        }

        Animator anim = root.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (anim.runtimeAnimatorController != null)
            {
                anim.Update(0f);
            }
        }
    }

    private static Bounds CalculateRendererBounds(GameObject root, Vector3 fallbackCenter)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasAny = false;
        Bounds combined = new Bounds(fallbackCenter + Vector3.up * 0.9f, new Vector3(1f, 1.8f, 1f));

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null || !r.enabled) continue;
            if (!hasAny)
            {
                combined = r.bounds;
                hasAny = true;
            }
            else
            {
                combined.Encapsulate(r.bounds);
            }
        }
        return combined;
    }

    private static Transform FindHeadBone(GameObject root)
    {
        Animator anim = root.GetComponentInChildren<Animator>(true);
        if (anim != null && anim.isHuman)
        {
            Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) return head;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            Transform t = children[i];
            if (t == null) continue;
            string n = t.name;
            if (n.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0
                && n.IndexOf("top", StringComparison.OrdinalIgnoreCase) < 0
                && n.IndexOf("end", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return t;
            }
        }
        return null;
    }

    private static GameObject CreateStudioLight(string name, Vector3 euler, Color color, float intensity)
    {
        GameObject go = new GameObject(name, typeof(Light));
        go.layer = PortraitLayer;
        go.transform.rotation = Quaternion.Euler(euler);

        Light l = go.GetComponent<Light>();
        l.type = LightType.Directional;
        l.color = color;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        l.cullingMask = ~0;
        return go;
    }

    private static void ApplyBottomVignette(Texture2D tex, Color themeColor)
    {
        int w = tex.width;
        int h = tex.height;
        Color32[] pixels = tex.GetPixels32();
        Color32 fadeTarget = new Color32(
            (byte)Mathf.RoundToInt(Mathf.Lerp(16f, themeColor.r * 255f, 0.12f)),
            (byte)Mathf.RoundToInt(Mathf.Lerp(14f, themeColor.g * 255f, 0.12f)),
            (byte)Mathf.RoundToInt(Mathf.Lerp(16f, themeColor.b * 255f, 0.12f)),
            255);

        int bandRows = Mathf.Max(1, Mathf.RoundToInt(h * 0.24f));
        for (int y = 0; y < h; y++)
        {
            int rowStart = y * w;
            if (y < bandRows)
            {
                float t = 1f - ((float)y / bandRows);
                float blend = t * t * 0.85f;
                for (int x = 0; x < w; x++)
                {
                    Color32 src = pixels[rowStart + x];
                    Color32 blended = Color32.Lerp(src, fadeTarget, blend);
                    blended.a = 255;
                    pixels[rowStart + x] = blended;
                }
            }
            else
            {
                for (int x = 0; x < w; x++)
                {
                    pixels[rowStart + x].a = 255;
                }
            }
        }

        tex.SetPixels32(pixels);
    }

    private static Texture2D CreateCrestFallbackTexture(Color accent, bool isUnknown)
    {
        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color bg = new Color(0.065f, 0.060f, 0.065f, 1f);
        Color glowCol = Color.Lerp(bg, accent, isUnknown ? 0.28f : 0.45f);
        Color emblemCol = Color.Lerp(accent, Color.white, 0.35f);

        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            float dy = (y - half + 0.5f) / half;
            int row = y * size;
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half + 0.5f) / half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float glow = Mathf.Clamp01(1f - dist * 0.9f);
                Color c = Color.Lerp(bg, glowCol, glow * glow * 0.65f);

                // Hình thoi biểu tượng ở tâm
                float diamond = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (diamond < 0.36f && diamond > 0.24f)
                {
                    c = Color.Lerp(c, emblemCol, 0.92f);
                }
                else if (diamond <= 0.16f)
                {
                    c = Color.Lerp(c, accent, 0.72f);
                }

                pixels[row + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }
}
