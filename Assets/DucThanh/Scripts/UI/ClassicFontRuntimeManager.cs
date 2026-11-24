using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Quản lý và tự động áp dụng bộ font cổ điển (Spectral + fallback Noto Sans Symbols 2)
/// cho toàn bộ TMP_Text ở runtime nhằm đồng nhất giao diện Dark Fantasy và chống lỗi mất glyph.
/// </summary>
public static class ClassicFontRuntimeManager
{
    private const string PrimarySdfPath = "Fonts/Spectral-Medium SDF";
    private const string PrimaryFontPath = "Fonts/Spectral-Medium";
    private const string BoldSdfPath = "Fonts/Spectral-Bold SDF";
    private const string BoldFontPath = "Fonts/Spectral-Bold";
    private const string ItalicSdfPath = "Fonts/Spectral-Italic SDF";
    private const string ItalicFontPath = "Fonts/Spectral-Italic";
    private const string BoldItalicSdfPath = "Fonts/Spectral-BoldItalic SDF";
    private const string BoldItalicFontPath = "Fonts/Spectral-BoldItalic";
    private const string SymbolSdfPath = "Fonts/NotoSansSymbols2-Regular SDF";
    private const string SymbolFontPath = "Fonts/NotoSansSymbols2-Regular";
    private const string LiberationFallbackPath = "Fonts & Materials/LiberationSans SDF - Fallback";

    public static TMP_FontAsset PrimaryFont { get; private set; }

    private static bool sceneHooked;
    private static readonly List<TMP_FontAsset> runtimeCreatedAssets = new List<TMP_FontAsset>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeOnBoot()
    {
        EnsureInitialized();

        if (!sceneHooked)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            sceneHooked = true;
        }
    }

    public static TMP_FontAsset GetPrimaryFont()
    {
        return EnsureInitialized();
    }

    public static TMP_FontAsset EnsureInitialized()
    {
        if (PrimaryFont != null)
        {
            return PrimaryFont;
        }

        PrimaryFont = LoadOrCreateFontAsset(PrimarySdfPath, PrimaryFontPath);
        if (PrimaryFont == null)
        {
            return null;
        }

        TMP_FontAsset boldFont = LoadOrCreateFontAsset(BoldSdfPath, BoldFontPath);
        TMP_FontAsset italicFont = LoadOrCreateFontAsset(ItalicSdfPath, ItalicFontPath);
        TMP_FontAsset boldItalicFont = LoadOrCreateFontAsset(BoldItalicSdfPath, BoldItalicFontPath);
        TMP_FontAsset symbolFallback = LoadOrCreateFontAsset(SymbolSdfPath, SymbolFontPath);
        TMP_FontAsset liberationFallback = Resources.Load<TMP_FontAsset>(LiberationFallbackPath);

        WireFontWeights(PrimaryFont, italicFont, boldFont, boldItalicFont);
        EnsureFallback(PrimaryFont, symbolFallback);
        EnsureFallback(PrimaryFont, liberationFallback);
        EnsureGlobalFallback(symbolFallback);

        return PrimaryFont;
    }

    public static void ApplyToAllActiveTexts()
    {
        TMP_Text[] texts = UnityEngine.Object.FindObjectsByType<TMP_Text>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < texts.Length; i++)
        {
            ApplyFont(texts[i]);
        }
    }

    public static void ApplyFont(TMP_Text tmp, bool force = false)
    {
        if (tmp == null) return;

        TMP_FontAsset targetFont = EnsureInitialized();
        if (targetFont == null) return;

        // Giữ nguyên material tùy biến (outline/shadow) nếu text đã gắn đúng PrimaryFont
        if (tmp.font == targetFont) return;

        bool isLiberation = tmp.font != null
            && tmp.font.name.StartsWith("LiberationSans", StringComparison.OrdinalIgnoreCase);

        if (!force && tmp.font != null && !isLiberation) return;

        tmp.font = targetFont;
        tmp.fontSharedMaterial = targetFont.material;
        tmp.SetAllDirty();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyToAllActiveTexts();
    }

    private static TMP_FontAsset LoadOrCreateFontAsset(string sdfResourcePath, string fontResourcePath)
    {
        TMP_FontAsset prebuilt = Resources.Load<TMP_FontAsset>(sdfResourcePath);
        if (prebuilt != null)
        {
            return prebuilt;
        }

        Font sourceFont = Resources.Load<Font>(fontResourcePath);
        if (sourceFont == null)
        {
            return null;
        }

        return CreateRuntimeSdf(sourceFont);
    }

    private static TMP_FontAsset CreateRuntimeSdf(Font font)
    {
        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            font,
            90,
            9,
            GlyphRenderMode.SDFAA,
            1024,
            1024,
            AtlasPopulationMode.Dynamic,
            true);

        if (fontAsset == null)
        {
            return null;
        }

        fontAsset.name = font.name + " SDF (Runtime)";
        fontAsset.isMultiAtlasTexturesEnabled = true;
        runtimeCreatedAssets.Add(fontAsset);
        return fontAsset;
    }

    private static void WireFontWeights(
        TMP_FontAsset primary,
        TMP_FontAsset italic,
        TMP_FontAsset bold,
        TMP_FontAsset boldItalic)
    {
        TMP_FontWeightPair[] table = primary.fontWeightTable;
        if (table == null || table.Length < 10) return;

        AssignWeightPairIfNull(table, 4, primary, italic);
        AssignWeightPairIfNull(table, 7, bold, boldItalic);
    }

    private static void AssignWeightPairIfNull(
        TMP_FontWeightPair[] table,
        int index,
        TMP_FontAsset regular,
        TMP_FontAsset italic)
    {
        TMP_FontWeightPair pair = table[index];
        if (pair.regularTypeface == null && regular != null) pair.regularTypeface = regular;
        if (pair.italicTypeface == null && italic != null) pair.italicTypeface = italic;
        table[index] = pair;
    }

    private static void EnsureFallback(TMP_FontAsset target, TMP_FontAsset fallback)
    {
        if (target == null || fallback == null || target == fallback) return;

        if (target.fallbackFontAssetTable == null)
        {
            target.fallbackFontAssetTable = new List<TMP_FontAsset>();
        }

        if (!target.fallbackFontAssetTable.Contains(fallback))
        {
            target.fallbackFontAssetTable.Add(fallback);
        }
    }

    private static void EnsureGlobalFallback(TMP_FontAsset fallback)
    {
        if (fallback == null || TMP_Settings.instance == null) return;

        List<TMP_FontAsset> globalFallbacks = TMP_Settings.fallbackFontAssets;
        if (globalFallbacks != null && !globalFallbacks.Contains(fallback))
        {
            globalFallbacks.Add(fallback);
        }
    }
}
