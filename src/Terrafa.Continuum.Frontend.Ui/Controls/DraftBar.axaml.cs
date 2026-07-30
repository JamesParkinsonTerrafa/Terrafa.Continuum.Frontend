// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;

namespace Terrafa.Continuum.Frontend.Controls;

public partial class DraftBar : UserControl
{
    public static readonly StyledProperty<string> CommandTextProperty =
        AvaloniaProperty.Register<DraftBar, string>(nameof(CommandText), "");

    public static readonly StyledProperty<bool> ShowCursorProperty =
        AvaloniaProperty.Register<DraftBar, bool>(nameof(ShowCursor));

    public static readonly StyledProperty<object?> ChipContentProperty =
        AvaloniaProperty.Register<DraftBar, object?>(nameof(ChipContent));

    public static readonly StyledProperty<string> CommitTextProperty =
        AvaloniaProperty.Register<DraftBar, string>(nameof(CommitText), "COMMIT <GO>");

    public DraftBar() => InitializeComponent();

    public string CommandText
    {
        get => GetValue(CommandTextProperty);
        set => SetValue(CommandTextProperty, value);
    }

    public bool ShowCursor
    {
        get => GetValue(ShowCursorProperty);
        set => SetValue(ShowCursorProperty, value);
    }

    public object? ChipContent
    {
        get => GetValue(ChipContentProperty);
        set => SetValue(ChipContentProperty, value);
    }

    public string CommitText
    {
        get => GetValue(CommitTextProperty);
        set => SetValue(CommitTextProperty, value);
    }
}
