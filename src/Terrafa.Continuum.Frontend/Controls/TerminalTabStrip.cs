using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public class TerminalTabStrip : UserControl
{
    private static readonly string[] TabLabels =
        ["1) NETWORK", "2) TRANSFER FUNCTION", "3) DASHBOARD", "4) DATA TREE", "5) MAP"];

    public static readonly StyledProperty<int> ActiveIndexProperty =
        AvaloniaProperty.Register<TerminalTabStrip, int>(nameof(ActiveIndex));

    public static readonly StyledProperty<string> HintTextProperty =
        AvaloniaProperty.Register<TerminalTabStrip, string>(nameof(HintText), "");

    public static readonly StyledProperty<IBrush?> HintBrushProperty =
        AvaloniaProperty.Register<TerminalTabStrip, IBrush?>(nameof(HintBrush));

    public static readonly StyledProperty<object?> RightContentProperty =
        AvaloniaProperty.Register<TerminalTabStrip, object?>(nameof(RightContent));

    public event Action<int>? TabSelected;

    private readonly List<TextBlock> tabBlocks = [];
    private readonly TextBlock hintBlock;
    private readonly ContentControl rightHost;

    public TerminalTabStrip()
    {
        Height = 32;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < TabLabels.Length; i++)
        {
            var index = i;
            var block = new TextBlock
            {
                Text = TabLabels[i],
                FontSize = 11,
                Padding = new Thickness(12, 4),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            block.PointerPressed += (_, _) => TabSelected?.Invoke(index);
            tabBlocks.Add(block);
            row.Children.Add(block);
        }

        hintBlock = new TextBlock
        {
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        rightHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center };

        var layout = new DockPanel { Margin = new Thickness(14, 0) };
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
        layout.Children.Add(row);

        Content = new Border
        {
            Background = Palette.BgPanel,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = layout
        };

        UpdateVisuals();
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActiveIndexProperty ||
            change.Property == HintTextProperty ||
            change.Property == HintBrushProperty ||
            change.Property == RightContentProperty)
        {
            UpdateVisuals();
        }
    }

    private void UpdateVisuals()
    {
        for (var i = 0; i < tabBlocks.Count; i++)
        {
            var isActive = i == ActiveIndex;
            tabBlocks[i].Background = isActive ? Palette.Amber : Brushes.Transparent;
            tabBlocks[i].Foreground = isActive ? Palette.TabActiveText : Palette.TextMuted;
            tabBlocks[i].FontWeight = isActive ? FontWeight.Bold : FontWeight.Normal;
        }
        hintBlock.Text = HintText;
        hintBlock.Foreground = HintBrush ?? Palette.TextFaint;
        hintBlock.IsVisible = HintText.Length > 0;
        rightHost.Content = RightContent;
        rightHost.IsVisible = RightContent is not null;
    }
}
