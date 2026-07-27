using Avalonia.Media;

namespace Terrafa.Continuum.Frontend.Themes;

/// <summary>Stable colour per mounted subtree — a dataset keeps its accent across every screen.</summary>
public static class SubtreeAccents
{
    private static readonly IBrush[] Strokes =
        [Palette.Cyan, Palette.Purple, Palette.Green, Palette.Amber, Palette.Red, Palette.TextSub];

    private static readonly IBrush[] Fills =
        [Palette.CyanFill, Palette.PurpleFill, Palette.GreenFill, Palette.AmberFill, Palette.RedZoneFill, Palette.ObjectFill];

    public static IBrush Stroke(int index) => Strokes[Math.Abs(index) % Strokes.Length];

    public static IBrush Fill(int index) => Fills[Math.Abs(index) % Fills.Length];
}
