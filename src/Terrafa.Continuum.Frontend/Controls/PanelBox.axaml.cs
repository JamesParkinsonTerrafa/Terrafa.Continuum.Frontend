using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public partial class PanelBox : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PanelBox, string>(nameof(Title), "");

    public static readonly StyledProperty<string> HintProperty =
        AvaloniaProperty.Register<PanelBox, string>(nameof(Hint), "");

    public static readonly StyledProperty<IBrush> HintForegroundProperty =
        AvaloniaProperty.Register<PanelBox, IBrush>(nameof(HintForeground), Palette.TextFaint);

    public static readonly StyledProperty<IBrush> HeaderBackgroundProperty =
        AvaloniaProperty.Register<PanelBox, IBrush>(nameof(HeaderBackground), Brushes.Transparent);

    public static readonly StyledProperty<Thickness> HeaderPaddingProperty =
        AvaloniaProperty.Register<PanelBox, Thickness>(nameof(HeaderPadding), new Thickness(12, 8));

    public static readonly StyledProperty<Thickness> FooterPaddingProperty =
        AvaloniaProperty.Register<PanelBox, Thickness>(nameof(FooterPadding), new Thickness(12, 10));

    public static readonly StyledProperty<object?> InnerContentProperty =
        AvaloniaProperty.Register<PanelBox, object?>(nameof(InnerContent));

    public static readonly StyledProperty<object?> FooterContentProperty =
        AvaloniaProperty.Register<PanelBox, object?>(nameof(FooterContent));

    public PanelBox() => InitializeComponent();

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public IBrush HintForeground
    {
        get => GetValue(HintForegroundProperty);
        set => SetValue(HintForegroundProperty, value);
    }

    public IBrush HeaderBackground
    {
        get => GetValue(HeaderBackgroundProperty);
        set => SetValue(HeaderBackgroundProperty, value);
    }

    public Thickness HeaderPadding
    {
        get => GetValue(HeaderPaddingProperty);
        set => SetValue(HeaderPaddingProperty, value);
    }

    public Thickness FooterPadding
    {
        get => GetValue(FooterPaddingProperty);
        set => SetValue(FooterPaddingProperty, value);
    }

    public object? InnerContent
    {
        get => GetValue(InnerContentProperty);
        set => SetValue(InnerContentProperty, value);
    }

    public object? FooterContent
    {
        get => GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
    }

    public bool HasHint => Hint.Length > 0;

    public bool HasFooter => FooterContent is not null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HintProperty)
            RaisePropertyChanged(HasHintProperty, !HasHint, HasHint);
        if (change.Property == FooterContentProperty)
            RaisePropertyChanged(HasFooterProperty, !HasFooter, HasFooter);
    }

    public static readonly DirectProperty<PanelBox, bool> HasHintProperty =
        AvaloniaProperty.RegisterDirect<PanelBox, bool>(nameof(HasHint), panel => panel.HasHint);

    public static readonly DirectProperty<PanelBox, bool> HasFooterProperty =
        AvaloniaProperty.RegisterDirect<PanelBox, bool>(nameof(HasFooter), panel => panel.HasFooter);
}
