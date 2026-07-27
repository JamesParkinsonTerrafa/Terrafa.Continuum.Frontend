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
        ["1) NETWORK", "2) TRANSFER FUNCTION", "3) DASHBOARD", "4) DATA TREE", "5) MAP"];

    private const double ClosableLabelMaxWidth = 150;

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
    private readonly List<Rectangle> tabSeparators = [];
    private readonly StackPanel tabRow;
    private readonly TextBlock hintBlock;
    private readonly ContentControl rightHost;

    public TerminalTabStrip()
    {
        Height = 40;

        tabRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center
        };

        hintBlock = new TextBlock
        {
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        rightHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };

        // 9 on the left so the first tab key sits as far from the window edge as it does from the
        // top of the strip; the right stays at 14 to line up with the bar above.
        var layout = new DockPanel { Margin = new Thickness(9, 0, 14, 0) };
        var rightRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        rightRow.Children.Add(hintBlock);
        rightRow.Children.Add(rightHost);
        DockPanel.SetDock(rightRow, Dock.Right);
        layout.Children.Add(rightRow);
        layout.Children.Add(tabRow);

        Content = new Border
        {
            Background = Palette.BgBar,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            ClipToBounds = true,
            Child = layout
        };

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
        UpdateVisuals();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        HintSettings.Changed -= UpdateVisuals;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelsProperty || change.Property == IsClosableProperty)
        {
            RebuildTabs();
            return;
        }
        if (change.Property == ActiveIndexProperty ||
            change.Property == HintTextProperty ||
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
        tabSeparators.Clear();

        for (var i = 0; i < Labels.Count; i++)
        {
            var index = i;
            if (i > 0)
            {
                var separator = new Rectangle
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
                Text = Labels[i],
                FontSize = 11,
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
            key.PointerPressed += (_, _) => TabSelected?.Invoke(index);
            tabBlocks.Add(block);
            tabKeys.Add(key);
            tabRow.Children.Add(key);
        }

        UpdateVisuals();
    }

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
        for (var i = 0; i < tabBlocks.Count; i++)
        {
            var isActive = i == ActiveIndex;
            tabKeys[i].Classes.Set("emboss", !isActive);
            tabKeys[i].Classes.Set("emboss-press", isActive);
            tabBlocks[i].Foreground = isActive ? Palette.Amber : Palette.TextMuted;
            tabBlocks[i].FontWeight = isActive ? FontWeight.Bold : FontWeight.Normal;
        }
        for (var i = 0; i < tabSeparators.Count; i++)
        {
            tabSeparators[i].IsVisible = ActiveIndex != i && ActiveIndex != i + 1;
        }
        hintBlock.Text = HintText;
        hintBlock.Foreground = HintBrush ?? Palette.TextFaint;
        hintBlock.IsVisible = HintText.Length > 0 && HintSettings.Enabled;
        rightHost.Content = RightContent;
        rightHost.IsVisible = RightContent is not null;
    }
}
