#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tự động sửa Texture Import (Normal Map, Alpha Transparency) và tạo/remap Material URP Lit chuẩn
/// cho 3 nhân vật Mixamo mới thêm (Player2 Maria, Player3 Vampire, Player4 Maw) để hết bóng nhựa,
/// nhận đúng chi tiết bề mặt, phát sáng Emission và Alpha Clipping.
/// </summary>
[InitializeOnLoad]
public static class HeroMaterialFixer
{
    private const string SessionFixedKey = "HeroMaterialsFixed_v1";
    private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
    private const string UrpLitShaderFallbackPath = "Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader";

    private static readonly int ShaderWorkflowMode = Shader.PropertyToID("_WorkflowMode");
    private static readonly int ShaderSurface = Shader.PropertyToID("_Surface");
    private static readonly int ShaderBaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ShaderColor = Shader.PropertyToID("_Color");
    private static readonly int ShaderBaseMap = Shader.PropertyToID("_BaseMap");
    private static readonly int ShaderMainTex = Shader.PropertyToID("_MainTex");
    private static readonly int ShaderBumpMap = Shader.PropertyToID("_BumpMap");
    private static readonly int ShaderBumpScale = Shader.PropertyToID("_BumpScale");
    private static readonly int ShaderMetallic = Shader.PropertyToID("_Metallic");
    private static readonly int ShaderSmoothness = Shader.PropertyToID("_Smoothness");
    private static readonly int ShaderSmoothnessTextureChannel = Shader.PropertyToID("_SmoothnessTextureChannel");
    private static readonly int ShaderSpecGlossMap = Shader.PropertyToID("_SpecGlossMap");
    private static readonly int ShaderMetallicGlossMap = Shader.PropertyToID("_MetallicGlossMap");
    private static readonly int ShaderEnvReflections = Shader.PropertyToID("_EnvironmentReflections");
    private static readonly int ShaderSpecularHighlights = Shader.PropertyToID("_SpecularHighlights");
    private static readonly int ShaderEmissionMap = Shader.PropertyToID("_EmissionMap");
    private static readonly int ShaderEmissionColor = Shader.PropertyToID("_EmissionColor");
    private static readonly int ShaderAlphaClip = Shader.PropertyToID("_AlphaClip");
    private static readonly int ShaderCutoff = Shader.PropertyToID("_Cutoff");
    private static readonly int ShaderCull = Shader.PropertyToID("_Cull");
    private static readonly int ShaderSrcBlend = Shader.PropertyToID("_SrcBlend");
    private static readonly int ShaderDstBlend = Shader.PropertyToID("_DstBlend");
    private static readonly int ShaderZWrite = Shader.PropertyToID("_ZWrite");
    private static readonly int ShaderReceiveShadows = Shader.PropertyToID("_ReceiveShadows");

    private struct HeroMaterialSpec
    {
        public string heroName;
        public string fbxPath;
        public string texturesDir;
        public string materialsDir;
        public string diffuseFile;
        public string transparentDiffuseFile;
        public string normalFile;
        public string emissionFile;
        public float metallic;
        public float smoothness;
        public float bumpScale;
        public Color emissionColor;
        public float transparentSmoothness;
        public float transparentBumpScale;
        public float transparentCutoff;
    }

    private static readonly HeroMaterialSpec[] HeroSpecs =
    {
        new HeroMaterialSpec
        {
            heroName = "Player2 (Maria)",
            fbxPath = "Assets/DucThanh/Assets/Player2/Unarmed Idle 01.fbx",
            texturesDir = "Assets/DucThanh/Assets/Player2/Textures",
            materialsDir = "Assets/DucThanh/Assets/Player2/Materials",
            diffuseFile = "maria_diffuse.png",
            transparentDiffuseFile = null,
            normalFile = "maria_normal.png",
            emissionFile = null,
            metallic = 0.12f,
            smoothness = 0.26f,
            bumpScale = 1.0f,
            emissionColor = Color.black,
            transparentSmoothness = 0.18f,
            transparentBumpScale = 0.85f,
            transparentCutoff = 0.45f
        },
        new HeroMaterialSpec
        {
            heroName = "Player3 (Vampire)",
            fbxPath = "Assets/DucThanh/Assets/Player3/Unarmed Idle 01 (2).fbx",
            texturesDir = "Assets/DucThanh/Assets/Player3/Textures",
            materialsDir = "Assets/DucThanh/Assets/Player3/Materials",
            diffuseFile = "Vampire_diffuse.png",
            transparentDiffuseFile = "Vampire_diffuse_transparent.png",
            normalFile = "Vampire_normal.png",
            emissionFile = "Vampire_emission.png",
            metallic = 0.08f,
            smoothness = 0.22f,
            bumpScale = 1.0f,
            emissionColor = new Color(1.25f, 1.25f, 1.25f, 1f),
            transparentSmoothness = 0.18f,
            transparentBumpScale = 0.85f,
            transparentCutoff = 0.45f
        },
        new HeroMaterialSpec
        {
            heroName = "Player4 (Maw)",
            fbxPath = "Assets/DucThanh/Assets/Player4/Unarmed Idle 3 (1).fbx",
            texturesDir = "Assets/DucThanh/Assets/Player4/Textures",
            materialsDir = "Assets/DucThanh/Assets/Player4/Materials",
            diffuseFile = "MAW_diffuse.png",
            transparentDiffuseFile = null,
            normalFile = "MAW_normal.png",
            emissionFile = "MAW_glow.png",
            metallic = 0.05f,
            smoothness = 0.18f,
            bumpScale = 1.0f,
            emissionColor = new Color(1.45f, 1.45f, 1.45f, 1f),
            transparentSmoothness = 0.18f,
            transparentBumpScale = 0.85f,
            transparentCutoff = 0.45f
        }
    };

    static HeroMaterialFixer()
    {
        if (NeedsMaterialFix() && !SessionState.GetBool(SessionFixedKey, false))
        {
            SessionState.SetBool(SessionFixedKey, true);
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode && NeedsMaterialFix())
                {
                    FixAllHeroMaterials(forceUpdate: true);
                }
            };
        }
    }

    private static bool NeedsMaterialFix()
    {
        for (int i = 0; i < HeroSpecs.Length; i++)
        {
            string matDir = HeroSpecs[i].materialsDir;
            if (!Directory.Exists(matDir))
            {
                return true;
            }

            string[] mats = Directory.GetFiles(matDir, "*.mat", SearchOption.TopDirectoryOnly);
            if (mats.Length == 0)
            {
                return true;
            }
        }

        return false;
    }

    [MenuItem("DucThanh/Fix Hero Materials (Player2-4)")]
    public static void FixHeroMaterialsMenu()
    {
        FixAllHeroMaterials(forceUpdate: true);
    }

    /// <summary>
    /// Chuẩn hóa TextureImporter (Normal Map / Alpha) và tạo + remap file .mat URP/Lit riêng cho Player2-4.
    /// </summary>
    public static void FixAllHeroMaterials(bool forceUpdate = true)
    {
        Shader urpLit = Shader.Find(UrpLitShaderName);
        if (urpLit == null)
        {
            urpLit = AssetDatabase.LoadAssetAtPath<Shader>(UrpLitShaderFallbackPath);
        }

        if (urpLit == null)
        {
            Debug.LogError("[HeroMaterialFixer] Không tìm thấy shader: " + UrpLitShaderName);
            return;
        }

        int totalFixedMaterials = 0;
        for (int i = 0; i < HeroSpecs.Length; i++)
        {
            totalFixedMaterials += ProcessHero(HeroSpecs[i], urpLit, forceUpdate);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[HeroMaterialFixer] Đã chuẩn hóa và remap {totalFixedMaterials} material cho Player2-4.");
    }

    private static int ProcessHero(HeroMaterialSpec spec, Shader urpLit, bool forceUpdate)
    {
        FixTexturesInDirectory(spec.texturesDir);

        ModelImporter importer = AssetImporter.GetAtPath(spec.fbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogWarning("[HeroMaterialFixer] Không tìm thấy FBX: " + spec.fbxPath);
            return 0;
        }

        Dictionary<string, bool> materialTransparentFlags = CollectFbxMaterials(spec.fbxPath, importer);
        if (materialTransparentFlags.Count == 0)
        {
            Debug.LogWarning($"[HeroMaterialFixer] Không tìm thấy material nào trên FBX {spec.heroName} ({spec.fbxPath}).");
            return 0;
        }

        EnsureAssetDirectory(spec.materialsDir);

        Texture2D diffuseTex = LoadHeroTexture(spec.texturesDir, spec.diffuseFile);
        Texture2D transparentDiffuseTex = LoadHeroTexture(spec.texturesDir, spec.transparentDiffuseFile);
        Texture2D normalTex = LoadHeroTexture(spec.texturesDir, spec.normalFile);
        Texture2D emissionTex = LoadHeroTexture(spec.texturesDir, spec.emissionFile);

        var externalMap = importer.GetExternalObjectMap();
        bool importerDirty = false;
        int fixedCount = 0;

        foreach (KeyValuePair<string, bool> entry in materialTransparentFlags)
        {
            string matName = entry.Key;
            bool isTransparent = entry.Value && transparentDiffuseTex != null;

            string safeFileName = SanitizeFileName(matName);
            string matPath = $"{spec.materialsDir}/{safeFileName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            bool createdNew = false;

            if (mat == null)
            {
                mat = new Material(urpLit) { name = safeFileName };
                AssetDatabase.CreateAsset(mat, matPath);
                createdNew = true;
            }
            else if (forceUpdate && mat.shader != urpLit)
            {
                mat.shader = urpLit;
            }

            if (createdNew || forceUpdate)
            {
                ConfigureMaterial(
                    mat,
                    spec,
                    isTransparent,
                    isTransparent ? transparentDiffuseTex : diffuseTex,
                    normalTex,
                    emissionTex);
                EditorUtility.SetDirty(mat);
            }

            var sourceId = new AssetImporter.SourceAssetIdentifier(typeof(Material), matName);
            if (!externalMap.TryGetValue(sourceId, out UnityEngine.Object existingRemap) || existingRemap != mat)
            {
                importer.AddRemap(sourceId, mat);
                importerDirty = true;
            }

            fixedCount++;
        }

        if (importerDirty)
        {
            importer.SaveAndReimport();
        }

        return fixedCount;
    }

    private static void FixTexturesInDirectory(string texturesDir)
    {
        if (!Directory.Exists(texturesDir))
        {
            return;
        }

        string[] files = Directory.GetFiles(texturesDir, "*.*", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < files.Length; i++)
        {
            string filePath = files[i].Replace('\\', '/');
            string ext = Path.GetExtension(filePath);
            if (!IsSupportedTextureExtension(ext))
            {
                continue;
            }

            string fileName = Path.GetFileNameWithoutExtension(filePath);
            bool isNormal = fileName.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isTransparent = fileName.IndexOf("transparent", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isNormal && !isTransparent)
            {
                continue;
            }

            TextureImporter texImporter = AssetImporter.GetAtPath(filePath) as TextureImporter;
            if (texImporter == null)
            {
                continue;
            }

            bool changed = false;
            if (isNormal)
            {
                if (texImporter.textureType != TextureImporterType.NormalMap)
                {
                    texImporter.textureType = TextureImporterType.NormalMap;
                    changed = true;
                }

                if (texImporter.sRGBTexture)
                {
                    texImporter.sRGBTexture = false;
                    changed = true;
                }
            }

            if (isTransparent && !texImporter.alphaIsTransparency)
            {
                texImporter.alphaIsTransparency = true;
                changed = true;
            }

            if (changed)
            {
                texImporter.SaveAndReimport();
            }
        }
    }

    private static Dictionary<string, bool> CollectFbxMaterials(string fbxPath, ModelImporter importer)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);

        UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        for (int i = 0; i < subAssets.Length; i++)
        {
            if (subAssets[i] is Material subMat)
            {
                result[subMat.name] = IsTransparentMaterial(subMat.name, subMat);
            }
        }

        Dictionary<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> externalMap = importer.GetExternalObjectMap();
        foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, UnityEngine.Object> kvp in externalMap)
        {
            if (kvp.Key.type != typeof(Material) || string.IsNullOrEmpty(kvp.Key.name))
            {
                continue;
            }

            bool transparent = IsTransparentMaterial(kvp.Key.name, kvp.Value as Material);
            if (result.TryGetValue(kvp.Key.name, out bool existingFlag))
            {
                result[kvp.Key.name] = existingFlag || transparent;
            }
            else
            {
                result[kvp.Key.name] = transparent;
            }
        }

        return result;
    }

    private static bool IsTransparentMaterial(string matName, Material mat)
    {
        if (!string.IsNullOrEmpty(matName) &&
            matName.IndexOf("transparent", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (mat == null)
        {
            return false;
        }

        Texture baseTex = mat.HasProperty(ShaderBaseMap) ? mat.GetTexture(ShaderBaseMap) : null;
        if (baseTex == null && mat.HasProperty(ShaderMainTex))
        {
            baseTex = mat.GetTexture(ShaderMainTex);
        }

        return baseTex != null &&
               baseTex.name.IndexOf("transparent", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void ConfigureMaterial(
        Material mat,
        HeroMaterialSpec spec,
        bool isTransparent,
        Texture2D albedoTex,
        Texture2D normalTex,
        Texture2D emissionTex)
    {
        // URP Lit: _WorkflowMode 1 = Metallic (0 = Specular). Xóa specular map để tránh bóng nhựa 100%.
        SetFloatIfPresent(mat, ShaderWorkflowMode, 1f);
        SetFloatIfPresent(mat, ShaderSurface, 0f);
        SetFloatIfPresent(mat, ShaderSmoothnessTextureChannel, 0f);
        SetFloatIfPresent(mat, ShaderSrcBlend, 1f);
        SetFloatIfPresent(mat, ShaderDstBlend, 0f);
        SetFloatIfPresent(mat, ShaderZWrite, 1f);
        SetFloatIfPresent(mat, ShaderReceiveShadows, 1f);
        mat.DisableKeyword("_SPECULAR_SETUP");
        mat.DisableKeyword("_METALLICSPECGLOSSMAP");
        mat.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

        SetTextureIfPresent(mat, ShaderSpecGlossMap, null);
        SetTextureIfPresent(mat, ShaderMetallicGlossMap, null);

        SetColorIfPresent(mat, ShaderBaseColor, Color.white);
        SetColorIfPresent(mat, ShaderColor, Color.white);
        SetTextureIfPresent(mat, ShaderBaseMap, albedoTex);
        SetTextureIfPresent(mat, ShaderMainTex, albedoTex);

        SetFloatIfPresent(mat, ShaderMetallic, isTransparent ? 0f : spec.metallic);
        SetFloatIfPresent(mat, ShaderSmoothness, isTransparent ? spec.transparentSmoothness : spec.smoothness);
        SetFloatIfPresent(mat, ShaderEnvReflections, 1f);
        SetFloatIfPresent(mat, ShaderSpecularHighlights, 1f);

        if (normalTex != null)
        {
            SetTextureIfPresent(mat, ShaderBumpMap, normalTex);
            SetFloatIfPresent(mat, ShaderBumpScale, isTransparent ? spec.transparentBumpScale : spec.bumpScale);
            mat.EnableKeyword("_NORMALMAP");
        }
        else
        {
            SetTextureIfPresent(mat, ShaderBumpMap, null);
            mat.DisableKeyword("_NORMALMAP");
        }

        if (!isTransparent && emissionTex != null)
        {
            SetTextureIfPresent(mat, ShaderEmissionMap, emissionTex);
            SetColorIfPresent(mat, ShaderEmissionColor, spec.emissionColor);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            SetTextureIfPresent(mat, ShaderEmissionMap, null);
            SetColorIfPresent(mat, ShaderEmissionColor, Color.black);
            mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        if (isTransparent)
        {
            SetFloatIfPresent(mat, ShaderAlphaClip, 1f);
            SetFloatIfPresent(mat, ShaderCutoff, spec.transparentCutoff);
            SetFloatIfPresent(mat, ShaderCull, 0f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
        }
        else
        {
            SetFloatIfPresent(mat, ShaderAlphaClip, 0f);
            SetFloatIfPresent(mat, ShaderCutoff, 0.5f);
            SetFloatIfPresent(mat, ShaderCull, 2f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = -1;
        }
    }

    private static Texture2D LoadHeroTexture(string texturesDir, string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(texturesDir))
        {
            return null;
        }

        string path = $"{texturesDir}/{fileName}";
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static bool IsSupportedTextureExtension(string ext)
    {
        return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".tga", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureAssetDirectory(string assetDir)
    {
        if (Directory.Exists(assetDir))
        {
            return;
        }

        Directory.CreateDirectory(assetDir);
        AssetDatabase.Refresh();
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "HeroMaterial";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private static void SetFloatIfPresent(Material mat, int propId, float value)
    {
        if (mat.HasProperty(propId))
        {
            mat.SetFloat(propId, value);
        }
    }

    private static void SetColorIfPresent(Material mat, int propId, Color value)
    {
        if (mat.HasProperty(propId))
        {
            mat.SetColor(propId, value);
        }
    }

    private static void SetTextureIfPresent(Material mat, int propId, Texture value)
    {
        if (mat.HasProperty(propId))
        {
            mat.SetTexture(propId, value);
        }
    }
}
#endif
