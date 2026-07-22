using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Terrafa.Continuum.Frontend.Controls.Charts;

public class Sparkline : Control
{
    private IReadOnlyList<double> values = [];
    private IBrush stroke = Brushes.White;

    public IReadOnlyList<double> Values
    {
        get => values;
        set
        {
            values = value;
            InvalidateVisual();
        }
    }

    public IBrush Stroke
    {
        get => stroke;
        set
        {
            stroke = value;
            InvalidateVisual();
        }
    }

    public double Thickness { get; set; } = 1.5;

    public override void Render(DrawingContext context)
    {
        if (values.Count < 2 || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var min = values.Min();
        var max = values.Max();
        var range = Math.Max(max - min, 0.0001);
        var padding = 2.0;
        var height = Bounds.Height - padding * 2;

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            for (var i = 0; i < values.Count; i++)
            {
                var x = Bounds.Width * i / (values.Count - 1);
                var y = padding + height * (1 - (values[i] - min) / range);
                if (i == 0) geometryContext.BeginFigure(new Point(x, y), false);
                else geometryContext.LineTo(new Point(x, y));
            }
            geometryContext.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(stroke, Thickness), geometry);
    }
}
