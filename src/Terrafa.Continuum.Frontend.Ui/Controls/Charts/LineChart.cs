// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls.Charts;

public class LineChart : Control
{
    private static readonly Typeface ChartTypeface = new(Palette.Font);

    public double XMin { get; set; }
    public double XMax { get; set; } = 1;
    public double YMin { get; set; }
    public double YMax { get; set; } = 1;
    public double MarginLeft { get; set; } = 36;
    public double MarginRight { get; set; } = 16;
    public double MarginTop { get; set; } = 16;
    public double MarginBottom { get; set; } = 30;
    public bool ShowAxes { get; set; } = true;
    public IReadOnlyList<double> VerticalGridValues { get; set; } = [];
    public IReadOnlyList<double> HorizontalGridValues { get; set; } = [];
    public IReadOnlyList<ChartRegion> Regions { get; set; } = [];
    public ChartBand? Band { get; set; }
    public IReadOnlyList<double> BarValues { get; set; } = [];
    public IReadOnlyList<IBrush> BarBrushes { get; set; } = [];

    /// <summary>
    /// 1σ per bar, drawn as a whisker. Shorter than <see cref="BarValues"/> means the trailing bars
    /// carry no variance and get no whisker; NaN does the same for a single bar.
    /// </summary>
    public IReadOnlyList<double> BarSigmas { get; set; } = [];

    public IBrush BarWhiskerBrush { get; set; } = Palette.TextSub;
    public IReadOnlyList<ChartSeries> Series { get; set; } = [];
    public double? ThresholdY { get; set; }
    public IBrush ThresholdBrush { get; set; } = Palette.Red;
    public string ThresholdLabel { get; set; } = "";
    public IReadOnlyList<ChartMarker> Markers { get; set; } = [];
    public IReadOnlyList<ChartLabel> Labels { get; set; } = [];
    public IReadOnlyList<AxisTick> XTicks { get; set; } = [];
    public string XAxisTitle { get; set; } = "";
    public string YAxisTitle { get; set; } = "";

    public void Refresh() => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        var plot = new Rect(
            MarginLeft,
            MarginTop,
            Math.Max(Bounds.Width - MarginLeft - MarginRight, 1),
            Math.Max(Bounds.Height - MarginTop - MarginBottom, 1));

        DrawRegions(context, plot);
        DrawGrid(context, plot);
        if (ShowAxes) DrawAxes(context, plot);

        using (context.PushClip(plot))
        {
            DrawBand(context, plot);
            DrawBars(context, plot);
            DrawSeriesLines(context, plot);
        }

        DrawThreshold(context, plot);
        DrawMarkers(context, plot);
        DrawSeriesLabels(context, plot);
        DrawLabels(context, plot);
        DrawTicks(context, plot);
        DrawAxisTitles(context, plot);
    }

    private Point Map(Point dataPoint, Rect plot)
    {
        var x = plot.X + (dataPoint.X - XMin) / (XMax - XMin) * plot.Width;
        var y = plot.Bottom - (dataPoint.Y - YMin) / (YMax - YMin) * plot.Height;
        return new Point(x, y);
    }

    private void DrawRegions(DrawingContext context, Rect plot)
    {
        foreach (var region in Regions)
        {
            var left = Map(new Point(region.XStart, 0), plot).X;
            var right = Map(new Point(region.XEnd, 0), plot).X;
            context.FillRectangle(region.Fill, new Rect(left, plot.Y, right - left, plot.Height));
        }
    }

    private void DrawGrid(DrawingContext context, Rect plot)
    {
        var pen = new Pen(Palette.GridFaint, 1);
        foreach (var value in VerticalGridValues)
        {
            var x = Map(new Point(value, 0), plot).X;
            context.DrawLine(pen, new Point(x, plot.Y), new Point(x, plot.Bottom));
        }
        foreach (var value in HorizontalGridValues)
        {
            var y = Map(new Point(0, value), plot).Y;
            context.DrawLine(pen, new Point(plot.X, y), new Point(plot.Right, y));
        }
    }

    private void DrawAxes(DrawingContext context, Rect plot)
    {
        var pen = new Pen(Palette.BorderMid, 1);
        context.DrawLine(pen, new Point(plot.X, plot.Bottom), new Point(plot.Right, plot.Bottom));
        context.DrawLine(pen, new Point(plot.X, plot.Bottom), new Point(plot.X, plot.Y));
    }

    private void DrawBand(DrawingContext context, Rect plot)
    {
        if (Band is null || Band.Upper.Count == 0) return;

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(Map(Band.Upper[0], plot), true);
            for (var i = 1; i < Band.Upper.Count; i++)
                geometryContext.LineTo(Map(Band.Upper[i], plot));
            for (var i = Band.Lower.Count - 1; i >= 0; i--)
                geometryContext.LineTo(Map(Band.Lower[i], plot));
            geometryContext.EndFigure(true);
        }
        context.DrawGeometry(Band.Fill, null, geometry);
    }

    private void DrawBars(DrawingContext context, Rect plot)
    {
        if (BarValues.Count == 0) return;

        var slot = plot.Width / BarValues.Count;
        var barWidth = slot * 0.62;
        for (var i = 0; i < BarValues.Count; i++)
        {
            var brush = i < BarBrushes.Count ? BarBrushes[i] : Palette.BarFillLow;
            var top = Map(new Point(0, BarValues[i]), plot).Y;
            var x = plot.X + slot * i + (slot - barWidth) / 2;
            context.FillRectangle(brush, new Rect(x, top, barWidth, plot.Bottom - top));
        }

        DrawBarWhiskers(context, plot, slot, barWidth);
    }

    /// <summary>A bar without a whisker states a quantity it cannot support — so σ is drawn on top of the fill.</summary>
    private void DrawBarWhiskers(DrawingContext context, Rect plot, double slot, double barWidth)
    {
        if (BarSigmas.Count == 0) return;

        var pen = new Pen(BarWhiskerBrush, 1);
        var capHalfWidth = Math.Max(barWidth * 0.22, 2);
        var limit = Math.Min(BarValues.Count, BarSigmas.Count);

        for (var i = 0; i < limit; i++)
        {
            var sigma = BarSigmas[i];
            if (double.IsNaN(sigma) || sigma <= 0) continue;

            var centre = plot.X + slot * i + slot / 2;
            var upper = Map(new Point(0, BarValues[i] + sigma), plot).Y;
            var lower = Map(new Point(0, BarValues[i] - sigma), plot).Y;

            context.DrawLine(pen, new Point(centre, upper), new Point(centre, lower));
            context.DrawLine(pen, new Point(centre - capHalfWidth, upper), new Point(centre + capHalfWidth, upper));
            context.DrawLine(pen, new Point(centre - capHalfWidth, lower), new Point(centre + capHalfWidth, lower));
        }
    }

    private void DrawSeriesLines(DrawingContext context, Rect plot)
    {
        foreach (var series in Series)
        {
            DrawSeriesBounds(context, plot, series);

            if (series.Points.Count < 2) continue;
            var pen = new Pen(series.Stroke, series.Thickness)
            {
                DashStyle = series.Dashes is null ? null : new DashStyle(series.Dashes, 0)
            };
            context.DrawGeometry(null, pen, Polyline(series.Points, plot));
        }
    }

    /// <summary>
    /// The line-chart form of a bound: a trace above and one below, thinner than the series so the
    /// central estimate still reads first, over a faint fill that ties the pair together.
    /// </summary>
    private void DrawSeriesBounds(DrawingContext context, Rect plot, ChartSeries series)
    {
        if (!series.HasBounds) return;

        var upper = series.Upper!;
        var lower = series.Lower!;

        if (series.BoundFill is { } fill)
        {
            var band = new StreamGeometry();
            using (var bandContext = band.Open())
            {
                bandContext.BeginFigure(Map(upper[0], plot), true);
                for (var i = 1; i < upper.Count; i++)
                    bandContext.LineTo(Map(upper[i], plot));
                for (var i = lower.Count - 1; i >= 0; i--)
                    bandContext.LineTo(Map(lower[i], plot));
                bandContext.EndFigure(true);
            }
            context.DrawGeometry(fill, null, band);
        }

        var pen = new Pen(series.Stroke, Math.Max(series.Thickness * 0.5, 0.75))
        {
            DashStyle = new DashStyle([3, 3], 0)
        };
        context.DrawGeometry(null, pen, Polyline(upper, plot));
        context.DrawGeometry(null, pen, Polyline(lower, plot));
    }

    private StreamGeometry Polyline(IReadOnlyList<Point> points, Rect plot)
    {
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(Map(points[0], plot), false);
            for (var i = 1; i < points.Count; i++)
                geometryContext.LineTo(Map(points[i], plot));
            geometryContext.EndFigure(false);
        }
        return geometry;
    }

    private void DrawThreshold(DrawingContext context, Rect plot)
    {
        if (ThresholdY is not { } threshold) return;

        var y = Map(new Point(0, threshold), plot).Y;
        var pen = new Pen(ThresholdBrush, 1) { DashStyle = new DashStyle([5, 4], 0) };
        context.DrawLine(pen, new Point(plot.X, y), new Point(plot.Right, y));
        if (ThresholdLabel.Length > 0)
        {
            var text = Format(ThresholdLabel, ThresholdBrush, 10);
            context.DrawText(text, new Point(plot.Right - text.Width, y - text.Height - 2));
        }
    }

    private void DrawMarkers(DrawingContext context, Rect plot)
    {
        foreach (var marker in Markers)
        {
            var center = Map(new Point(marker.X, marker.Y), plot);
            context.DrawEllipse(marker.Fill, null, center, marker.Radius, marker.Radius);
        }
    }

    private void DrawSeriesLabels(DrawingContext context, Rect plot)
    {
        foreach (var series in Series)
        {
            if (series.Label is null || series.LabelAt is not { } at) continue;
            var text = Format(series.Label, series.Stroke, 10);
            var position = Map(at, plot);
            var x = series.LabelAnchorEnd ? position.X - text.Width : position.X;
            context.DrawText(text, new Point(x, position.Y - text.Height / 2));
        }
    }

    private void DrawLabels(DrawingContext context, Rect plot)
    {
        foreach (var label in Labels)
        {
            var text = Format(label.Text, label.Brush, label.FontSize);
            var position = Map(new Point(label.X, label.Y), plot);
            var x = label.AnchorEnd ? position.X - text.Width : position.X;
            context.DrawText(text, new Point(x, position.Y - text.Height / 2));
        }
    }

    private void DrawTicks(DrawingContext context, Rect plot)
    {
        foreach (var tick in XTicks)
        {
            var text = Format(tick.Label, Palette.TextFaint, 10);
            var x = Map(new Point(tick.Value, 0), plot).X;
            context.DrawText(text, new Point(x - text.Width / 2, plot.Bottom + 4));
        }
    }

    private void DrawAxisTitles(DrawingContext context, Rect plot)
    {
        if (XAxisTitle.Length > 0)
        {
            var text = Format(XAxisTitle, Palette.TextMuted, 11);
            context.DrawText(text, new Point(
                plot.X + plot.Width / 2 - text.Width / 2,
                Bounds.Height - text.Height - 1));
        }
        if (YAxisTitle.Length > 0)
        {
            var text = Format(YAxisTitle, Palette.TextMuted, 11);
            var centerY = plot.Y + plot.Height / 2;
            var matrix = Matrix.CreateTranslation(-text.Width / 2, -text.Height / 2) *
                         Matrix.CreateRotation(-Math.PI / 2) *
                         Matrix.CreateTranslation(MarginLeft - 22, centerY);
            using (context.PushTransform(matrix))
            {
                context.DrawText(text, default);
            }
        }
    }

    private static FormattedText Format(string value, IBrush brush, double fontSize) =>
        new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, ChartTypeface, TypographySettings.Size(fontSize), brush);
}
