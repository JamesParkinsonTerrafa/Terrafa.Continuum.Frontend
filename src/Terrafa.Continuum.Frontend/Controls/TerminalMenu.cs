using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

/// <summary>The terminal-style pop-up menu shared by the canvases.</summary>
public static class TerminalMenu
{
    public const double RowHeight = 30;
    public const double HeaderHeight = 30;
    public const double MinWidth = 190;

    public static Border Build(string header, IReadOnlyList<(string Label, Action Action)> items, Action close)
    {
        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            Padding = new Thickness(12, 7, 12, 5),
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new TextBlock
            {
                Text = header.ToUpperInvariant(),
                FontSize = 9,
                LetterSpacing = 1,
                Foreground = Palette.TextFaint
            }
        });

        foreach (var (label, action) in items)
        {
            var itemText = new TextBlock
            {
                Text = label,
                FontSize = 10,
                LetterSpacing = 1,
                Foreground = Palette.Text
            };
            var item = new Border
            {
                Padding = new Thickness(12, 7),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = itemText
            };
            var itemAction = action;
            item.PointerEntered += (_, _) =>
            {
                item.Background = Palette.BgField;
                itemText.Foreground = Palette.Amber;
            };
            item.PointerExited += (_, _) =>
            {
                item.Background = Brushes.Transparent;
                itemText.Foreground = Palette.Text;
            };
            item.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                close();
                itemAction();
            };
            stack.Children.Add(item);
        }

        return new Border
        {
            Background = Palette.BgBar,
            BorderBrush = Palette.BorderMid,
            BorderThickness = new Thickness(1),
            MinWidth = MinWidth,
            Child = stack
        };
    }
}

/// <summary>Transparent overlay that hosts one <see cref="TerminalMenu"/> and swallows the dismiss click.</summary>
public class ContextMenuLayer : Canvas
{
    public ContextMenuLayer()
    {
        IsVisible = false;
        Background = Brushes.Transparent;
        PointerPressed += (_, e) =>
        {
            if (e.Source != this) return;
            Close();
            e.Handled = true;
        };
    }

    public void Show(string header, IReadOnlyList<(string Label, Action Action)> items, Point point)
    {
        if (items.Count == 0) return;
        Children.Clear();

        var menu = TerminalMenu.Build(header, items, Close);
        var estimatedHeight = TerminalMenu.HeaderHeight + items.Count * TerminalMenu.RowHeight;
        SetLeft(menu, Math.Max(0, Math.Min(point.X, Bounds.Width - TerminalMenu.MinWidth - 10)));
        SetTop(menu, Math.Max(0, Math.Min(point.Y, Bounds.Height - estimatedHeight)));
        Children.Add(menu);
        IsVisible = true;
    }

    public void Close()
    {
        if (!IsVisible) return;
        IsVisible = false;
        Children.Clear();
    }
}
