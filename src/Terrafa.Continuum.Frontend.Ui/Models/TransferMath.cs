// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Models;

/// <summary>How a transfer reduces the several things wired into it to one number.</summary>
public enum TransferCombiner
{
    Sum,
    Mean,
    Product
}

/// <summary>
/// One thing wired into a transfer — a leaf reading or another transfer's output, flattened so the
/// maths below never has to care which it was.
/// </summary>
public sealed record TransferInput(
    string Label,
    double Value,
    double Sigma,
    string Unit,
    IReadOnlyList<double> History,
    IReadOnlyList<double> SigmaHistory,
    bool IsBoolean = false,
    double SigmaLevel = double.NaN)
{
    public double ValueAt(int index) => index >= 0 && index < History.Count ? History[index] : Value;

    public double SigmaAt(int index) => index >= 0 && index < SigmaHistory.Count ? SigmaHistory[index] : Sigma;
}

/// <summary>What came out of a transfer, in the same shape a leaf reading arrives in.</summary>
/// <param name="IsBoolean">True for a comparator's output — a determination, encoded 1/0.</param>
/// <param name="SigmaLevel">
/// The margin in σ units behind a determination, |a−b|/√(σa²+σb²) — unsigned; the direction is
/// the determination itself. NaN in the vacuous regime (an input's σ unknown), infinite when the
/// spread is exactly zero and the determination is exact.
/// </param>
public sealed record TransferResult(
    double Value,
    double Sigma,
    string Unit,
    IReadOnlyList<double> History,
    IReadOnlyList<double> SigmaHistory,
    bool Linearised,
    string Note,
    bool IsBoolean = false,
    double SigmaLevel = double.NaN,
    IReadOnlyList<double>? SigmaLevelHistory = null)
{
    public bool HasVariance => !double.IsNaN(Sigma) && Sigma > 0;
}

/// <summary>
/// Pushes readings through a transfer and carries their σ with them.
///
/// The chain the app draws — leaf → transfer → dashboard figure — used to end in numbers written by
/// hand, so the figure on the network screen and the tile on the dashboard agreed only because the
/// same string had been typed twice. This is what makes the figure a function of the leaves instead:
/// change a leaf, or rewire the network, and the tile that plots it moves.
///
/// σ propagates by the Jacobian while the stage is affine, which is exact, and falls back to a
/// sigma-point transform where it is not — the same distinction the network screen's status bar
/// claims. Both are deterministic, so a snapshot renders the same numbers on every run.
///
/// Inputs are treated as independent. That is the assumption a covariance the feed does not carry
/// cannot avoid, and it is stated on the card rather than hidden here.
/// </summary>
public static class TransferMath
{
    /// <summary>√3 — the sigma-point offset that matches a normal through its third moment.</summary>
    private const double SigmaPointSpread = 1.7320508075688772;

    private const double CentreWeight = 2.0 / 3.0;
    private const double WingWeight = 1.0 / 6.0;

    private readonly record struct Step(double Value, double Sigma, bool Linearised);

    public static TransferResult? Evaluate(
        TransferCombiner combiner, LibraryFunction? stage, IReadOnlyList<TransferInput> inputs)
    {
        if (inputs.Count == 0) return null;
        if (inputs.Any(input => double.IsNaN(input.Value))) return null;

        var head = Push(combiner, stage, inputs.Select(input => (input.Value, input.Sigma)).ToList());

        var length = inputs.Min(input => input.History.Count);
        var history = new List<double>(length);
        var sigmaHistory = new List<double>(length);
        for (var index = 0; index < length; index++)
        {
            var step = Push(combiner, stage,
                inputs.Select(input => (input.ValueAt(index), input.SigmaAt(index))).ToList());
            history.Add(step.Value);
            sigmaHistory.Add(step.Sigma);
        }

        // A flat σ needs no per-step series, and carrying one would make a constant band look
        // heteroscedastic to anything that checks whether SigmaHistory is populated.
        var varying = sigmaHistory.Count > 0 &&
                      sigmaHistory.All(sigma => !double.IsNaN(sigma)) &&
                      sigmaHistory.Any(sigma => Math.Abs(sigma - head.Sigma) > Math.Abs(head.Sigma) * 1e-9);

        return new TransferResult(
            head.Value,
            head.Sigma,
            ResolveUnit(combiner, stage, inputs),
            history,
            varying ? sigmaHistory : [],
            head.Linearised,
            BuildNote(combiner, stage, inputs.Count, head));
    }

    public static TransferResult? EvaluateEstimator(
        FunctionEstimator estimator, TransferInput? xTrain, TransferInput? yTrain, TransferInput? predict)
    {
        if (EstimatorObjection(xTrain, yTrain, predict) is not null) return null;

        var model = estimator.FitSeries(xTrain!.History, yTrain!.History);
        var note = model.CarriesNaN
            ? model.Summary
            : $"{model.Summary} · refit on every recompute · σ not derived — parameter uncertainty not carried";
        return new TransferResult(
            model.Predict(predict!.Value),
            double.NaN,
            yTrain.Unit,
            predict.History.Select(model.Predict).ToList(),
            [],
            true,
            note);
    }

    public static string? EstimatorObjection(TransferInput? xTrain, TransferInput? yTrain, TransferInput? predict)
    {
        var missing = new List<string>();
        if (xTrain is null) missing.Add("x[]");
        if (yTrain is null) missing.Add("y[]");
        if (predict is null) missing.Add("predict");
        if (missing.Count > 0)
            return $"nothing usable on {string.Join(", ", missing)} — wire the port, or the leaf behind it carries no value";
        if (xTrain!.History.Count != yTrain!.History.Count)
        {
            return $"training series differ — x[] carries {xTrain.History.Count} point(s), y[] {yTrain.History.Count}. " +
                   "Series read from one dataset align row-by-row; pairing these by index would invent data";
        }
        return yTrain.History.Count < 2 ? "a line through fewer than two training points is not a fit" : null;
    }

    // ── comparison ───────────────────────────────────────────────────────────

    private readonly record struct Determination(double Value, double Level);

    /// <summary>
    /// Why the comparison cannot run, or null when it can. Like units only: "18 bbl > 20 h" is
    /// not a statement either way, and evaluating it anyway would dress a category error up as a
    /// determination.
    /// </summary>
    public static string? ComparisonObjection(TransferInput? a, TransferInput? b)
    {
        var missing = new List<string>();
        if (a is null) missing.Add("a");
        if (b is null) missing.Add("b");
        if (missing.Count > 0)
            return $"nothing usable on {string.Join(", ", missing)} — wire the port, or the leaf behind it carries no value";
        if (a!.Unit.Length > 0 && b!.Unit.Length > 0 && a.Unit != b.Unit)
            return $"cannot compare {a.Unit} with {b.Unit} — like units only";
        return null;
    }

    /// <summary>
    /// Compares a against b and states how firmly. The determination is the operator applied to
    /// the readings; the σ level is the margin in σ units, |a−b|/√(σa²+σb²), inputs independent.
    /// Either σ being NaN means unknown variance — the same house rule <see cref="Combine"/>
    /// applies — so the level is withheld rather than computed from a variance nobody measured:
    /// the vacuous regime, shown as "no σ". A spread of exactly zero makes the determination
    /// exact, which an infinite level is the honest spelling of.
    /// </summary>
    public static TransferResult? EvaluateComparison(LibraryFunction operation, TransferInput? a, TransferInput? b)
    {
        if (ComparisonObjection(a, b) is not null) return null;
        if (double.IsNaN(a!.Value) || double.IsNaN(b!.Value)) return null;

        var head = Compare(operation, a.Value, a.Sigma, b.Value, b.Sigma);

        var length = Math.Min(a.History.Count, b.History.Count);
        var determinations = new List<double>(length);
        var levels = new List<double>(length);
        for (var index = 0; index < length; index++)
        {
            var step = Compare(operation, a.ValueAt(index), a.SigmaAt(index), b.ValueAt(index), b.SigmaAt(index));
            determinations.Add(step.Value);
            levels.Add(step.Level);
        }

        var note = double.IsNaN(head.Level)
            ? "no σ level — an input carries no σ · determination stated bare"
            : double.IsPositiveInfinity(head.Level)
                ? "inputs carry no spread — the determination is exact"
                : "σ level = |a−b|/√(σa²+σb²) · inputs independent";

        return new TransferResult(
            head.Value, double.NaN, "", determinations, [],
            Linearised: false,
            Note: note,
            IsBoolean: true,
            SigmaLevel: head.Level,
            SigmaLevelHistory: levels.Any(level => !double.IsNaN(level)) ? levels : null);
    }

    /// <summary>
    /// One comparison step, for callers that pair values row-by-row themselves — the select's
    /// computed columns, whose row order comes from the join rather than from series indices.
    /// </summary>
    public static (double Determination, double SigmaLevel) CompareValues(
        LibraryFunction operation, double a, double sigmaA, double b, double sigmaB)
    {
        var step = Compare(operation, a, sigmaA, b, sigmaB);
        return (step.Value, step.Level);
    }

    private static Determination Compare(LibraryFunction operation, double a, double sigmaA, double b, double sigmaB)
    {
        var determination = operation.Apply([a, b]);
        if (double.IsNaN(sigmaA) || double.IsNaN(sigmaB)) return new Determination(determination, double.NaN);
        var spread = Math.Sqrt(sigmaA * sigmaA + sigmaB * sigmaB);
        return new Determination(
            determination,
            spread > 0 ? Math.Abs(a - b) / spread : double.PositiveInfinity);
    }

    /// <summary>The formula a comparator card titles itself with, e.g. "level > capacity".</summary>
    public static string ComparisonFormula(LibraryFunction? operation, string? aLabel, string? bLabel)
    {
        var formula = operation is null
            ? $"{aLabel ?? "a"} ? {bLabel ?? "b"}"
            : operation.FormatApplied([aLabel ?? "a", bLabel ?? "b"]);
        return formula.Length > 34 ? formula[..33] + "…" : formula;
    }

    public static string EstimatorFormula(string name, string? xLabel, string? yLabel, string? predictLabel)
    {
        var formula = $"{name}({xLabel ?? "x[]"}, {yLabel ?? "y[]"})({predictLabel ?? "…"})";
        return formula.Length > 34 ? formula[..33] + "…" : formula;
    }

    /// <summary>The formula the card shows, e.g. "exp(sum(tank_01.level, tank_02.level))".</summary>
    public static string Formula(TransferCombiner combiner, LibraryFunction? stage, IEnumerable<string> labels)
    {
        var joined = string.Join(", ", labels);
        if (joined.Length == 0) joined = "…";
        var inner = $"{Verb(combiner)}({joined})";
        var formula = stage is null ? inner : stage.FormatApplied([inner]);
        return formula.Length > 34 ? formula[..33] + "…" : formula;
    }

    public static string Verb(TransferCombiner combiner) => combiner switch
    {
        TransferCombiner.Sum => "sum",
        TransferCombiner.Mean => "mean",
        _ => "product"
    };

    private static Step Push(
        TransferCombiner combiner, LibraryFunction? stage, IReadOnlyList<(double Value, double Sigma)> terms)
    {
        var combined = Combine(combiner, terms);
        return stage is null ? combined : Apply(stage, combined);
    }

    /// <summary>
    /// Reduces the inputs, in quadrature. A σ that is not there stays not there: one input without
    /// variance leaves the whole output without it, rather than quietly contributing a zero and
    /// producing a band narrower than the truth.
    /// </summary>
    private static Step Combine(TransferCombiner combiner, IReadOnlyList<(double Value, double Sigma)> terms)
    {
        var bare = terms.Any(term => double.IsNaN(term.Sigma));

        switch (combiner)
        {
            case TransferCombiner.Product:
            {
                var value = terms.Aggregate(1.0, (product, term) => product * term.Value);
                if (bare || terms.Any(term => term.Value == 0)) return new Step(value, double.NaN, true);
                var relative = terms.Sum(term => Square(term.Sigma / term.Value));
                return new Step(value, Math.Abs(value) * Math.Sqrt(relative), true);
            }
            case TransferCombiner.Mean:
            {
                var value = terms.Average(term => term.Value);
                var sigma = bare ? double.NaN : Math.Sqrt(terms.Sum(term => Square(term.Sigma))) / terms.Count;
                return new Step(value, sigma, true);
            }
            default:
            {
                var value = terms.Sum(term => term.Value);
                var sigma = bare ? double.NaN : Math.Sqrt(terms.Sum(term => Square(term.Sigma)));
                return new Step(value, sigma, true);
            }
        }
    }

    /// <summary>
    /// Runs the stage. Affine stages take the Jacobian, which is exact; anything else takes three
    /// sigma points, because linearising a curve at one point and calling the result exact is the
    /// error this whole screen exists to refuse.
    /// </summary>
    private static Step Apply(LibraryFunction stage, Step input)
    {
        var value = stage.ApplyUnary(input.Value);
        if (double.IsNaN(input.Sigma) || input.Sigma <= 0)
            return new Step(value, double.NaN, IsAffine(stage, input.Value, StepSize(input.Value)));

        if (IsAffine(stage, input.Value, StepSize(input.Value)))
        {
            var h = StepSize(input.Value);
            var slope = (stage.ApplyUnary(input.Value + h) - stage.ApplyUnary(input.Value - h)) / (2 * h);
            return new Step(value, Math.Abs(slope) * input.Sigma, true);
        }

        var offset = SigmaPointSpread * input.Sigma;
        var low = stage.ApplyUnary(input.Value - offset);
        var high = stage.ApplyUnary(input.Value + offset);
        var mean = CentreWeight * value + WingWeight * (low + high);
        var variance = CentreWeight * Square(value - mean) +
                       WingWeight * (Square(low - mean) + Square(high - mean));
        return new Step(mean, Math.Sqrt(Math.Max(variance, 0)), false);
    }

    /// <summary>Affine iff the second difference vanishes — checked at the scale of the reading.</summary>
    private static bool IsAffine(LibraryFunction stage, double value, double h)
    {
        var curvature = stage.ApplyUnary(value + h) + stage.ApplyUnary(value - h) - 2 * stage.ApplyUnary(value);
        if (double.IsNaN(curvature)) return false;
        var scale = Math.Max(Math.Abs(stage.ApplyUnary(value)), 1e-9);
        return Math.Abs(curvature) <= scale * 1e-9;
    }

    private static double StepSize(double value) => Math.Max(Math.Abs(value), 1.0) * 1e-5;

    /// <summary>
    /// A unit survives a sum or a mean of like-united inputs and nothing else. exp(bbl) has no unit,
    /// and neither does bbl × bbl in any sense the readout could print.
    /// </summary>
    private static string ResolveUnit(
        TransferCombiner combiner, LibraryFunction? stage, IReadOnlyList<TransferInput> inputs)
    {
        if (stage is not null || combiner == TransferCombiner.Product) return "";
        var unit = inputs[0].Unit;
        return inputs.All(input => input.Unit == unit) ? unit : "";
    }

    private static string BuildNote(TransferCombiner combiner, LibraryFunction? stage, int count, Step head)
    {
        var reduction = combiner == TransferCombiner.Product
            ? $"relative quadrature over {count} input(s)"
            : $"quadrature over {count} input(s)";
        if (stage is null) return double.IsNaN(head.Sigma) ? $"{reduction} · an input carries no σ" : $"σ_out — {reduction}";
        return head.Linearised
            ? $"σ_out = |{stage.Name}′|·σ — exact · {reduction}"
            : $"σ_out by sigma-point transform — {stage.Name} nonlinear, linearisation refused";
    }

    private static double Square(double value) => value * value;
}
