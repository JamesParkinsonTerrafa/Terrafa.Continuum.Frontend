using Terrafa.Continuum.Analytics.Regression;

namespace Terrafa.Continuum.Analytics.Tests;

/// <summary>
/// Pins the fit to numbers computed by hand, so a wrong sign or a swapped intercept/slope out of
/// the library — or out of a future replacement for it — fails loudly rather than drawing a
/// plausible-looking trend. Also pins the shape rules: structural misuse throws, NaN data stays
/// NaN, because a regression that silently repaired its inputs would put invented lines on charts.
/// </summary>
public class LinearRegressionTests
{
    [Fact]
    public void RecoversAnExactLine()
    {
        var fit = LinearRegression.Fit([1.0, 2.0, 3.0, 4.0], [5.0, 7.0, 9.0, 11.0]);

        Assert.Equal(3.0, fit.Intercept, precision: 10);
        Assert.Equal(2.0, fit.Slope, precision: 10);
        Assert.Equal(1.0, fit.RSquared, precision: 10);
        Assert.Equal(13.0, fit.Predict(5.0), precision: 10);
    }

    /// <summary>
    /// x = {0, 1, 2, 3}, y = {1, 3, 2, 5}: slope = 22/20 = 1.1, intercept = 1.1,
    /// R² = 1 − 2.7/8.75. Worked by hand, which is the point — the expected numbers do not come
    /// from the library under test.
    /// </summary>
    [Fact]
    public void LeastSquaresOverScatteredPoints()
    {
        var fit = LinearRegression.Fit([0.0, 1.0, 2.0, 3.0], [1.0, 3.0, 2.0, 5.0]);

        Assert.Equal(1.1, fit.Intercept, precision: 12);
        Assert.Equal(1.1, fit.Slope, precision: 12);
        Assert.Equal(1.0 - 2.7 / 8.75, fit.RSquared, precision: 12);
    }

    /// <summary>The x-less overload is index regression — the x a chart gives a lone series.</summary>
    [Fact]
    public void IndexOverloadMatchesExplicitIndices()
    {
        double[] history = [10.0, 12.0, 14.0, 16.0];

        var byIndex = LinearRegression.Fit(history);
        var explicitly = LinearRegression.Fit([0.0, 1.0, 2.0, 3.0], history);

        Assert.Equal(explicitly, byIndex);
        Assert.Equal(10.0, byIndex.Intercept, precision: 10);
        Assert.Equal(2.0, byIndex.Slope, precision: 10);
    }

    /// <summary>
    /// The axis-leaf case: x as timestamps with a gap. The slope must come out per x-unit, not
    /// per row — which is exactly what regressing against index would get wrong here.
    /// </summary>
    [Fact]
    public void UnevenAxisSpacingWeighsIn()
    {
        var fit = LinearRegression.Fit([0.0, 60.0, 120.0, 300.0], [10.0, 11.0, 12.0, 15.0]);

        Assert.Equal(10.0, fit.Intercept, precision: 10);
        Assert.Equal(1.0 / 60.0, fit.Slope, precision: 12);
        Assert.Equal(1.0, fit.RSquared, precision: 10);
    }

    /// <summary>NaN is the tree's "not plottable"; a fit over it must stay NaN, not paper over it.</summary>
    [Fact]
    public void NaNReadingPropagates()
    {
        var fit = LinearRegression.Fit([0.0, 1.0, 2.0], [1.0, double.NaN, 3.0]);

        Assert.True(double.IsNaN(fit.Intercept));
        Assert.True(double.IsNaN(fit.Slope));
    }

    [Fact]
    public void MismatchedSeriesThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            LinearRegression.Fit([1.0, 2.0], [1.0, 2.0, 3.0]));
    }

    [Fact]
    public void OnePointIsNotAFit()
    {
        Assert.Throws<ArgumentException>(() =>
            LinearRegression.Fit([1.0], [2.0]));
    }
}
