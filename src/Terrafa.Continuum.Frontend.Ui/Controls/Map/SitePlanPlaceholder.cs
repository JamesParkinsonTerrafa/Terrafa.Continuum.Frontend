// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Terrafa.Continuum.Frontend.Controls.Map;

/// <summary>A named piece of the drawn plan, in normalized plan coordinates (0..1), with its
/// rotation in degrees. Zones and seed pins are anchored off these so the geometry is stated
/// once — move a tank cluster here and the zone that frames it follows.</summary>
public sealed record PlanFeature(string Key, Point Centre, Size Size, double Angle);

/// <summary>
/// Stand-in for the client's own aerial photo: a site drawn from fixed numbers so the map has
/// something real to pin onto before anyone uploads anything, and so the snapshots are stable.
///
/// Colours here are literal rather than themed. This is standing in for a photograph, and a
/// photograph does not repaint itself when the operator switches to the dark theme.
/// </summary>
public sealed class SitePlanPlaceholder : Control
{
    public const double DesignWidth = 1500;
    public const double DesignHeight = 1000;
    public const double Aspect = DesignWidth / DesignHeight;

    private const double PadAngle = -7;
    private const double BerthAngle = 3;
    private const double TankRadius = 52;
    private static readonly Point PadCentre = new(805, 425);
    private static readonly Size PadSize = new(880, 430);
    private static readonly Point BerthCentre = new(300, 720);
    private static readonly Size BerthSize = new(330, 120);

    // Tank centres in pad-local space, so the clusters stay square to the concrete they sit on.
    private static readonly Point[] TankCluster01 = [new(-215, -55), new(-105, -55), new(-215, 55), new(-105, 55)];
    private static readonly Point[] TankCluster02 = [new(110, -70), new(220, -70), new(110, 40), new(220, 40)];

    private static readonly Point[] Shoreline =
        [new(0, 520), new(140, 660), new(250, 762), new(330, 862), new(430, 1000)];

    private static readonly SolidColorBrush Ground = new(Color.Parse("#C7B189"));
    private static readonly SolidColorBrush MottleDark = new(Color.Parse("#0EA78F63"));
    private static readonly SolidColorBrush MottleLight = new(Color.Parse("#0EDCC9A2"));
    private static readonly SolidColorBrush Water = new(Color.Parse("#3B4A53"));
    private static readonly SolidColorBrush Sand = new(Color.Parse("#DBCCA6"));
    private static readonly SolidColorBrush Concrete = new(Color.Parse("#8E8E8A"));
    private static readonly SolidColorBrush Building = new(Color.Parse("#E4E2DC"));
    private static readonly SolidColorBrush Scrub = new(Color.Parse("#8C77855A"));
    private static readonly SolidColorBrush TankHub = new(Color.Parse("#A9A9A3"));
    private static readonly SolidColorBrush Shadow = new(Color.Parse("#26000000"));

    private static readonly LinearGradientBrush TankBody = new()
    {
        StartPoint = RelativePoint.TopLeft,
        EndPoint = RelativePoint.BottomRight,
        GradientStops =
        {
            new GradientStop(Color.Parse("#FFFFFF"), 0),
            new GradientStop(Color.Parse("#F2F2EF"), 0.45),
            new GradientStop(Color.Parse("#C9C9C3"), 1)
        }
    };

    private static readonly Pen SeamPen = new(new SolidColorBrush(Color.Parse("#7F7F7B")), 1.2);
    private static readonly Pen TankEdgePen = new(new SolidColorBrush(Color.Parse("#B2B2AC")), 1.4);
    private static readonly Pen TankSeamPen = new(new SolidColorBrush(Color.Parse("#D3D3CD")), 1.4);
    private static readonly Pen RoadPen = new(new SolidColorBrush(Color.Parse("#D6C7A3")), 17)
    {
        LineCap = PenLineCap.Round
    };
    private static readonly Pen PipePen = new(new SolidColorBrush(Color.Parse("#8E887A")), 8);
    private static readonly Pen PipeCorePen = new(new SolidColorBrush(Color.Parse("#6F6A5E")), 2);
    private static readonly Pen ShorePen = new(new SolidColorBrush(Color.Parse("#C9D1D3")), 3) { LineCap = PenLineCap.Round };

    private static readonly Matrix PadTransform =
        Matrix.CreateRotation(PadAngle * Math.PI / 180) * Matrix.CreateTranslation(PadCentre.X, PadCentre.Y);

    private static readonly Matrix BerthTransform =
        Matrix.CreateRotation(BerthAngle * Math.PI / 180) * Matrix.CreateTranslation(BerthCentre.X, BerthCentre.Y);

    /// <summary>Where the overlay hangs its zones. Normalized, so they survive an image swap.</summary>
    public static IReadOnlyList<PlanFeature> Features { get; } =
    [
        ClusterFeature("tank_01", TankCluster01),
        ClusterFeature("tank_02", TankCluster02),
        new("berth", Normalize(BerthCentre), Normalize(new Size(BerthSize.Width + 40, BerthSize.Height + 40)), BerthAngle)
    ];

    public static PlanFeature Feature(string key) => Features.First(feature => feature.Key == key);

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        using var clip = context.PushClip(new Rect(Bounds.Size));
        using var scale = context.PushTransform(
            Matrix.CreateScale(Bounds.Width / DesignWidth, Bounds.Height / DesignHeight));

        DrawGround(context);
        DrawWater(context);
        DrawScrub(context);
        DrawRoad(context);
        DrawPipeline(context);
        DrawBerth(context);
        DrawPad(context);
    }

    private static void DrawGround(DrawingContext context)
    {
        context.FillRectangle(Ground, new Rect(0, 0, DesignWidth, DesignHeight));

        var rng = new Random(20260726);
        for (var i = 0; i < 320; i++)
        {
            var centre = new Point(rng.NextDouble() * DesignWidth, rng.NextDouble() * DesignHeight);
            var radiusX = 14 + rng.NextDouble() * 55;
            var radiusY = radiusX * (0.4 + rng.NextDouble() * 0.5);
            context.DrawEllipse(i % 2 == 0 ? MottleDark : MottleLight, null, centre, radiusX, radiusY);
        }
    }

    private static void DrawWater(DrawingContext context)
    {
        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(Shoreline[0], true);
            for (var i = 1; i < Shoreline.Length; i++) sink.LineTo(Shoreline[i]);
            sink.LineTo(new Point(0, DesignHeight));
            sink.EndFigure(true);
        }

        // Sand first, water over it: what is left showing is a beach on the land side only.
        for (var i = 1; i < Shoreline.Length; i++)
        {
            context.DrawLine(new Pen(Sand, 26), Shoreline[i - 1], Shoreline[i]);
        }
        context.DrawGeometry(Water, null, geometry);
        for (var i = 1; i < Shoreline.Length; i++)
        {
            context.DrawLine(ShorePen, Shoreline[i - 1], Shoreline[i]);
        }
    }

    private static void DrawScrub(DrawingContext context)
    {
        var rng = new Random(1284102);
        for (var i = 0; i < 90; i++)
        {
            var centre = new Point(rng.NextDouble() * DesignWidth, rng.NextDouble() * DesignHeight);
            if (!IsOpenGround(centre)) continue;
            var radius = 5 + rng.NextDouble() * 9;
            context.DrawEllipse(Scrub, null, centre, radius, radius * (0.55 + rng.NextDouble() * 0.4));
        }
    }

    private static void DrawRoad(DrawingContext context)
    {
        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(new Point(DesignWidth, 120), false);
            sink.CubicBezierTo(new Point(1330, 100), new Point(1258, 235), new Point(1232, 340));
            sink.CubicBezierTo(new Point(1210, 430), new Point(1225, 520), new Point(1290, 620));
            sink.EndFigure(false);
        }
        context.DrawGeometry(null, RoadPen, geometry);
    }

    private static void DrawPipeline(DrawingContext context)
    {
        var from = new Point(424, 690);
        var to = new Point(626, 566);
        context.DrawLine(PipePen, from, to);
        context.DrawLine(PipeCorePen, from, to);
    }

    private static void DrawBerth(DrawingContext context)
    {
        // Jetty finger, drawn in design space: it reaches off the apron into the water.
        using (context.PushTransform(
                   Matrix.CreateRotation(-35 * Math.PI / 180) * Matrix.CreateTranslation(150, 830)))
        {
            context.FillRectangle(Shadow, new Rect(-119, -11, 250, 34));
            context.FillRectangle(Concrete, new Rect(-125, -17, 250, 34));
            context.DrawLine(SeamPen, new Point(-125, 0), new Point(125, 0));
        }

        using (context.PushTransform(BerthTransform))
        {
            var apron = new Rect(-BerthSize.Width / 2, -BerthSize.Height / 2, BerthSize.Width, BerthSize.Height);
            context.FillRectangle(Shadow, apron.Translate(new Vector(6, 8)));
            context.FillRectangle(Concrete, apron);
            for (var x = apron.X + 70; x < apron.Right; x += 70)
            {
                context.DrawLine(SeamPen, new Point(x, apron.Y), new Point(x, apron.Bottom));
            }
            context.FillRectangle(Shadow, new Rect(44, -22, 95, 58).Translate(new Vector(5, 7)));
            context.FillRectangle(Building, new Rect(44, -22, 95, 58));
        }
    }

    private static void DrawPad(DrawingContext context)
    {
        using var transform = context.PushTransform(PadTransform);

        var pad = new Rect(-PadSize.Width / 2, -PadSize.Height / 2, PadSize.Width, PadSize.Height);
        context.FillRectangle(Shadow, pad.Translate(new Vector(7, 9)));
        context.FillRectangle(Concrete, pad);
        for (var x = pad.X + 88; x < pad.Right; x += 88)
        {
            context.DrawLine(SeamPen, new Point(x, pad.Y), new Point(x, pad.Bottom));
        }

        context.FillRectangle(Shadow, new Rect(292, -168, 130, 72).Translate(new Vector(6, 8)));
        context.FillRectangle(Building, new Rect(292, -168, 130, 72));

        foreach (var centre in TankCluster01) DrawTank(context, centre);
        foreach (var centre in TankCluster02) DrawTank(context, centre);
    }

    private static void DrawTank(DrawingContext context, Point centre)
    {
        context.DrawEllipse(Shadow, null, centre + new Vector(8, 10), TankRadius, TankRadius);
        context.DrawEllipse(TankBody, TankEdgePen, centre, TankRadius, TankRadius);
        context.DrawEllipse(null, TankSeamPen, centre, TankRadius * 0.62, TankRadius * 0.62);
        context.DrawLine(TankSeamPen, centre - new Vector(TankRadius, 0), centre + new Vector(TankRadius, 0));
        context.DrawLine(TankSeamPen, centre - new Vector(0, TankRadius), centre + new Vector(0, TankRadius));
        context.DrawEllipse(TankHub, null, centre, 4, 4);
    }

    /// <summary>Scrub only grows where there is neither water nor concrete.</summary>
    private static bool IsOpenGround(Point point)
    {
        if (point.Y > ShorelineDepth(point.X) - 30) return false;

        var onPad = point.Transform(PadTransform.Invert());
        if (Math.Abs(onPad.X) < PadSize.Width / 2 + 30 && Math.Abs(onPad.Y) < PadSize.Height / 2 + 30) return false;

        var onBerth = point.Transform(BerthTransform.Invert());
        return Math.Abs(onBerth.X) > BerthSize.Width / 2 + 30 || Math.Abs(onBerth.Y) > BerthSize.Height / 2 + 30;
    }

    private static double ShorelineDepth(double x)
    {
        if (x >= Shoreline[^1].X) return DesignHeight * 2;
        for (var i = 1; i < Shoreline.Length; i++)
        {
            if (x > Shoreline[i].X) continue;
            var (start, end) = (Shoreline[i - 1], Shoreline[i]);
            var span = end.X - start.X;
            var t = span <= 0 ? 0 : (x - start.X) / span;
            return start.Y + (end.Y - start.Y) * t;
        }
        return DesignHeight * 2;
    }

    private static PlanFeature ClusterFeature(string key, Point[] tanks)
    {
        var padding = TankRadius + 18;
        var left = tanks.Min(tank => tank.X) - padding;
        var top = tanks.Min(tank => tank.Y) - padding;
        var right = tanks.Max(tank => tank.X) + padding;
        var bottom = tanks.Max(tank => tank.Y) + padding;
        var centre = new Point((left + right) / 2, (top + bottom) / 2).Transform(PadTransform);
        return new PlanFeature(key, Normalize(centre), Normalize(new Size(right - left, bottom - top)), PadAngle);
    }

    private static Point Normalize(Point point) => new(point.X / DesignWidth, point.Y / DesignHeight);

    private static Size Normalize(Size size) => new(size.Width / DesignWidth, size.Height / DesignHeight);
}
