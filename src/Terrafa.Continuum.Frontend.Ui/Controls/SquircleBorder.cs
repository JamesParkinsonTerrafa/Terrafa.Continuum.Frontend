// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Terrafa.Continuum.Frontend.Controls;

public class SquircleBorder : Decorator
{
    /// <summary>Figma's "iOS" preset — the smoothing that matches Apple's continuous corners.</summary>
    public const double AppleCornerSmoothing = 0.6;

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<SquircleBorder, IBrush?>(nameof(Background));

    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<SquircleBorder, double>(nameof(CornerRadius), 12);

    public static readonly StyledProperty<double> CornerSmoothingProperty =
        AvaloniaProperty.Register<SquircleBorder, double>(nameof(CornerSmoothing), AppleCornerSmoothing);

    public static readonly StyledProperty<BoxShadows> BoxShadowProperty =
        AvaloniaProperty.Register<SquircleBorder, BoxShadows>(nameof(BoxShadow));

    public static readonly StyledProperty<double> ShadowStrengthProperty =
        AvaloniaProperty.Register<SquircleBorder, double>(nameof(ShadowStrength), 1);

    // Avalonia's BoxShadows can only be cast from a RoundedRect, so the shadows are drawn as
    // blurred copies of the squircle instead. A blur has to live on a visual (DrawingContext has
    // no effect push), and a clip only contains a blur when it sits on the *parent* — hence one
    // host per shadow direction, each holding one blurred visual per shadow.
    private readonly ShadowHost raisedShadows = new();
    private readonly ShadowHost recessedShadows = new();

    // The unmodified outline is wanted by Render, by both host clips, and again on every repaint,
    // so it is kept until its inputs actually move. Same for the shadow shapes, which survive any
    // change that is only a change of colour.
    private StreamGeometry? outline;
    private (Size Size, double Radius, double Smoothing) outlineKey;
    private List<Layer>? layers;
    private (Size Size, double Radius, double Smoothing) layerKey;
    private BoxShadows layerShadows;
    private Geometry? exterior;

    static SquircleBorder()
    {
        AffectsRender<SquircleBorder>(BackgroundProperty);
    }

    public SquircleBorder()
    {
        // Both go in ahead of the decorated child so they stay underneath it. The raised host is
        // clipped to the region outside the outline, so it never fights the background for z-order.
        VisualChildren.Add(raisedShadows);
        VisualChildren.Add(recessedShadows);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double CornerSmoothing
    {
        get => GetValue(CornerSmoothingProperty);
        set => SetValue(CornerSmoothingProperty, value);
    }

    public BoxShadows BoxShadow
    {
        get => GetValue(BoxShadowProperty);
        set => SetValue(BoxShadowProperty, value);
    }

    public double ShadowStrength
    {
        get => GetValue(ShadowStrengthProperty);
        set => SetValue(ShadowStrengthProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CornerRadiusProperty || change.Property == CornerSmoothingProperty ||
            change.Property == BoxShadowProperty || change.Property == ShadowStrengthProperty)
        {
            InvalidateVisual();
            RebuildShadows(Bounds.Size);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        RebuildShadows(size);
        return size;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (Background is { } background)
            context.DrawGeometry(background, null, Outline(bounds));
    }

    private StreamGeometry Outline(Rect bounds)
    {
        var key = (bounds.Size, ClampedRadius(bounds), Math.Clamp(CornerSmoothing, 0, 1));
        if (outline is null || key != outlineKey)
        {
            outline = BuildOutline(bounds, key.Item2);
            outlineKey = key;
        }
        return outline;
    }

    private void RebuildShadows(Size size)
    {
        var bounds = new Rect(size);
        var strength = Math.Clamp(ShadowStrength, 0, 1);

        // Only the tint depends on strength, so the shapes survive an emboss-slider drag.
        List<Pass> raised = [];
        List<Pass> recessed = [];
        foreach (var layer in EnsureLayers(bounds))
        {
            var color = strength < 1 ? FadeAlpha(layer.Color, strength) : layer.Color;
            if (color.A == 0) continue;
            var pass = new Pass(layer.Shape, new ImmutableSolidColorBrush(color), layer.Blur);
            (layer.Inset ? recessed : raised).Add(pass);
        }

        // A raised shadow is never drawn under the shape it belongs to, matching CSS and the
        // BoxShadows this replaced — which matters as soon as a background is translucent.
        raisedShadows.Set(raised, bounds, raised.Count == 0 ? null : exterior);
        recessedShadows.Set(recessed, bounds, recessed.Count == 0 ? null : Outline(bounds));
    }

    private List<Layer> EnsureLayers(Rect bounds)
    {
        var shadows = BoxShadow;
        var radius = ClampedRadius(bounds);
        var smoothing = Math.Clamp(CornerSmoothing, 0, 1);
        if (layers is not null && layerKey == (bounds.Size, radius, smoothing) && SameShape(layerShadows, shadows))
            return layers;

        layerKey = (bounds.Size, radius, smoothing);
        layerShadows = shadows;
        layers = [];
        var margin = 0d;

        for (var i = 0; i < shadows.Count && bounds.Width > 0 && bounds.Height > 0; i++)
        {
            var shadow = shadows[i];
            var reach = Reach(shadow);
            margin = Math.Max(margin, reach);
            var offset = new Vector(shadow.OffsetX, shadow.OffsetY);

            var shape = shadow.IsInset
                // Everything outside the shrunken, offset shape — blurred, then clipped back to
                // the outline by the host. That ring of darkness is what reads as a recess.
                ? Exterior(bounds.Inflate(reach),
                    BuildOutline(bounds.Deflate(shadow.Spread).Translate(offset), radius - shadow.Spread))
                : BuildOutline(bounds.Inflate(shadow.Spread).Translate(offset), radius + shadow.Spread);

            layers.Add(new Layer(shape, shadow.Color, shadow.Blur, shadow.IsInset));
        }

        exterior = Exterior(bounds.Inflate(margin), Outline(bounds));
        return layers;
    }

    /// <summary>Compares everything about a shadow set except colour — the part that costs geometry.</summary>
    private static bool SameShape(BoxShadows a, BoxShadows b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            var (x, y) = (a[i], b[i]);
            if (x.OffsetX != y.OffsetX || x.OffsetY != y.OffsetY || x.Blur != y.Blur ||
                x.Spread != y.Spread || x.IsInset != y.IsInset)
                return false;
        }
        return true;
    }

    /// <summary>How far past the edge a shadow can still put ink.</summary>
    private static double Reach(BoxShadow shadow) =>
        shadow.Blur * 2 + Math.Abs(shadow.OffsetX) + Math.Abs(shadow.OffsetY) +
        Math.Abs(shadow.Spread) + 2;

    /// <summary>Everything in <paramref name="outer"/> that <paramref name="hole"/> does not cover.</summary>
    private static Geometry Exterior(Rect outer, Geometry hole) => new GeometryGroup
    {
        FillRule = FillRule.EvenOdd,
        Children = { new RectangleGeometry(outer), hole }
    };

    private double ClampedRadius(Rect bounds) =>
        Math.Min(CornerRadius, Math.Min(bounds.Width, bounds.Height) / 2);

    private StreamGeometry BuildOutline(Rect bounds, double radius)
    {
        // Floored, because a spread big enough to deflate the rect past zero would otherwise
        // hand Math.Clamp a max below its min.
        var budget = Math.Max(0, Math.Min(bounds.Width, bounds.Height) / 2);
        var corner = Corner.Solve(Math.Clamp(radius, 0, budget), Math.Clamp(CornerSmoothing, 0, 1), budget);

        var right = new Vector(1, 0);
        var down = new Vector(0, 1);
        var left = new Vector(-1, 0);
        var up = new Vector(0, -1);

        var geometry = new StreamGeometry();
        using var path = geometry.Open();

        path.BeginFigure(bounds.TopLeft + right * corner.Extent, true);

        path.LineTo(bounds.TopRight + left * corner.Extent);
        corner.Trace(path, bounds.TopRight, right, down);

        path.LineTo(bounds.BottomRight + up * corner.Extent);
        corner.Trace(path, bounds.BottomRight, down, left);

        path.LineTo(bounds.BottomLeft + right * corner.Extent);
        corner.Trace(path, bounds.BottomLeft, left, up);

        path.LineTo(bounds.TopLeft + down * corner.Extent);
        corner.Trace(path, bounds.TopLeft, up, right);

        path.EndFigure(true);
        return geometry;
    }

    /// <summary>
    /// One corner of a continuous ("squircle") rounded rectangle, built the way Apple's
    /// <c>.continuous</c> corner style is: a cubic that peels the curvature away from the straight
    /// edge, a circular arc through the diagonal, then the mirrored cubic. A plain rounded
    /// rectangle is the <c>smoothing == 0</c> case, where the two cubics collapse to nothing.
    /// <para>
    /// <c>Extent</c> is how far the corner reaches back along each edge — always at least the
    /// radius, and up to twice it at full smoothing. That reach is what separates a squircle from
    /// a single-bezier approximation, which has to fake the whole corner inside one radius.
    /// </para>
    /// </summary>
    private readonly record struct Corner(
        double Radius, double Extent, double Lead, double Ease, double Shoulder, double Lift,
        double ArcSpan)
    {
        public static Corner Solve(double radius, double smoothing, double budget)
        {
            if (radius <= 0) return default;

            var extent = radius * (1 + smoothing);

            // The arc keeps whatever of the 90 degrees the smoothing has not eaten.
            var arcSweep = 90 * (1 - smoothing);
            var arcSpan = Math.Sin(Radians(arcSweep / 2)) * radius * Math.Sqrt(2);

            // Where the cubic hands over to the arc, and how far off the edge that point sits.
            var handover = radius * Math.Tan(Radians((90 - arcSweep) / 4));
            var tilt = Radians(45 * smoothing);
            var shoulder = handover * Math.Cos(tilt);
            var lift = shoulder * Math.Tan(tilt);

            // The remaining reach is split 2:1 between the two control points of the cubic.
            var ease = (extent - arcSpan - shoulder - lift) / 3;
            var lead = 2 * ease;

            if (extent > budget)
            {
                // Too little room for the full reach: compress the cubic rather than give up
                // smoothing, so the corner still reads as continuous on short controls.
                var available = budget - lift - arcSpan - shoulder;
                ease = Math.Min(ease, available * 5 / 6);
                lead = available - ease;
                extent = budget;
            }

            return new Corner(radius, extent, lead, ease, shoulder, lift, arcSpan);
        }

        /// <summary>
        /// Emits the corner at <paramref name="apex"/>, arriving along <paramref name="inbound"/>
        /// and leaving along <paramref name="outbound"/> (both unit vectors, turning clockwise).
        /// The pen must already sit at <c>apex - inbound * Extent</c>.
        /// </summary>
        public void Trace(StreamGeometryContext path, Point apex, Vector inbound, Vector outbound)
        {
            if (Radius <= 0)
            {
                path.LineTo(apex);
                return;
            }

            var start = apex - inbound * Extent;
            path.CubicBezierTo(
                start + inbound * Lead,
                start + inbound * (Lead + Ease),
                start + inbound * (Lead + Ease + Shoulder) + outbound * Lift);

            if (ArcSpan > 0)
            {
                path.ArcTo(
                    start + inbound * (Lead + Ease + Shoulder + ArcSpan) + outbound * (Lift + ArcSpan),
                    new Size(Radius, Radius), 0, false, SweepDirection.Clockwise);
            }

            var exit = apex + outbound * Extent;
            path.CubicBezierTo(
                exit - outbound * (Lead + Ease),
                exit - outbound * Lead,
                exit);
        }

        private static double Radians(double degrees) => degrees * Math.PI / 180;
    }

    private static Color FadeAlpha(Color color, double strength) =>
        new((byte)Math.Round(color.A * strength), color.R, color.G, color.B);

    /// <summary>Carries the clip that trims its blurred children. The clip has to be here rather
    /// than on the passes themselves: a visual's own clip is applied before its effect.</summary>
    private sealed class ShadowHost : Control
    {
        public void Set(List<Pass> passes, Rect bounds, Geometry? clip)
        {
            Clip = clip;

            // Reused rather than rebuilt — hovering a control or dragging the emboss slider
            // re-runs this constantly, and swapping visuals in and out is the expensive part.
            while (VisualChildren.Count > passes.Count)
                VisualChildren.RemoveAt(VisualChildren.Count - 1);
            while (VisualChildren.Count < passes.Count)
                VisualChildren.Add(new ShadowPass());
            if (passes.Count == 0) return;

            Measure(bounds.Size);
            Arrange(bounds);
            for (var i = 0; i < passes.Count; i++)
            {
                var pass = (ShadowPass)VisualChildren[i];
                pass.Update(passes[i]);
                pass.Measure(bounds.Size);
                pass.Arrange(bounds);
            }
        }
    }

    private readonly record struct Pass(Geometry Shape, IBrush Fill, double Blur);

    /// <summary>A shadow reduced to the parts that cost something to build.</summary>
    private readonly record struct Layer(Geometry Shape, Color Color, double Blur, bool Inset);

    /// <summary>A single shadow: one filled squircle, blurred by the compositor.</summary>
    private sealed class ShadowPass : Control
    {
        private Pass current;

        public void Update(Pass pass)
        {
            // Measured against Avalonia's own BoxShadow: the radii match 1:1, so the existing
            // shadow specs carry over untouched.
            if (current.Blur != pass.Blur)
                Effect = pass.Blur > 0 ? new ImmutableBlurEffect(pass.Blur) : null;
            current = pass;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            if (current.Shape is { } shape && current.Fill is { } fill)
                context.DrawGeometry(fill, null, shape);
        }
    }
}
