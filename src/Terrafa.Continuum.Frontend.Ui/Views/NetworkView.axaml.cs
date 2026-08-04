// Copyright (c) 2026 Terrafa Limited. All rights reserved.

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
    private sealed record RailRow(
        DataTreeNode Node,
        MountedSubtree Subtree,
        string LeafTitle,
        Border Shell,
        TextBlock CheckBlock,
        TextBlock NameBlock);

    /// <summary>A drag out of the left rail, and what to do where it lands on the canvas.</summary>
    private sealed record RailDrag(string Label, IBrush Accent, Action<Point> Drop);

    private readonly Action<int> navigate;
    private readonly Workspace workspace = Workspace.Instance;
    private readonly NetworkGraph graph = NetworkGraph.Instance;
    private readonly Dictionary<string, RailRow> railRows = [];
    private readonly Dictionary<string, DiagramNode> placed = [];
    private readonly Dictionary<string, string> cardObjections = [];
    private readonly HashSet<string> collapsedSubtrees = [];
    private readonly HashSet<string> canvasHoverPaths = [];
    private RailDrag? railDrag;
    private Border? railGhost;

    public NetworkView() : this(DemoContent.Create(), _ => { })
    {
    }

    public NetworkView(DemoContent snapshot, Action<int> navigate)
    {
        this.navigate = navigate;
        InitializeComponent();
        Tabs.TabSelected += navigate;

        AsOfText.Text = snapshot.AsOf.ToString("dd-MMM-yyyy HH:mm").ToUpperInvariant() + " ▸ LIVE";
        EventCountText.Text = $"EVENTS {snapshot.EventCount:N0} · APPEND-ONLY";

        Diagram.ConnectionStyle = (source, target) => graph.PortOf(source.Id, target.Id) switch
        {
            NetworkGraph.EstimatorPortX or NetworkGraph.EstimatorPortY => (Palette.Cyan, [4, 4], 0.85),
            NetworkGraph.EstimatorPortPredict => (Palette.Green, null, 0.85),
            NetworkGraph.ComparePortA or NetworkGraph.ComparePortB => (Palette.Amber, [4, 4], 0.85),
            _ => source.Card.Variant == NodeCardVariant.Measure
                ? (source.Card.AccentOverride ?? Palette.Cyan, null, 0.7)
                : target.Card.Variant == NodeCardVariant.Provisional
                    ? (Palette.Purple, [6, 5], 0.8)
                    : (Palette.Green, null, 0.8)
        };
        Diagram.MenuProvider = BuildNodeMenu;
        Diagram.CanConnect = (source, target) => graph.CanConnect(source.Id, target.Id);
        Diagram.Connected += OnConnected;
        Diagram.NodeMoved += OnNodeMoved;
        Diagram.NodeHoverChanged += OnDiagramNodeHover;

        BuildBuildList();
        Render();
        BuildLegend();

        PointerMoved += (_, e) => OnRailDragMoved(e);
        PointerReleased += (_, e) => OnRailDragReleased(e);

        NoiseOverlay.Attach(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SnapSettings.Changed += SyncSnap;
        // Snap may have been switched on while this screen was off show.
        SyncSnap();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        SnapSettings.Changed -= SyncSnap;
    }

    /// <summary>
    /// Going from free placement to the grid locks every node to its nearest gridline, the same
    /// contract as the dashboard. A no-op when everything already sits on the grid.
    /// </summary>
    private void SyncSnap()
    {
        if (!SnapSettings.Enabled) return;
        var moved = false;
        foreach (var node in graph.Nodes)
        {
            var x = SnapSettings.Snap(node.X);
            var y = SnapSettings.Snap(node.Y);
            if (x == node.X && y == node.Y) continue;
            graph.Move(node.Id, x, y);
            moved = true;
        }
        if (moved) Render();
    }

    // ── canvas ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws whatever the graph holds. Everything that changes the network goes through the model
    /// and comes back through here, so the canvas can never hold a wire the figures were not
    /// computed from.
    /// </summary>
    private void Render()
    {
        Diagram.Clear();
        placed.Clear();
        canvasHoverPaths.Clear();

        // The checker's findings, gathered once per render and read while the cards build — a
        // card must state its objection in the same pass that draws the wire it objects to.
        cardObjections.Clear();
        foreach (var group in NetworkChecker.Check(graph).GroupBy(objection => objection.NodeId))
            cardObjections[group.Key] = string.Join(" · ", group.Select(objection => objection.Message));

        foreach (var node in graph.Nodes)
        {
            placed[node.Id] = Diagram.AddNode(
                node.Id,
                BuildCard(node),
                leftPort: node.Kind != NetworkNodeKind.Measure,
                rightPort: node.Kind is not (NetworkNodeKind.Figure or NetworkNodeKind.Table),
                new Point(node.X, node.Y));
        }

        foreach (var edge in graph.Edges)
        {
            if (placed.TryGetValue(edge.FromId, out var from) && placed.TryGetValue(edge.ToId, out var to))
                Diagram.Connect(from, to);
        }

        BuildMeasureList();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var figures = FigureCatalog.Instance.Figures;
        var derived = figures.Count(figure => figure.Origin == FigureOrigin.Derived && figure.HasValue);
        FigureCountText.Text = $"{figures.Count} DASHBOARD FIG(S) · {derived} COMPUTED FROM THE TREE";
    }

    private void OnConnected(DiagramNode source, DiagramNode target)
    {
        graph.Connect(source.Id, target.Id);
        Render();
    }

    private void OnNodeMoved(DiagramNode node)
    {
        var position = Diagram.NodePositionOf(node);
        graph.Move(node.Id, position.X, position.Y);
    }

    /// <summary>Ten grid cells — every card on this canvas is the same width.</summary>
    private const double CardWidth = SnapSettings.GridSize * 10;

    private NodeCard BuildCard(NetworkNode node)
    {
        var card = node.Kind switch
        {
            NetworkNodeKind.Measure => BuildLeafCard(node),
            NetworkNodeKind.Figure => BuildFigureCard(node),
            NetworkNodeKind.Compare => BuildComparatorCard(node),
            NetworkNodeKind.Select => BuildSelectCard(node),
            NetworkNodeKind.Table => BuildTableCard(node),
            _ => BuildTransferCard(node)
        };
        // One size, in whole grid units, and opaque against the canvas — with a snapped top-left
        // that keeps every edge of every card on a gridline, with no gridline showing through.
        // Heights step in double cells, so cards grow in fewer, more deliberate increments.
        card.Width = CardWidth;
        card.GridHeight = SnapSettings.GridSize * 2;
        card.Backdrop = Palette.BgDeep;
        return card;
    }

    private NodeCard BuildLeafCard(NetworkNode node)
    {
        var subtree = workspace.SubtreeOf(node.Key);
        var reading = Workspace.ReadingAt(node.Key);
        var accentIndex = subtree?.AccentIndex ?? 0;

        return new NodeCard
        {
            Variant = NodeCardVariant.Measure,
            TagText = "MEASURE · LEAF",
            TagRight = subtree?.Dataset.ToLowerInvariant() ?? "",
            Title = NetworkGraph.LeafTitle(node.Key),
            ValueMain = reading?.Display ?? "—",
            ValueAccent = reading?.SigmaDisplay ?? "",
            Note = reading?.Detail ?? "no value read for this leaf",
            AccentOverride = SubtreeAccents.Stroke(accentIndex),
            FillOverride = SubtreeAccents.Fill(accentIndex)
        };
    }

    /// <summary>
    /// A transfer states what it did to its inputs and what that cost in σ. The seeded hazard states
    /// instead that it will not do it — its branch is not identifiable from these leaves, and a
    /// number there would be the assertion the whole screen exists to refuse.
    /// </summary>
    private NodeCard BuildTransferCard(NetworkNode node)
    {
        var tag = node.Id.StartsWith("transfer:", StringComparison.Ordinal)
            ? node.Id["transfer:".Length..].ToUpperInvariant()
            : node.Id.ToUpperInvariant();

        if (node.IsOpaque) return BuildOpaqueTransferCard(node, tag);
        if (node.IsEstimator) return BuildEstimatorCard(node, tag);

        var result = graph.Evaluate(node);
        return new NodeCard
        {
            Variant = NodeCardVariant.Transfer,
            TagText = $"TRANSFER · {tag}",
            TagRight = "dν/dµ",
            Title = graph.Title(node),
            TitleSize = 12,
            ValueMain = result is null ? "" : $"{MeasureNumerics.Format(result.Value)} {result.Unit}".Trim(),
            ValueAccent = result is { } value && value.HasVariance
                ? $"± {MeasureNumerics.FormatSigma(value.Sigma)}"
                : "",
            ExtraContent = TransferExtra(
                result, graph.InputsOf(node.Id).Count(), cardObjections.GetValueOrDefault(node.Id))
        };
    }

    /// <summary>
    /// A comparator states its determination and how many σ it holds by. The determination is the
    /// headline; the σ level is the accent, in the slot a transfer uses for its ±σ — firmness
    /// where variance would sit, which is exactly the trade the card is making.
    /// </summary>
    private NodeCard BuildComparatorCard(NetworkNode node)
    {
        var tag = node.Id.StartsWith("compare:", StringComparison.Ordinal)
            ? node.Id["compare:".Length..].ToUpperInvariant()
            : node.Id.ToUpperInvariant();

        var result = graph.Evaluate(node);
        return new NodeCard
        {
            Variant = NodeCardVariant.Transfer,
            TagText = $"COMPARATOR · {tag}",
            TagRight = "a ▸ b",
            Title = graph.Title(node),
            TitleSize = 12,
            ValueMain = result is null ? "" : MeasureNumerics.FormatBoolean(result.Value),
            ValueAccent = result is null ? "" : MeasureNumerics.FormatSigmaLevel(result.SigmaLevel),
            ExtraContent = ComparatorExtra(node, result)
        };
    }

    private Control ComparatorExtra(NetworkNode node, TransferResult? result)
    {
        var extra = new TextBlock
        {
            FontSize = TypographySettings.Size(9),
            LineHeight = TypographySettings.Size(14),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextMuted
        };

        var inlines = new List<Inline>();
        foreach (var port in NetworkGraph.ComparePorts)
        {
            var source = graph.SourceTitleOnPort(node, port);
            inlines.Add(new Run($"{port} ← ") { Foreground = Palette.TextFaint });
            inlines.Add(source is null
                ? new Run("—") { Foreground = Palette.Amber }
                : new Run(source) { Foreground = Palette.Amber });
            inlines.Add(new LineBreak());
        }

        var checkerObjection = cardObjections.GetValueOrDefault(node.Id);
        if (result is null)
        {
            // The checker's finding is the likelier explanation when evaluation declined — a
            // cross-table comparison — so it takes the slot rather than being appended twice.
            // And a cross-table comparator that feeds a SELECT is not objected to at all: the
            // join is its row order, and the card says where its answer lives.
            var objection = TransferMath.ComparisonObjection(
                graph.InputOnPort(node, NetworkGraph.ComparePortA),
                graph.InputOnPort(node, NetworkGraph.ComparePortB));
            var outgoing = graph.Edges.Where(edge => edge.FromId == node.Id).ToList();
            var feedsSelect = outgoing.Count > 0 &&
                outgoing.All(edge => graph.Find(edge.ToId) is { Kind: NetworkNodeKind.Select });

            if (objection is null && checkerObjection is null && feedsSelect)
                inlines.Add(new Run("cross-table — evaluated per joined row by the SELECT it feeds"));
            else
                inlines.Add(new Run(objection ?? checkerObjection ?? "operator missing from the library")
                {
                    Foreground = Palette.Amber
                });
        }
        else
        {
            inlines.Add(new Run(result.Note));
            if (checkerObjection is not null)
            {
                inlines.Add(new LineBreak());
                inlines.Add(new Run($"⚠ {checkerObjection}") { Foreground = Palette.Amber });
            }
        }

        extra.Inlines = [.. inlines];
        return extra;
    }

    private static NodeCard BuildOpaqueTransferCard(NetworkNode node, string tag)
    {
        var extra = new TextBlock { FontSize = TypographySettings.Size(9), LineHeight = TypographySettings.Size(14), Foreground = Palette.TextMuted };
        extra.Inlines =
        [
            new Run("ν ≪ µ "),
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
            TagText = $"TRANSFER · {tag}",
            TagRight = "dν/dµ",
            Title = node.OpaqueTitle,
            TitleSize = 12,
            ExtraContent = extra
        };
    }

    private NodeCard BuildEstimatorCard(NetworkNode node, string tag)
    {
        var result = graph.Evaluate(node);
        return new NodeCard
        {
            Variant = NodeCardVariant.Transfer,
            TagText = $"REGRESSOR · {tag}",
            TagRight = "fit ▸ predict",
            Title = graph.Title(node),
            TitleSize = 12,
            ValueMain = result is null || double.IsNaN(result.Value)
                ? ""
                : $"{MeasureNumerics.Format(result.Value)} {result.Unit}".Trim(),
            AccentOverride = Palette.Cyan,
            FillOverride = Palette.CyanFill,
            ExtraContent = EstimatorExtra(node, result)
        };
    }

    private Control EstimatorExtra(NetworkNode node, TransferResult? result)
    {
        var extra = new TextBlock
        {
            FontSize = TypographySettings.Size(9),
            LineHeight = TypographySettings.Size(14),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextMuted
        };

        var inlines = new List<Inline>();
        foreach (var port in NetworkGraph.EstimatorPorts)
        {
            var source = graph.SourceTitleOnPort(node, port);
            var label = port switch
            {
                NetworkGraph.EstimatorPortX => "train x[]",
                NetworkGraph.EstimatorPortY => "train y[]",
                _ => "predict"
            };
            inlines.Add(new Run($"{label} ← ") { Foreground = Palette.TextFaint });
            inlines.Add(source is null
                ? new Run("—") { Foreground = Palette.Amber }
                : new Run(source)
                {
                    Foreground = port == NetworkGraph.EstimatorPortPredict ? Palette.Green : Palette.Cyan
                });
            inlines.Add(new LineBreak());
        }

        if (result is null)
        {
            var objection = TransferMath.EstimatorObjection(
                graph.InputOnPort(node, NetworkGraph.EstimatorPortX),
                graph.InputOnPort(node, NetworkGraph.EstimatorPortY),
                graph.InputOnPort(node, NetworkGraph.EstimatorPortPredict));
            inlines.Add(new Run(objection ?? "estimator missing from the library") { Foreground = Palette.Amber });
        }
        else
        {
            inlines.Add(new Run(result.Note));
        }

        extra.Inlines = [.. inlines];
        return extra;
    }

    private static Control TransferExtra(TransferResult? result, int inputCount, string? objection)
    {
        var extra = new TextBlock
        {
            FontSize = TypographySettings.Size(9),
            LineHeight = TypographySettings.Size(14),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextMuted
        };

        var inlines = new List<Inline>();
        if (result is null)
        {
            inlines.Add(new Run(inputCount == 0
                ? "no inputs — wire a leaf into the left port"
                : "inputs carry nothing this transfer can push")
            {
                Foreground = Palette.Amber
            });
        }
        else
        {
            inlines.Add(new Run("ν ≪ µ "));
            inlines.Add(new Run("✓") { Foreground = Palette.Green });
            inlines.Add(new Run(" · "));
            inlines.Add(result.Linearised
                ? new Run("C¹ (linear) ✓") { Foreground = Palette.Green }
                : new Run("NONLINEAR") { Foreground = Palette.Red });
            inlines.Add(new LineBreak());
            inlines.Add(new Run(result.Note));
        }

        if (objection is not null)
        {
            inlines.Add(new LineBreak());
            inlines.Add(new Run($"⚠ {objection}") { Foreground = Palette.Amber });
        }

        extra.Inlines = [.. inlines];
        return extra;
    }

    private NodeCard BuildSelectCard(NetworkNode node)
    {
        var tag = node.Id.StartsWith("select:", StringComparison.Ordinal)
            ? node.Id["select:".Length..].ToUpperInvariant()
            : node.Id.ToUpperInvariant();

        var table = graph.EvaluateSelect(node);
        return new NodeCard
        {
            Variant = NodeCardVariant.Transfer,
            TagText = $"SELECT · {tag}",
            TagRight = "rows",
            Title = graph.Title(node),
            TitleSize = 12,
            ValueMain = table.HasRows ? table.StateNote : "",
            ExtraContent = SelectExtra(node, table)
        };
    }

    private Control SelectExtra(NetworkNode node, DerivedTable table)
    {
        var extra = new TextBlock
        {
            FontSize = TypographySettings.Size(9),
            LineHeight = TypographySettings.Size(14),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextMuted
        };

        var inlines = new List<Inline>
        {
            // The note carries the whole story either way — counts when it evaluates, and the
            // objection (two tables, no cells, nothing wired) when it does not.
            new Run(table.Note) { Foreground = table.HasRows ? Palette.TextMuted : Palette.Amber }
        };

        if (cardObjections.GetValueOrDefault(node.Id) is { } checkerObjection)
        {
            inlines.Add(new LineBreak());
            inlines.Add(new Run($"⚠ {checkerObjection}") { Foreground = Palette.Amber });
        }

        extra.Inlines = [.. inlines];
        return extra;
    }

    private static NodeCard BuildTableCard(NetworkNode node)
    {
        var table = TableCatalog.Instance.Find(node.Key);
        if (table is null)
        {
            return new NodeCard
            {
                Variant = NodeCardVariant.Provisional,
                TagText = "DASHBOARD TABLE · MISSING",
                Title = $"tbl.{node.Key}",
                Note = "not in the table catalogue"
            };
        }

        var empty = !table.HasRows;
        return new NodeCard
        {
            Variant = empty ? NodeCardVariant.Provisional : NodeCardVariant.Figure,
            TagText = empty ? "DASHBOARD TABLE · EMPTY" : "DASHBOARD TABLE",
            TagRight = empty ? "" : "committed",
            Title = table.Name,
            ValueMain = empty ? "—" : table.StateNote,
            ValueSize = 16,
            Note = table.Note
        };
    }

    /// <summary>
    /// Figure cards read from <see cref="FigureCatalog"/> rather than restating their own values —
    /// the dashboard offers the same figures as tile sources, and the two screens have to agree.
    /// </summary>
    private static NodeCard BuildFigureCard(NetworkNode node)
    {
        var figure = FigureCatalog.Instance.Find(node.Key);
        if (figure is null)
        {
            return new NodeCard
            {
                Variant = NodeCardVariant.Provisional,
                TagText = "DASHBOARD FIG · MISSING",
                Title = $"fig.{node.Key}",
                Note = "not in the figure catalogue"
            };
        }

        var unwired = !figure.HasValue;
        return new NodeCard
        {
            Variant = figure.IsProvisional || unwired ? NodeCardVariant.Provisional : NodeCardVariant.Figure,
            TagText = unwired
                ? "DASHBOARD FIG · UNWIRED"
                : figure.IsProvisional
                    ? "DASHBOARD FIG · PROVISIONAL"
                    : "DASHBOARD FIG",
            TagRight = figure.Origin == FigureOrigin.Derived && !unwired ? "computed" : "",
            Title = figure.Name,
            ValueMain = figure.Display,
            ValueAccent = figure.SigmaDisplay,
            ValueSize = 16,
            Note = figure.Note
        };
    }

    // ── node menus ───────────────────────────────────────────────────────────────────────────

    private IReadOnlyList<(string Label, Action Action)> BuildNodeMenu(DiagramNode node)
    {
        if (graph.Find(node.Id) is not { } model) return [("REMOVE FROM DIAGRAM", () => RemoveNode(node.Id))];

        return model.Kind switch
        {
            NetworkNodeKind.Measure =>
            [
                ("PUBLISH AS DASHBOARD FIG", () => ShowFigureDialog(model.X + 300, model.Y, model.Id)),
                ("REMOVE FROM DIAGRAM", () => RemoveNode(node.Id))
            ],
            NetworkNodeKind.Figure =>
            [
                ("CLEAR INPUTS", () => ClearInputs(node.Id)),
                ("REMOVE FROM DIAGRAM", () => RemoveNode(node.Id))
            ],
            NetworkNodeKind.Compare =>
            [
                ("CHANGE OPERATOR", () => Mutate(() => graph.CycleOperator(model))),
                ("SWAP WIRES a ↔ b", () => Mutate(() => graph.SwapCompareWires(model))),
                ("CLEAR INPUTS", () => ClearInputs(node.Id)),
                ("REMOVE FROM DIAGRAM", () => RemoveNode(node.Id))
            ],
            NetworkNodeKind.Select =>
            [
                ("COMMIT AS DASHBOARD TABLE", () => ShowTableDialog(model.X + 300, model.Y, model.Id)),
                ("CLEAR INPUTS", () => ClearInputs(node.Id)),
                ("REMOVE FROM DIAGRAM", () => RemoveNode(node.Id))
            ],
            NetworkNodeKind.Table =>
            [
                ("CLEAR INPUTS", () => ClearInputs(node.Id)),
                ("REMOVE FROM DIAGRAM", () => RemoveNode(node.Id))
            ],
            _ when model.IsOpaque =>
            [
                ("MODIFY FUNCTION", () => navigate(1)),
                ("CLEAR INPUTS", () => ClearInputs(node.Id)),
                ("REMOVE FROM DIAGRAM", () => RemoveNode(node.Id))
            ],
            _ when model.IsEstimator =>
            [
                ("SWAP TRAINING WIRES x[] ↔ y[]", () => Mutate(() => graph.SwapTrainingWires(model))),
                ("ROTATE PORT ROLES", () => Mutate(() => graph.RotatePortRoles(model))),
                ("MODIFY FUNCTION", () => navigate(1)),
                ("CLEAR INPUTS", () => ClearInputs(node.Id)),
                ("REMOVE FROM DIAGRAM", () => RemoveNode(node.Id))
            ],
            _ =>
            [
                ("CHANGE FUNCTION", () => Mutate(() => graph.CycleStage(model))),
                ("CHANGE COMBINER", () => Mutate(() => graph.CycleCombiner(model))),
                ("MODIFY FUNCTION", () => navigate(1)),
                ("CLEAR INPUTS", () => ClearInputs(node.Id)),
                ("REMOVE FROM DIAGRAM", () => RemoveNode(node.Id))
            ]
        };
    }

    private void Mutate(Action change)
    {
        change();
        Render();
    }

    private void ClearInputs(string id) => Mutate(() => graph.ClearInputs(id));

    private void RemoveNode(string id) => Mutate(() => graph.Remove(id));

    // ── left rail ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The blocks the network is built from. A leaf below is data the tree already holds; these two
    /// are the operator's own — a transfer to combine leaves, and the figure that commits the result
    /// to the dashboard.
    /// </summary>
    private void BuildBuildList()
    {
        BuildList.Children.Clear();
        BuildList.Children.Add(RailHeader("build /", 0, Palette.Amber));

        BuildList.Children.Add(BuildElementRow(
            "TRANSFER",
            "combines its inputs and carries their σ",
            Palette.Amber,
            point => Mutate(() => graph.AddTransfer(point.X - 125, point.Y - 40))));

        BuildList.Children.Add(BuildElementRow(
            "REGRESSOR",
            "fits y[] on x[] from two wired series · predicts a third input · refits on every recompute",
            Palette.Cyan,
            point => Mutate(() => graph.AddEstimator("fit_linear", point.X - 125, point.Y - 40))));

        BuildList.Children.Add(BuildElementRow(
            "COMPARATOR",
            "tests a against b · states the determination and how many σ it holds by",
            Palette.Amber,
            point => Mutate(() => graph.AddComparator(point.X - 125, point.Y - 40))));

        BuildList.Children.Add(BuildElementRow(
            "SELECT",
            "gathers columns into rows · one table for now — cross-table matches arrive with ≡ links",
            Palette.Amber,
            point => Mutate(() => graph.AddSelect(point.X - 125, point.Y - 40))));

        BuildList.Children.Add(BuildElementRow(
            "DASHBOARD FIG",
            "commits a value the dashboard can plot",
            Palette.Green,
            point => ShowFigureDialog(point.X - 135, point.Y - 40, wireFrom: null)));

        BuildList.Children.Add(BuildElementRow(
            "DASHBOARD TABLE",
            "commits a select's rows the dashboard can grid",
            Palette.Green,
            point => ShowTableDialog(point.X - 135, point.Y - 40, wireFrom: null)));
    }

    private Border BuildElementRow(string label, string detail, IBrush accent, Action<Point> drop)
    {
        var body = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = label, FontSize = TypographySettings.Size(10), LetterSpacing = 1, Foreground = accent },
                new TextBlock
                {
                    Text = detail,
                    FontSize = TypographySettings.Size(9),
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Palette.TextFaint
                }
            }
        };

        var shell = new Border
        {
            Margin = new Thickness(10, 0, 0, 0),
            BorderBrush = Palette.TextGhost,
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
            Padding = new Thickness(9, 7),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = body
        };
        shell.PointerEntered += (_, _) => shell.Background = Palette.BgField;
        shell.PointerExited += (_, _) => shell.Background = Brushes.Transparent;
        shell.PointerPressed += (_, e) => BeginRailDrag(new RailDrag(label, accent, drop), shell, e);
        shell.PointerMoved += (_, e) => OnRailDragMoved(e);
        shell.PointerReleased += (_, e) => OnRailDragReleased(e);
        shell.PointerCaptureLost += (_, _) => CancelRailDrag();
        return shell;
    }

    private void BuildMeasureList()
    {
        MeasureList.Children.Clear();
        railRows.Clear();

        foreach (var subtree in workspace.Subtrees)
        {
            MeasureList.Children.Add(SubtreeHeader(subtree));
            if (collapsedSubtrees.Contains(subtree.Dataset)) continue;

            foreach (var objectNode in new[] { subtree.Root }.Concat(subtree.Root.Descendants()).Where(node =>
                         node.Kind == DataNodeKind.Object &&
                         node.Children.Any(child => child.Kind == DataNodeKind.Measure)))
            {
                if (objectNode != subtree.Root)
                {
                    var relativePath = objectNode.Path[(subtree.Root.Path.Length + 1)..].Replace(".", " / ");
                    MeasureList.Children.Add(RailHeader($"{relativePath} /", 12, Palette.TextMuted));
                }

                foreach (var measure in objectNode.Children.Where(child => child.Kind == DataNodeKind.Measure))
                    MeasureList.Children.Add(BuildRailRow(measure, subtree));
            }
        }

        if (workspace.Subtrees.Count != 0) return;
        MeasureList.Children.Add(new TextBlock
        {
            Text = "nothing mounted — open 6) DATA SOURCES",
            FontSize = TypographySettings.Size(11),
            Foreground = Palette.TextFaint
        });
    }

    private Control BuildRailRow(DataTreeNode measure, MountedSubtree subtree)
    {
        var reading = measure.Reading!;
        var row = new DockPanel();
        var sigma = new TextBlock
        {
            Text = reading.SigmaKind,
            FontSize = TypographySettings.Size(11),
            Foreground = Palette.TextFaint
        };
        DockPanel.SetDock(sigma, Dock.Right);
        row.Children.Add(sigma);

        var checkBlock = new TextBlock { Text = "[ ]", FontSize = TypographySettings.Size(11) };
        var nameBlock = new TextBlock { Text = measure.Name, FontSize = TypographySettings.Size(11) };
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

        var railRow = new RailRow(
            measure, subtree, NetworkGraph.LeafTitle(measure.Path), shell, checkBlock, nameBlock);
        railRows[measure.Path] = railRow;
        shell.PointerEntered += (_, _) =>
        {
            UpdateRailRow(railRow, hover: true);
            SetNodeHighlight(railRow, true);
        };
        shell.PointerExited += (_, _) =>
        {
            UpdateRailRow(railRow, hover: false);
            SetNodeHighlight(railRow, false);
        };
        shell.PointerPressed += (_, e) => BeginMeasureDrag(railRow, e);
        shell.PointerMoved += (_, e) => OnRailDragMoved(e);
        shell.PointerReleased += (_, e) => OnRailDragReleased(e);
        shell.PointerCaptureLost += (_, _) => CancelRailDrag();
        UpdateRailRow(railRow);
        return shell;
    }

    private Control SubtreeHeader(MountedSubtree subtree)
    {
        var collapsed = collapsedSubtrees.Contains(subtree.Dataset);
        var accent = SubtreeAccents.Stroke(subtree.AccentIndex);

        var caret = new TextBlock
        {
            Text = collapsed ? "▸" : "▾",
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        var marker = new Rectangle
        {
            Width = 8,
            Height = 8,
            Fill = accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        var name = new TextBlock
        {
            Text = $"{subtree.Dataset.ToLowerInvariant()} /",
            FontSize = TypographySettings.Size(11),
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        var count = new TextBlock
        {
            Text = $"{subtree.Leaves.Count(leaf => graph.Contains(leaf.Path))} placed",
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        left.Children.Add(caret);
        left.Children.Add(marker);
        left.Children.Add(name);

        var row = new DockPanel();
        DockPanel.SetDock(count, Dock.Right);
        row.Children.Add(count);
        row.Children.Add(left);

        var shell = new Border
        {
            Margin = new Thickness(0, 6, 0, 2),
            Padding = new Thickness(2, 2),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row
        };
        shell.PointerEntered += (_, _) => shell.Background = Palette.BgField;
        shell.PointerExited += (_, _) => shell.Background = Brushes.Transparent;
        shell.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (!collapsedSubtrees.Remove(subtree.Dataset)) collapsedSubtrees.Add(subtree.Dataset);
            BuildMeasureList();
        };
        return shell;
    }

    private static TextBlock RailHeader(string text, double indent, IBrush brush) => new()
    {
        Text = text,
        FontSize = TypographySettings.Size(11),
        Foreground = brush,
        Margin = new Thickness(indent, 0, 0, 0)
    };

    private void UpdateRailRow(RailRow row, bool hover = false)
    {
        var isPlaced = graph.Contains(row.Node.Path);
        var brush = isPlaced
            ? SubtreeAccents.Stroke(row.Subtree.AccentIndex)
            : hover ? Palette.TextSub : Palette.TextFaint;
        row.CheckBlock.Text = isPlaced ? "[x]" : "[ ]";
        row.CheckBlock.Foreground = brush;
        row.NameBlock.Foreground = brush;
        // Lit while the pointer is on the row, and while it is on the row's box on the canvas —
        // the same light in both directions, so either side finds the other.
        row.Shell.Background = hover || canvasHoverPaths.Contains(row.Node.Path)
            ? Palette.BgField
            : Brushes.Transparent;
        row.Shell.Cursor = new Cursor(isPlaced ? StandardCursorType.Arrow : StandardCursorType.Hand);
    }

    /// <summary>Rail row hovered — light the leaf's box on the canvas, if it is placed.</summary>
    private void SetNodeHighlight(RailRow row, bool on)
    {
        if (placed.TryGetValue(row.Node.Path, out var node)) node.Card.IsHighlighted = on;
    }

    /// <summary>Canvas box hovered — light its rail row and bring it into view.</summary>
    private void OnDiagramNodeHover(DiagramNode node, bool hovering)
    {
        if (!railRows.TryGetValue(node.Id, out var row)) return;
        if (hovering) canvasHoverPaths.Add(node.Id);
        else canvasHoverPaths.Remove(node.Id);
        UpdateRailRow(row);
        if (hovering) row.Shell.BringIntoView();
    }

    // ── figure naming ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Names the figure before it exists. A dashboard fig is addressed by name from three screens,
    /// so it is asked for up front rather than left as "figure_3" for someone to find later.
    /// </summary>
    private void ShowFigureDialog(double x, double y, string? wireFrom)
    {
        var suggestion = FigureCatalog.Instance.NextKey(
            wireFrom is null ? "figure" : Slug(NetworkGraph.LeafTitle(wireFrom)));

        var box = new TextBox { Classes = { "field" }, Text = suggestion, Watermark = "figure name" };
        var preview = new TextBlock { FontSize = TypographySettings.Size(11), Foreground = Palette.Green };
        var warning = new TextBlock
        {
            FontSize = TypographySettings.Size(10),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.Amber
        };

        void Sync()
        {
            var key = Slug(box.Text ?? "");
            preview.Text = key.Length == 0 ? "fig.—" : $"fig.{key}";
            warning.Text = key.Length == 0
                ? "a figure needs a name — it is how the dashboard and the map address it"
                : FigureCatalog.Instance.Contains(key)
                    ? $"fig.{key} already exists — committing here replaces what it publishes"
                    : "";
        }

        box.TextChanged += (_, _) => Sync();
        Sync();

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(DialogBlock("NAME", new SquircleBorder
        {
            Classes = { "emboss-press" },
            Background = Palette.BgField,
            Child = box
        }));
        body.Children.Add(DialogBlock("PUBLISHES AS", preview));
        body.Children.Add(DialogBlock("SOURCE", new TextBlock
        {
            Text = wireFrom is null
                ? "nothing yet — drag a wire into its left port once it lands"
                : NetworkGraph.LeafTitle(wireFrom),
            FontSize = TypographySettings.Size(11),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.Text
        }));
        body.Children.Add(warning);

        Dialog.Show("DASHBOARD FIG", body, "COMMIT <GO>", () =>
        {
            var key = Slug(box.Text ?? "");
            if (key.Length == 0) return false;

            var node = graph.AddFigure(key, x, y);
            if (wireFrom is not null) graph.Connect(wireFrom, node.Id);
            Render();
            return true;
        });
    }

    /// <summary>
    /// Names the table before it exists, the way <see cref="ShowFigureDialog"/> names a figure —
    /// a committed table is addressed by name from the tile editor, so it is asked for up front.
    /// </summary>
    private void ShowTableDialog(double x, double y, string? wireFrom)
    {
        var suggestion = TableCatalog.Instance.NextKey("table");

        var box = new TextBox { Classes = { "field" }, Text = suggestion, Watermark = "table name" };
        var preview = new TextBlock { FontSize = TypographySettings.Size(11), Foreground = Palette.Green };
        var warning = new TextBlock
        {
            FontSize = TypographySettings.Size(10),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.Amber
        };

        void Sync()
        {
            var key = Slug(box.Text ?? "");
            preview.Text = key.Length == 0 ? "tbl.—" : $"tbl.{key}";
            warning.Text = key.Length == 0
                ? "a table needs a name — it is how the tile editor addresses it"
                : TableCatalog.Instance.Contains(key)
                    ? $"tbl.{key} already exists — committing here replaces what it publishes"
                    : "";
        }

        box.TextChanged += (_, _) => Sync();
        Sync();

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(DialogBlock("NAME", new SquircleBorder
        {
            Classes = { "emboss-press" },
            Background = Palette.BgField,
            Child = box
        }));
        body.Children.Add(DialogBlock("PUBLISHES AS", preview));
        body.Children.Add(DialogBlock("SOURCE", new TextBlock
        {
            Text = wireFrom is not null && graph.Find(wireFrom) is { } select
                ? graph.Title(select)
                : "nothing yet — wire a SELECT into its left port once it lands",
            FontSize = TypographySettings.Size(11),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.Text
        }));
        body.Children.Add(warning);

        Dialog.Show("DASHBOARD TABLE", body, "COMMIT <GO>", () =>
        {
            var key = Slug(box.Text ?? "");
            if (key.Length == 0) return false;

            var node = graph.AddTableSink(key, x, y);
            if (wireFrom is not null) graph.Connect(wireFrom, node.Id);
            Render();
            return true;
        });
    }

    /// <summary>A figure key: lowercase, words joined by underscores, nothing else.</summary>
    private static string Slug(string text)
    {
        var slug = new string(text.Trim().ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_')
            .ToArray())
            .Trim('_');
        while (slug.Contains("__", StringComparison.Ordinal))
            slug = slug.Replace("__", "_", StringComparison.Ordinal);
        return slug;
    }

    private static Control DialogBlock(string label, Control body)
    {
        var stack = new StackPanel { Spacing = 5 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = TypographySettings.Size(9),
            LetterSpacing = 1.5,
            Foreground = Palette.TextFaint
        });
        stack.Children.Add(body);
        return stack;
    }

    // ── rail drag ────────────────────────────────────────────────────────────────────────────

    private void BeginMeasureDrag(RailRow row, PointerPressedEventArgs e)
    {
        if (graph.Contains(row.Node.Path)) return;
        var accent = SubtreeAccents.Stroke(row.Subtree.AccentIndex);
        BeginRailDrag(
            new RailDrag(row.LeafTitle, accent, point => PlaceMeasure(row, point)),
            row.Shell,
            e);
    }

    private void PlaceMeasure(RailRow row, Point point)
    {
        graph.PlaceMeasure(row.Node.Path, point.X - 110, point.Y - 40);
        Render();
    }

    private void BeginRailDrag(RailDrag drag, Border shell, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(shell).Properties.IsLeftButtonPressed) return;
        CancelRailDrag();
        railDrag = drag;
        railGhost = BuildGhost(drag.Label, drag.Accent);
        GhostLayer.Children.Add(railGhost);
        PositionGhost(e.GetPosition(this));
        e.Pointer.Capture(shell);
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
        var drag = railDrag;
        var dropPoint = e.GetPosition(Diagram);
        CancelRailDrag();

        if (dropPoint.X < 0 || dropPoint.Y < 0 ||
            dropPoint.X > Diagram.Bounds.Width || dropPoint.Y > Diagram.Bounds.Height) return;
        drag.Drop(Diagram.ViewportToWorld(dropPoint));
    }

    private void CancelRailDrag()
    {
        if (railGhost is not null)
        {
            GhostLayer.Children.Remove(railGhost);
            railGhost = null;
        }
        railDrag = null;
        foreach (var row in railRows.Values) UpdateRailRow(row);
    }

    private void PositionGhost(Point position)
    {
        if (railGhost is null) return;
        Canvas.SetLeft(railGhost, position.X + 10);
        Canvas.SetTop(railGhost, position.Y + 8);
    }

    private static Border BuildGhost(string leafTitle, IBrush accent) => new()
    {
        BorderBrush = accent,
        BorderThickness = new Thickness(1),
        Background = Palette.CanvasNoteBackdrop,
        Padding = new Thickness(8, 4),
        Child = new TextBlock { Text = leafTitle, FontSize = TypographySettings.Size(10), Foreground = accent }
    };

    // ── legend ───────────────────────────────────────────────────────────────────────────────

    private void BuildLegend()
    {
        foreach (var subtree in workspace.Subtrees)
        {
            LegendPanel.Children.Add(LegendRow(
                SubtreeAccents.Stroke(subtree.AccentIndex),
                SubtreeAccents.Fill(subtree.AccentIndex),
                false,
                $"{subtree.Dataset.ToLowerInvariant()} leaf"));
        }
        LegendPanel.Children.Add(LegendRow(Palette.Amber, Palette.AmberFill, false, "TRANSFER — density dν/dµ"));
        LegendPanel.Children.Add(LegendRow(Palette.Amber, Palette.AmberFill, true, "COMPARATOR — a/b wires dashed"));
        LegendPanel.Children.Add(LegendRow(Palette.Cyan, Palette.CyanFill, true, "REGRESSOR — training wires dashed"));
        LegendPanel.Children.Add(LegendRow(Palette.Amber, Palette.AmberFill, false, "SELECT — columns → rows"));
        LegendPanel.Children.Add(LegendRow(Palette.Green, Palette.GreenFill, false, "FIGURE — projection E[X|𝒢]"));
        LegendPanel.Children.Add(LegendRow(Palette.Green, Palette.GreenFill, false, "TABLE — committed rows"));
        LegendPanel.Children.Add(LegendRow(Palette.Purple, null, true, "PROVISIONAL — under-determined"));
    }

    public static Control LegendRow(IBrush stroke, IBrush? fill, bool dashed, string text)
    {
        var swatch = new Rectangle
        {
            Width = 10,
            Height = 10,
            Stroke = AppearanceSettings.Toned(stroke),
            StrokeThickness = 1,
            Fill = fill is null ? Brushes.Transparent : AppearanceSettings.Toned(fill),
            StrokeDashArray = dashed ? [2, 2] : null,
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(swatch);
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = TypographySettings.Size(9),
            LetterSpacing = 0.5,
            Foreground = Palette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }
}
