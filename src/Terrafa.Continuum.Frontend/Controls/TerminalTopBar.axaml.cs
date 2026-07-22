using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public partial class TerminalTopBar : UserControl
{
    public static readonly StyledProperty<string> CommandTextProperty =
        AvaloniaProperty.Register<TerminalTopBar, string>(nameof(CommandText), "");

    public static readonly StyledProperty<string> SubtitleTextProperty =
        AvaloniaProperty.Register<TerminalTopBar, string>(nameof(SubtitleText), "");

    public static readonly StyledProperty<object?> RightContentProperty =
        AvaloniaProperty.Register<TerminalTopBar, object?>(nameof(RightContent));

    public TerminalTopBar()
    {
        InitializeComponent();
        ThemeToggle.PointerPressed += (_, _) => ThemeManager.Toggle();
        var activeLabel = ThemeManager.IsLight ? LightLabel : DarkLabel;
        var inactiveLabel = ThemeManager.IsLight ? DarkLabel : LightLabel;
        activeLabel.Foreground = Palette.Amber;
        activeLabel.FontWeight = FontWeight.Bold;
        inactiveLabel.Foreground = Palette.TextFaint;
    }

    public string CommandText
    {
        get => GetValue(CommandTextProperty);
        set => SetValue(CommandTextProperty, value);
    }

    public string SubtitleText
    {
        get => GetValue(SubtitleTextProperty);
        set => SetValue(SubtitleTextProperty, value);
    }

    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }
}
