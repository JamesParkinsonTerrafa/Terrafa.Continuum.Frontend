// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public class Chip : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<Chip, string>(nameof(Text), "");

    public static readonly StyledProperty<string> AccentProperty =
        AvaloniaProperty.Register<Chip, string>(nameof(Accent), "green");

    private readonly Border border;
    private readonly TextBlock textBlock;

    public Chip()
    {
        textBlock = new TextBlock { FontSize = TypographySettings.Size(10) };
        border = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4),
            Child = textBlock
        };
        Content = border;
        UpdateVisuals();
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == AccentProperty)
            UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        textBlock.Text = Text;
        var (foreground, borderBrush) = ResolveAccent(Accent);
        textBlock.Foreground = foreground;
        border.BorderBrush = borderBrush;
    }

    private static (IBrush Foreground, IBrush Border) ResolveAccent(string accent) => accent switch
    {
        "amber" => (Palette.Amber, Palette.AmberChipBorder),
        "cyan" => (Palette.Cyan, Palette.CyanChipBorder),
        "red" => (Palette.Red, Palette.Red),
        "purple" => (Palette.Purple, Palette.Purple),
        _ => (Palette.Green, Palette.GreenChipBorder)
    };
}
