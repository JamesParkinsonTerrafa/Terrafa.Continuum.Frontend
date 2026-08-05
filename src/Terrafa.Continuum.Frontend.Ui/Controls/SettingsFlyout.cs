// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Controls;

public class SettingsFlyout : Panel
{
    public static event Action? ToggleRequested;
    public static event Action? SignInRequested;

    public static void RequestToggle() => ToggleRequested?.Invoke();

    internal Border PanelBorder { get; }
    internal Border GrainToggleRow { get; }
    internal Border ButtonToggleRow { get; }
    internal Border BubbleToggleRow { get; }
    internal Border AppearanceToggleRow { get; }
    internal Slider IntensitySlider { get; }
    internal Slider SlopeSlider { get; }
    internal Slider WarpSlider { get; }
    internal Slider GrainSlider { get; }
    internal Slider IdleEmbossSlider { get; }
    internal Slider CornerRadiusSlider { get; }
    internal Slider PopSpeedSlider { get; }
    internal Slider PopForceSlider { get; }
    internal Slider WobbleSlider { get; }
    internal Slider HoldToPopSlider { get; }
    internal Slider SaturationSlider { get; }
    internal Slider NodeCornerRadiusSlider { get; }
    internal Slider HighlightSaturationSlider { get; }
    internal Slider HighlightBrightnessSlider { get; }
    internal Slider TextSizeSlider { get; }
    internal Slider UiScaleSlider { get; }
    internal Border TourToggleRow { get; }
    internal Slider DropHeightSlider { get; }
    internal Slider BounceSlider { get; }
    internal Slider FallSpeedSlider { get; }
    internal Border CsvExportToggleRow { get; }
    internal Slider CacheRowsSlider { get; }
    internal Slider EvictionRowsSlider { get; }

    private readonly StackPanel grainBody;
    private readonly StackPanel buttonBody;
    private readonly StackPanel bubbleBody;
    private readonly StackPanel tourBody;
    private readonly StackPanel appearanceBody;
    private readonly StackPanel csvBody;
    private readonly TextBlock grainArrow;
    private readonly TextBlock buttonArrow;
    private readonly TextBlock bubbleArrow;
    private readonly TextBlock tourArrow;
    private readonly TextBlock appearanceArrow;
    private readonly TextBlock csvArrow;
    private readonly TextBlock darkLabel;
    private readonly TextBlock lightLabel;
    private readonly TextBlock hintsOnLabel;
    private readonly TextBlock hintsOffLabel;
    private readonly TextBlock tabsVerticalLabel;
    private readonly TextBlock tabsHorizontalLabel;
    private readonly TextBlock snapOnLabel;
    private readonly TextBlock snapOffLabel;
    private readonly TextBlock gridLinesOnLabel;
    private readonly TextBlock gridLinesOffLabel;
    private readonly TextBlock waveValue;
    private readonly TextBlock accountName;
    private readonly TextBlock accountAction;

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

        darkLabel = BuildToggleLabel("DARK");
        lightLabel = BuildToggleLabel("LIGHT");
        hintsOnLabel = BuildToggleLabel("ON");
        hintsOffLabel = BuildToggleLabel("OFF");
        tabsVerticalLabel = BuildToggleLabel("VERTICAL");
        tabsHorizontalLabel = BuildToggleLabel("HORIZONTAL");
        snapOnLabel = BuildToggleLabel("ON");
        snapOffLabel = BuildToggleLabel("OFF");
        gridLinesOnLabel = BuildToggleLabel("ON");
        gridLinesOffLabel = BuildToggleLabel("OFF");
        grainArrow = new TextBlock { Text = "▸", FontSize = 10, Foreground = Palette.TextFaint };
        buttonArrow = new TextBlock { Text = "▸", FontSize = 10, Foreground = Palette.TextFaint };
        bubbleArrow = new TextBlock { Text = "▸", FontSize = 10, Foreground = Palette.TextFaint };
        tourArrow = new TextBlock { Text = "▸", FontSize = 10, Foreground = Palette.TextFaint };
        appearanceArrow = new TextBlock { Text = "▸", FontSize = 10, Foreground = Palette.TextFaint };
        csvArrow = new TextBlock { Text = "▸", FontSize = 10, Foreground = Palette.TextFaint };
        waveValue = new TextBlock { FontSize = 10, Foreground = Palette.Text };
        accountName = new TextBlock
        {
            FontSize = 10,
            LetterSpacing = 1,
            Foreground = Palette.TextSub
        };
        accountAction = new TextBlock
        {
            FontSize = 9,
            LetterSpacing = 1,
            FontWeight = FontWeight.Bold,
            Foreground = Palette.Amber
        };
        grainBody = BuildSectionBody();
        buttonBody = BuildSectionBody();
        bubbleBody = BuildSectionBody();
        tourBody = BuildSectionBody();
        appearanceBody = BuildSectionBody();
        csvBody = BuildSectionBody();

        TextSizeSlider = AddSliderRow(appearanceBody, "TEXT SIZE", TypographySettings.MinScale,
            TypographySettings.MaxScale, 0.05, TypographySettings.Scale, "0.00", TypographySettings.SetScale);
        UiScaleSlider = AddSliderRow(appearanceBody, "UI SCALE", UiScaleSettings.MinScale,
            UiScaleSettings.MaxScale, 0.05, UiScaleSettings.Scale, "0.00", UiScaleSettings.SetScale);
        appearanceBody.Children.Add(new TextBlock
        {
            Text = "TEXT SIZE scales the type alone · UI SCALE scales the whole screen, " +
                   "on top of the window fit",
            FontSize = 9,
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        SaturationSlider = AddSliderRow(appearanceBody, "SATURATION", 0, 1, 0.05,
            AppearanceSettings.NodeSaturation, "0.00", AppearanceSettings.SetNodeSaturation);
        NodeCornerRadiusSlider = AddSliderRow(appearanceBody, "CORNER RADIUS", 0, AppearanceSettings.MaxCornerRadius, 1,
            AppearanceSettings.NodeCornerRadius, "0", AppearanceSettings.SetNodeCornerRadius);
        appearanceBody.Children.Add(new TextBlock
        {
            Text = "applies to the input, function and output boxes",
            FontSize = 9,
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        HighlightSaturationSlider = AddSliderRow(appearanceBody, "HIGHLIGHT SAT", 0,
            AppearanceSettings.MaxHighlightSaturation, 0.05,
            AppearanceSettings.HighlightSaturation, "0.00", AppearanceSettings.SetHighlightSaturation);
        HighlightBrightnessSlider = AddSliderRow(appearanceBody, "HIGHLIGHT BRIGHT",
            AppearanceSettings.MinHighlightBrightness, AppearanceSettings.MaxHighlightBrightness, 0.05,
            AppearanceSettings.HighlightBrightness, "0.00", AppearanceSettings.SetHighlightBrightness);
        appearanceBody.Children.Add(new TextBlock
        {
            Text = "applies to the amber — command keys, section titles, accent values",
            FontSize = 9,
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        IdleEmbossSlider = AddSliderRow(buttonBody, "UNSELECTED DEPTH", 0, 1, 0.05,
            ButtonSettings.IdleEmbossStrength, "0.00", ButtonSettings.SetIdleEmbossStrength);
        CornerRadiusSlider = AddSliderRow(buttonBody, "CORNER RADIUS", 0, ButtonSettings.MaxCornerRadius, 1,
            ButtonSettings.CornerRadius, "0", ButtonSettings.SetCornerRadius);
        buttonBody.Children.Add(new TextBlock
        {
            Text = "Depth applies to raised buttons only — the selected tab stays fully pressed.",
            FontSize = 9,
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        PopSpeedSlider = AddSliderRow(bubbleBody, "POP SPEED", BubbleSettings.MinPopSpeed,
            BubbleSettings.MaxPopSpeed, 0.05, BubbleSettings.PopSpeed, "0.00", BubbleSettings.SetPopSpeed);
        PopForceSlider = AddSliderRow(bubbleBody, "POP FORCE", BubbleSettings.MinPopForce,
            BubbleSettings.MaxPopForce, 0.05, BubbleSettings.PopForce, "0.00", BubbleSettings.SetPopForce);
        WobbleSlider = AddSliderRow(bubbleBody, "WOBBLE", 0, 1, 0.05,
            BubbleSettings.Wobble, "0.00", BubbleSettings.SetWobble);
        HoldToPopSlider = AddSliderRow(bubbleBody, "HOLD TO POP", BubbleSettings.MinHoldSeconds,
            BubbleSettings.MaxHoldSeconds, 0.05, BubbleSettings.HoldSeconds, "0.00", BubbleSettings.SetHoldSeconds);
        bubbleBody.Children.Add(new TextBlock
        {
            Text = "Tabs pop like bubble wrap. SPEED is the tempo, FORCE how deep the pop crushes, " +
                   "WOBBLE how springy the settle is. Hold a tab for HOLD TO POP seconds and it pops itself.",
            FontSize = 9,
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        DropHeightSlider = AddSliderRow(tourBody, "DROP HEIGHT", TourSettings.MinDropHeight,
            TourSettings.MaxDropHeight, 10, TourSettings.DropHeight, "0", TourSettings.SetDropHeight);
        BounceSlider = AddSliderRow(tourBody, "BOUNCE", 0, TourSettings.MaxBounce, 0.05,
            TourSettings.Bounce, "0.00", TourSettings.SetBounce);
        FallSpeedSlider = AddSliderRow(tourBody, "FALL SPEED", TourSettings.MinFallSpeed,
            TourSettings.MaxFallSpeed, 0.05, TourSettings.FallSpeed, "0.00", TourSettings.SetFallSpeed);
        tourBody.Children.Add(new TextBlock
        {
            Text = "The tour card drops into the corner like a bouncy ball. DROP HEIGHT is how far " +
                   "above its place it is let go, BOUNCE how much speed it keeps off the floor, " +
                   "FALL SPEED the pull. Moving any of them drops the card again.",
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

        CacheRowsSlider = AddSliderRow(csvBody, "CACHE ROWS", TableCacheSettings.MinCacheRows,
            TableCacheSettings.MaxCacheRows, 10_000, TableCacheSettings.CacheRows, "0",
            value => TableCacheSettings.SetCacheRows((int)value));
        EvictionRowsSlider = AddSliderRow(csvBody, "EVICTION ROWS", TableCacheSettings.MinEvictionRows,
            TableCacheSettings.MaxEvictionRows, 5_000, TableCacheSettings.EvictionRows, "0",
            value => TableCacheSettings.SetEvictionRows((int)value));
        csvBody.Children.Add(new TextBlock
        {
            Text = "CACHE ROWS is the most decoded rows the export grid holds; its window slides in " +
                   "EVICTION ROWS chunks (capped at a quarter of the cache) as the cursor nears an edge.",
            FontSize = 9,
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        AppearanceToggleRow = BuildSectionRow("APPEARANCE", appearanceArrow, appearanceBody);
        ButtonToggleRow = BuildSectionRow("BUTTON UI", buttonArrow, buttonBody);
        BubbleToggleRow = BuildSectionRow("BUBBLE POP", bubbleArrow, bubbleBody);
        TourToggleRow = BuildSectionRow("TOUR CARD", tourArrow, tourBody);
        GrainToggleRow = BuildSectionRow("GRAIN EFFECTS", grainArrow, grainBody);
        CsvExportToggleRow = BuildSectionRow("CSV EXPORT", csvArrow, csvBody);

        var column = new StackPanel();
        column.Children.Add(BuildHeaderRow());
        column.Children.Add(BuildAccountRow());
        column.Children.Add(BuildTabLayoutRow());
        column.Children.Add(BuildThemeRow());
        column.Children.Add(BuildHintsRow());
        column.Children.Add(BuildSnapRow());
        column.Children.Add(BuildGridLinesRow());
        column.Children.Add(AppearanceToggleRow);
        column.Children.Add(appearanceBody);
        column.Children.Add(ButtonToggleRow);
        column.Children.Add(buttonBody);
        column.Children.Add(BubbleToggleRow);
        column.Children.Add(bubbleBody);
        column.Children.Add(TourToggleRow);
        column.Children.Add(tourBody);
        column.Children.Add(GrainToggleRow);
        column.Children.Add(grainBody);
        column.Children.Add(CsvExportToggleRow);
        column.Children.Add(csvBody);

        PanelBorder = new Border
        {
            Width = 320,
            Background = Palette.BgPanel,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 10, 10),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = column
            }
        };
        Children.Add(PanelBorder);
    }

    public void Toggle() => IsVisible = !IsVisible;

    public void Hide() => IsVisible = false;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeManager.Changed += RefreshThemeLabels;
        HintSettings.Changed += RefreshHintLabels;
        SnapSettings.Changed += RefreshSnapLabels;
        TabLayoutSettings.Changed += RefreshTabLayoutLabels;
        AuthSession.Instance.Changed += RefreshAccountRow;
        RefreshThemeLabels();
        RefreshHintLabels();
        RefreshSnapLabels();
        RefreshTabLayoutLabels();
        RefreshAccountRow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThemeManager.Changed -= RefreshThemeLabels;
        HintSettings.Changed -= RefreshHintLabels;
        SnapSettings.Changed -= RefreshSnapLabels;
        TabLayoutSettings.Changed -= RefreshTabLayoutLabels;
        AuthSession.Instance.Changed -= RefreshAccountRow;
    }

    private static TextBlock BuildToggleLabel(string text) => new()
    {
        Text = text,
        FontSize = 9,
        LetterSpacing = 1
    };

    private void RefreshThemeLabels() =>
        MarkActive(ThemeManager.IsLight ? lightLabel : darkLabel, ThemeManager.IsLight ? darkLabel : lightLabel);

    private void RefreshHintLabels() =>
        MarkActive(HintSettings.Enabled ? hintsOnLabel : hintsOffLabel,
            HintSettings.Enabled ? hintsOffLabel : hintsOnLabel);

    private void RefreshTabLayoutLabels() =>
        MarkActive(TabLayoutSettings.Vertical ? tabsVerticalLabel : tabsHorizontalLabel,
            TabLayoutSettings.Vertical ? tabsHorizontalLabel : tabsVerticalLabel);

    private void RefreshSnapLabels()
    {
        MarkActive(SnapSettings.Enabled ? snapOnLabel : snapOffLabel,
            SnapSettings.Enabled ? snapOffLabel : snapOnLabel);
        MarkActive(SnapSettings.ShowGridLines ? gridLinesOnLabel : gridLinesOffLabel,
            SnapSettings.ShowGridLines ? gridLinesOffLabel : gridLinesOnLabel);
    }

    private static void MarkActive(TextBlock active, TextBlock inactive)
    {
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

    private Border BuildAccountRow()
    {
        var content = new DockPanel();
        DockPanel.SetDock(accountAction, Dock.Right);
        content.Children.Add(accountAction);
        content.Children.Add(accountName);

        var row = BuildRow(content, separator: true);
        row.Cursor = new Cursor(StandardCursorType.Hand);
        row.PointerPressed += (_, e) =>
        {
            if (AuthSession.Instance.IsSignedIn)
            {
                AuthSession.Instance.SignOut();
            }
            else
            {
                Hide();
                SignInRequested?.Invoke();
            }
            e.Handled = true;
        };
        return row;
    }

    private void RefreshAccountRow()
    {
        var isSignedIn = AuthSession.Instance.IsSignedIn;
        accountName.Text = isSignedIn ? AuthSession.Instance.Username ?? "" : "NOT SIGNED IN";
        accountAction.Text = isSignedIn ? "SIGN OUT" : "SIGN IN";
    }

    private Border BuildThemeRow() =>
        BuildToggleRow("THEME", darkLabel, lightLabel, ThemeManager.Toggle);

    private Border BuildHintsRow() =>
        BuildToggleRow("HINTS", hintsOnLabel, hintsOffLabel, HintSettings.Toggle);

    private Border BuildTabLayoutRow() =>
        BuildToggleRow("TAB LAYOUT", tabsVerticalLabel, tabsHorizontalLabel, TabLayoutSettings.Toggle);

    private Border BuildSnapRow() =>
        BuildToggleRow("GRID SNAP", snapOnLabel, snapOffLabel, SnapSettings.Toggle);

    private Border BuildGridLinesRow() =>
        BuildToggleRow("GRID LINES", gridLinesOnLabel, gridLinesOffLabel, SnapSettings.ToggleGridLines);

    private static Border BuildToggleRow(string label, TextBlock first, TextBlock second, Action toggle)
    {
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        options.Children.Add(first);
        options.Children.Add(new TextBlock { Text = "/", FontSize = 9, Foreground = Palette.TextGhost });
        options.Children.Add(second);

        var content = new DockPanel();
        DockPanel.SetDock(options, Dock.Right);
        content.Children.Add(options);
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
            toggle();
            e.Handled = true;
        };
        return row;
    }

    private static StackPanel BuildSectionBody() =>
        new() { Margin = new Thickness(14, 8, 14, 14), Spacing = 8, IsVisible = false };

    private static Border BuildSectionRow(string label, TextBlock arrow, StackPanel body)
    {
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
        var chip = new SquircleBorder
        {
            Classes = { "emboss" },
            Background = Palette.EmbossSurface,
            Padding = new Thickness(10, 4),
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

    private static Slider AddSliderRow(StackPanel target, string label, double min, double max,
        double step, double initial, string format, Action<double> apply)
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
        target.Children.Add(rowStack);
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
