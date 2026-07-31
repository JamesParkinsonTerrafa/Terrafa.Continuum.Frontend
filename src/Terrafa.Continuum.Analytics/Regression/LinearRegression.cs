// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using MathNet.Numerics;
using MathNet.Numerics.LinearRegression;

namespace Terrafa.Continuum.Analytics.Regression;

/// <summary>
/// A fitted line. <see cref="Predict"/> evaluated at each drawn x is what overlays the trend on a
/// chart; <see cref="RSquared"/> is there for the caller to show, so the line never asserts more
/// than the points behind it do.5
/// </summary>
public sealed record LinearFit(double Intercept, double Slope, double RSquared)
{
    /// <summary>The fitted y at <paramref name="x"/>.</summary>
    public double Predict(double x) => Intercept + Slope * x;
}

/// <summary>
/// Single-variate least squares over series in the shape the data tree hands out.
///
/// <para>
/// A tree leaf's series is an <c>IReadOnlyList&lt;double&gt;</c>, oldest first — Measure.History
/// on the frontend — and two leaves read from one dataset align row-by-row because the service
/// returns one row per axis value. Both overloads take exactly that shape, so a regression wires
/// straight off the tree: the axis leaf's series as x for "level against timestamp", or another
/// measure's series as x for a cross-measure fit. The x-less overload regresses against index
/// 0..n−1, which is the same x a chart gives a series drawn on its own.
/// </para>
///
/// <para>
/// Data problems and structural problems part ways here as they do in the tree. A NaN reading —
/// the tree's spelling of "not plottable" — propagates to NaN coefficients rather than being
/// silently dropped, because repairing a series is upstream's job. Mismatched lengths and fewer
/// than two points throw: those are broken callers, not broken data.
/// </para>
/// </summary>
public static class LinearRegression
{
    /// <summary>Fits y against its own indices 0..n−1 — the x a chart drawing this series uses.</summary>
    public static LinearFit Fit(IReadOnlyList<double> y)
    {
        var x = new double[y.Count];
        for (var i = 0; i < x.Length; i++) x[i] = i;
        return Fit(x, y);
    }

    /// <summary>Fits y = a + b·x by ordinary least squares.</summary>
    public static LinearFit Fit(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        if (x.Count != y.Count)
            throw new ArgumentException(
                $"x carries {x.Count} points and y {y.Count}. Series read from one dataset align " +
                "row-by-row; a mismatch means these came from different reads, and pairing them " +
                "by index would invent data.");
        if (y.Count < 2)
            throw new ArgumentException("A line through fewer than two points is not a fit.", nameof(y));

        double[] xs = [.. x];
        double[] ys = [.. y];
        var (intercept, slope) = SimpleRegression.Fit(xs, ys);
        return new LinearFit(
            intercept,
            slope,
            GoodnessOfFit.RSquared(xs.Select(value => intercept + slope * value), ys));
    }
}
