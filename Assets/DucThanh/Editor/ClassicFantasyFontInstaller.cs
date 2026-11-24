#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Editor tool tự động tạo TMP_FontAsset cho bộ font cổ điển Spectral + Noto Sans Symbols 2,
/// gán làm font mặc định trong TMP Settings và hỗ trợ áp dụng vào toàn bộ 7 scene trong Edit Mode.
/// </summary>
[InitializeOnLoad]
public static class ClassicFantasyFontInstaller
{
    private const string SessionCheckedKey = "ClassicFantasyFontInstaller_Checked";
    private const string FontsFolder = "Assets/DucThanh/Resources/Fonts";
    private const string ScenesFolder = "Assets/DucThanh/Scene";
    private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
    private const string LiberationFallbackPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset";

    private static readonly string[] FontNames =
    {
        "Spectral-Medium",
        "Spectral-Bold",
        "Spectral-Italic",
        "Spectral-BoldItalic",
        "NotoSansSymbols2-Regular"
    };

    static ClassicFantasyFontInstaller()
    {
        EditorApplication.delayCall += AutoInstallAssetsIfMissing;
    }

    private static void AutoInstallAssetsIfMissing()
    {
        if (SessionState.GetBool(SessionCheckedKey, false)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        string primarySdfPath = FontsFolder + "/Spectral-Medium SDF.asset";
        string symbolSdfPath = FontsFolder + "/NotoSansSymbols2-Regular SDF.asset";

        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(primarySdfPath) != null
            && AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(symbolSdfPath) != null)
        {
            SessionState.SetBool(SessionCheckedKey, true);
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<Font>(FontsFolder + "/Spectral-Medium.ttf") == null)
        {
            return;
        }

        SessionState.SetBool(SessionCheckedKey, true);
        GenerateFontAssetsAndConfigureTmpSettings();
    }

    [MenuItem("Tools/DucThanh/1. Generate Classic Fantasy Font Assets (Spectral)")]
    public static TMP_FontAsset GenerateFontAssetsAndConfigureTmpSettings()
    {
        EnsureTrueTypeFontDataIncluded();

        TMP_FontAsset primary = EnsureSdfFontAsset("Spectral-Medium");
        TMP_FontAsset bold = EnsureSdfFontAsset("Spectral-Bold");
        TMP_FontAsset italic = EnsureSdfFontAsset("Spectral-Italic");
        TMP_FontAsset boldItalic = EnsureSdfFontAsset("Spectral-BoldItalic");
        TMP_FontAsset symbolFallback = EnsureSdfFontAsset("NotoSansSymbols2-Regular");

        if (primary == null)
        {
            Debug.LogError("[ClassicFantasyFontInstaller] Không thể tạo Spectral-Medium SDF.asset. Hãy kiểm tra file TTF trong " + FontsFolder);
            return null;
        }

        TMP_FontAsset liberationFallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationFallbackPath);

        TMP_FontWeightPair[] table = primary.fontWeightTable;
        if (table != null && table.Length >= 10)
        {
            table[4] = new TMP_FontWeightPair
            {
                regularTypeface = primary,
                italicTypeface = italic
            };
            table[7] = new TMP_FontWeightPair
            {
                regularTypeface = bold,
                italicTypeface = boldItalic
            };
        }

        if (primary.fallbackFontAssetTable == null)
        {
            primary.fallbackFontAssetTable = new List<TMP_FontAsset>();
        }

        AddUniqueFallback(primary.fallbackFontAssetTable, symbolFallback);
        AddUniqueFallback(primary.fallbackFontAssetTable, liberationFallback);
        EditorUtility.SetDirty(primary);

        ConfigureTmpSettings(primary, symbolFallback);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[ClassicFantasyFontInstaller] Đã tạo và cấu hình bộ font Spectral + Noto Sans Symbols 2 làm font mặc định trong TMP Settings.");
        return primary;
    }

    [MenuItem("Tools/DucThanh/2. Apply Classic Fantasy Font To All 7 Scenes")]
    public static void ApplyClassicFontToAllScenes()
    {
        TMP_FontAsset primaryFont = GenerateFontAssetsAndConfigureTmpSettings();
        if (primaryFont == null) return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (!Directory.Exists(ScenesFolder))
        {
            Debug.LogError("[ClassicFantasyFontInstaller] Không tìm thấy thư mục scene: " + ScenesFolder);
            return;
        }

        string originalScenePath = SceneManager.GetActiveScene().path;
        string[] sceneFiles = Directory.GetFiles(ScenesFolder, "*.unity", SearchOption.TopDirectoryOnly);
        Array.Sort(sceneFiles, StringComparer.OrdinalIgnoreCase);

        int updatedSceneCount = 0;
        int updatedTextCount = 0;

        try
        {
            for (int i = 0; i < sceneFiles.Length; i++)
            {
                string scenePath = sceneFiles[i].Replace('\\', '/');
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                TMP_Text[] texts = UnityEngine.Object.FindObjectsByType<TMP_Text>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                int sceneChanges = 0;
                for (int j = 0; j < texts.Length; j++)
                {
                    TMP_Text tmp = texts[j];
                    if (tmp == null) continue;

                    if (tmp.font != primaryFont || tmp.fontSharedMaterial != primaryFont.material)
                    {
                        Undo.RecordObject(tmp, "Apply Classic Fantasy Font");
                        tmp.font = primaryFont;
                        tmp.fontSharedMaterial = primaryFont.material;
                        EditorUtility.SetDirty(tmp);
                        sceneChanges++;
                    }
                }

                if (sceneChanges > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    updatedSceneCount++;
                    updatedTextCount += sceneChanges;
                }
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(originalScenePath) && File.Exists(originalScenePath))
            {
                EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[ClassicFantasyFontInstaller] Hoàn tất! Đã cập nhật {updatedTextCount} TMP_Text trên {updatedSceneCount}/{sceneFiles.Length} scene sang font Spectral-Medium SDF.");
    }

    private static void EnsureTrueTypeFontDataIncluded()
    {
        for (int i = 0; i < FontNames.Length; i++)
        {
            string ttfPath = $"{FontsFolder}/{FontNames[i]}.ttf";
            TrueTypeFontImporter importer = AssetImporter.GetAtPath(ttfPath) as TrueTypeFontImporter;
            if (importer != null && !importer.includeFontData)
            {
                importer.includeFontData = true;
                importer.SaveAndReimport();
            }
        }
    }

    private static TMP_FontAsset EnsureSdfFontAsset(string baseName)
    {
        string assetPath = $"{FontsFolder}/{baseName} SDF.asset";
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
        if (existing != null)
        {
            EnsureSubAssetsAttached(existing, assetPath, baseName);
            return existing;
        }

        string ttfPath = $"{FontsFolder}/{baseName}.ttf";
        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        if (sourceFont == null)
        {
            Debug.LogWarning("[ClassicFantasyFontInstaller] Không tìm thấy font TTF: " + ttfPath);
            return null;
        }

        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            90,
            9,
            GlyphRenderMode.SDFAA,
            1024,
            1024,
            AtlasPopulationMode.Dynamic,
            true);

        if (fontAsset == null)
        {
            Debug.LogError("[ClassicFantasyFontInstaller] TMP_FontAsset.CreateFontAsset trả về null cho: " + ttfPath);
            return null;
        }

        fontAsset.name = baseName + " SDF";
        fontAsset.isMultiAtlasTexturesEnabled = true;

        AssetDatabase.CreateAsset(fontAsset, assetPath);

        if (fontAsset.atlasTextures != null)
        {
            for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
            {
                Texture2D tex = fontAsset.atlasTextures[i];
                if (tex == null) continue;
                tex.name = i == 0 ? baseName + " SDF Atlas" : $"{baseName} SDF Atlas {i}";
                AssetDatabase.AddObjectToAsset(tex, fontAsset);
            }
        }

        if (fontAsset.material != null)
        {
            fontAsset.material.name = baseName + " SDF Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        SerializedObject so = new SerializedObject(fontAsset);
        SerializedProperty clearProp = so.FindProperty("m_ClearDynamicDataOnBuild");
        if (clearProp != null) clearProp.boolValue = true;

        SerializedProperty guidProp = so.FindProperty("m_SourceFontFileGUID");
        if (guidProp != null && string.IsNullOrEmpty(guidProp.stringValue))
        {
            guidProp.stringValue = AssetDatabase.AssetPathToGUID(ttfPath);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(fontAsset);
        return fontAsset;
    }

    private static void EnsureSubAssetsAttached(TMP_FontAsset fontAsset, string assetPath, string baseName)
    {
        bool changed = false;

        if (fontAsset.atlasTextures != null)
        {
            for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
            {
                Texture2D tex = fontAsset.atlasTextures[i];
                if (tex != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(tex)))
                {
                    tex.name = i == 0 ? baseName + " SDF Atlas" : $"{baseName} SDF Atlas {i}";
                    AssetDatabase.AddObjectToAsset(tex, assetPath);
                    changed = true;
                }
            }
        }

        if (fontAsset.material != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(fontAsset.material)))
        {
            fontAsset.material.name = baseName + " SDF Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, assetPath);
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(fontAsset);
        }
    }

    private static void ConfigureTmpSettings(TMP_FontAsset defaultFont, TMP_FontAsset symbolFallback)
    {
        TMP_Settings tmpSettings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
        if (tmpSettings == null)
        {
            Debug.LogWarning("[ClassicFantasyFontInstaller] Không tìm thấy TMP Settings tại: " + TmpSettingsPath);
            return;
        }

        SerializedObject so = new SerializedObject(tmpSettings);
        SerializedProperty defaultFontProp = so.FindProperty("m_defaultFontAsset");
        if (defaultFontProp != null && defaultFont != null)
        {
            defaultFontProp.objectReferenceValue = defaultFont;
        }

        SerializedProperty fallbackListProp = so.FindProperty("m_fallbackFontAssets");
        if (fallbackListProp != null && symbolFallback != null)
        {
            bool exists = false;
            for (int i = 0; i < fallbackListProp.arraySize; i++)
            {
                if (fallbackListProp.GetArrayElementAtIndex(i).objectReferenceValue == symbolFallback)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                int newIndex = fallbackListProp.arraySize;
                fallbackListProp.InsertArrayElementAtIndex(newIndex);
                fallbackListProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = symbolFallback;
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(tmpSettings);
    }

    private static void AddUniqueFallback(List<TMP_FontAsset> list, TMP_FontAsset fallback)
    {
        if (list == null || fallback == null || list.Contains(fallback)) return;
        list.Add(fallback);
    }
}
#endif
