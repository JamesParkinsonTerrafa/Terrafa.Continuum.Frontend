// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Terrafa.Continuum.Frontend.Controls.Charts;

public sealed class Edge
{
    public required Point From { get; init; }
    public required Point To { get; init; }
    public required IBrush Stroke { get; init; }
    public double Thickness { get; init; } = 1.5;
    public double[]? Dashes { get; init; }
    public bool ArrowAtEnd { get; init; }
    public double Opacity { get; init; } = 1.0;
    public Point? BendControl1 { get; init; }
    public Point? BendControl2 { get; init; }
}

public class EdgeLayer : Control
{
    private IReadOnlyList<Edge> edges = [];

    public IReadOnlyList<Edge> Edges
    {
        get => edges;
        set
        {
            edges = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        foreach (var edge in edges)
        {
            var pen = new Pen(edge.Stroke, edge.Thickness)
            {
                DashStyle = edge.Dashes is null ? null : new DashStyle(edge.Dashes, 0)
            };

            using (context.PushOpacity(edge.Opacity))
            {
                if (edge.BendControl1 is { } control1)
                {
                    var control2 = edge.BendControl2 ?? control1;
                    var geometry = new StreamGeometry();
                    using (var geometryContext = geometry.Open())
                    {
                        geometryContext.BeginFigure(edge.From, false);
                        geometryContext.CubicBezierTo(control1, control2, edge.To);
                        geometryContext.EndFigure(false);
                    }
                    context.DrawGeometry(null, pen, geometry);
                }
                else
                {
                    context.DrawLine(pen, edge.From, edge.To);
                }

                if (edge.ArrowAtEnd)
                    DrawArrowHead(context, edge);
            }
        }
    }

    private static void DrawArrowHead(DrawingContext context, Edge edge)
    {
        var origin = edge.BendControl2 ?? edge.BendControl1 ?? edge.From;
        var direction = edge.To - origin;
        var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (length < 0.001) return;

        var unit = new Point(direction.X / length, direction.Y / length);
        var perpendicular = new Point(-unit.Y, unit.X);
        var back = new Point(edge.To.X - unit.X * 12, edge.To.Y - unit.Y * 12);
        var left = new Point(back.X + perpendicular.X * 6, back.Y + perpendicular.Y * 6);
        var right = new Point(back.X - perpendicular.X * 6, back.Y - perpendicular.Y * 6);

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(edge.To, true);
            geometryContext.LineTo(left);
            geometryContext.LineTo(right);
            geometryContext.EndFigure(true);
        }
        context.DrawGeometry(edge.Stroke, null, geometry);
    }
}
