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
    internal Slider IntensitySlider { get; }
    internal Slider SlopeSlider { get; }
    internal Slider WarpSlider { get; }
    internal Slider GrainSlider { get; }

    private readonly StackPanel grainBody;
    private readonly TextBlock grainArrow;
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
        grainArrow = new TextBlock { Text = "▸", FontSize = 10, Foreground = Palette.TextFaint };
        waveValue = new TextBlock { FontSize = 10, Foreground = Palette.Text };
        grainBody = new StackPanel { Margin = new Thickness(14, 8, 14, 14), Spacing = 8, IsVisible = false };

        IntensitySlider = AddSliderRow("INTENSITY", 0, GrainSettings.MaxIntensity, 1,
            GrainSettings.Intensity, "0", GrainSettings.SetIntensity);
        AddWavelengthRow();
        SlopeSlider = AddSliderRow("TV SLOPE", 0, 1.5, 0.05,
            GrainSettings.SpectralSlope, "0.00", GrainSettings.SetSpectralSlope);
        WarpSlider = AddSliderRow("WARP", 0, 100, 1,
            GrainSettings.WarpStrength, "0", GrainSettings.SetWarpStrength);
        GrainSlider = AddSliderRow("FINE GRAIN", 0, 8, 0.5,
            GrainSettings.FineGrain, "0.0", GrainSettings.SetFineGrain);
        grainBody.Children.Add(new TextBlock
        {
            Text = "TV SLOPE 1.00 → every scale contributes equal per-pixel variation",
            FontSize = 9,
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        GrainToggleRow = BuildGrainToggleRow();

        var column = new StackPanel();
        column.Children.Add(BuildHeaderRow());
        column.Children.Add(BuildThemeRow());
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

    private Border BuildGrainToggleRow()
    {
        var content = new DockPanel();
        DockPanel.SetDock(grainArrow, Dock.Right);
        content.Children.Add(grainArrow);
        content.Children.Add(new TextBlock
        {
            Text = "GRAIN EFFECTS",
            FontSize = 10,
            LetterSpacing = 1,
            Foreground = Palette.TextSub
        });

        var row = BuildRow(content, separator: true);
        row.Cursor = new Cursor(StandardCursorType.Hand);
        row.PointerPressed += (_, e) =>
        {
            grainBody.IsVisible = !grainBody.IsVisible;
            grainArrow.Text = grainBody.IsVisible ? "▾" : "▸";
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

    private Slider AddSliderRow(string label, double min, double max, double step,
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
        grainBody.Children.Add(rowStack);
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
