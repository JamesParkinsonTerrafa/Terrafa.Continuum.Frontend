// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Models;

public sealed class Measure
{
    public string Display { get; init; } = "";
    public string SigmaDisplay { get; init; } = "";
    public string SigmaKind { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool Selected { get; init; }
    public bool IsNew { get; init; }
    public bool IsVector { get; init; }

    /// <summary>
    /// The reading as a number, NaN when the leaf is categorical, withheld, or otherwise not
    /// plottable. <see cref="Display"/> stays the thing a tree row shows; this is what a chart
    /// consumes.
    /// </summary>
    public double Value { get; init; } = double.NaN;

    /// <summary>
    /// 1σ on <see cref="Value"/>, NaN when the leaf carries no variance at all. Exact counts and
    /// enums land here — the dashboard blanks any tile wired to one while variance is on, which is
    /// why this is separate from the <see cref="SigmaDisplay"/> string.
    /// </summary>
    public double Sigma { get; init; } = double.NaN;

    public string Unit { get; init; } = "";

    /// <summary>Recent readings, oldest first. Empty when nothing is stored behind the leaf.</summary>
    public IReadOnlyList<double> History { get; init; } = [];

    /// <summary>
    /// σ per entry in <see cref="History"/>, when the leaf carries one — a heteroscedastic σ(x)
    /// rather than a flat figure. Empty means <see cref="Sigma"/> applies at every step.
    /// </summary>
    public IReadOnlyList<double> SigmaHistory { get; init; } = [];

    /// <summary>
    /// True for a leaf that exists to carry another leaf's σ — the "sigma" child under a measure.
    /// It is real data and stays visible in the tree, but it is not a quantity to plot on its own,
    /// so source pickers leave it out.
    /// </summary>
    public bool IsSigmaCarrier { get; init; }

    public bool HasValue => !double.IsNaN(Value);

    /// <summary>Whether a chart can draw bounds for this leaf without inventing them.</summary>
    public bool HasVariance => HasValue && !double.IsNaN(Sigma) && Sigma > 0;

    /// <summary>
    /// What a source list shows on the right of the row. "no value" is its own state and not a
    /// synonym for "no σ": a column out of the catalogue that has not been sampled is in the tree
    /// and pickable, it just has nothing behind it to draw yet.
    /// </summary>
    public string StateNote => !HasValue ? "no value" : HasVariance ? SigmaDisplay : "no σ";
}
