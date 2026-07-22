using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Controls.Charts;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class NetworkView : UserControl
{
    public NetworkView() : this(DemoData.CreateSnapshot(), _ => { })
    {
    }

    public NetworkView(DataSnapshot snapshot, Action<int> navigate)
    {
        InitializeComponent();
        Tabs.TabSelected += navigate;

        FeedBadge.TimeText = snapshot.AsOf.ToString("dd-MMM-yyyy HH:mm:ss 'UTC'").ToUpperInvariant();
        AsOfText.Text = snapshot.AsOf.ToString("dd-MMM-yyyy HH:mm").ToUpperInvariant() + " ▸ LIVE";
        EventCountText.Text = $"EVENTS {snapshot.EventCount:N0} · APPEND-ONLY";

        FillLeafCards(snapshot.Site);
        FigInventoryCard.ValueMain = "24,085 bbl";
        FigInventoryCard.ValueAccent = "± 152";
        FigExpiryCard.ValueMain = "λ 0.031 /d";
        FigExpiryCard.ValueAccent = "± 0.019";

        BuildMeasureList(snapshot.Tree);
        BuildLegend();
        Edges.Edges = BuildEdges();
    }

    private void FillLeafCards(SiteAlpha site)
    {
        Leaf1Card.Title = "tank_01.level";
        Leaf1Card.ValueMain = site.TankFarm.Tank01.Level.Display;
        Leaf1Card.ValueAccent = site.TankFarm.Tank01.Level.SigmaDisplay;
        Leaf1Card.Note = site.TankFarm.Tank01.Level.Detail;

        Leaf2Card.Title = "tank_02.level";
        Leaf2Card.ValueMain = site.TankFarm.Tank02.Level.Display;
        Leaf2Card.ValueAccent = site.TankFarm.Tank02.Level.SigmaDisplay;
        Leaf2Card.Note = site.TankFarm.Tank02.Level.Detail;

        Leaf3Card.Title = "tank_01.temp";
        Leaf3Card.ValueMain = site.TankFarm.Tank01.Temp.Display;
        Leaf3Card.ValueAccent = site.TankFarm.Tank01.Temp.SigmaDisplay;
        Leaf3Card.Note = site.TankFarm.Tank01.Temp.Detail;

        Leaf4Card.Title = "tank_01.spoilage";
        Leaf4Card.ValueMain = site.TankFarm.Tank01.Spoilage.Display;
        Leaf4Card.ValueAccent = site.TankFarm.Tank01.Spoilage.SigmaDisplay;
        Leaf4Card.Note = site.TankFarm.Tank01.Spoilage.Detail;
    }

    private void BuildMeasureList(DataTreeNode tree)
    {
        MeasureList.Children.Add(RailHeader($"{tree.Name.ToLowerInvariant()} /", 0));

        foreach (var objectNode in tree.Descendants().Where(node =>
                     node.Kind == DataNodeKind.Object &&
                     node.Children.Any(child => child.Kind == DataNodeKind.Measure)))
        {
            var relativePath = objectNode.Path[(tree.Path.Length + 1)..].Replace(".", " / ");
            MeasureList.Children.Add(RailHeader($"{relativePath} /", 12));

            foreach (var measure in objectNode.Children.Where(child => child.Kind == DataNodeKind.Measure))
            {
                var reading = measure.Reading!;
                var row = new DockPanel { Margin = new Thickness(26, 0, 0, 0) };
                var sigma = new TextBlock
                {
                    Text = reading.SigmaKind,
                    FontSize = 11,
                    Foreground = Palette.TextFaint
                };
                DockPanel.SetDock(sigma, Dock.Right);
                row.Children.Add(sigma);

                var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var rowBrush = reading.Selected ? Palette.Cyan : Palette.TextFaint;
                label.Children.Add(new TextBlock
                {
                    Text = reading.Selected ? "[x]" : "[ ]",
                    FontSize = 11,
                    Foreground = rowBrush
                });
                label.Children.Add(new TextBlock
                {
                    Text = measure.Name,
                    FontSize = 11,
                    Foreground = rowBrush
                });
                row.Children.Add(label);
                MeasureList.Children.Add(row);
            }
        }
    }

    private static TextBlock RailHeader(string text, double indent) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = Palette.TextMuted,
        Margin = new Thickness(indent, 0, 0, 0)
    };

    private void BuildLegend()
    {
        LegendPanel.Children.Add(LegendRow(Palette.Cyan, Palette.CyanFill, false, "MEASURE — leaf, emission p(y|x)"));
        LegendPanel.Children.Add(LegendRow(Palette.Amber, Palette.AmberFill, false, "TRANSFER — density dμ/dν"));
        LegendPanel.Children.Add(LegendRow(Palette.Green, Palette.GreenFill, false, "FIGURE — projection E[X|𝒢]"));
        LegendPanel.Children.Add(LegendRow(Palette.Purple, null, true, "PROVISIONAL — under-determined"));
    }

    public static Control LegendRow(IBrush stroke, IBrush? fill, bool dashed, string text)
    {
        var swatch = new Rectangle
        {
            Width = 10,
            Height = 10,
            Stroke = stroke,
            StrokeThickness = 1,
            Fill = fill ?? Brushes.Transparent,
            StrokeDashArray = dashed ? [2, 2] : null,
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(swatch);
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 9,
            LetterSpacing = 0.5,
            Foreground = Palette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }

    private static List<Edge> BuildEdges() =>
    [
        new() { From = new Point(290, 152), To = new Point(450, 212), Stroke = Palette.Cyan, Opacity = 0.7, ArrowAtEnd = true },
        new() { From = new Point(290, 292), To = new Point(450, 242), Stroke = Palette.Cyan, Opacity = 0.7, ArrowAtEnd = true },
        new() { From = new Point(290, 452), To = new Point(450, 512), Stroke = Palette.Cyan, Opacity = 0.7, ArrowAtEnd = true },
        new() { From = new Point(290, 592), To = new Point(450, 542), Stroke = Palette.Cyan, Opacity = 0.7, ArrowAtEnd = true },
        new() { From = new Point(700, 227), To = new Point(866, 235), Stroke = Palette.Green, Opacity = 0.8, ArrowAtEnd = true },
        new() { From = new Point(700, 527), To = new Point(866, 520), Stroke = Palette.Purple, Opacity = 0.8, Dashes = [6, 5], ArrowAtEnd = true }
    ];
}
