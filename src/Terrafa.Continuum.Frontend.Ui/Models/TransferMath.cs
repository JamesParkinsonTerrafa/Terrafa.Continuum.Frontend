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
    IReadOnlyList<double> SigmaHistory)
{
    public double ValueAt(int index) => index >= 0 && index < History.Count ? History[index] : Value;

    public double SigmaAt(int index) => index >= 0 && index < SigmaHistory.Count ? SigmaHistory[index] : Sigma;
}

/// <summary>What came out of a transfer, in the same shape a leaf reading arrives in.</summary>
public sealed record TransferResult(
    double Value,
    double Sigma,
    string Unit,
    IReadOnlyList<double> History,
    IReadOnlyList<double> SigmaHistory,
    bool Linearised,
    string Note)
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
