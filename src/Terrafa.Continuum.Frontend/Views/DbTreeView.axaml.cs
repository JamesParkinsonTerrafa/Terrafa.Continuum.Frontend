using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Controls.Charts;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class DbTreeView : UserControl
{
    private sealed record NodeSlot(double X, double Y, double Width);

    private const double ObjectCardHeight = 52;

    private static readonly Dictionary<string, NodeSlot> Layout = new()
    {
        ["SITE_ALPHA"] = new NodeSlot(470, 40, 180),
        ["SITE_ALPHA.tank_farm"] = new NodeSlot(200, 180, 180),
        ["SITE_ALPHA.berth_delivery"] = new NodeSlot(706, 180, 190),
        ["SITE_ALPHA.tank_farm.tank_01"] = new NodeSlot(74, 316, 154),
        ["SITE_ALPHA.tank_farm.tank_02"] = new NodeSlot(270, 316, 153),
        ["SITE_ALPHA.tank_farm.tank_03"] = new NodeSlot(465, 316, 153),
        ["SITE_ALPHA.berth_delivery.pump_a"] = new NodeSlot(662, 316, 156),
        ["SITE_ALPHA.berth_delivery.meter"] = new NodeSlot(855, 316, 150),
        ["SITE_ALPHA.tank_farm.tank_01.level"] = new NodeSlot(44, 450, 174),
        ["SITE_ALPHA.tank_farm.tank_01.temp"] = new NodeSlot(44, 530, 174),
        ["SITE_ALPHA.tank_farm.tank_01.grade @ intake"] = new NodeSlot(44, 610, 174),
        ["SITE_ALPHA.tank_farm.tank_02.level"] = new NodeSlot(244, 450, 174),
        ["SITE_ALPHA.tank_farm.tank_02.temp"] = new NodeSlot(244, 530, 174),
        ["SITE_ALPHA.tank_farm.tank_03.level"] = new NodeSlot(442, 450, 174),
        ["SITE_ALPHA.tank_farm.tank_03.temp"] = new NodeSlot(442, 530, 174),
        ["SITE_ALPHA.berth_delivery.meter.flow"] = new NodeSlot(818, 450, 174)
    };

    private static readonly (string Left, string Right)[] AdjacencyPairs =
    [
        ("SITE_ALPHA.tank_farm.tank_01", "SITE_ALPHA.tank_farm.tank_02"),
        ("SITE_ALPHA.tank_farm.tank_02", "SITE_ALPHA.tank_farm.tank_03"),
        ("SITE_ALPHA.berth_delivery.pump_a", "SITE_ALPHA.berth_delivery.meter")
    ];

    public DbTreeView() : this(DemoData.CreateSnapshot(), _ => { })
    {
    }

    public DbTreeView(DataSnapshot snapshot, Action<int> navigate)
    {
        InitializeComponent();
        Tabs.TabSelected += navigate;

        BuildTreeNodes(snapshot.Tree);
        Edges.Edges = BuildEdges(snapshot.Tree);
        BuildEventLog(snapshot);
        BuildLegend();
    }

    private void BuildTreeNodes(DataTreeNode tree)
    {
        PlaceNode(tree, isRoot: true);
        foreach (var node in tree.Descendants())
            PlaceNode(node, isRoot: false);
    }

    private void PlaceNode(DataTreeNode node, bool isRoot)
    {
        if (!Layout.TryGetValue(node.Path, out var slot)) return;

        var card = new NodeCard
        {
            Width = slot.Width,
            TagText = isRoot ? "OBJECT · ROOT" : node.KindLabel,
            TagRight = node.IsNew ? "+NEW" : "",
            Title = BuildTitle(node),
            TitleSize = isRoot ? 13 : 12,
            Note = node.Kind == DataNodeKind.Measure ? BuildLeafNote(node.Reading!) : "",
            Variant = ResolveVariant(node)
        };
        Canvas.SetLeft(card, slot.X);
        Canvas.SetTop(card, slot.Y);
        TreeCanvas.Children.Add(card);
    }

    private static string BuildTitle(DataTreeNode node) =>
        node.Path == "SITE_ALPHA.berth_delivery.meter.flow" ? "flow @ meter" : node.Name;

    private static NodeCardVariant ResolveVariant(DataTreeNode node)
    {
        if (node.IsNew) return NodeCardVariant.NewNode;
        return node.Kind == DataNodeKind.Measure ? NodeCardVariant.Measure : NodeCardVariant.ObjectNode;
    }

    private static string BuildLeafNote(Measure reading)
    {
        if (reading.SigmaDisplay.Length == 0) return reading.Detail;
        var sigmaSuffix = reading.SigmaKind is "σ(x)" or "Σ aniso" ? $" · {reading.SigmaKind}" : "";
        return $"{reading.Display} {reading.SigmaDisplay}{sigmaSuffix}";
    }

    private static List<Edge> BuildEdges(DataTreeNode tree)
    {
        var edges = new List<Edge>();
        AppendContainmentEdges(tree, edges);

        foreach (var (leftPath, rightPath) in AdjacencyPairs)
        {
            if (!Layout.TryGetValue(leftPath, out var left) || !Layout.TryGetValue(rightPath, out var right))
                continue;
            var y = left.Y + 25;
            edges.Add(new Edge
            {
                From = new Point(left.X + left.Width, y),
                To = new Point(right.X, y),
                Stroke = Palette.Amber,
                Dashes = [5, 4]
            });
        }

        edges.Add(new Edge
        {
            From = new Point(218, 660),
            To = new Point(900, 516),
            BendControl1 = new Point(500, 760),
            BendControl2 = new Point(760, 640),
            Stroke = Palette.Purple,
            Dashes = [2, 4]
        });

        return edges;
    }

    private static void AppendContainmentEdges(DataTreeNode parent, List<Edge> edges)
    {
        foreach (var child in parent.Children)
        {
            if (Layout.TryGetValue(parent.Path, out var from) && Layout.TryGetValue(child.Path, out var to))
            {
                edges.Add(new Edge
                {
                    From = new Point(from.X + from.Width / 2, from.Y + ObjectCardHeight),
                    To = new Point(to.X + to.Width / 2, to.Y),
                    Stroke = Palette.TextGhost,
                    Thickness = child.Kind == DataNodeKind.Measure ? 1 : 1.5
                });
            }
            AppendContainmentEdges(child, edges);
        }
    }

    private void BuildEventLog(DataSnapshot snapshot)
    {
        foreach (var entry in snapshot.Events)
        {
            var idBrush = entry.Accent switch
            {
                "red" => Palette.Red,
                "green" => Palette.Green,
                _ => Palette.Cyan
            };

            var header = new TextBlock { FontSize = 10, LineHeight = 15 };
            header.Inlines =
            [
                new Run(entry.Time + " ") { Foreground = Palette.TextFaint },
                new Run(entry.Id + " ") { Foreground = idBrush },
                new Run(entry.Kind) { Foreground = Palette.Text }
            ];
            var detail = new TextBlock
            {
                Text = entry.Detail,
                FontSize = 10,
                LineHeight = 15,
                Foreground = Palette.TextMuted,
                TextWrapping = TextWrapping.Wrap
            };

            var body = new StackPanel();
            body.Children.Add(header);
            body.Children.Add(detail);

            EventRows.Children.Add(new Border
            {
                BorderBrush = Palette.RowSeparator,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 7),
                Child = body
            });
        }

        EventRows.Children.Add(new Border
        {
            Padding = new Thickness(12, 7),
            Child = new TextBlock
            {
                Text = $"… {snapshot.EventCount - snapshot.Events.Count:N0} earlier events retained",
                FontSize = 10,
                Foreground = Palette.TextGhost
            }
        });
    }

    private void BuildLegend()
    {
        LegendPanel.Children.Add(NetworkView.LegendRow(Palette.ObjectBorder, null, false, "OBJECT — parent, owns fields"));
        LegendPanel.Children.Add(NetworkView.LegendRow(Palette.Cyan, Palette.CyanFill, false, "MEASURE — leaf from a source"));
        LegendPanel.Children.Add(LineLegendRow(Palette.Amber, "ADJACENCY — meta-descriptor, not containment"));
        LegendPanel.Children.Add(LineLegendRow(Palette.Purple, "EQUALITY — same underlying thing"));
        LegendPanel.Children.Add(new TextBlock
        {
            Text = "no aggregates in the tree — count/max live in queries",
            FontSize = 9,
            Foreground = Palette.TextFaint,
            Margin = new Thickness(0, 2, 0, 0)
        });
    }

    private static Control LineLegendRow(IBrush stroke, string text)
    {
        var swatch = new Line
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(14, 0),
            Stroke = stroke,
            StrokeThickness = 1,
            StrokeDashArray = [2, 2],
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
}
