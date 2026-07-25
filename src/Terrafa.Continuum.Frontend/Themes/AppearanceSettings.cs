using Avalonia.Media;

namespace Terrafa.Continuum.Frontend.Themes;

public static class AppearanceSettings
{
    public const double MaxCornerRadius = 12;

    private static readonly Dictionary<ISolidColorBrush, SolidColorBrush> TonedBrushes =
        new(ReferenceEqualityComparer.Instance);

    public static double NodeSaturation { get; private set; } = 0.55;
    public static double NodeCornerRadius { get; private set; } = 3;

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
