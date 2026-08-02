// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public class HintPointerLayer : Canvas
{
    private const double PlateWidth = 1560;
    private const double PlateHeight = 980;
    private const double PlateMargin = 12;
    private const double BubbleWidth = 260;
    private const double TargetGap = 18;
    private const double TargetOutlineInflation = 3;
    private const double ConnectorDotDiameter = 7;

    public static readonly StyledProperty<int> ScreenIndexProperty =
        AvaloniaProperty.Register<HintPointerLayer, int>(nameof(ScreenIndex));

    private readonly List<Rect> lastTargetRects = [];

    private bool rebuilding;

    public HintPointerLayer()
    {
        Background = null;
    }

    public int ScreenIndex
    {
        get => GetValue(ScreenIndexProperty);
        set => SetValue(ScreenIndexProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        PointerHintSettings.Changed += Rebuild;
        BuilderModeSettings.Changed += Rebuild;
        LayoutUpdated += OnLayoutUpdated;
        ShowOnFirstVisit();
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        PointerHintSettings.Changed -= Rebuild;
        BuilderModeSettings.Changed -= Rebuild;
        LayoutUpdated -= OnLayoutUpdated;
        base.OnDetachedFromVisualTree(e);
    }

    private void ShowOnFirstVisit()
    {
        if (!PointerHintSettings.AutoShow) return;
        if (HintCatalog.For(ScreenIndex).Count == 0) return;
        if (!PointerHintSettings.MarkVisited(ScreenIndex)) return;
        PointerHintSettings.SetEnabled(true);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (rebuilding || !PointerHintSettings.Enabled) return;
        if (ResolveTargetRects().SequenceEqual(lastTargetRects)) return;
        Rebuild();
    }

    private void Rebuild()
    {
        if (rebuilding) return;
        rebuilding = true;
        try
        {
            Children.Clear();
            lastTargetRects.Clear();
            if (!PointerHintSettings.Enabled) return;

            foreach (var (hint, targetRect) in VisibleHints())
            {
                lastTargetRects.Add(targetRect);

                var bubble = BuildBubble(hint);
                bubble.Measure(new Size(BubbleWidth, PlateHeight));
                var bubbleRect = new Rect(PlaceBubble(targetRect, bubble.DesiredSize, hint), bubble.DesiredSize);

                Children.Add(BuildTargetOutline(targetRect));
                Children.Add(BuildConnector(bubbleRect, targetRect));
                Children.Add(BuildConnectorDot(bubbleRect, targetRect));
                SetLeft(bubble, bubbleRect.X);
                SetTop(bubble, bubbleRect.Y);
                Children.Add(bubble);
            }
        }
        finally
        {
            rebuilding = false;
        }
    }

    private List<Rect> ResolveTargetRects() => [.. VisibleHints().Select(visible => visible.Rect)];

    private List<(HintPointer Hint, Rect Rect)> VisibleHints()
    {
        var visible = new List<(HintPointer, Rect)>();
        foreach (var hint in HintCatalog.For(ScreenIndex))
        {
            if (PointerHintSettings.IsDismissed(ScreenIndex, hint.TargetName)) continue;
            if (ResolveTargetRect(hint.TargetName) is not { } rect) continue;
            visible.Add((hint, rect));
        }
        return visible;
    }

    /// <summary>
    /// Closing the last tip on a screen also lifts the button, so the key never stays down over a
    /// screen with nothing on it.
    /// </summary>
    private void DismissHint(HintPointer hint)
    {
        PointerHintSettings.Dismiss(ScreenIndex, hint.TargetName);
        if (VisibleHints().Count == 0) PointerHintSettings.SetEnabled(false);
    }

    private Rect? ResolveTargetRect(string targetName)
    {
        if (this.FindAncestorOfType<UserControl>() is not { } host) return null;
        if (host.FindControl<Control>(targetName) is not { } target) return null;
        if (!target.IsEffectivelyVisible) return null;
        if (target.Bounds.Width <= 0 || target.Bounds.Height <= 0) return null;
        if (target.TranslatePoint(default, this) is not { } topLeft) return null;

        var visible = ClipToAncestors(target, new Rect(topLeft, target.Bounds.Size));
        return visible.Width <= 0 || visible.Height <= 0 ? null : visible;
    }

    /// <summary>
    /// A rail inside a scroll viewer measures to its whole content, so its raw rect runs off the
    /// bottom of the screen. Every clipping ancestor has to cut it back to the part on show.
    /// </summary>
    private Rect ClipToAncestors(Visual target, Rect rect)
    {
        var clipped = rect.Intersect(new Rect(0, 0, PlateWidth, PlateHeight));
        foreach (var ancestor in target.GetVisualAncestors())
        {
            if (!ancestor.ClipToBounds) continue;
            if (ancestor.TranslatePoint(default, this) is not { } origin) continue;
            clipped = clipped.Intersect(new Rect(origin, ancestor.Bounds.Size));
        }
        return clipped;
    }

    private static Point PlaceBubble(Rect target, Size bubbleSize, HintPointer hint)
    {
        var anchored = hint.Side switch
        {
            HintSide.Left => new Point(
                target.X - TargetGap - bubbleSize.Width, target.Center.Y - bubbleSize.Height / 2),
            HintSide.Right => new Point(
                target.Right + TargetGap, target.Center.Y - bubbleSize.Height / 2),
            HintSide.Above => new Point(
                target.Center.X - bubbleSize.Width / 2, target.Y - TargetGap - bubbleSize.Height),
            _ => new Point(
                target.Center.X - bubbleSize.Width / 2, target.Bottom + TargetGap)
        };

        return new Point(
            Math.Clamp(
                anchored.X + hint.Nudge.X, PlateMargin, PlateWidth - bubbleSize.Width - PlateMargin),
            Math.Clamp(
                anchored.Y + hint.Nudge.Y, PlateMargin, PlateHeight - bubbleSize.Height - PlateMargin));
    }

    private static Point NearestPointOn(Rect rect, Point from) => new(
        Math.Clamp(from.X, rect.X, rect.Right),
        Math.Clamp(from.Y, rect.Y, rect.Bottom));

    private static (Point Start, Point End) ConnectorEnds(Rect bubble, Rect target)
    {
        var start = NearestPointOn(bubble, target.Center);
        return (start, NearestPointOn(target, start));
    }

    private static Shape BuildConnector(Rect bubble, Rect target)
    {
        var (start, end) = ConnectorEnds(bubble, target);
        return new Avalonia.Controls.Shapes.Path
        {
            Data = new LineGeometry(start, end),
            Stretch = Stretch.None,
            Stroke = Palette.Amber,
            StrokeThickness = 1,
            Width = PlateWidth,
            Height = PlateHeight,
            IsHitTestVisible = false
        };
    }

    private static Ellipse BuildConnectorDot(Rect bubble, Rect target)
    {
        var (_, end) = ConnectorEnds(bubble, target);
        var dot = new Ellipse
        {
            Width = ConnectorDotDiameter,
            Height = ConnectorDotDiameter,
            Fill = Palette.Amber,
            IsHitTestVisible = false
        };
        SetLeft(dot, end.X - ConnectorDotDiameter / 2);
        SetTop(dot, end.Y - ConnectorDotDiameter / 2);
        return dot;
    }

    private static Rectangle BuildTargetOutline(Rect target)
    {
        var outline = target.Inflate(TargetOutlineInflation);
        var shape = new Rectangle
        {
            Width = outline.Width,
            Height = outline.Height,
            Stroke = Palette.Amber,
            StrokeThickness = 1,
            StrokeDashArray = [3, 3],
            IsHitTestVisible = false
        };
        SetLeft(shape, outline.X);
        SetTop(shape, outline.Y);
        return shape;
    }

    private SquircleBorder BuildBubble(HintPointer hint)
    {
        var title = new TextBlock
        {
            Text = hint.Title,
            FontSize = TypographySettings.Size(10),
            LetterSpacing = 1,
            FontWeight = FontWeight.Bold,
            Foreground = Palette.Amber,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var close = new TextBlock
        {
            Text = "✕",
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextFaint,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        close.PointerPressed += (_, e) =>
        {
            DismissHint(hint);
            e.Handled = true;
        };

        var header = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(title);

        var body = new TextBlock
        {
            Text = hint.Body,
            FontSize = TypographySettings.Size(11),
            LineHeight = TypographySettings.Size(16),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextSub,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var column = new StackPanel();
        column.Children.Add(header);
        column.Children.Add(body);

        var bubble = new SquircleBorder
        {
            Classes = { "emboss-card" },
            Width = BubbleWidth,
            Padding = new Thickness(14, 12),
            Background = Palette.BgPanel,
            Child = column
        };
        bubble.PointerPressed += (_, e) => e.Handled = true;
        return bubble;
    }
}
