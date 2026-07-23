using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Controls.Diagram;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class NetworkView : UserControl
{
    private static readonly string[] FunctionLibrary =
        ["exp(θ·x)", "log(x)", "clip(x, lo, hi)", "sum(a, b)", "hazard λ₀(t)·exp(θᵀx)"];

    private sealed record RailRow(DataTreeNode Node, string LeafTitle, Border Shell, TextBlock CheckBlock, TextBlock NameBlock);

    private readonly Action<int> navigate;
    private readonly Dictionary<string, RailRow> railRows = [];
    private readonly Dictionary<string, DiagramNode> placedMeasures = [];
    private RailRow? railDrag;
    private Border? railGhost;

    public NetworkView() : this(DemoData.CreateSnapshot(), _ => { })
    {
    }

    public NetworkView(DataSnapshot snapshot, Action<int> navigate)
    {
        this.navigate = navigate;
        InitializeComponent();
        Tabs.TabSelected += navigate;

        FeedBadge.TimeText = snapshot.AsOf.ToString("dd-MMM-yyyy HH:mm:ss 'UTC'").ToUpperInvariant();
        AsOfText.Text = snapshot.AsOf.ToString("dd-MMM-yyyy HH:mm").ToUpperInvariant() + " ▸ LIVE";
        EventCountText.Text = $"EVENTS {snapshot.EventCount:N0} · APPEND-ONLY";

        Diagram.ConnectionStyle = (source, target) =>
            source.Card.Variant == NodeCardVariant.Measure
                ? (Palette.Cyan, null, 0.7)
                : target.Card.Variant == NodeCardVariant.Provisional
                    ? (Palette.Purple, [6, 5], 0.8)
                    : (Palette.Green, null, 0.8);
        Diagram.MenuProvider = BuildNodeMenu;

        BuildMeasureList(snapshot.Tree);
        SeedDiagram(snapshot.Tree);
        BuildLegend();

        PointerMoved += (_, e) => OnRailDragMoved(e);
        PointerReleased += (_, e) => OnRailDragReleased(e);

        NoiseOverlay.Attach(this);
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
                var row = new DockPanel();
                var sigma = new TextBlock
                {
                    Text = reading.SigmaKind,
                    FontSize = 11,
                    Foreground = Palette.TextFaint
                };
                DockPanel.SetDock(sigma, Dock.Right);
                row.Children.Add(sigma);

                var checkBlock = new TextBlock { Text = "[ ]", FontSize = 11 };
                var nameBlock = new TextBlock { Text = measure.Name, FontSize = 11 };
                var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                label.Children.Add(checkBlock);
                label.Children.Add(nameBlock);
                row.Children.Add(label);

                var shell = new Border
                {
                    Margin = new Thickness(22, 0, 0, 0),
                    Padding = new Thickness(4, 1),
                    Background = Brushes.Transparent,
                    Child = row
                };

                var railRow = new RailRow(measure, LeafTitle(measure), shell, checkBlock, nameBlock);
                railRows[measure.Path] = railRow;
                shell.PointerEntered += (_, _) => UpdateRailRow(railRow, hover: true);
                shell.PointerExited += (_, _) => UpdateRailRow(railRow, hover: false);
                shell.PointerPressed += (_, e) => BeginRailDrag(railRow, e);
                shell.PointerMoved += (_, e) => OnRailDragMoved(e);
                shell.PointerReleased += (_, e) => OnRailDragReleased(e);
                shell.PointerCaptureLost += (_, _) => CancelRailDrag();
                UpdateRailRow(railRow);
                MeasureList.Children.Add(shell);
            }
        }
    }

    private static string LeafTitle(DataTreeNode measure)
    {
        var segments = measure.Path.Split('.');
        return segments.Length >= 2 ? $"{segments[^2]}.{segments[^1]}" : measure.Path;
    }

    private static TextBlock RailHeader(string text, double indent) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = Palette.TextMuted,
        Margin = new Thickness(indent, 0, 0, 0)
    };

    private void SeedDiagram(DataTreeNode tree)
    {
        PlaceSeedMeasure(tree, "tank_farm.tank_01.level", new Point(70, 118));
        PlaceSeedMeasure(tree, "tank_farm.tank_02.level", new Point(70, 258));
        PlaceSeedMeasure(tree, "tank_farm.tank_01.temp", new Point(70, 418));
        PlaceSeedMeasure(tree, "tank_farm.tank_01.spoilage", new Point(70, 558));

        var transfer1 = Diagram.AddNode("transfer:t1", BuildTransfer1Card(), leftPort: true, rightPort: true, new Point(450, 172));
        var transfer2 = Diagram.AddNode("transfer:t2", BuildTransfer2Card(), leftPort: true, rightPort: true, new Point(450, 468));
        var figInventory = Diagram.AddNode("figure:total_inventory", BuildInventoryFigureCard(), leftPort: true, rightPort: false, new Point(866, 190));
        var figExpiry = Diagram.AddNode("figure:expiry_risk", BuildExpiryFigureCard(), leftPort: true, rightPort: false, new Point(866, 472));

        ConnectSeedMeasure(tree, "tank_farm.tank_01.level", transfer1);
        ConnectSeedMeasure(tree, "tank_farm.tank_02.level", transfer1);
        ConnectSeedMeasure(tree, "tank_farm.tank_01.temp", transfer2);
        ConnectSeedMeasure(tree, "tank_farm.tank_01.spoilage", transfer2);
        Diagram.Connect(transfer1, figInventory);
        Diagram.Connect(transfer2, figExpiry);
    }

    private void PlaceSeedMeasure(DataTreeNode tree, string relativePath, Point position)
    {
        if (railRows.TryGetValue($"{tree.Path}.{relativePath}", out var row)) PlaceMeasure(row, position);
    }

    private void ConnectSeedMeasure(DataTreeNode tree, string relativePath, DiagramNode target)
    {
        if (placedMeasures.TryGetValue($"{tree.Path}.{relativePath}", out var source)) Diagram.Connect(source, target);
    }

    private void PlaceMeasure(RailRow row, Point position)
    {
        var node = Diagram.AddNode(row.Node.Path, BuildLeafCard(row.LeafTitle, row.Node.Reading!), leftPort: false, rightPort: true, position);
        placedMeasures[row.Node.Path] = node;
        UpdateRailRow(row);
    }

    private void UpdateRailRow(RailRow row, bool hover = false)
    {
        var placed = placedMeasures.ContainsKey(row.Node.Path);
        var highlighted = hover && !placed;
        var brush = placed ? Palette.Cyan : highlighted ? Palette.TextSub : Palette.TextFaint;
        row.CheckBlock.Text = placed ? "[x]" : "[ ]";
        row.CheckBlock.Foreground = brush;
        row.NameBlock.Foreground = brush;
        row.Shell.Background = highlighted ? Palette.BgField : Brushes.Transparent;
        row.Shell.Cursor = new Cursor(placed ? StandardCursorType.Arrow : StandardCursorType.Hand);
    }

    private IReadOnlyList<(string Label, Action Action)> BuildNodeMenu(DiagramNode node) =>
        node.Card.Variant == NodeCardVariant.Transfer
            ?
            [
                ("CHANGE FUNCTION", () => CycleTransferFunction(node)),
                ("MODIFY FUNCTION", () => navigate(1)),
                ("REMOVE FROM DIAGRAM", () => RemoveDiagramNode(node))
            ]
            : [("REMOVE FROM DIAGRAM", () => RemoveDiagramNode(node))];

    private static void CycleTransferFunction(DiagramNode node)
    {
        var index = Array.IndexOf(FunctionLibrary, node.Card.Title);
        node.Card.Title = FunctionLibrary[(index + 1) % FunctionLibrary.Length];
    }

    private void RemoveDiagramNode(DiagramNode node)
    {
        Diagram.RemoveNode(node);
        if (placedMeasures.Remove(node.Id) && railRows.TryGetValue(node.Id, out var row)) UpdateRailRow(row);
    }

    private void BeginRailDrag(RailRow row, PointerPressedEventArgs e)
    {
        if (placedMeasures.ContainsKey(row.Node.Path)) return;
        if (!e.GetCurrentPoint(row.Shell).Properties.IsLeftButtonPressed) return;
        CancelRailDrag();
        railDrag = row;
        railGhost = BuildGhost(row.LeafTitle);
        GhostLayer.Children.Add(railGhost);
        PositionGhost(e.GetPosition(this));
        e.Pointer.Capture(row.Shell);
        e.Handled = true;
    }

    private void OnRailDragMoved(PointerEventArgs e)
    {
        if (railDrag is null) return;
        PositionGhost(e.GetPosition(this));
    }

    private void OnRailDragReleased(PointerReleasedEventArgs e)
    {
        if (railDrag is null) return;
        var row = railDrag;
        var dropPoint = e.GetPosition(Diagram);
        CancelRailDrag();

        if (dropPoint.X < 0 || dropPoint.Y < 0 ||
            dropPoint.X > Diagram.Bounds.Width || dropPoint.Y > Diagram.Bounds.Height) return;
        var worldPoint = Diagram.ViewportToWorld(dropPoint);
        PlaceMeasure(row, new Point(worldPoint.X - 110, worldPoint.Y - 40));
    }

    private void CancelRailDrag()
    {
        if (railGhost is not null)
        {
            GhostLayer.Children.Remove(railGhost);
            railGhost = null;
        }
        if (railDrag is not null)
        {
            var row = railDrag;
            railDrag = null;
            UpdateRailRow(row);
        }
    }

    private void PositionGhost(Point position)
    {
        if (railGhost is null) return;
        Canvas.SetLeft(railGhost, position.X + 10);
        Canvas.SetTop(railGhost, position.Y + 8);
    }

    private static Border BuildGhost(string leafTitle) => new()
    {
        BorderBrush = Palette.Cyan,
        BorderThickness = new Thickness(1),
        Background = Palette.CanvasNoteBackdrop,
        Padding = new Thickness(8, 4),
        Child = new TextBlock { Text = leafTitle, FontSize = 10, Foreground = Palette.Cyan }
    };

    private static NodeCard BuildLeafCard(string leafTitle, Measure reading) => new()
    {
        Variant = NodeCardVariant.Measure,
        TagText = "MEASURE · LEAF",
        Width = 220,
        Title = leafTitle,
        ValueMain = reading.Display,
        ValueAccent = reading.SigmaDisplay,
        Note = reading.Detail
    };

    private static NodeCard BuildTransfer1Card()
    {
        var extra = new TextBlock { FontSize = 9, LineHeight = 14, Foreground = Palette.TextMuted };
        extra.Inlines =
        [
            new Run("ν ≪ μ "),
            new Run("✓") { Foreground = Palette.Green },
            new Run(" · C¹ (linear) "),
            new Run("✓") { Foreground = Palette.Green },
            new LineBreak(),
            new Run("σ_out = √(J Σ Jᵀ) — exact")
        ];
        return new NodeCard
        {
            Variant = NodeCardVariant.Transfer,
            TagText = "TRANSFER · T1",
            TagRight = "dμ/dν",
            Title = "sum(level_01, level_02)",
            TitleSize = 12,
            Width = 250,
            ExtraContent = extra
        };
    }

    private static NodeCard BuildTransfer2Card()
    {
        var extra = new TextBlock { FontSize = 9, LineHeight = 14, Foreground = Palette.TextMuted };
        extra.Inlines =
        [
            new Run("ν ≪ μ "),
            new Run("✓") { Foreground = Palette.Green },
            new Run(" · "),
            new Run("NONLINEAR") { Foreground = Palette.Red },
            new Run(" — linearisation refused"),
            new LineBreak(),
            new Run("⚠ branch auto-switched to MONTE-CARLO σ") { Foreground = Palette.Amber }
        ];
        return new NodeCard
        {
            Variant = NodeCardVariant.Transfer,
            TagText = "TRANSFER · T2",
            TagRight = "dμ/dν",
            Title = "hazard λ₀(t)·exp(θᵀx)",
            TitleSize = 12,
            Width = 250,
            ExtraContent = extra
        };
    }

    private static NodeCard BuildInventoryFigureCard() => new()
    {
        Variant = NodeCardVariant.Figure,
        TagText = "DASHBOARD FIG",
        Title = "fig.total_inventory",
        ValueMain = "24,085 bbl",
        ValueAccent = "± 152",
        ValueSize = 16,
        Width = 270,
        Note = "σ composed up the tree · pinned to DASH & MAP"
    };

    private static NodeCard BuildExpiryFigureCard() => new()
    {
        Variant = NodeCardVariant.Provisional,
        TagText = "DASHBOARD FIG · PROVISIONAL",
        TagRight = "L4",
        Title = "fig.expiry_risk",
        ValueMain = "λ 0.031 /d",
        ValueAccent = "± 0.019",
        ValueSize = 16,
        Width = 270,
        Note = "⚠ under-determined — frailty Z not identifiable from these leaves. Drawn provisional, not asserted. Add prior or leaf to pin down."
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
}
