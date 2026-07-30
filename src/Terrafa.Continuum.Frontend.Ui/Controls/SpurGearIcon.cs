// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Media;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Terrafa.Continuum.Frontend.Controls;

public sealed class SpurGearIcon : ShapePath
{
    private const int ToothCount = 8;
    private const double TipRadius = 5.5;
    private const double RootRadius = 4.1;
    private const double BoreRadius = 1.9;

    public SpurGearIcon()
    {
        Data = BuildGear();
        Stretch = Stretch.None;
        Width = TipRadius * 2;
        Height = TipRadius * 2;
    }

    private static Geometry BuildGear()
    {
        var center = new Point(TipRadius, TipRadius);
        var step = Math.Tau / ToothCount;
        var tipHalfAngle = step * 0.2;
        var rootHalfAngle = step * 0.3;

        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.SetFillRule(FillRule.EvenOdd);

        context.BeginFigure(Polar(center, RootRadius, -rootHalfAngle), true);
        for (var tooth = 0; tooth < ToothCount; tooth++)
        {
            var angle = tooth * step;
            context.LineTo(Polar(center, TipRadius, angle - tipHalfAngle));
            context.LineTo(Polar(center, TipRadius, angle + tipHalfAngle));
            context.LineTo(Polar(center, RootRadius, angle + rootHalfAngle));
            context.LineTo(Polar(center, RootRadius, angle + step - rootHalfAngle));
        }
        context.EndFigure(true);

        var boreRight = new Point(center.X + BoreRadius, center.Y);
        var boreLeft = new Point(center.X - BoreRadius, center.Y);
        var boreSize = new Size(BoreRadius, BoreRadius);
        context.BeginFigure(boreRight, true);
        context.ArcTo(boreLeft, boreSize, 0, false, SweepDirection.Clockwise);
        context.ArcTo(boreRight, boreSize, 0, false, SweepDirection.Clockwise);
        context.EndFigure(true);

        return geometry;
    }

    private static Point Polar(Point center, double radius, double angle) =>
        new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
}
