// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public class TerminalTabStrip : UserControl
{
    public static readonly IReadOnlyList<string> NavigationLabels =
        ["1) NETWORK", "2) TRANSFER FUNCTION", "3) DASHBOARD", "4) DATA TREE", "5) MAP", "6) DATA SOURCES", "7) CSV EXPORT"];

    private const double ClosableLabelMaxWidth = 150;

    /// <summary>Travel along the strip's axis before a press on a nav key becomes a reorder drag.</summary>
    private const double DragThreshold = 6;

    public static readonly StyledProperty<IReadOnlyList<string>> LabelsProperty =
        AvaloniaProperty.Register<TerminalTabStrip, IReadOnlyList<string>>(nameof(Labels), NavigationLabels);

    public static readonly StyledProperty<bool> IsClosableProperty =
        AvaloniaProperty.Register<TerminalTabStrip, bool>(nameof(IsClosable));

    public static readonly StyledProperty<int> ActiveIndexProperty =
        AvaloniaProperty.Register<TerminalTabStrip, int>(nameof(ActiveIndex));

    public static readonly StyledProperty<string> HintTextProperty =
        AvaloniaProperty.Register<TerminalTabStrip, string>(nameof(HintText), "");

    public static readonly StyledProperty<IBrush?> HintBrushProperty =
        AvaloniaProperty.Register<TerminalTabStrip, IBrush?>(nameof(HintBrush));

    public static readonly StyledProperty<object?> RightContentProperty =
        AvaloniaProperty.Register<TerminalTabStrip, object?>(nameof(RightContent));

    public event Action<int>? TabSelected;

    public event Action<int>? TabCloseRequested;

    private readonly List<TextBlock> tabBlocks = [];
    private readonly List<SquircleBorder> tabKeys = [];
    private readonly List<BubbleKeyAnimator> tabBubbles = [];
    private readonly List<Rectangle> tabSeparators = [];
    private readonly StackPanel tabRow;
    private readonly TextBlock hintBlock;
    private readonly ContentControl rightHost;
    private readonly StackPanel rightRow;
    private readonly DockPanel layout;
    private readonly Border chrome;

    // Display position -> index into Labels. Identity except on the nav strip, where it mirrors
    // NavOrderSettings; everything below the public surface works in display positions, and the
    // screen index only appears at the ActiveIndex / TabSelected boundary.
    private List<int> order = [];
    private readonly List<string> displayLabels = [];

    // The key the pointer went down on keeps the capture for the whole drag, while the dragged
    // tab's slot walks away from it as labels swap underneath — hence two positions.
    private int grabPos = -1;
    private int dragPos = -1;
    private bool dragReordering;
    private double dragPressAxis;
    private bool committingOrder;

    public TerminalTabStrip()
    {
        tabRow = new StackPanel { Spacing = 5 };

        hintBlock = new TextBlock
        {
            FontSize = TypographySettings.Size(11),
            VerticalAlignment = VerticalAlignment.Center
        };
        rightHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };

        layout = new DockPanel();
        rightRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        rightRow.Children.Add(hintBlock);
        rightRow.Children.Add(rightHost);
        layout.Children.Add(rightRow);
        layout.Children.Add(tabRow);

        chrome = new Border
        {
            Background = Palette.BgBar,
            BorderBrush = Palette.Border,
            ClipToBounds = true,
            Child = layout
        };
        Content = chrome;

        ApplyChrome();
        RebuildTabs();
    }

    /// <summary>Vertical layout applies to the shared nav strip only — ad-hoc strips (closable
    /// document tabs and the like) keep their inline horizontal shape.</summary>
    private bool IsVertical => IsNavStrip && TabLayoutSettings.Vertical;

    /// <summary>Everything about the strip's shell that depends on its orientation.</summary>
    private void ApplyChrome()
    {
        var vertical = IsVertical;
        DockPanel.SetDock(this, vertical ? Dock.Left : Dock.Top);
        Height = vertical ? double.NaN : 40;

        chrome.BorderThickness = vertical ? new Thickness(0, 0, 1, 0) : new Thickness(0, 0, 0, 1);

        tabRow.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        tabRow.VerticalAlignment = vertical ? VerticalAlignment.Top : VerticalAlignment.Center;

        // 9 on the near edge so the first tab key sits as far from the window edge as it does
        // from the start of the strip; the far side stays at 14 to line up with the bar alongside.
        layout.Margin = vertical ? new Thickness(9, 9, 9, 9) : new Thickness(9, 0, 14, 0);

        rightRow.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        DockPanel.SetDock(rightRow, vertical ? Dock.Bottom : Dock.Right);
        rightRow.HorizontalAlignment = vertical ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        rightRow.Margin = vertical ? new Thickness(0, 8, 0, 0) : default;
        hintBlock.TextWrapping = vertical ? TextWrapping.Wrap : TextWrapping.NoWrap;
        // The bottom-of-strip extras must not widen the column past the tabs above them.
        rightRow.MaxWidth = vertical ? 170 : double.PositiveInfinity;
    }

    private void OnTabLayoutChanged()
    {
        if (!IsNavStrip) return;
        ApplyChrome();
        RebuildTabs();
    }

    public IReadOnlyList<string> Labels
    {
        get => GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    public bool IsClosable
    {
        get => GetValue(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    public int ActiveIndex
    {
        get => GetValue(ActiveIndexProperty);
        set => SetValue(ActiveIndexProperty, value);
    }

    public string HintText
    {
        get => GetValue(HintTextProperty);
        set => SetValue(HintTextProperty, value);
    }

    public IBrush? HintBrush
    {
        get => GetValue(HintBrushProperty);
        set => SetValue(HintBrushProperty, value);
    }

    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HintSettings.Changed += UpdateVisuals;
        NavOrderSettings.Changed += OnNavOrderChanged;
        TabLayoutSettings.Changed += OnTabLayoutChanged;
        ApplyChrome();
        // A cached screen reattaches with the order it was built under — catch up on a reorder
        // made from another screen's strip while this one was off the tree.
        if (IsNavStrip && !order.SequenceEqual(NavOrderSettings.OrderFor(Labels.Count))) RebuildTabs();
        UpdateVisuals();
        if (!TryContinueHandoff()) SyncBubbles(animated: false);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        HintSettings.Changed -= UpdateVisuals;
        NavOrderSettings.Changed -= OnNavOrderChanged;
        TabLayoutSettings.Changed -= OnTabLayoutChanged;
    }

    /// <summary>The shared navigation strip, as opposed to a strip given its own labels.</summary>
    private bool IsNavStrip => ReferenceEquals(Labels, NavigationLabels);

    private void OnNavOrderChanged()
    {
        if (committingOrder || !IsNavStrip) return;
        RebuildTabs();
    }

    private int ToDisplay(int labelIndex) => order.IndexOf(labelIndex);

    private int ToScreen(int displayPos) => order[displayPos];

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelsProperty || change.Property == IsClosableProperty)
        {
            ApplyChrome();
            RebuildTabs();
            return;
        }
        if (change.Property == ActiveIndexProperty)
        {
            UpdateVisuals();
            SyncBubbles(animated: IsLoaded);
            return;
        }
        if (change.Property == HintTextProperty ||
            change.Property == HintBrushProperty ||
            change.Property == RightContentProperty)
        {
            UpdateVisuals();
        }
    }

    private void RebuildTabs()
    {
        tabRow.Children.Clear();
        tabBlocks.Clear();
        tabKeys.Clear();
        tabBubbles.Clear();
        tabSeparators.Clear();
        grabPos = -1;
        dragPos = -1;
        dragReordering = false;

        order = IsNavStrip
            ? [.. NavOrderSettings.OrderFor(Labels.Count)]
            : [.. Enumerable.Range(0, Labels.Count)];
        RefreshDisplayLabels();

        for (var i = 0; i < Labels.Count; i++)
        {
            var index = i;
            if (i > 0)
            {
                var separator = IsVertical
                    ? new Rectangle
                    {
                        Width = 14,
                        Height = 1,
                        Fill = Palette.TextGhost,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                    : new Rectangle
                    {
                        Width = 1,
                        Height = 14,
                        Fill = Palette.TextGhost,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                tabSeparators.Add(separator);
                tabRow.Children.Add(separator);
            }

            var block = new TextBlock
            {
                Text = displayLabels[i],
                FontSize = TypographySettings.Size(11),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (IsClosable)
            {
                block.MaxWidth = ClosableLabelMaxWidth;
                block.TextTrimming = TextTrimming.CharacterEllipsis;
            }

            var key = new SquircleBorder
            {
                Padding = new Thickness(12, 5),
                Background = Palette.EmbossSurface,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = IsClosable ? WithCloseButton(block, index) : block
            };
            var bubble = new BubbleKeyAnimator(key);
            bubble.PopStarted += pressure => OnBubblePopStarted(index, pressure);
            if (IsNavStrip)
            {
                key.PointerPressed += (_, e) => OnKeyPressed(index, e);
                key.PointerMoved += (_, e) => OnKeyMoved(index, e);
                key.PointerReleased += (_, _) => EndDrag();
                key.PointerCaptureLost += (_, _) => EndDrag();
            }
            tabBlocks.Add(block);
            tabKeys.Add(key);
            tabBubbles.Add(bubble);
            tabRow.Children.Add(key);
        }

        UpdateVisuals();
        SyncBubbles(animated: false);
    }

    /// <summary>
    /// The label shown at each display position: the tab's name under the position's number, so
    /// the keys stay 1..6 left to right however the names are arranged.
    /// </summary>
    private void RefreshDisplayLabels()
    {
        displayLabels.Clear();
        for (var pos = 0; pos < order.Count; pos++)
        {
            var label = Labels[order[pos]];
            displayLabels.Add(IsNavStrip ? $"{pos + 1}) {NameOf(label)}" : label);
        }
    }

    private static string NameOf(string label)
    {
        var cut = label.IndexOf(") ", StringComparison.Ordinal);
        return cut < 0 ? label : label[(cut + 2)..];
    }

    private void OnKeyPressed(int pos, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(tabRow).Properties.IsLeftButtonPressed) return;
        grabPos = pos;
        dragPos = pos;
        dragReordering = false;
        dragPressAxis = AxisPosition(e);
    }

    /// <summary>Pointer position along the strip's run direction — X across, Y down the side.</summary>
    private double AxisPosition(PointerEventArgs e)
    {
        var position = e.GetPosition(tabRow);
        return IsVertical ? position.Y : position.X;
    }

    private double AxisCenter(SquircleBorder key) =>
        IsVertical ? key.Bounds.Center.Y : key.Bounds.Center.X;

    private void OnKeyMoved(int pos, PointerEventArgs e)
    {
        if (grabPos < 0 || pos != grabPos) return;
        var axis = AxisPosition(e);

        if (!dragReordering)
        {
            if (Math.Abs(axis - dragPressAxis) < DragThreshold) return;
            dragReordering = true;
            tabBubbles[grabPos].CancelPress();
        }

        // The keys never move — crossing a neighbour's midpoint swaps the labels underneath, and
        // the drag continues from the neighbouring key.
        while (dragPos + 1 < tabKeys.Count && axis > AxisCenter(tabKeys[dragPos + 1]))
            SwapWithNext(dragPos++);
        while (dragPos > 0 && axis < AxisCenter(tabKeys[dragPos - 1]))
            SwapWithNext(--dragPos);
    }

    private void SwapWithNext(int pos)
    {
        (order[pos], order[pos + 1]) = (order[pos + 1], order[pos]);
        RefreshDisplayLabels();
        tabBlocks[pos].Text = displayLabels[pos];
        tabBlocks[pos + 1].Text = displayLabels[pos + 1];
        UpdateVisuals();
        SyncBubbles(animated: false);
    }

    private void EndDrag()
    {
        if (grabPos < 0) return;
        var reordered = dragReordering;
        grabPos = -1;
        dragPos = -1;
        dragReordering = false;
        if (!reordered) return;

        // This strip already shows the new order — suppress its own Changed rebuild, which would
        // tear down the keys mid-release.
        committingOrder = true;
        NavOrderSettings.Set(order);
        committingOrder = false;
    }

    private void OnBubblePopStarted(int poppingIndex, double pressure)
    {
        for (var i = 0; i < tabBubbles.Count; i++)
        {
            if (i != poppingIndex && tabBubbles[i].IsPoppedOrPopping) tabBubbles[i].Inflate();
        }
        if (!IsClosable)
        {
            BubbleHandoff.Record(
                displayLabels[poppingIndex], poppingIndex, ToDisplay(ActiveIndex),
                tabBubbles[poppingIndex].CurrentScale, pressure);
        }
        TabSelected?.Invoke(ToScreen(poppingIndex));
    }

    private bool TryContinueHandoff()
    {
        if (IsClosable) return false;
        if (!BubbleHandoff.TryTake(displayLabels, ToDisplay(ActiveIndex), out var handoff)) return false;
        for (var i = 0; i < tabBubbles.Count; i++)
        {
            if (i == handoff.PoppingIndex)
            {
                tabBubbles[i].ContinuePop(handoff.Position, handoff.Pressure);
            }
            else if (i == handoff.PreviousIndex)
            {
                tabBubbles[i].RestPopped();
                tabBubbles[i].Inflate();
            }
            else
            {
                tabBubbles[i].RestInflated();
            }
        }
        return true;
    }

    private void SyncBubbles(bool animated)
    {
        var activePos = ToDisplay(ActiveIndex);
        for (var i = 0; i < tabBubbles.Count; i++)
        {
            var shouldBePopped = i == activePos;
            if (!animated)
            {
                if (shouldBePopped) tabBubbles[i].RestPopped();
                else tabBubbles[i].RestInflated();
            }
            else if (shouldBePopped)
            {
                if (!tabBubbles[i].IsPoppedOrPopping) tabBubbles[i].PopProgrammatic();
            }
            else
            {
                tabBubbles[i].Inflate();
            }
        }
    }

    internal SquircleBorder KeyAt(int index) => tabKeys[index];

    internal BubbleKeyAnimator BubbleAt(int index) => tabBubbles[index];

    private Control WithCloseButton(TextBlock label, int index)
    {
        // U+00D7 rather than a heavier cross glyph: it is present in the terminal font, so the
        // close button inherits the label's line box and the key stays the height of a nav tab.
        var glyph = new TextBlock
        {
            Text = "×",
            FontSize = label.FontSize,
            Foreground = Palette.TextFaint
        };
        var close = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(4, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = glyph
        };
        close.PointerEntered += (_, _) => glyph.Foreground = Palette.Red;
        close.PointerExited += (_, _) => glyph.Foreground = Palette.TextFaint;
        close.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            TabCloseRequested?.Invoke(index);
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { label, close }
        };
    }

    private void UpdateVisuals()
    {
        var activePos = ToDisplay(ActiveIndex);
        for (var i = 0; i < tabBlocks.Count; i++)
        {
            var isActive = i == activePos;
            tabBlocks[i].Foreground = isActive ? Palette.Amber : Palette.TextMuted;
            tabBlocks[i].FontWeight = isActive ? FontWeight.Bold : FontWeight.Normal;
        }
        for (var i = 0; i < tabSeparators.Count; i++)
        {
            tabSeparators[i].IsVisible = activePos != i && activePos != i + 1;
        }
        hintBlock.Text = HintText;
        hintBlock.Foreground = HintBrush ?? Palette.TextFaint;
        hintBlock.IsVisible = HintText.Length > 0 && HintSettings.Enabled;
        rightHost.Content = RightContent;
        rightHost.IsVisible = RightContent is not null;
    }
}
