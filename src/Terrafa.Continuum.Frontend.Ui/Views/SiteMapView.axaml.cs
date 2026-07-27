using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Controls.Charts;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class SiteMapView : UserControl
{
    private static readonly (string Label, IBrush Swatch, bool Enabled)[] Layers =
    [
        ("facility image", Palette.TextMuted, true),
        ("measure zones", Palette.Cyan, true),
        ("pinned figures", Palette.Green, true),
        ("provisional flows", Palette.Purple, true),
        ("labels", Palette.TextGhost, false)
    ];

    private static readonly string[] UnplacedMeasures =
    [
        "tank_02.temp ⠿",
        "tank_03.level ⠿",
        "intake.grade ⠿"
    ];

    public SiteMapView() : this(DemoData.CreateSnapshot(), _ => { })
    {
    }

    public SiteMapView(DataSnapshot snapshot, Action<int> navigate)
    {
        InitializeComponent();
        Tabs.TabSelected += navigate;

        FeedBadge.TimeText = snapshot.AsOf.ToString("dd-MMM-yyyy HH:mm:ss 'UTC'").ToUpperInvariant();

        BuildLayerRows();
        BuildUnplacedChips();
        FillPinnedFigures(snapshot);

        FlowEdge.Edges =
        [
            new Edge
            {
                From = new Point(285, 620),
                To = new Point(520, 450),
                Stroke = Palette.Purple,
                Dashes = [7, 5],
                ArrowAtEnd = true
            }
        ];

        NoiseOverlay.Attach(this);
    }

    private void BuildLayerRows()
    {
        foreach (var (label, swatch, enabled) in Layers)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = enabled ? "[x]" : "[ ]",
                FontSize = 11,
                Foreground = enabled ? Palette.Green : Palette.TextFaint
            });
            row.Children.Add(new Rectangle
            {
                Width = 10,
                Height = 2,
                Fill = swatch,
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = Palette.Text
            });
            LayerRows.Children.Add(row);
        }
    }

    private void BuildUnplacedChips()
    {
        foreach (var label in UnplacedMeasures)
        {
            var chip = new Panel { Margin = new Thickness(0, 0, 6, 6) };
            chip.Children.Add(new Rectangle
            {
                Stroke = Palette.Cyan,
                StrokeThickness = 1,
                StrokeDashArray = [3, 3],
                Fill = Brushes.Transparent
            });
            chip.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = Palette.Cyan,
                Margin = new Thickness(8, 3)
            });
            UnplacedChips.Children.Add(chip);
        }
    }

    private void FillPinnedFigures(DataSnapshot snapshot)
    {
        var tank01Level = snapshot.Site.TankFarm.Tank01.Level;
        Tank01Fig.ValueMain = tank01Level.Display;
        Tank01Fig.ValueAccent = tank01Level.SigmaDisplay;
        Tank01Fig.ExtraContent = CapacityGauge(0.71, "71% capacity · σ from Type A");

        var tank02Level = snapshot.Site.TankFarm.Tank02.Level;
        Tank02Fig.ValueMain = tank02Level.Display;
        Tank02Fig.ValueAccent = tank02Level.SigmaDisplay;
        Tank02Fig.ExtraContent = CapacityGauge(0.49, "49% capacity · β=+14 declared");

        var berthFlow = snapshot.Site.BerthDelivery.Meter.Flow;
        BerthFlowFig.ValueMain = berthFlow.Display;
        BerthFlowFig.ValueAccent = berthFlow.SigmaDisplay;
        BerthFlowFig.ExtraContent = ErrorEllipseRow();
    }

    private static Control CapacityGauge(double fraction, string caption)
    {
        var track = new Grid
        {
            Height = 6,
            Background = Palette.BgField,
            ColumnDefinitions = new ColumnDefinitions($"{fraction:0.###}*,{1 - fraction:0.###}*")
        };
        var fill = new Rectangle { Fill = Palette.Green, Opacity = 0.75 };
        Grid.SetColumn(fill, 0);
        track.Children.Add(fill);

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(track);
        stack.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 9,
            Foreground = Palette.TextFaint
        });
        return stack;
    }

    private static Control ErrorEllipseRow()
    {
        var glyph = new Panel { Width = 52, Height = 34 };
        glyph.Children.Add(new Ellipse
        {
            Width = 44,
            Height = 16,
            Stroke = Palette.Amber,
            StrokeThickness = 1.2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new RotateTransform(-18)
        });
        glyph.Children.Add(new Ellipse
        {
            Width = 4,
            Height = 4,
            Fill = Palette.Amber,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(glyph);
        stack.Children.Add(new TextBlock
        {
            Text = "error ellipse — long axis = least-sure direction",
            FontSize = 9,
            Foreground = Palette.TextFaint
        });
        return stack;
    }
}
