// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia.Media;

namespace Terrafa.Continuum.Frontend.Themes;

public static class AppearanceSettings
{
    public const double MaxCornerRadius = 12;
    public const double MaxHighlightSaturation = 2;
    public const double MinHighlightBrightness = 0.5;
    public const double MaxHighlightBrightness = 3;

    private static readonly Dictionary<ISolidColorBrush, SolidColorBrush> TonedBrushes =
        new(ReferenceEqualityComparer.Instance);

    public static double NodeSaturation { get; private set; } = 0.25;
    public static double NodeCornerRadius { get; private set; } = 3;
    public static double HighlightSaturation { get; private set; } = 0.2;
    public static double HighlightBrightness { get; private set; } = 1.55;

    public static event Action? Changed;

    public static void SetNodeSaturation(double value)
    {
        NodeSaturation = Math.Clamp(value, 0, 1);
        RefreshTonedBrushes();
        Changed?.Invoke();
    }

    public static void SetNodeCornerRadius(double value)
    {
        NodeCornerRadius = Math.Clamp(value, 0, MaxCornerRadius);
        Changed?.Invoke();
    }

    public static void SetHighlightSaturation(double value)
    {
        var clamped = Math.Clamp(value, 0, MaxHighlightSaturation);
        if (Math.Abs(HighlightSaturation - clamped) < 0.0001) return;
        HighlightSaturation = clamped;
        Palette.RefreshHighlightBrushes();
        Changed?.Invoke();
    }

    public static void SetHighlightBrightness(double value)
    {
        var clamped = Math.Clamp(value, MinHighlightBrightness, MaxHighlightBrightness);
        if (Math.Abs(HighlightBrightness - clamped) < 0.0001) return;
        HighlightBrightness = clamped;
        Palette.RefreshHighlightBrushes();
        Changed?.Invoke();
    }

    public static IBrush Toned(IBrush brush)
    {
        if (brush is not ISolidColorBrush solid) return brush;
        if (TonedBrushes.TryGetValue(solid, out var existing)) return existing;
        var toned = new SolidColorBrush(TowardGrey(solid.Color));
        TonedBrushes[solid] = toned;
        return toned;
    }

    internal static void RefreshTonedBrushes()
    {
        foreach (var (source, toned) in TonedBrushes)
        {
            toned.Color = TowardGrey(source.Color);
        }
    }

    /// <summary>
    /// Scales a highlight colour's saturation and lightness in HSL, leaving hue and alpha alone,
    /// so the amber family shifts as one family rather than drifting apart channel by channel.
    /// </summary>
    internal static Color Highlighted(Color color)
    {
        if (HighlightSaturation == 1 && HighlightBrightness == 1) return color;
        var hsl = color.ToHsl();
        var scaled = new HslColor(
            1,
            hsl.H,
            Math.Clamp(hsl.S * HighlightSaturation, 0, 1),
            Math.Clamp(hsl.L * HighlightBrightness, 0, 1)).ToRgb();
        return Color.FromArgb(color.A, scaled.R, scaled.G, scaled.B);
    }

    private static Color TowardGrey(Color color)
    {
        if (NodeSaturation >= 1) return color;
        var luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        return Color.FromArgb(
            color.A,
            Blend(color.R, luminance),
            Blend(color.G, luminance),
            Blend(color.B, luminance));
    }

    private static byte Blend(byte channel, double luminance) =>
        (byte)Math.Clamp(Math.Round(luminance + (channel - luminance) * NodeSaturation), 0, 255);
}
