// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia.Controls;

namespace Terrafa.Continuum.Frontend.Themes;

/// <summary>
/// The global text scale. Every font size in the app is a base size multiplied through
/// <see cref="Size"/> — C# builds route through it directly and rebuild on <see cref="Changed"/>,
/// while axaml reads the FontSizeN / LineHeightN resources this class keeps written.
/// </summary>
public static class TypographySettings
{
    public const double MinScale = 0.85;
    public const double MaxScale = 1.3;

    /// <summary>The base sizes the axaml files use; each is published as FontSize{n}.</summary>
    private static readonly double[] FontSizes = [9, 10, 11, 12];

    /// <summary>The base line heights the axaml files use; each is published as LineHeight{n}.</summary>
    private static readonly double[] LineHeights = [14, 15, 16];

    private static IResourceDictionary? registeredResources;

    public static double Scale { get; private set; } = 1.0;

    public static event Action? Changed;

    public static void RegisterResources(IResourceDictionary resources)
    {
        registeredResources = resources;
        WriteResources();
    }

    public static void SetScale(double value)
    {
        var clamped = Math.Clamp(value, MinScale, MaxScale);
        if (Math.Abs(Scale - clamped) < 0.0001) return;
        Scale = clamped;
        WriteResources();
        Changed?.Invoke();
    }

    /// <summary>A base size under the current scale, rounded to the half point so glyphs stay crisp.</summary>
    public static double Size(double baseSize) => Math.Round(baseSize * Scale * 2) / 2;

    private static void WriteResources()
    {
        if (registeredResources is null) return;
        foreach (var size in FontSizes) registeredResources[$"FontSize{size:0}"] = Size(size);
        foreach (var height in LineHeights) registeredResources[$"LineHeight{height:0}"] = Size(height);
    }
}
