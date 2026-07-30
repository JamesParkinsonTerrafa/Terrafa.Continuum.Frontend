// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Models;

public enum FigureOrigin
{
    /// <summary>Stated up front. Nothing in the network computes it, so nothing can move it.</summary>
    Declared,

    /// <summary>Computed from the leaves wired into it on the network canvas.</summary>
    Derived
}

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

    /// <summary>σ per entry in <see cref="History"/> where the chain produced a varying one.</summary>
    public IReadOnlyList<double> SigmaHistory { get; init; } = [];

    public string Note { get; init; } = "";

    public FigureOrigin Origin { get; init; }

    /// <summary>What the network wired into it. Empty for a declared figure, or one left unwired.</summary>
    public IReadOnlyList<string> Inputs { get; init; } = [];

    /// <summary>Drawn dashed and purple wherever it appears — under-determined, not asserted.</summary>
    public bool IsProvisional { get; init; }

    public string Name => $"fig.{Key}";

    public bool HasValue => !double.IsNaN(Value);

    public bool HasVariance => HasValue && !double.IsNaN(Sigma) && Sigma > 0;

    /// <summary>What a source list shows on the right of the row when there is no σ to show.</summary>
    public string StateNote => HasVariance ? SigmaDisplay : HasValue ? "no σ" : "unwired";
}

/// <summary>
/// The figures the app knows about, shared by the screens that show them.
///
/// Figures used to exist only as hardcoded cards on the network canvas, which meant the dashboard
/// had nothing to offer as a data source and the two screens could not have disagreed more quietly.
/// Registering them here makes "dashboard fig" one concept: the network draws from it and writes
/// back to it, the dashboard tile editor lists from it.
///
/// A figure is either declared — a number stated up front, which the demo network's under-determined
/// branch has to stay, since asserting a value the leaves do not identify is the thing this app is
/// built to refuse — or derived by <see cref="NetworkGraph"/> from what is wired into it. The
/// declared form is kept even after a figure is derived, so unwiring the chain falls back to it
/// rather than leaving a figure that used to have a value and now silently has none.
/// </summary>
public sealed class FigureCatalog
{
    public static FigureCatalog Instance { get; } = new();

    private readonly List<DashboardFigure> figures = [];
    private readonly Dictionary<string, DashboardFigure> declared = [];

    public event Action? Changed;

    public IReadOnlyList<DashboardFigure> Figures => figures;

    private FigureCatalog() => SeedDefaults();

    public DashboardFigure? Find(string key) =>
        figures.FirstOrDefault(figure => figure.Key == key);

    /// <summary>The figure as stated up front, before anything in the network touched it.</summary>
    public DashboardFigure? DeclaredFor(string key) =>
        declared.GetValueOrDefault(key);

    public bool Contains(string key) => figures.Any(figure => figure.Key == key);

    /// <summary>Adds the figure, replacing any existing one under the same key.</summary>
    public void Register(DashboardFigure figure)
    {
        var index = figures.FindIndex(existing => existing.Key == figure.Key);
        if (index >= 0)
        {
            if (Same(figures[index], figure)) return;
            figures[index] = figure;
        }
        else
        {
            figures.Add(figure);
        }
        Changed?.Invoke();
    }

    public void Remove(string key)
    {
        if (figures.RemoveAll(figure => figure.Key == key) == 0) return;
        Changed?.Invoke();
    }

    /// <summary>A key nothing is registered under yet, e.g. "figure_2" from the stem "figure".</summary>
    public string NextKey(string stem)
    {
        if (!Contains(stem)) return stem;
        var index = 2;
        while (Contains($"{stem}_{index}")) index++;
        return $"{stem}_{index}";
    }

    /// <summary>Drops every figure the session produced and puts the declared ones back.</summary>
    public void Reset()
    {
        figures.Clear();
        figures.AddRange(declared.Values);
        Changed?.Invoke();
    }

    /// <summary>
    /// Whether re-registering would change anything a screen draws. Recomputing the network hands
    /// every figure back on each pass, and raising Changed for an identical figure would loop
    /// through the screens that rebuild on it.
    /// </summary>
    private static bool Same(DashboardFigure left, DashboardFigure right) =>
        left.Display == right.Display &&
        left.SigmaDisplay == right.SigmaDisplay &&
        left.Note == right.Note &&
        left.Origin == right.Origin &&
        left.IsProvisional == right.IsProvisional &&
        Nearly(left.Value, right.Value) &&
        Nearly(left.Sigma, right.Sigma) &&
        left.History.Count == right.History.Count &&
        left.History.Zip(right.History).All(pair => Nearly(pair.First, pair.Second));

    private static bool Nearly(double left, double right) =>
        (double.IsNaN(left) && double.IsNaN(right)) || Math.Abs(left - right) < 1e-9;

    /// <summary>
    /// The figures the seeded network chain produces. total_inventory is wired on that canvas and so
    /// is recomputed from the tank levels the moment the graph is built — it is stated here only so
    /// the dashboard has it before anyone opens the network screen. log_score is deliberately
    /// variance-free: it is a proper score, not a measured quantity, and it is the case the dashboard
    /// has to refuse to draw bounds for rather than invent them.
    /// </summary>
    private void SeedDefaults()
    {
        Declare(new DashboardFigure
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

        Declare(new DashboardFigure
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

        Declare(new DashboardFigure
        {
            Key = "log_score",
            Display = "−241.7",
            Value = -241.7,
            Unit = "Σ log S",
            History = MeasureNumerics.History("fig.log_score", -241.7, double.NaN),
            Note = "proper score · leads the board per event — carries no σ of its own"
        });
    }

    private void Declare(DashboardFigure figure)
    {
        declared[figure.Key] = figure;
        Register(figure);
    }
}
