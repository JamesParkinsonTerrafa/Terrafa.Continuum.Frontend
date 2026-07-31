// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

/// <summary>
/// The faint gridlines behind a pannable canvas. Drawn in viewport space so they stay
/// hairline-crisp at any zoom, stepping in world-grid multiples so they always sit under the
/// snap targets — and doubling the step when a zoomed-out grid would turn to noise.
/// </summary>
internal sealed class GridLayer : Control
{
    private const double MinSpacing = 14;

    private readonly TranslateTransform pan;
    private readonly ScaleTransform zoom;

    public GridLayer(TranslateTransform pan, ScaleTransform zoom)
    {
        this.pan = pan;
        this.zoom = zoom;
    }

    public override void Render(DrawingContext context)
    {
        var spacing = SnapSettings.GridSize * zoom.ScaleX;
        while (spacing < MinSpacing) spacing *= 2;
        var pen = new Pen(Palette.GridFaint, 1);
        for (var x = Mod(pan.X, spacing); x <= Bounds.Width; x += spacing)
        {
            var crisp = Math.Round(x) + 0.5;
            context.DrawLine(pen, new Point(crisp, 0), new Point(crisp, Bounds.Height));
        }
        for (var y = Mod(pan.Y, spacing); y <= Bounds.Height; y += spacing)
        {
            var crisp = Math.Round(y) + 0.5;
            context.DrawLine(pen, new Point(0, crisp), new Point(Bounds.Width, crisp));
        }
    }

    private static double Mod(double value, double spacing) => ((value % spacing) + spacing) % spacing;
}
