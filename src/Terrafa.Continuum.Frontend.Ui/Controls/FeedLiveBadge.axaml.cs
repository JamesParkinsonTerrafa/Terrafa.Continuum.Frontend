using Avalonia;
using Avalonia.Controls;

namespace Terrafa.Continuum.Frontend.Controls;

public partial class FeedLiveBadge : UserControl
{
    public static readonly StyledProperty<string> TimeTextProperty =
        AvaloniaProperty.Register<FeedLiveBadge, string>(nameof(TimeText), "");

    public FeedLiveBadge() => InitializeComponent();

    public string TimeText
    {
        get => GetValue(TimeTextProperty);
        set => SetValue(TimeTextProperty, value);
    }
}
