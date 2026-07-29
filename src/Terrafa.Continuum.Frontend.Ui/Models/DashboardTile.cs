namespace Terrafa.Continuum.Frontend.Models;

public enum TileKind
{
    Line,
    Bar,
    Table
}

public enum TileSourceKind
{
    Measure,
    Figure
}

/// <summary>
/// One wired input on a tile. <paramref name="Path"/> is a leaf path for a measure and a bare key
/// for a figure — resolved late, so a source that is unmounted between edits degrades to "missing"
/// rather than dangling a stale reading.
///
/// <paramref name="SigmaFigureKey"/> is the customisation route: a measure whose tree carries no σ
/// can borrow one from a dashboard figure, which lets an operator build their own variance metric
/// out of the network. It is only ever set on a measure. A figure's own σ is propagated up the
/// chain and is not something anyone gets to nominate.
/// </summary>
public sealed record TileSource(TileSourceKind Kind, string Path, string? SigmaFigureKey = null)
{
    public string Display => Kind == TileSourceKind.Figure ? $"fig.{Path}" : Path;

    /// <summary>Whether this source may be given a σ figure at all.</summary>
    public bool AcceptsSigmaFigure => Kind == TileSourceKind.Measure;

    public bool Matches(TileSourceKind kind, string path) => Kind == kind && Path == path;

    public string ShortLabel
    {
        get
        {
            if (Kind == TileSourceKind.Figure) return $"fig.{Path}";
            var segments = Path.Split('.');
            return segments.Length >= 2 ? $"{segments[^2]}.{segments[^1]}" : Path;
        }
    }
}

/// <summary>A resolved source: what a chart actually draws, independent of where it came from.</summary>
public sealed record TileSeries(
    string Label,
    double Value,
    double Sigma,
    string Unit,
    IReadOnlyList<double> History,
    bool HasVariance,
    bool IsProvisional)
{
    /// <summary>σ per step where the source carries one; empty means <see cref="Sigma"/> is flat.</summary>
    public IReadOnlyList<double> SigmaHistory { get; init; } = [];

    /// <summary>
    /// Set when σ came from a figure the operator nominated rather than from the source's own
    /// tree. Drawn in the provisional language, because it is asserted rather than computed.
    /// </summary>
    public bool IsAssertedSigma { get; init; }

    public string SigmaNote { get; init; } = "";

    /// <summary>σ at step <paramref name="index"/>, falling back to the flat figure.</summary>
    public double SigmaAt(int index) =>
        index >= 0 && index < SigmaHistory.Count ? SigmaHistory[index] : Sigma;
}

public sealed class DashboardTile
{
    private static int nextId;

    public DashboardTile(TileKind kind, string name)
    {
        Id = $"tile:{++nextId}";
        Kind = kind;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; set; }
    public TileKind Kind { get; set; }
    public List<TileSource> Sources { get; } = [];

    public bool IsWired => Sources.Count > 0;

    public static string KindLabel(TileKind kind) => kind switch
    {
        TileKind.Line => "LINE CHART",
        TileKind.Bar => "BAR CHART",
        _ => "TABLE"
    };
}

/// <summary>
/// Turns a tile's wired sources into series. Measures come from the mounted workspace, figures from
/// the shared catalogue, and anything that no longer resolves returns null so the tile can say so.
/// </summary>
public static class TileData
{
    public static TileSeries? Resolve(TileSource source) => source.Kind switch
    {
        TileSourceKind.Measure => FromMeasure(source),
        _ => FromFigure(source)
    };

    private static TileSeries? FromMeasure(TileSource source)
    {
        if (Workspace.Instance.FindNode(source.Path)?.Reading is not { } reading) return null;
        if (!reading.HasValue) return null;

        var series = new TileSeries(
            source.ShortLabel,
            reading.Value,
            reading.Sigma,
            reading.Unit,
            reading.History,
            reading.HasVariance,
            IsProvisional: false)
        {
            SigmaHistory = reading.SigmaHistory,
            SigmaNote = reading.SigmaHistory.Count > 0 ? "σ(x) from tree leaf" : "σ from tree"
        };

        if (source.SigmaFigureKey is not { } key) return series;
        if (FigureCatalog.Instance.Find(key) is not { HasValue: true } figure) return series;

        // A nominated figure replaces whatever the tree said, and says so: the operator asserted
        // this pairing, so it must not read the same as one the contract carries.
        return series with
        {
            Sigma = Math.Abs(figure.Value),
            HasVariance = Math.Abs(figure.Value) > 0,
            SigmaHistory = figure.History.Select(Math.Abs).ToList(),
            IsAssertedSigma = true,
            SigmaNote = $"σ asserted from {figure.Name}"
        };
    }

    private static TileSeries? FromFigure(TileSource source)
    {
        if (FigureCatalog.Instance.Find(source.Path) is not { } figure) return null;
        if (!figure.HasValue) return null;

        return new TileSeries(
            figure.Name,
            figure.Value,
            figure.Sigma,
            figure.Unit,
            figure.History,
            figure.HasVariance,
            figure.IsProvisional)
        {
            SigmaNote = "σ propagated up the chain"
        };
    }

    /// <summary>
    /// Every measure currently mounted that a chart could plot. σ carriers are left out: a "sigma"
    /// leaf is real data and stays in the tree, but offering it here would invite someone to plot
    /// a standard deviation as though it were a quantity.
    /// </summary>
    public static IEnumerable<DataTreeNode> AvailableMeasures(MountedSubtree subtree) =>
        subtree.Leaves.Where(leaf =>
            leaf.Reading is { HasValue: true, IsSigmaCarrier: false });
}
