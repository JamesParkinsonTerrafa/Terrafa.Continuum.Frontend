using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

/// <summary>
/// The right-click menu the canvas surfaces share. Both the model network and the site plan
/// hang a hit-test layer over themselves and drop one of these into it, so the metrics used
/// to keep a menu inside the viewport live here rather than being guessed at each call site.
/// </summary>
public static class CanvasMenu
{
    public const double Width = 190;
    private const double HeaderHeight = 30;
    private const double ItemHeight = 30;

    public static double EstimateHeight(int itemCount) => HeaderHeight + itemCount * ItemHeight;

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
            stack.Children.Add(BuildItem(label, action, close));
        }

        return new Border
        {
            Background = Palette.BgBar,
            BorderBrush = Palette.BorderMid,
            BorderThickness = new Thickness(1),
            MinWidth = Width,
            Child = stack
        };
    }

    private static Control BuildItem(string label, Action action, Action close)
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
            action();
        };
        return item;
    }
}
