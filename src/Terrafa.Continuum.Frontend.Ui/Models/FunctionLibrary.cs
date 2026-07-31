// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Analytics.Regression;

namespace Terrafa.Continuum.Frontend.Models;

public enum PortKind
{
    Scalar,
    Array
}

public sealed record FunctionPort(string Name, PortKind Kind);

public sealed class LibraryFunction
{
    public required string Name { get; init; }
    public required IReadOnlyList<FunctionPort> Inputs { get; init; }
    public required Func<IReadOnlyList<double>, double> Apply { get; init; }
    public required Func<IReadOnlyList<string>, string> FormatApplied { get; init; }
    public string Note { get; init; } = "";
    public string Group { get; init; } = "";
    public bool IsPrimitive { get; init; }
    public CompositionNode? Definition { get; init; }

    public bool HasArrayInput => Inputs.Any(port => port.Kind == PortKind.Array);

    public bool IsUnaryScalar => Inputs is [{ Kind: PortKind.Scalar }];

    public string ArityLabel => HasArrayInput ? "AGGREGATE" : Inputs.Count switch
    {
        1 => "UNARY",
        2 => "BINARY",
        _ => $"{Inputs.Count}-ARY"
    };

    public string SignatureText => HasArrayInput
        ? $"({Inputs[0].Name}1 … {Inputs[0].Name}n) → y"
        : $"({string.Join(", ", Inputs.Select(port => port.Name))}) → y";

    public IReadOnlyList<string> PlaceholderArguments => HasArrayInput
        ? [$"{Inputs[0].Name}1", "…", $"{Inputs[0].Name}n"]
        : Inputs.Select(port => port.Name).ToArray();

    public string DisplayFormula => Trim(
        Definition is null ? FormatApplied(PlaceholderArguments) : Definition.Formula("x"));

    public double ApplyUnary(double u) => IsUnaryScalar
        ? Apply([u])
        : throw new InvalidOperationException($"{Name} is {ArityLabel.ToLowerInvariant()}, not unary");

    private static string Trim(string formula) =>
        formula.Length > 60 ? formula[..57] + "…" : formula;
}

public sealed class FunctionLibrary
{
    public const string ArithmeticGroup = "arithmetic";
    public const string LogExpGroup = "log / exp";
    public const string PowerGroup = "power";
    public const string ClipsGroup = "clips";
    public const string TrigonometricGroup = "trigonometric";
    public const string AggregatesGroup = "aggregates";
    public const string CompositesGroup = "composites";
    public const string RegressionGroup = "regression";

    public static IReadOnlyList<string> PrimitiveGroups { get; } =
        [ArithmeticGroup, LogExpGroup, PowerGroup, ClipsGroup, TrigonometricGroup, AggregatesGroup];

    public static IReadOnlyList<string> EstimatorGroups { get; } = [RegressionGroup];

    public static IReadOnlyList<string> PlannedGroups { get; } =
        ["clustering", "optimisation"];

    public static FunctionLibrary Instance { get; } = new();

    private readonly List<LibraryFunction> userFunctions = [];

    /// <summary>Raised when the saved composites change — the durable part of the library.</summary>
    public event Action? Changed;

    public IReadOnlyList<LibraryFunction> Primitives { get; }

    public IReadOnlyList<FunctionEstimator> Estimators { get; }

    public IReadOnlyList<LibraryFunction> UserFunctions => userFunctions;

    private FunctionLibrary()
    {
        Primitives =
        [
            Binary("add", ArithmeticGroup, (a, b) => a + b, (a, b) => $"{a} + {b}", "commutative"),
            Binary("subtract", ArithmeticGroup, (a, b) => a - b, (a, b) => $"{a} − {WrapTerm(b)}", ""),
            Binary("multiply", ArithmeticGroup, (a, b) => a * b, (a, b) => $"{WrapFactor(a)}·{WrapFactor(b)}", "commutative"),
            Binary("divide", ArithmeticGroup, (a, b) => a / b, (a, b) => $"{WrapFactor(a)}/{WrapFactor(b)}", "pole at b=0 flagged"),
            Unary("negate", ArithmeticGroup, u => -u, inner => $"−{Wrap(inner)}", "linear"),
            Unary("log", LogExpGroup, Math.Log, inner => $"log({inner})", "domain u>0"),
            Unary("exp", LogExpGroup, Math.Exp, inner => $"exp({inner})", "C∞ · monotone"),
            Unary("square", PowerGroup, u => u * u, inner => $"{Wrap(inner)}²", "C∞ · monotone on u>0"),
            Unary("sqrt", PowerGroup, Math.Sqrt, inner => $"√{Wrap(inner)}", "domain u≥0"),
            Unary("reciprocal", PowerGroup, u => 1 / u, inner => $"1/{Wrap(inner)}", "C¹ on u≠0 · pole flagged"),
            Unary("clip", ClipsGroup, u => Math.Clamp(u, -1.0, 1.0), inner => $"clip({inner}, −1, 1)", "C⁰ · lo=−1 hi=1"),
            Unary("floor", ClipsGroup, Math.Floor, inner => $"⌊{inner}⌋", "piecewise constant · steps at integers"),
            Unary("sin", TrigonometricGroup, Math.Sin, inner => $"sin({inner})", "C∞ · bounded"),
            Unary("tanh", TrigonometricGroup, Math.Tanh, inner => $"tanh({inner})", "C∞ · bounded"),
            Aggregate("max", values => values.Max(), "upper envelope of its arguments"),
            Aggregate("min", values => values.Min(), "lower envelope of its arguments"),
            Aggregate("mean", values => values.Average(), "equal-weight average")
        ];
        Estimators =
        [
            new FunctionEstimator
            {
                Name = "fit_linear",
                Group = RegressionGroup,
                DisplayFormula = "y ≈ a + b·x",
                Note = "least squares over two wired series · the fitted line predicts a third input · lives on the NETWORK canvas",
                FitSeries = FitLine
            }
        ];
    }

    public IReadOnlyList<LibraryFunction> PrimitivesInGroup(string group) =>
        Primitives.Where(function => function.Group == group).ToArray();

    public IReadOnlyList<FunctionEstimator> EstimatorsInGroup(string group) =>
        Estimators.Where(estimator => estimator.Group == group).ToArray();

    public FunctionEstimator? FindEstimator(string name) =>
        Estimators.FirstOrDefault(estimator => estimator.Name == name);

    private static FittedModel FitLine(IReadOnlyList<double> xTrain, IReadOnlyList<double> yTrain)
    {
        var fit = LinearRegression.Fit(xTrain, yTrain);
        var carriesNaN = double.IsNaN(fit.Slope) || double.IsNaN(fit.Intercept);
        var summary = carriesNaN
            ? "fit carries NaN — a training reading is not plottable"
            : $"ŷ = {ConstantNode.Format(fit.Slope)}·x {(fit.Intercept < 0 ? "−" : "+")} " +
              $"{ConstantNode.Format(Math.Abs(fit.Intercept))} · R² {ConstantNode.Format(fit.RSquared)} · n {yTrain.Count}";
        return new FittedModel(fit.Predict, summary, carriesNaN);
    }

    public LibraryFunction? FindUserFunction(string name) =>
        userFunctions.FirstOrDefault(function => function.Name == name);

    /// <summary>
    /// Any function by name, primitive or saved. A network transfer stores the name rather than the
    /// function, so a composite the operator saves on the TRANSFER FN screen is usable as a stage
    /// without the network holding a reference to a definition that may since have been replaced.
    /// </summary>
    public LibraryFunction? Find(string name) =>
        Primitives.FirstOrDefault(function => function.Name == name) ?? FindUserFunction(name);

    public bool IsPrimitiveName(string name) =>
        Primitives.Any(function => function.Name == name);

    public LibraryFunction SaveComposite(string name, CompositionNode root)
    {
        var composite = BuildComposite(name, root);
        var replacedIndex = userFunctions.FindIndex(function => function.Name == name);
        if (replacedIndex >= 0)
            userFunctions[replacedIndex] = composite;
        else
            userFunctions.Add(composite);
        Changed?.Invoke();
        return composite;
    }

    /// <summary>Replaces the saved composites with loaded state, announcing once.</summary>
    public void LoadUserFunctions(IEnumerable<LibraryFunction> loaded)
    {
        userFunctions.Clear();
        userFunctions.AddRange(loaded);
        Changed?.Invoke();
    }

    /// <summary>A composite as <see cref="SaveComposite"/> builds it, without registering it —
    /// durable-state restore builds them ahead so one composite can reference another.</summary>
    public static LibraryFunction BuildComposite(string name, CompositionNode root)
    {
        var definition = root.Clone();
        return new LibraryFunction
        {
            Name = name,
            Inputs = [new FunctionPort("x", PortKind.Scalar)],
            Apply = arguments => definition.Evaluate(arguments[0]),
            FormatApplied = arguments => $"{name}({arguments[0]})",
            Note = $"composite · {definition.CountFunctionNodes()} node(s) · depth {definition.Depth()}",
            Group = CompositesGroup,
            IsPrimitive = false,
            Definition = definition
        };
    }

    private static LibraryFunction Unary(
        string name, string group, Func<double, double> apply, Func<string, string> format, string note) => new()
    {
        Name = name,
        Inputs = [new FunctionPort("u", PortKind.Scalar)],
        Apply = arguments => apply(arguments[0]),
        FormatApplied = arguments => format(arguments[0]),
        Note = note,
        Group = group,
        IsPrimitive = true
    };

    private static LibraryFunction Binary(
        string name, string group, Func<double, double, double> apply, Func<string, string, string> format, string note) => new()
    {
        Name = name,
        Inputs = [new FunctionPort("a", PortKind.Scalar), new FunctionPort("b", PortKind.Scalar)],
        Apply = arguments => apply(arguments[0], arguments[1]),
        FormatApplied = arguments => format(arguments[0], arguments[1]),
        Note = note,
        Group = group,
        IsPrimitive = true
    };

    private static LibraryFunction Aggregate(
        string name, Func<IReadOnlyList<double>, double> apply, string note) => new()
    {
        Name = name,
        Inputs = [new FunctionPort("u", PortKind.Array)],
        Apply = apply,
        FormatApplied = arguments => $"{name}({string.Join(", ", arguments)})",
        Note = note,
        Group = AggregatesGroup,
        IsPrimitive = true
    };

    private static string Wrap(string inner) => inner.Length == 1 ? inner : $"({inner})";

    private static string WrapTerm(string inner) => inner.Contains(" + ") || inner.Contains(" − ")
        ? $"({inner})"
        : inner;

    private static string WrapFactor(string inner) => inner.Contains(' ')
        ? $"({inner})"
        : inner;
}
