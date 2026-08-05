// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

/// <summary>
/// The guided tour's card — the bouncy box. It drops into the bottom-left corner of the screen it
/// belongs to and bounces itself to a stop. Unlike the pointer tips it does not point at anything,
/// and unlike a dialog it does not take the screen: pick it up, put it anywhere, and letting go
/// drops it straight down to the floor it was on.
/// </summary>
public class TourLayer : Canvas
{
    private const double PlateWidth = 1560;
    private const double PlateHeight = 980;
    private const double CardWidth = 560;
    private const double CardMargin = 28;

    /// <summary>Fixed integration step, as for the key springs — a long frame must not fling the card.</summary>
    private const double MaxStepSeconds = 0.004;

    private const double MaxFrameSeconds = 1.0 / 30;

    public static readonly StyledProperty<int> ScreenIndexProperty =
        AvaloniaProperty.Register<TourLayer, int>(nameof(ScreenIndex));

    private readonly Stopwatch frameClock = new();

    /// <summary>Drives the card's Y only: it falls to its resting place and bounces there.</summary>
    private TranslateTransform? drop;

    /// <summary>Where the card comes to rest — set by the layout, not by where it is carried to.</summary>
    private double floor;

    private double height;
    private double velocity;
    private double lastFrameSeconds;
    private bool falling;
    private bool frameLoopRunning;

    private SquircleBorder? card;
    private IPointer? carrier;
    private Point grip;

    public TourLayer()
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
        TourGuide.Changed += Rebuild;
        // Tuning the drop replays it, so the sliders show their work on the card in front of you.
        TourSettings.Changed += Replay;
        TourGuide.StartOnce(ScreenIndex);
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        TourGuide.Changed -= Rebuild;
        TourSettings.Changed -= Replay;
        // Drops the frame loop with the card: the next frame finds nothing to drive and stops
        // asking for another, rather than beating on against a top level this layer has left.
        drop = null;
        falling = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void Rebuild()
    {
        Children.Clear();
        drop = null;
        card = null;
        carrier = null;
        falling = false;
        if (TourGuide.StopOn(ScreenIndex) is not { } stop) return;

        card = BuildCard(stop);
        card.Measure(new Size(CardWidth, PlateHeight));
        floor = PlateHeight - card.DesiredSize.Height - CardMargin;
        SetLeft(card, CardMargin);
        SetTop(card, floor);

        drop = new TranslateTransform();
        card.RenderTransform = drop;
        Children.Add(card);

        Drop(TourSettings.DropHeight);
    }

    /// <summary>Lifts the card to a height above its floor and lets go of it.</summary>
    private void Drop(double from)
    {
        if (drop is null) return;
        height = from;
        velocity = 0;
        falling = height > 0;
        drop.Y = falling ? -height : 0;
        if (!falling) return;

        frameClock.Restart();
        lastFrameSeconds = 0;
        RequestFrame();
    }

    /// <summary>Tuning the drop settings replays the drop from the new height.</summary>
    private void Replay() => Drop(TourSettings.DropHeight);

    // ── carrying the box ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Picking the box up stops the fall dead — while it is held it goes exactly where the pointer
    /// takes it, sideways as well as up. Presses that a control inside the card has already dealt
    /// with never reach here, so the keys keep working.
    /// </summary>
    private void OnCardPressed(PointerPressedEventArgs e)
    {
        if (card is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        carrier = e.Pointer;
        e.Pointer.Capture(card);
        grip = e.GetPosition(this) - new Point(GetLeft(card), floor - height);
        falling = false;
        velocity = 0;
        e.Handled = true;
    }

    private void OnCardMoved(PointerEventArgs e)
    {
        if (card is null || drop is null || e.Pointer != carrier) return;
        var corner = e.GetPosition(this) - grip;

        SetLeft(card, Math.Clamp(
            corner.X, CardMargin, PlateWidth - card.DesiredSize.Width - CardMargin));
        // Height above the floor, so the box cannot be pushed through it or off the top.
        height = Math.Clamp(floor - corner.Y, 0, floor - CardMargin);
        drop.Y = -height;
    }

    /// <summary>
    /// Letting go drops it from where it is held, with no sideways throw — it falls straight down
    /// the column it was released over and bounces on the same floor as before.
    /// </summary>
    private void OnCardReleased(IPointer pointer)
    {
        if (pointer != carrier) return;
        carrier = null;
        Drop(height);
    }

    private void RequestFrame()
    {
        if (frameLoopRunning || !falling) return;
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        frameLoopRunning = true;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan _)
    {
        frameLoopRunning = false;
        if (drop is null || !falling) return;

        var now = frameClock.Elapsed.TotalSeconds;
        var elapsed = Math.Clamp(now - lastFrameSeconds, 0.0001, MaxFrameSeconds);
        lastFrameSeconds = now;
        Fall(elapsed);

        drop.Y = falling ? -height : 0;
        RequestFrame();
    }

    /// <summary>
    /// A ball under gravity: it gathers speed on the way down, and each time it reaches the floor
    /// it leaves with the fraction of that speed the bounce setting keeps. The hops shorten of
    /// their own accord, and once the rebound is too small to see the card is simply laid down.
    /// </summary>
    private void Fall(double elapsedSeconds)
    {
        var remaining = elapsedSeconds;
        while (remaining > 0 && falling)
        {
            var step = Math.Min(remaining, MaxStepSeconds);
            remaining -= step;

            velocity -= TourSettings.Gravity * step;
            height += velocity * step;
            if (height > 0) continue;

            height = 0;
            velocity = -velocity * TourSettings.Bounce;
            if (velocity < TourSettings.RestVelocity)
            {
                velocity = 0;
                falling = false;
            }
        }
    }

    private SquircleBorder BuildCard(TourStop stop)
    {
        var step = new TextBlock
        {
            Text = $"STEP {TourGuide.StepIndex + 1} OF {TourCatalog.Length}",
            FontSize = TypographySettings.Size(10),
            LetterSpacing = 1,
            Foreground = Palette.TextFaint
        };

        var title = new TextBlock
        {
            Text = stop.Title,
            FontSize = TypographySettings.Size(21),
            LetterSpacing = 1,
            FontWeight = FontWeight.Bold,
            Foreground = Palette.Amber,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var body = new TextBlock
        {
            Text = stop.Body,
            FontSize = TypographySettings.Size(13),
            LineHeight = TypographySettings.Size(21),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextSub,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var column = new StackPanel();
        column.Children.Add(step);
        column.Children.Add(title);
        column.Children.Add(body);
        column.Children.Add(BuildActionRow(stop));

        var built = new SquircleBorder
        {
            Classes = { "emboss-card" },
            Width = CardWidth,
            Padding = new Thickness(32, 28),
            Background = Palette.BgPanel,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = column
        };
        built.PointerPressed += (_, e) => OnCardPressed(e);
        built.PointerMoved += (_, e) => OnCardMoved(e);
        built.PointerReleased += (_, e) => OnCardReleased(e.Pointer);
        built.PointerCaptureLost += (_, e) => OnCardReleased(e.Pointer);
        return built;
    }

    /// <summary>
    /// The keys under the words. A stop with one way on keeps the key and the skip on one line; a
    /// stop that forks needs the width for both of its keys, so the skip drops to a line of its own
    /// underneath rather than being squeezed out to the edge.
    /// </summary>
    private Control BuildActionRow(TourStop stop)
    {
        var keys = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12
        };

        if (stop.Choices is { Count: > 0 } choices)
        {
            foreach (var choice in choices)
                keys.Children.Add(BuildKey(choice.Label, () => TourGuide.Choose(choice.Screen)));
        }
        else
        {
            keys.Children.Add(BuildKey(stop.ActionLabel, TourGuide.Advance));
        }

        var skip = new TextBlock
        {
            Text = "skip the tour",
            FontSize = TypographySettings.Size(11),
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        skip.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            TourGuide.Skip();
        };

        if (stop.Choices is { Count: > 0 })
        {
            skip.Margin = new Thickness(0, 14, 0, 0);
            skip.HorizontalAlignment = HorizontalAlignment.Left;
            return new StackPanel
            {
                Margin = new Thickness(0, 20, 0, 0),
                Children = { keys, skip }
            };
        }

        keys.Margin = new Thickness(0, 20, 0, 0);
        keys.Children.Add(skip);
        keys.Spacing = 16;
        return keys;
    }

    private static SquircleBorder BuildKey(string label, Action press)
    {
        var key = new SquircleBorder
        {
            Classes = { "emboss-key" },
            Padding = new Thickness(20, 9),
            Background = Palette.Amber,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = label,
                FontSize = TypographySettings.Size(12),
                FontWeight = FontWeight.Bold,
                Foreground = Palette.TabActiveText
            }
        };
        key.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            press();
        };
        return key;
    }
}
