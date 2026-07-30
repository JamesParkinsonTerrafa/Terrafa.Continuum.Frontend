// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;

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
        SettingsButton.PointerPressed += (_, e) =>
        {
            SettingsFlyout.RequestToggle();
            e.Handled = true;
        };
        BrandButton.PointerPressed += (_, e) =>
        {
            ContactDialog.RequestShow();
            e.Handled = true;
        };
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
