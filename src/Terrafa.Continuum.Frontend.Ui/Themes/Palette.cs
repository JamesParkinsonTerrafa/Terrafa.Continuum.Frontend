using Avalonia.Controls;
using Avalonia.Media;

namespace Terrafa.Continuum.Frontend.Themes;

public static class Palette
{
    private static readonly List<(SolidColorBrush Brush, string ResourceKey, Color Dark, Color Light, bool Highlight)> ThemedBrushes = [];
    private static readonly List<(string ResourceKey, Color Dark, Color Light)> ThemedColors = [];
    private static readonly List<(string ResourceKey, double Dark, double Light)> ThemedDoubles = [];
    private static readonly List<(string ResourceKey, BoxShadows Dark, BoxShadows Light)> ThemedShadows = [];
    private static IResourceDictionary? registeredResources;
    private static bool isLight = true;

    public static readonly SolidColorBrush BgDeep = Themed("BgDeepBrush", "#04050A", "#EDF0F4");
    public static readonly SolidColorBrush BgPanel = Themed("BgPanelBrush", "#07080C", "#F8FAFC");
    public static readonly SolidColorBrush BgBar = Themed("BgBarBrush", "#0A0C10", "#EDF3F9");
    public static readonly SolidColorBrush EmbossSurface = Themed("EmbossSurfaceBrush", "#0A0C10", "#EDF3F9");
    public static readonly SolidColorBrush BgField = Themed("BgFieldBrush", "#11151C", "#DCE1E8");
    public static readonly SolidColorBrush BgChart = Themed("BgChartBrush", "#070A0E", "#F3F6F9");
    public static readonly SolidColorBrush RowSeparator = Themed("RowSeparatorBrush", "#14181F", "#E0E4EA");
    public static readonly SolidColorBrush GridFaint = Themed("GridFaintBrush", "#171B22", "#E2E6EC");
    public static readonly SolidColorBrush Border = Themed("BorderBrush", "#262C36", "#C2C9D3");
    public static readonly SolidColorBrush BorderMid = Themed("BorderMidBrush", "#2F3742", "#ADB6C2");
    public static readonly SolidColorBrush TextGhost = Themed("TextGhostBrush", "#3A4250", "#B7BFCA");
    public static readonly SolidColorBrush TextFaint = Themed("TextFaintBrush", "#566070", "#8792A0");
    public static readonly SolidColorBrush TextMuted = Themed("TextMutedBrush", "#7C8593", "#67707E");
    public static readonly SolidColorBrush TextSub = Themed("TextSubBrush", "#AEB6C2", "#49515F");
    public static readonly SolidColorBrush Text = Themed("TextBrush", "#D6DBE3", "#2A303A");
    public static readonly SolidColorBrush TextBright = Themed("TextBrightBrush", "#E8EDF4", "#14181F");
    public static readonly SolidColorBrush TextStrong = Themed("TextStrongBrush", "#FFFFFF", "#04060B");
    public static readonly SolidColorBrush TabActiveText = Themed("TabActiveTextBrush", "#04050A", "#FFFFFF");
    public static readonly SolidColorBrush EngraveText = Themed("EngraveTextBrush", "#05070C", "#666666");
    public static readonly SolidColorBrush Amber = Highlight("AmberBrush", "#FFAB26", "#B27102");
    public static readonly SolidColorBrush AmberSoft = Highlight("AmberSoftBrush", "#FFD9A0", "#7E5304");
    public static readonly SolidColorBrush AmberPale = Highlight("AmberPaleBrush", "#FFE9C4", "#64430A");
    public static readonly SolidColorBrush AmberFill = Highlight("AmberFillBrush", "#12FFAB26", "#1FB27102");
    public static readonly SolidColorBrush AmberChipBorder = Highlight("AmberChipBorderBrush", "#4A3A1E", "#E0C494");
    public static readonly SolidColorBrush Cyan = Themed("CyanBrush", "#4FD4E8", "#077A8F");
    public static readonly SolidColorBrush CyanSoft = Themed("CyanSoftBrush", "#DFF7FB", "#06404C");
    public static readonly SolidColorBrush CyanPale = Themed("CyanPaleBrush", "#E8F6FA", "#0A333D");
    public static readonly SolidColorBrush CyanFill = Themed("CyanFillBrush", "#124FD4E8", "#1F077A8F");
    public static readonly SolidColorBrush CyanZoneFill = Themed("CyanZoneFillBrush", "#1F4FD4E8", "#26077A8F");
    public static readonly SolidColorBrush CyanChipBorder = Themed("CyanChipBorderBrush", "#234A52", "#94C6D0");
    public static readonly SolidColorBrush Green = Themed("GreenBrush", "#2FE07A", "#0F8A45");
    public static readonly SolidColorBrush GreenSoft = Themed("GreenSoftBrush", "#E9FDF1", "#0A4A26");
    public static readonly SolidColorBrush GreenFill = Themed("GreenFillBrush", "#122FE07A", "#1F0F8A45");
    public static readonly SolidColorBrush GreenChipBorder = Themed("GreenChipBorderBrush", "#1E4A32", "#9ED4B6");
    public static readonly SolidColorBrush Red = Themed("RedBrush", "#FF5C5C", "#C22F2F");
    public static readonly SolidColorBrush RedZoneFill = Themed("RedZoneFillBrush", "#14FF5C5C", "#1FC22F2F");
    public static readonly SolidColorBrush Purple = Themed("PurpleBrush", "#CF8BFF", "#7A3BC2");
    public static readonly SolidColorBrush PurpleSoft = Themed("PurpleSoftBrush", "#F3E9FD", "#3A1D5E");
    public static readonly SolidColorBrush PurpleMuted = Themed("PurpleMutedBrush", "#8A6FA8", "#7C64A0");
    public static readonly SolidColorBrush PurpleFill = Themed("PurpleFillBrush", "#0DCF8BFF", "#147A3BC2");
    public static readonly SolidColorBrush ObjectFill = Themed("ObjectFillBrush", "#0DD6DBE3", "#142B3546");
    public static readonly SolidColorBrush ObjectBorder = Themed("ObjectBorderBrush", "#D6DBE3", "#3E4959");
    public static readonly SolidColorBrush BarFillLow = Themed("BarFillLowBrush", "#1D3F4A", "#C2D6DD");
    public static readonly SolidColorBrush BarFillMid = Themed("BarFillMidBrush", "#2A5866", "#9CC0CB");
    public static readonly SolidColorBrush BarFillHigh = Themed("BarFillHighBrush", "#38707F", "#74A7B7");
    public static readonly SolidColorBrush ZoneLabelBackdrop = Themed("ZoneLabelBackdropBrush", "#B304050A", "#B3F4F6F9");
    public static readonly SolidColorBrush PinnedCardFill = Themed("PinnedCardFillBrush", "#E304050A", "#E3F8FAFC");
    public static readonly SolidColorBrush CanvasNoteBackdrop = Themed("CanvasNoteBackdropBrush", "#D904050A", "#D9F8FAFC");
    public static readonly SolidColorBrush CanvasPanelBackdrop = Themed("CanvasPanelBackdropBrush", "#D90A0C10", "#D9E9EDF2");
    public static readonly SolidColorBrush Scrim = Themed("ScrimBrush", "#A604050A", "#5904050A");
    public static readonly FontFamily Font = AppFonts.Primary;

    static Palette()
    {
        ThemedColor("CanvasGlowInnerColor", "#090B12", "#F5F7FA");
        ThemedColor("CanvasGlowOuterColor", "#04050A", "#EDF0F4");
        ThemedColor("EngraveHighlightColor", "#8C7A8698", "#F0FFFFFF");
        ThemedDouble("EngraveBlurRadius", 1.5, 0);

        ThemedShadow("EmbossRaisedShadow",
            "-2 -2 5 0 #14FFFFFF, 3 3 6 -1 #B3000000",
            "-3 -3 6 0 #FFFFFFFF, 3 3 5 -1 #457496B3");
        ThemedShadow("EmbossPressedShadow",
            "inset 3 3 6 0 #E6000000, inset -2 -2 4 0 #1AFFFFFF",
            "inset 3 3 6 0 #667496B3, inset -2 -2 4 0 #FFFFFFFF");
        ThemedShadow("EmbossCardShadow",
            "-5 -5 14 0 #14FFFFFF, 7 7 12 -4 #CC000000",
            "-7 -7 16 0 #FFFFFFFF, 7 7 10 -4 #457496B3");
        ThemedShadow("EmbossInnerShadow",
            "inset 3 3 7 0 #CC000000, inset -4 -4 6 0 #14FFFFFF",
            "inset 3 3 7 0 #527496B3, inset -4 -4 6 0 #FFFFFFFF");
    }

    public static void RegisterResources(IResourceDictionary resources)
    {
        registeredResources = resources;
        foreach (var (brush, resourceKey, _, _, _) in ThemedBrushes)
        {
            resources[resourceKey] = brush;
        }
    }

    public static void Apply(bool light)
    {
        isLight = light;
        foreach (var (brush, _, dark, lightColor, highlight) in ThemedBrushes)
        {
            var color = light ? lightColor : dark;
            brush.Color = highlight ? AppearanceSettings.Highlighted(color) : color;
        }
        WriteValueResources(light);
        AppearanceSettings.RefreshTonedBrushes();
    }

    internal static void RefreshHighlightBrushes()
    {
        foreach (var (brush, _, dark, light, highlight) in ThemedBrushes)
        {
            if (highlight) brush.Color = AppearanceSettings.Highlighted(isLight ? light : dark);
        }
        AppearanceSettings.RefreshTonedBrushes();
    }

    private static void WriteValueResources(bool light)
    {
        if (registeredResources is null) return;
        foreach (var (resourceKey, dark, lightColor) in ThemedColors)
        {
            registeredResources[resourceKey] = light ? lightColor : dark;
        }
        foreach (var (resourceKey, dark, lightValue) in ThemedDoubles)
        {
            registeredResources[resourceKey] = light ? lightValue : dark;
        }
        foreach (var (resourceKey, dark, lightShadows) in ThemedShadows)
        {
            registeredResources[resourceKey] = light ? lightShadows : dark;
        }
    }

    private static SolidColorBrush Themed(string resourceKey, string darkHex, string lightHex) =>
        Register(resourceKey, darkHex, lightHex, highlight: false);

    /// <summary>Registers a brush that also tracks the highlight saturation and brightness settings.</summary>
    private static SolidColorBrush Highlight(string resourceKey, string darkHex, string lightHex) =>
        Register(resourceKey, darkHex, lightHex, highlight: true);

    private static SolidColorBrush Register(string resourceKey, string darkHex, string lightHex, bool highlight)
    {
        var dark = Color.Parse(darkHex);
        var light = Color.Parse(lightHex);
        var brush = new SolidColorBrush(dark);
        ThemedBrushes.Add((brush, resourceKey, dark, light, highlight));
        return brush;
    }

    private static void ThemedColor(string resourceKey, string darkHex, string lightHex) =>
        ThemedColors.Add((resourceKey, Color.Parse(darkHex), Color.Parse(lightHex)));

    private static void ThemedDouble(string resourceKey, double dark, double light) =>
        ThemedDoubles.Add((resourceKey, dark, light));

    private static void ThemedShadow(string resourceKey, string darkSpec, string lightSpec) =>
        ThemedShadows.Add((resourceKey, BoxShadows.Parse(darkSpec), BoxShadows.Parse(lightSpec)));
}
