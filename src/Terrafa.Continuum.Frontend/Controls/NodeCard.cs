using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public enum NodeCardVariant
{
    Measure,
    Transfer,
    Figure,
    Provisional,
    ObjectNode,
    NewNode
}

public class NodeCard : UserControl
{
    public static readonly StyledProperty<NodeCardVariant> VariantProperty =
        AvaloniaProperty.Register<NodeCard, NodeCardVariant>(nameof(Variant));

    public static readonly StyledProperty<string> TagTextProperty =
        AvaloniaProperty.Register<NodeCard, string>(nameof(TagText), "");

    public static readonly StyledProperty<string> TagRightProperty =
        AvaloniaProperty.Register<NodeCard, string>(nameof(TagRight), "");

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<NodeCard, string>(nameof(Title), "");

    public static readonly StyledProperty<string> ValueMainProperty =
        AvaloniaProperty.Register<NodeCard, string>(nameof(ValueMain), "");

    public static readonly StyledProperty<string> ValueAccentProperty =
        AvaloniaProperty.Register<NodeCard, string>(nameof(ValueAccent), "");

    public static readonly StyledProperty<string> NoteProperty =
        AvaloniaProperty.Register<NodeCard, string>(nameof(Note), "");

    public static readonly StyledProperty<double> TitleSizeProperty =
        AvaloniaProperty.Register<NodeCard, double>(nameof(TitleSize), 13);

    public static readonly StyledProperty<double> ValueSizeProperty =
        AvaloniaProperty.Register<NodeCard, double>(nameof(ValueSize), 13);

    public static readonly StyledProperty<object?> ExtraContentProperty =
        AvaloniaProperty.Register<NodeCard, object?>(nameof(ExtraContent));

    public static readonly StyledProperty<IBrush?> FillOverrideProperty =
        AvaloniaProperty.Register<NodeCard, IBrush?>(nameof(FillOverride));

    private readonly Rectangle frame;
    private readonly TextBlock tagBlock;
    private readonly TextBlock tagRightBlock;
    private readonly DockPanel tagRow;
    private readonly TextBlock titleBlock;
    private readonly TextBlock valueBlock;
    private readonly Run valueMainRun;
    private readonly Run valueAccentRun;
    private readonly TextBlock noteBlock;
    private readonly ContentControl extraHost;

    public NodeCard()
    {
        frame = new Rectangle { StrokeThickness = 1 };

        tagBlock = new TextBlock { FontSize = 9, LetterSpacing = 1 };
        tagRightBlock = new TextBlock { FontSize = 9, LetterSpacing = 1 };
        tagRow = new DockPanel();
        DockPanel.SetDock(tagRightBlock, Dock.Right);
        tagRow.Children.Add(tagRightBlock);
        tagRow.Children.Add(tagBlock);

        titleBlock = new TextBlock { Margin = new Thickness(0, 2, 0, 0) };
        valueMainRun = new Run();
        valueAccentRun = new Run();
        valueBlock = new TextBlock { Margin = new Thickness(0, 2, 0, 0), Foreground = Palette.TextStrong };
        valueBlock.Inlines = [valueMainRun, new Run(" "), valueAccentRun];
        noteBlock = new TextBlock
        {
            FontSize = 9,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap
        };
        extraHost = new ContentControl { Margin = new Thickness(0, 6, 0, 0) };

        var body = new StackPanel { Margin = new Thickness(10, 8) };
        body.Children.Add(tagRow);
        body.Children.Add(titleBlock);
        body.Children.Add(valueBlock);
        body.Children.Add(noteBlock);
        body.Children.Add(extraHost);

        var layers = new Panel();
        layers.Children.Add(frame);
        layers.Children.Add(body);
        Content = layers;

        UpdateVisuals();
    }

    public NodeCardVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public string TagText
    {
        get => GetValue(TagTextProperty);
        set => SetValue(TagTextProperty, value);
    }

    public string TagRight
    {
        get => GetValue(TagRightProperty);
        set => SetValue(TagRightProperty, value);
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string ValueMain
    {
        get => GetValue(ValueMainProperty);
        set => SetValue(ValueMainProperty, value);
    }

    public string ValueAccent
    {
        get => GetValue(ValueAccentProperty);
        set => SetValue(ValueAccentProperty, value);
    }

    public string Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    public double TitleSize
    {
        get => GetValue(TitleSizeProperty);
        set => SetValue(TitleSizeProperty, value);
    }

    public double ValueSize
    {
        get => GetValue(ValueSizeProperty);
        set => SetValue(ValueSizeProperty, value);
    }

    public object? ExtraContent
    {
        get => GetValue(ExtraContentProperty);
        set => SetValue(ExtraContentProperty, value);
    }

    public IBrush? FillOverride
    {
        get => GetValue(FillOverrideProperty);
        set => SetValue(FillOverrideProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AppearanceSettings.Changed += UpdateVisuals;
        UpdateVisuals();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        AppearanceSettings.Changed -= UpdateVisuals;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == VariantProperty || change.Property == TagTextProperty ||
            change.Property == TagRightProperty || change.Property == TitleProperty ||
            change.Property == ValueMainProperty || change.Property == ValueAccentProperty ||
            change.Property == NoteProperty || change.Property == TitleSizeProperty ||
            change.Property == ValueSizeProperty || change.Property == ExtraContentProperty ||
            change.Property == FillOverrideProperty)
        {
            UpdateVisuals();
        }
    }

    private void UpdateVisuals()
    {
        var style = ResolveStyle(Variant);
        frame.RadiusX = AppearanceSettings.NodeCornerRadius;
        frame.RadiusY = AppearanceSettings.NodeCornerRadius;
        frame.Stroke = AppearanceSettings.Toned(style.Accent);
        frame.Fill = FillOverride ?? AppearanceSettings.Toned(style.Fill);
        frame.StrokeDashArray = style.Dashed ? [4, 3] : null;

        tagBlock.Text = TagText;
        tagBlock.Foreground = AppearanceSettings.Toned(style.TagBrush);
        tagRightBlock.Text = TagRight;
        tagRightBlock.Foreground = AppearanceSettings.Toned(style.TagRightBrush);
        tagRightBlock.IsVisible = TagRight.Length > 0;

        titleBlock.Text = Title;
        titleBlock.FontSize = TitleSize;
        titleBlock.Foreground = AppearanceSettings.Toned(style.TitleBrush);
        titleBlock.IsVisible = Title.Length > 0;

        valueMainRun.Text = ValueMain;
        valueBlock.FontSize = ValueSize;
        valueAccentRun.Text = ValueAccent;
        valueAccentRun.Foreground = AppearanceSettings.Toned(style.Accent);
        valueAccentRun.FontSize = Math.Max(ValueSize - 3, 9);
        valueBlock.IsVisible = ValueMain.Length > 0;

        noteBlock.Text = Note;
        noteBlock.Foreground = AppearanceSettings.Toned(style.NoteBrush);
        noteBlock.IsVisible = Note.Length > 0;

        extraHost.Content = ExtraContent;
        extraHost.IsVisible = ExtraContent is not null;
    }

    public static IBrush AccentFor(NodeCardVariant variant) =>
        AppearanceSettings.Toned(ResolveStyle(variant).Accent);

    private sealed record CardStyle(
        IBrush Accent,
        IBrush Fill,
        IBrush TagBrush,
        IBrush TagRightBrush,
        IBrush TitleBrush,
        IBrush NoteBrush,
        bool Dashed);

    private static CardStyle ResolveStyle(NodeCardVariant variant) => variant switch
    {
        NodeCardVariant.Measure => new CardStyle(
            Palette.Cyan, Palette.CyanFill, Palette.Cyan, Palette.TextFaint,
            Palette.CyanPale, Palette.TextFaint, false),
        NodeCardVariant.Transfer => new CardStyle(
            Palette.Amber, Palette.AmberFill, Palette.Amber, Palette.TextFaint,
            Palette.AmberSoft, Palette.TextMuted, false),
        NodeCardVariant.Figure => new CardStyle(
            Palette.Green, Palette.GreenFill, Palette.Green, Palette.TextFaint,
            Palette.GreenSoft, Palette.TextFaint, false),
        NodeCardVariant.Provisional => new CardStyle(
            Palette.Purple, Palette.PurpleFill, Palette.Purple, Palette.Purple,
            Palette.PurpleSoft, Palette.PurpleMuted, true),
        NodeCardVariant.NewNode => new CardStyle(
            Palette.Green, Palette.GreenFill, Palette.Green, Palette.Green,
            Palette.TextStrong, Palette.TextFaint, false),
        _ => new CardStyle(
            Palette.ObjectBorder, Palette.ObjectFill, Palette.TextMuted, Palette.TextFaint,
            Palette.TextStrong, Palette.TextFaint, false)
    };
}
