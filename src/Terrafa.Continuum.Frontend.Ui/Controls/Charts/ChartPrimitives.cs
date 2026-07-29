using Avalonia;
using Avalonia.Media;

namespace Terrafa.Continuum.Frontend.Controls.Charts;

public sealed class ChartSeries
{
    public required IReadOnlyList<Point> Points { get; init; }
    public required IBrush Stroke { get; init; }
    public double Thickness { get; init; } = 1.5;
    public double[]? Dashes { get; init; }
    public string? Label { get; init; }
    public Point? LabelAt { get; init; }
    public bool LabelAnchorEnd { get; init; } = true;

    /// <summary>
    /// Upper and lower bound traces, drawn as thin lines either side of the series with a faint
    /// fill between them. Both must be set for either to draw — a one-sided bound is a bug, not a
    /// style, and silently drawing it would misreport the interval.
    /// </summary>
    public IReadOnlyList<Point>? Upper { get; init; }

    public IReadOnlyList<Point>? Lower { get; init; }

    public IBrush? BoundFill { get; init; }

    public bool HasBounds => Upper is { Count: > 1 } && Lower is { Count: > 1 };
}

public sealed record AxisTick(double Value, string Label);

public sealed record ChartLabel(double X, double Y, string Text, IBrush Brush, bool AnchorEnd = false, double FontSize = 10);

public sealed record ChartMarker(double X, double Y, double Radius, IBrush Fill);

public sealed record ChartBand(IReadOnlyList<Point> Upper, IReadOnlyList<Point> Lower, IBrush Fill);

public sealed record ChartRegion(double XStart, double XEnd, IBrush Fill);

public static class ChartSampling
{
    public static IReadOnlyList<Point> Sample(Func<double, double> function, double xMin, double xMax, int count = 120)
    {
        var points = new List<Point>(count);
        for (var i = 0; i < count; i++)
        {
            var x = xMin + (xMax - xMin) * i / (count - 1);
            points.Add(new Point(x, function(x)));
        }
        return points;
    }
}
