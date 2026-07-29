namespace Terrafa.Continuum.Frontend.Models;

/// <summary>
/// A figure the network has committed to — the output end of a transfer chain, addressable by name
/// from any screen. Carries the same numeric contract as <see cref="Measure"/> so a dashboard tile
/// can plot either without caring which it got.
/// </summary>
public sealed class DashboardFigure
{
    /// <summary>Bare key, e.g. "total_inventory". <see cref="Name"/> is the "fig." form.</summary>
    public required string Key { get; init; }

    public required string Display { get; init; }
    public string SigmaDisplay { get; init; } = "";
    public double Value { get; init; } = double.NaN;
    public double Sigma { get; init; } = double.NaN;
    public string Unit { get; init; } = "";
    public IReadOnlyList<double> History { get; init; } = [];
    public string Note { get; init; } = "";

    /// <summary>Drawn dashed and purple wherever it appears — under-determined, not asserted.</summary>
    public bool IsProvisional { get; init; }

    public string Name => $"fig.{Key}";

    public bool HasValue => !double.IsNaN(Value);

    public bool HasVariance => HasValue && !double.IsNaN(Sigma) && Sigma > 0;
}

/// <summary>
/// The figures the app knows about, shared by the screens that show them.
///
/// Figures used to exist only as hardcoded cards on the network canvas, which meant the dashboard
/// had nothing to offer as a data source and the two screens could not have disagreed more quietly.
/// Registering them here makes "dashboard fig" one concept: the network draws from it, the
/// dashboard tile editor lists from it.
/// </summary>
public sealed class FigureCatalog
{
    public static FigureCatalog Instance { get; } = new();

    private readonly List<DashboardFigure> figures = [];

    public event Action? Changed;

    public IReadOnlyList<DashboardFigure> Figures => figures;

    private FigureCatalog() => SeedDefaults();

    public DashboardFigure? Find(string key) =>
        figures.FirstOrDefault(figure => figure.Key == key);

    /// <summary>Adds the figure, replacing any existing one under the same key.</summary>
    public void Register(DashboardFigure figure)
    {
        var index = figures.FindIndex(existing => existing.Key == figure.Key);
        if (index >= 0) figures[index] = figure;
        else figures.Add(figure);
        Changed?.Invoke();
    }

    public void Remove(string key)
    {
        if (figures.RemoveAll(figure => figure.Key == key) == 0) return;
        Changed?.Invoke();
    }

    /// <summary>
    /// The figures the seeded network chain produces. log_score is deliberately variance-free: it
    /// is a proper score, not a measured quantity, and it is the case the dashboard has to refuse
    /// to draw bounds for rather than invent them.
    /// </summary>
    private void SeedDefaults()
    {
        Register(new DashboardFigure
        {
            Key = "total_inventory",
            Display = "24,085 bbl",
            SigmaDisplay = "± 152",
            Value = 24085,
            Sigma = 152,
            Unit = "bbl",
            History = MeasureNumerics.History("fig.total_inventory", 24085, 152),
            Note = "σ composed up the tree · pinned to DASH & MAP"
        });

        Register(new DashboardFigure
        {
            Key = "expiry_risk",
            Display = "λ 0.031 /d",
            SigmaDisplay = "± 0.019",
            Value = 0.031,
            Sigma = 0.019,
            Unit = "/d",
            History = MeasureNumerics.History("fig.expiry_risk", 0.031, 0.019),
            IsProvisional = true,
            Note = "⚠ under-determined — frailty Z not identifiable from these leaves. " +
                   "Drawn provisional, not asserted. Add prior or leaf to pin down."
        });

        Register(new DashboardFigure
        {
            Key = "log_score",
            Display = "−241.7",
            Value = -241.7,
            Unit = "Σ log S",
            History = MeasureNumerics.History("fig.log_score", -241.7, double.NaN),
            Note = "proper score · leads the board per event — carries no σ of its own"
        });
    }
}
