using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

/// <summary>Modal panel in the style of <see cref="ContactDialog"/>, driven from code per use.</summary>
public class OverlayDialog : Panel
{
    private readonly TextBlock titleBlock;
    private readonly ContentControl bodyHost;
    private readonly Border panel;
    private readonly Border confirmButton;
    private readonly TextBlock confirmText;

    private Func<bool>? confirm;

    public OverlayDialog()
    {
        IsVisible = false;

        var backdrop = new Border { Background = new SolidColorBrush(Colors.Black, 0.55) };
        backdrop.PointerPressed += (_, e) =>
        {
            Hide();
            e.Handled = true;
        };
        Children.Add(backdrop);

        titleBlock = new TextBlock
        {
            FontSize = 10,
            LetterSpacing = 2,
            FontWeight = FontWeight.Bold,
            Foreground = Palette.Amber,
            VerticalAlignment = VerticalAlignment.Center
        };
        bodyHost = new ContentControl { Margin = new Thickness(18, 16, 18, 4) };
        confirmText = new TextBlock { FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brushes.Black };
        confirmButton = new Border
        {
            Background = Palette.Amber,
            Padding = new Thickness(14, 5),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = confirmText
        };
        confirmButton.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (confirm is null || confirm()) Hide();
        };

        var column = new StackPanel();
        column.Children.Add(BuildHeaderRow());
        column.Children.Add(bodyHost);
        column.Children.Add(BuildFooterRow());

        panel = new Border
        {
            Width = 460,
            Background = Palette.BgPanel,
            BorderBrush = Palette.BorderMid,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = column
        };
        panel.PointerPressed += (_, e) => e.Handled = true;
        Children.Add(panel);
    }

    /// <summary><paramref name="onConfirm"/> returns false to keep the dialog open (nothing selected yet).</summary>
    public void Show(string title, Control body, string confirmLabel, Func<bool> onConfirm, double width = 460)
    {
        titleBlock.Text = title;
        bodyHost.Content = body;
        confirmText.Text = confirmLabel;
        confirm = onConfirm;
        panel.Width = width;
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
        bodyHost.Content = null;
        confirm = null;
    }

    private Border BuildHeaderRow()
    {
        var closeBlock = new TextBlock
        {
            Text = "✕",
            FontSize = 10,
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        closeBlock.PointerPressed += (_, e) =>
        {
            Hide();
            e.Handled = true;
        };

        var content = new DockPanel();
        DockPanel.SetDock(closeBlock, Dock.Right);
        content.Children.Add(closeBlock);
        content.Children.Add(titleBlock);

        return new Border
        {
            Padding = new Thickness(18, 12),
            Background = Palette.BgBar,
            BorderBrush = Palette.RowSeparator,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = content
        };
    }

    private Border BuildFooterRow()
    {
        var cancelText = new TextBlock
        {
            Text = "CANCEL",
            FontSize = 11,
            LetterSpacing = 1,
            Foreground = Palette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        var cancel = new Border
        {
            Padding = new Thickness(12, 5),
            Background = Palette.BgField,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = cancelText
        };
        cancel.PointerEntered += (_, _) => cancelText.Foreground = Palette.Text;
        cancel.PointerExited += (_, _) => cancelText.Foreground = Palette.TextMuted;
        cancel.PointerPressed += (_, e) =>
        {
            Hide();
            e.Handled = true;
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        row.Children.Add(cancel);
        row.Children.Add(confirmButton);

        return new Border
        {
            Padding = new Thickness(18, 12),
            BorderBrush = Palette.RowSeparator,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = row
        };
    }
}
