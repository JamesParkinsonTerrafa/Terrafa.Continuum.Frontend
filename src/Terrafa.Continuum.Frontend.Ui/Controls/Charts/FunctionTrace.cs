// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls.Charts;

public static class FunctionTrace
{
    public static IReadOnlyList<IReadOnlyList<Point>> CreateTrace(
        Func<double, double> function, double xMin, double xMax, int count = 320)
    {
        var segments = new List<IReadOnlyList<Point>>();
        var current = new List<Point>();
        for (var i = 0; i < count; i++)
        {
            var x = xMin + (xMax - xMin) * i / (count - 1);
            var y = function(x);
            if (double.IsFinite(y))
            {
                current.Add(new Point(x, y));
            }
            else if (current.Count > 0)
            {
                segments.Add(current);
                current = [];
            }
        }
        if (current.Count > 0)
            segments.Add(current);
        return segments;
    }

    public static ChartBand? CreateVarianceTrace(
        Func<double, double> function, double xMin, double xMax, int count = 320)
    {
        var upper = new List<Point>();
        var lower = new List<Point>();
        for (var i = 0; i < count; i++)
        {
            var x = xMin + (xMax - xMin) * i / (count - 1);
            var y = function(x);
            if (!double.IsFinite(y)) continue;
            var placeholderSigma = Math.Abs(y) * 0.08 + 0.02;
            upper.Add(new Point(x, y + placeholderSigma));
            lower.Add(new Point(x, y - placeholderSigma));
        }
        return upper.Count == 0 ? null : new ChartBand(upper, lower, Palette.AmberFill);
    }

    public static (double Min, double Max) RobustRange(IReadOnlyList<IReadOnlyList<Point>> segments)
    {
        var ys = segments.SelectMany(segment => segment).Select(point => point.Y).Order().ToArray();
        if (ys.Length == 0) return (0, 1);
        var lo = Percentile(ys, 0.02);
        var hi = Percentile(ys, 0.98);
        if (hi - lo < 1e-9)
        {
            lo -= 1;
            hi += 1;
        }
        var pad = (hi - lo) * 0.1;
        return (lo - pad, hi + pad);
    }

    public static IReadOnlyList<double> NiceSteps(double min, double max, int targetCount)
    {
        var range = max - min;
        if (range <= 0 || !double.IsFinite(range)) return [];
        var step = NiceStep(range / targetCount);
        var values = new List<double>();
        for (var value = Math.Ceiling(min / step) * step; value <= max + step * 1e-6; value += step)
            values.Add(value);
        return values;
    }

    private static double NiceStep(double rough)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var normalized = rough / magnitude;
        var multiplier = normalized < 1.5 ? 1 : normalized < 3.5 ? 2 : normalized < 7.5 ? 5 : 10;
        return magnitude * multiplier;
    }

    private static double Percentile(double[] sorted, double fraction)
    {
        var position = fraction * (sorted.Length - 1);
        var index = (int)position;
        var next = Math.Min(index + 1, sorted.Length - 1);
        var weight = position - index;
        return sorted[index] * (1 - weight) + sorted[next] * weight;
    }
}
