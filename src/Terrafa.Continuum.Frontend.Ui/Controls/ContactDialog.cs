// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public class ContactDialog : Panel
{
    public static event Action? ShowRequested;

    public static void RequestShow() => ShowRequested?.Invoke();

    public ContactDialog()
    {
        IsVisible = false;

        var backdrop = new Border { Background = new SolidColorBrush(Colors.Black, 0.55) };
        backdrop.PointerPressed += (_, e) =>
        {
            Hide();
            e.Handled = true;
        };
        Children.Add(backdrop);

        var column = new StackPanel();
        column.Children.Add(BuildHeaderRow());
        column.Children.Add(BuildBody());

        var panel = new Border
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

    public void Show() => IsVisible = true;

    public void Hide() => IsVisible = false;

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
        content.Children.Add(new TextBlock
        {
            Text = "GET IN TOUCH",
            FontSize = 10,
            LetterSpacing = 2,
            FontWeight = FontWeight.Bold,
            Foreground = Palette.Amber,
            VerticalAlignment = VerticalAlignment.Center
        });

        return new Border
        {
            Padding = new Thickness(18, 12),
            Background = Palette.BgBar,
            BorderBrush = Palette.RowSeparator,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = content
        };
    }

    private static Control BuildBody()
    {
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 18), Spacing = 14 };
        body.Children.Add(new TextBlock
        {
            Text = "Get in touch with our engineers to get your data connected.",
            FontSize = 12,
            LineHeight = 18,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextSub
        });

        var links = new StackPanel { Spacing = 8 };
        links.Children.Add(BuildLinkRow("PHONE", "+44 20 7946 0142"));
        links.Children.Add(BuildLinkRow("EMAIL", "engineering@terrafa.example"));
        links.Children.Add(BuildLinkRow("WEB", "https://terrafa.example/connect"));
        body.Children.Add(links);

        body.Children.Add(new TextBlock
        {
            Text = "Placeholder contact details — not yet wired to a real desk.",
            FontSize = 9,
            LetterSpacing = 0.5,
            Foreground = Palette.TextFaint
        });

        return body;
    }

    private static Control BuildLinkRow(string label, string value)
    {
        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 12,
            Foreground = Palette.Amber,
            TextDecorations = TextDecorations.Underline,
            VerticalAlignment = VerticalAlignment.Center
        };

        var row = new DockPanel();
        DockPanel.SetDock(valueBlock, Dock.Right);
        row.Children.Add(valueBlock);
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            LetterSpacing = 1,
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        });

        var shell = new Border
        {
            Padding = new Thickness(12, 8),
            Background = Palette.BgField,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row
        };
        shell.PointerEntered += (_, _) => valueBlock.Foreground = Palette.AmberSoft;
        shell.PointerExited += (_, _) => valueBlock.Foreground = Palette.Amber;
        return shell;
    }
}
