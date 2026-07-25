using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public class SettingsFlyout : Panel
{
    public static event Action? ToggleRequested;

    public static void RequestToggle() => ToggleRequested?.Invoke();

    internal Border PanelBorder { get; }
    internal Border GrainToggleRow { get; }
    internal Border AppearanceToggleRow { get; }
    internal Slider IntensitySlider { get; }
    internal Slider SlopeSlider { get; }
    internal Slider WarpSlider { get; }
    internal Slider GrainSlider { get; }
    internal Slider SaturationSlider { get; }
    internal Slider CornerRadiusSlider { get; }

    private readonly StackPanel grainBody;
    private readonly StackPanel appearanceBody;
    private readonly TextBlock darkLabel;
    private readonly TextBlock lightLabel;
    private readonly TextBlock waveValue;

    public SettingsFlyout()
    {
        IsVisible = false;

        var backdrop = new Border { Background = Brushes.Transparent };
        backdrop.PointerPressed += (_, e) =>
        {
            Hide();
            e.Handled = true;
        };
        Children.Add(backdrop);

        darkLabel = BuildThemeLabel("DARK");
        lightLabel = BuildThemeLabel("LIGHT");
        waveValue = new TextBlock { FontSize = 10, Foreground = Palette.Text };
        grainBody = BuildSectionBody();
        appearanceBody = BuildSectionBody();

        SaturationSlider = AddSliderRow(appearanceBody, "SATURATION", 0, 1, 0.05,
            AppearanceSettings.NodeSaturation, "0.00", AppearanceSettings.SetNodeSaturation);
        CornerRadiusSlider = AddSliderRow(appearanceBody, "CORNER RADIUS", 0, AppearanceSettings.MaxCornerRadius, 1,
            AppearanceSettings.NodeCornerRadius, "0", AppearanceSettings.SetNodeCornerRadius);
        appearanceBody.Children.Add(new TextBlock
        {
            Text = "applies to the input, function and output boxes",
            FontSize = 9,
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        IntensitySlider = AddSliderRow(grainBody, "INTENSITY", 0, GrainSettings.MaxIntensity, 1,
            GrainSettings.Intensity, "0", GrainSettings.SetIntensity);
        AddWavelengthRow();
        SlopeSlider = AddSliderRow(grainBody, "TV SLOPE", 0, 1.5, 0.05,
            GrainSettings.SpectralSlope, "0.00", GrainSettings.SetSpectralSlope);
        WarpSlider = AddSliderRow(grainBody, "WARP", 0, 100, 1,
            GrainSettings.WarpStrength, "0", GrainSettings.SetWarpStrength);
        GrainSlider = AddSliderRow(grainBody, "FINE GRAIN", 0, 8, 0.5,
            GrainSettings.FineGrain, "0.0", GrainSettings.SetFineGrain);
        grainBody.Children.Add(new TextBlock
        {
            Text = "TV SLOPE 1.00 → every scale contributes equal per-pixel variation",
            FontSize = 9,
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        AppearanceToggleRow = BuildSectionToggleRow("APPEARANCE", appearanceBody);
        GrainToggleRow = BuildSectionToggleRow("GRAIN EFFECTS", grainBody);

        var column = new StackPanel();
        column.Children.Add(BuildHeaderRow());
        column.Children.Add(BuildThemeRow());
        column.Children.Add(AppearanceToggleRow);
        column.Children.Add(appearanceBody);
        column.Children.Add(GrainToggleRow);
        column.Children.Add(grainBody);

        PanelBorder = new Border
        {
            Width = 320,
            Background = Palette.BgPanel,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 10, 0),
            Child = column
        };
        Children.Add(PanelBorder);
    }

    public void Toggle() => IsVisible = !IsVisible;

    public void Hide() => IsVisible = false;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeManager.Changed += RefreshThemeLabels;
        RefreshThemeLabels();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThemeManager.Changed -= RefreshThemeLabels;
    }

    private static TextBlock BuildThemeLabel(string text) => new()
    {
        Text = text,
        FontSize = 9,
        LetterSpacing = 1
    };

    private void RefreshThemeLabels()
    {
        var active = ThemeManager.IsLight ? lightLabel : darkLabel;
        var inactive = ThemeManager.IsLight ? darkLabel : lightLabel;
        active.Foreground = Palette.Amber;
        active.FontWeight = FontWeight.Bold;
        inactive.Foreground = Palette.TextFaint;
        inactive.FontWeight = FontWeight.Normal;
    }

    private Border BuildHeaderRow()
    {
        var closeBlock = new TextBlock
        {
            Text = "✕",
            FontSize = 10,
            Foreground = Palette.TextFaint,
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
            Text = "SETTINGS",
            FontSize = 10,
            LetterSpacing = 2,
            FontWeight = FontWeight.Bold,
            Foreground = Palette.Amber
        });
        return BuildRow(content, separator: true);
    }

    private Border BuildThemeRow()
    {
        var toggle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        toggle.Children.Add(darkLabel);
        toggle.Children.Add(new TextBlock { Text = "/", FontSize = 9, Foreground = Palette.TextGhost });
        toggle.Children.Add(lightLabel);

        var content = new DockPanel();
        DockPanel.SetDock(toggle, Dock.Right);
        content.Children.Add(toggle);
        content.Children.Add(new TextBlock
        {
            Text = "THEME",
            FontSize = 10,
            LetterSpacing = 1,
            Foreground = Palette.TextSub
        });

        var row = BuildRow(content, separator: true);
        row.Cursor = new Cursor(StandardCursorType.Hand);
        row.PointerPressed += (_, e) =>
        {
            ThemeManager.Toggle();
            e.Handled = true;
        };
        return row;
    }

    private static StackPanel BuildSectionBody() =>
        new() { Margin = new Thickness(14, 8, 14, 14), Spacing = 8, IsVisible = false };

    private static Border BuildSectionToggleRow(string label, StackPanel body)
    {
        var arrow = new TextBlock { Text = "▸", FontSize = 10, Foreground = Palette.TextFaint };
        var content = new DockPanel();
        DockPanel.SetDock(arrow, Dock.Right);
        content.Children.Add(arrow);
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            LetterSpacing = 1,
            Foreground = Palette.TextSub
        });

        var row = BuildRow(content, separator: true);
        row.Cursor = new Cursor(StandardCursorType.Hand);
        row.PointerPressed += (_, e) =>
        {
            body.IsVisible = !body.IsVisible;
            arrow.Text = body.IsVisible ? "▾" : "▸";
            e.Handled = true;
        };
        return row;
    }

    private void AddWavelengthRow()
    {
        waveValue.Text = WavelengthLabel();
        var chip = new Border
        {
            Background = Palette.BgField,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = waveValue
        };
        chip.PointerPressed += (_, e) =>
        {
            var options = GrainSettings.BaseWavelengthOptions;
            var next = options[(Array.IndexOf(options, GrainSettings.BaseWavelength) + 1) % options.Length];
            GrainSettings.SetBaseWavelength(next);
            waveValue.Text = WavelengthLabel();
            e.Handled = true;
        };

        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        DockPanel.SetDock(chip, Dock.Right);
        row.Children.Add(chip);
        row.Children.Add(new TextBlock
        {
            Text = "BASE λ",
            FontSize = 10,
            LetterSpacing = 1,
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        });
        grainBody.Children.Add(row);
    }

    private static string WavelengthLabel() => $"◂ {GrainSettings.BaseWavelength} px ▸";

    private static Slider AddSliderRow(StackPanel host, string label, double min, double max, double step,
        double initial, string format, Action<double> apply)
    {
        var readout = new TextBlock
        {
            Text = initial.ToString(format),
            FontSize = 11,
            Foreground = Palette.Text
        };
        var header = new DockPanel();
        DockPanel.SetDock(readout, Dock.Right);
        header.Children.Add(readout);
        header.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            LetterSpacing = 1,
            Foreground = Palette.TextFaint
        });

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = initial,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, -6, 0, -6)
        };
        slider.Resources["SliderTrackValueFill"] = Palette.Amber;
        slider.Resources["SliderTrackValueFillPointerOver"] = Palette.Amber;
        slider.Resources["SliderTrackValueFillPressed"] = Palette.Amber;
        slider.Resources["SliderTrackFill"] = Palette.BorderMid;
        slider.Resources["SliderTrackFillPointerOver"] = Palette.BorderMid;
        slider.Resources["SliderTrackFillPressed"] = Palette.BorderMid;
        slider.Resources["SliderThumbBackground"] = Palette.Amber;
        slider.Resources["SliderThumbBackgroundPointerOver"] = Palette.AmberSoft;
        slider.Resources["SliderThumbBackgroundPressed"] = Palette.AmberSoft;
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                readout.Text = slider.Value.ToString(format);
                apply(slider.Value);
            }
        };

        var rowStack = new StackPanel();
        rowStack.Children.Add(header);
        rowStack.Children.Add(slider);
        host.Children.Add(rowStack);
        return slider;
    }

    private static Border BuildRow(Control content, bool separator) => new()
    {
        Padding = new Thickness(14, 9),
        Background = Brushes.Transparent,
        BorderBrush = Palette.RowSeparator,
        BorderThickness = new Thickness(0, 0, 0, separator ? 1 : 0),
        Child = content
    };
}
