// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Models;

public enum NetworkNodeKind
{
    Measure,
    Transfer,
    Figure
}

/// <summary>
/// One card on the network canvas. The three kinds share a type because the canvas, the edges and
/// the evaluator all address them the same way; the fields that only one kind uses are grouped and
/// commented rather than split into a hierarchy nothing else would benefit from.
/// </summary>
public sealed class NetworkNode
{
    public required string Id { get; init; }
    public required NetworkNodeKind Kind { get; init; }

    /// <summary>Leaf path for a measure, bare figure key for a figure, empty for a transfer.</summary>
    public string Key { get; init; } = "";

    public double X { get; set; }
    public double Y { get; set; }

    // ── transfer only ────────────────────────────────────────────────────────────────────────

    public TransferCombiner Combiner { get; set; }

    /// <summary>A <see cref="FunctionLibrary"/> name applied after the combine; empty is identity.</summary>
    public string Stage { get; set; } = "";

    public string Estimator { get; init; } = "";

    public bool IsEstimator => Estimator.Length > 0;

    /// <summary>
    /// A transfer the app will not evaluate — the seeded hazard, whose frailty term is not
    /// identifiable from the leaves feeding it. A figure downstream of one keeps whatever it
    /// declares rather than being handed a number the chain cannot support.
    /// </summary>
    public bool IsOpaque { get; init; }

    public string OpaqueTitle { get; init; } = "";
}

public sealed record NetworkEdge(string FromId, string ToId, string Port = "");

/// <summary>
/// The network canvas as session state: which leaves have been placed, which transfers and figures
/// sit beside them, and what is wired to what.
///
/// It lives outside the view for two reasons. Mounting a dataset rebuilds every screen that is not
/// on show, and a canvas that only existed inside <see cref="Views.NetworkView"/> lost everything
/// the operator had built each time they visited DATA SOURCES. And a figure is only "created in the
/// network" in any useful sense if the dashboard can see it: the graph evaluates each figure from
/// the leaves wired into it and writes the result to <see cref="FigureCatalog"/>, which is what the
/// tile editor lists.
/// </summary>
public sealed class NetworkGraph
{
    public const string EstimatorPortX = "x";
    public const string EstimatorPortY = "y";
    public const string EstimatorPortPredict = "predict";

    public static IReadOnlyList<string> EstimatorPorts { get; } =
        [EstimatorPortX, EstimatorPortY, EstimatorPortPredict];

    public static NetworkGraph Instance { get; } = new();

    /// <summary>What "CHANGE FUNCTION" cycles through: identity, then the primitives.</summary>
    private static readonly string[] StageCycle =
        ["", "exp", "log", "sqrt", "square", "tanh", "negate", "clip"];

    private readonly List<NetworkNode> nodes = [];
    private readonly List<NetworkEdge> edges = [];
    private int nextTransfer;
    private int suspended;

    public event Action? Changed;

    /// <summary>
    /// Raised for mutations no screen redraws for — a card drag — but that durable state must
    /// still record. See <see cref="Dashboard.Edited"/>.
    /// </summary>
    public event Action? Edited;

    public IReadOnlyList<NetworkNode> Nodes => nodes;

    public IReadOnlyList<NetworkEdge> Edges => edges;

    private NetworkGraph()
    {
        Seed();
        Workspace.Instance.Changed += PruneUnmounted;
    }

    public static string FigureId(string key) => $"figure:{key}";

    public NetworkNode? Find(string id) => nodes.FirstOrDefault(node => node.Id == id);

    public bool Contains(string id) => Find(id) is not null;

    public IEnumerable<string> InputsOf(string id) =>
        edges.Where(edge => edge.ToId == id).Select(edge => edge.FromId);

    // ── building ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Where a newly placed node lands: on the nearest gridline while snap is on.</summary>
    private static double Placed(double value) =>
        SnapSettings.Enabled ? SnapSettings.Snap(value) : value;

    public NetworkNode PlaceMeasure(string path, double x, double y)
    {
        if (Find(path) is { } existing) return existing;
        var node = new NetworkNode
        {
            Id = path,
            Kind = NetworkNodeKind.Measure,
            Key = path,
            X = Placed(x),
            Y = Placed(y)
        };
        nodes.Add(node);
        Publish();
        return node;
    }

    public NetworkNode AddTransfer(double x, double y)
    {
        var node = new NetworkNode
        {
            Id = $"transfer:t{++nextTransfer}",
            Kind = NetworkNodeKind.Transfer,
            Combiner = TransferCombiner.Sum,
            X = Placed(x),
            Y = Placed(y)
        };
        nodes.Add(node);
        Publish();
        return node;
    }

    public NetworkNode AddEstimator(string estimatorName, double x, double y)
    {
        var node = new NetworkNode
        {
            Id = $"transfer:t{++nextTransfer}",
            Kind = NetworkNodeKind.Transfer,
            Estimator = estimatorName,
            X = Placed(x),
            Y = Placed(y)
        };
        nodes.Add(node);
        Publish();
        return node;
    }

    public NetworkNode AddFigure(string key, double x, double y)
    {
        if (Find(FigureId(key)) is { } existing) return existing;
        var node = new NetworkNode
        {
            Id = FigureId(key),
            Kind = NetworkNodeKind.Figure,
            Key = key,
            X = Placed(x),
            Y = Placed(y)
        };
        nodes.Add(node);
        Publish();
        return node;
    }

    public void Remove(string id)
    {
        if (Find(id) is not { } node) return;
        nodes.Remove(node);
        edges.RemoveAll(edge => edge.FromId == id || edge.ToId == id);

        // Taking a figure off the canvas withdraws it from the dashboard as well — unless it was
        // declared, in which case it goes back to being the number it states.
        if (node.Kind == NetworkNodeKind.Figure)
        {
            if (FigureCatalog.Instance.DeclaredFor(node.Key) is { } declared)
                FigureCatalog.Instance.Register(declared);
            else
                FigureCatalog.Instance.Remove(node.Key);
        }
        Publish();
    }

    /// <summary>
    /// Whether the wire may be drawn. Measures only ever feed, figures only ever receive, and a loop
    /// is refused outright rather than being caught later by the evaluator's own guard.
    /// </summary>
    public bool CanConnect(string fromId, string toId)
    {
        if (fromId == toId) return false;
        if (Find(fromId) is not { } from || Find(toId) is not { } to) return false;
        if (from.Kind == NetworkNodeKind.Figure || to.Kind == NetworkNodeKind.Measure) return false;
        if (edges.Any(edge => edge.FromId == fromId && edge.ToId == toId)) return false;
        if (to.IsEstimator && InputsOf(toId).Count() >= EstimatorPorts.Count) return false;
        return !Reaches(toId, fromId);
    }

    public bool Connect(string fromId, string toId)
    {
        if (!CanConnect(fromId, toId)) return false;
        var port = Find(toId) is { IsEstimator: true } ? NextFreePort(toId) : "";
        edges.Add(new NetworkEdge(fromId, toId, port));
        Publish();
        return true;
    }

    private string NextFreePort(string toId)
    {
        var taken = edges.Where(edge => edge.ToId == toId).Select(edge => edge.Port).ToHashSet();
        return EstimatorPorts.First(port => !taken.Contains(port));
    }

    public string? SourceOnPort(NetworkNode node, string port) =>
        edges.FirstOrDefault(edge => edge.ToId == node.Id && edge.Port == port)?.FromId;

    public string PortOf(string fromId, string toId) =>
        edges.FirstOrDefault(edge => edge.FromId == fromId && edge.ToId == toId)?.Port ?? "";

    public void SwapTrainingWires(NetworkNode node)
    {
        if (!node.IsEstimator) return;
        var swapped = false;
        for (var index = 0; index < edges.Count; index++)
        {
            if (edges[index].ToId != node.Id) continue;
            var port = edges[index].Port switch
            {
                EstimatorPortX => EstimatorPortY,
                EstimatorPortY => EstimatorPortX,
                _ => edges[index].Port
            };
            if (port == edges[index].Port) continue;
            edges[index] = edges[index] with { Port = port };
            swapped = true;
        }
        if (swapped) Publish();
    }

    public void RotatePortRoles(NetworkNode node)
    {
        if (!node.IsEstimator) return;
        var incomingIndexes = Enumerable.Range(0, edges.Count)
            .Where(index => edges[index].ToId == node.Id)
            .ToList();
        if (incomingIndexes.Count < 2) return;
        var ports = incomingIndexes.Select(index => edges[index].Port).ToList();
        for (var position = 0; position < incomingIndexes.Count; position++)
        {
            var index = incomingIndexes[position];
            edges[index] = edges[index] with { Port = ports[(position + 1) % ports.Count] };
        }
        Publish();
    }

    public void ClearInputs(string id)
    {
        if (edges.RemoveAll(edge => edge.ToId == id) == 0) return;
        Publish();
    }

    /// <summary>Positions are remembered but do not invalidate anything — no figure moves with them.</summary>
    public void Move(string id, double x, double y)
    {
        if (Find(id) is not { } node) return;
        node.X = x;
        node.Y = y;
        Edited?.Invoke();
    }

    /// <summary>
    /// Replaces the canvas with loaded state, then recomputes and announces once. The transfer
    /// counter resumes past the highest loaded id so a new transfer cannot collide with one the
    /// load brought back.
    /// </summary>
    public void Load(IEnumerable<NetworkNode> loadedNodes, IEnumerable<NetworkEdge> loadedEdges)
    {
        suspended++;
        nodes.Clear();
        edges.Clear();
        nodes.AddRange(loadedNodes);
        edges.AddRange(loadedEdges);
        nextTransfer = nodes
            .Select(node => node.Id.StartsWith("transfer:t", StringComparison.Ordinal)
                && int.TryParse(node.Id["transfer:t".Length..], out var index) ? index : 0)
            .DefaultIfEmpty(0)
            .Max();
        suspended--;
        Publish();
    }

    public void CycleStage(NetworkNode transfer)
    {
        if (transfer.Kind != NetworkNodeKind.Transfer || transfer.IsOpaque || transfer.IsEstimator) return;
        var index = Array.IndexOf(StageCycle, transfer.Stage);
        transfer.Stage = StageCycle[(index + 1) % StageCycle.Length];
        Publish();
    }

    public void CycleCombiner(NetworkNode transfer)
    {
        if (transfer.Kind != NetworkNodeKind.Transfer || transfer.IsOpaque || transfer.IsEstimator) return;
        transfer.Combiner = transfer.Combiner switch
        {
            TransferCombiner.Sum => TransferCombiner.Mean,
            TransferCombiner.Mean => TransferCombiner.Product,
            _ => TransferCombiner.Sum
        };
        Publish();
    }

    // ── reading ──────────────────────────────────────────────────────────────────────────────

    /// <summary>What a card is titled: the leaf's short path, the transfer's formula, or "fig.key".</summary>
    public string Title(NetworkNode node) => node.Kind switch
    {
        NetworkNodeKind.Measure => LeafTitle(node.Key),
        NetworkNodeKind.Figure => $"fig.{node.Key}",
        _ when node.IsOpaque => node.OpaqueTitle,
        _ when node.IsEstimator => TransferMath.EstimatorFormula(
            node.Estimator,
            SourceTitleOnPort(node, EstimatorPortX),
            SourceTitleOnPort(node, EstimatorPortY),
            SourceTitleOnPort(node, EstimatorPortPredict)),
        _ => TransferMath.Formula(node.Combiner, Stage(node), InputLabels(node))
    };

    public string? SourceTitleOnPort(NetworkNode node, string port) =>
        SourceOnPort(node, port) is { } sourceId && Find(sourceId) is { } source ? Title(source) : null;

    public IReadOnlyList<string> InputLabels(NetworkNode node) =>
        InputsOf(node.Id).Select(id => Find(id) is { } source ? Title(source) : id).ToList();

    /// <summary>The transfer's own output, for the card that draws it. Null when it cannot be had.</summary>
    public TransferResult? Evaluate(NetworkNode node)
    {
        if (node.Kind != NetworkNodeKind.Transfer || node.IsOpaque) return null;
        if (node.IsEstimator) return EvaluateEstimatorNode(node, []);
        var terms = InputsOf(node.Id)
            .Select(id => Reading(id, []))
            .OfType<TransferInput>()
            .ToList();
        return TransferMath.Evaluate(node.Combiner, Stage(node), terms);
    }

    public TransferInput? InputOnPort(NetworkNode node, string port) => ReadingOnPort(node, port, []);

    private TransferResult? EvaluateEstimatorNode(NetworkNode node, HashSet<string> path)
    {
        if (FunctionLibrary.Instance.FindEstimator(node.Estimator) is not { } estimator) return null;
        return TransferMath.EvaluateEstimator(
            estimator,
            ReadingOnPort(node, EstimatorPortX, path),
            ReadingOnPort(node, EstimatorPortY, path),
            ReadingOnPort(node, EstimatorPortPredict, path));
    }

    private TransferInput? ReadingOnPort(NetworkNode node, string port, HashSet<string> path) =>
        SourceOnPort(node, port) is { } sourceId ? Reading(sourceId, path) : null;

    public LibraryFunction? Stage(NetworkNode node) =>
        node.Stage.Length == 0
            ? null
            : FunctionLibrary.Instance.Find(node.Stage) is { IsUnaryScalar: true } stage ? stage : null;

    // ── evaluation ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recomputes every figure on the canvas and hands it to the catalogue. Called after anything
    /// structural, and after the workspace changes — a leaf that has just been unmounted must stop
    /// contributing to a figure the dashboard is still plotting.
    /// </summary>
    public void Recompute()
    {
        foreach (var node in nodes.Where(node => node.Kind == NetworkNodeKind.Figure).ToList())
            FigureCatalog.Instance.Register(BuildFigure(node));
    }

    private DashboardFigure BuildFigure(NetworkNode node)
    {
        var declared = FigureCatalog.Instance.DeclaredFor(node.Key);
        var inputs = InputsOf(node.Id).ToList();

        if (inputs.Count > 1)
        {
            return declared ?? Unwired(node.Key,
                $"{inputs.Count} inputs wired — a figure commits to one quantity. " +
                "Combine them through a transfer first.");
        }
        if (inputs.Count == 0)
            return declared ?? Unwired(node.Key, "nothing wired into it yet — drag a wire into its left port");

        if (Find(inputs[0]) is not { } source || Reading(inputs[0], []) is not { } reading)
            return declared ?? Unwired(node.Key, "upstream carries no value the chain can commit to");

        var note = source.Kind == NetworkNodeKind.Transfer
            ? Evaluate(source)?.Note ?? ""
            : "σ straight from the tree leaf";

        return new DashboardFigure
        {
            Key = node.Key,
            Display = $"{MeasureNumerics.Format(reading.Value)} {reading.Unit}".Trim(),
            SigmaDisplay = double.IsNaN(reading.Sigma) || reading.Sigma <= 0
                ? ""
                : $"± {MeasureNumerics.FormatSigma(reading.Sigma)}",
            Value = reading.Value,
            Sigma = reading.Sigma,
            Unit = reading.Unit,
            History = reading.History,
            SigmaHistory = reading.SigmaHistory,
            Note = note,
            Origin = FigureOrigin.Derived,
            Inputs = [Title(source)]
        };
    }

    /// <summary>
    /// The reading at a node, flattened for the maths. <paramref name="path"/> is the chain being
    /// walked, so a diamond evaluates twice rather than being mistaken for a loop.
    /// </summary>
    private TransferInput? Reading(string id, HashSet<string> path)
    {
        if (!path.Add(id)) return null;
        try
        {
            if (Find(id) is not { } node) return null;
            switch (node.Kind)
            {
                case NetworkNodeKind.Measure:
                {
                    if (Workspace.Instance.FindNode(node.Key)?.Reading is not { HasValue: true } reading) return null;
                    return new TransferInput(
                        LeafTitle(node.Key), reading.Value, reading.Sigma, reading.Unit,
                        reading.History, reading.SigmaHistory);
                }
                case NetworkNodeKind.Transfer:
                {
                    if (node.IsOpaque) return null;
                    var result = node.IsEstimator
                        ? EvaluateEstimatorNode(node, path)
                        : TransferMath.Evaluate(node.Combiner, Stage(node),
                            InputsOf(id).Select(input => Reading(input, path)).OfType<TransferInput>().ToList());
                    if (result is null) return null;
                    return new TransferInput(
                        Title(node), result.Value, result.Sigma, result.Unit,
                        result.History, result.SigmaHistory);
                }
                default:
                    return null;
            }
        }
        finally
        {
            path.Remove(id);
        }
    }

    private bool Reaches(string fromId, string toId) =>
        fromId == toId || InputsOf(toId).Any(input => Reaches(fromId, input));

    private static DashboardFigure Unwired(string key, string note) => new()
    {
        Key = key,
        Display = "—",
        Note = note,
        Origin = FigureOrigin.Derived
    };

    public static string LeafTitle(string path)
    {
        var segments = path.Split('.');
        return segments.Length >= 2 ? $"{segments[^2]}.{segments[^1]}" : path;
    }

    // ── session ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Drops leaves whose dataset is no longer mounted, then recomputes what fed on them.</summary>
    private void PruneUnmounted()
    {
        var orphans = nodes
            .Where(node => node.Kind == NetworkNodeKind.Measure && Workspace.Instance.FindNode(node.Key) is null)
            .Select(node => node.Id)
            .ToList();

        foreach (var id in orphans)
        {
            nodes.RemoveAll(node => node.Id == id);
            edges.RemoveAll(edge => edge.FromId == id || edge.ToId == id);
        }

        // Even with nothing pruned the readings behind the placed leaves may have moved, so the
        // figures are recomputed either way.
        Publish();
    }

    public void Reset(bool seedDemo)
    {
        suspended++;
        nodes.Clear();
        edges.Clear();
        nextTransfer = 0;
        FigureCatalog.Instance.Reset();
        if (seedDemo) Seed();
        suspended--;
        Publish();
    }

    /// <summary>
    /// The chain the app opens on: two tank levels summed into total_inventory, and the temperature
    /// and spoilage leaves feeding the hazard branch the app refuses to identify.
    /// </summary>
    private void Seed()
    {
        if (Workspace.Instance.Find("SITE_ALPHA")?.Root.Path is not { } root) return;

        suspended++;

        // Laid out in grid multiples, so the canvas opens already sitting on the snap grid.
        var level01 = PlaceMeasure($"{root}.tank_farm.tank_01.level", 75, 125);
        var level02 = PlaceMeasure($"{root}.tank_farm.tank_02.level", 75, 250);
        var temp01 = PlaceMeasure($"{root}.tank_farm.tank_01.temp", 75, 425);
        var spoilage = PlaceMeasure($"{root}.tank_farm.tank_01.spoilage", 75, 550);

        var transfer1 = AddTransfer(450, 175);
        var transfer2 = new NetworkNode
        {
            Id = $"transfer:t{++nextTransfer}",
            Kind = NetworkNodeKind.Transfer,
            IsOpaque = true,
            OpaqueTitle = "hazard λ₀(t)·exp(θᵀx)",
            X = 450,
            Y = 475
        };
        nodes.Add(transfer2);

        var inventory = AddFigure("total_inventory", 875, 200);
        var expiry = AddFigure("expiry_risk", 875, 475);

        Connect(level01.Id, transfer1.Id);
        Connect(level02.Id, transfer1.Id);
        Connect(temp01.Id, transfer2.Id);
        Connect(spoilage.Id, transfer2.Id);
        Connect(transfer1.Id, inventory.Id);
        Connect(transfer2.Id, expiry.Id);

        suspended--;
        Publish();
    }

    /// <summary>Recomputes and announces, unless a batch is in progress.</summary>
    private void Publish()
    {
        if (suspended > 0) return;
        Recompute();
        Changed?.Invoke();
    }
}
