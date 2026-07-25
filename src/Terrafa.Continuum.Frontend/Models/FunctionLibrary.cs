namespace Terrafa.Continuum.Frontend.Models;

public sealed class LibraryFunction
{
    public required string Name { get; init; }
    public required Func<double, double> Apply { get; init; }
    public required Func<string, string> FormatApplied { get; init; }
    public string Note { get; init; } = "";
    public bool IsPrimitive { get; init; }
    public IReadOnlyList<LibraryFunction> Components { get; init; } = [];

    public string StageFormula => FormatApplied("u");

    public string DisplayFormula => IsPrimitive || Components.Count == 0
        ? StageFormula
        : FunctionLibrary.ComposeFormula(Components, "u");
}

public sealed class FunctionLibrary
{
    public static FunctionLibrary Instance { get; } = new();

    private readonly List<LibraryFunction> userFunctions = [];

    public IReadOnlyList<LibraryFunction> Primitives { get; }

    public IReadOnlyList<LibraryFunction> UserFunctions => userFunctions;

    private FunctionLibrary()
    {
        Primitives =
        [
            Primitive("square", u => u * u, inner => $"{Wrap(inner)}²", "C∞ · monotone on u>0"),
            Primitive("reciprocal", u => 1 / u, inner => $"1/{Wrap(inner)}", "C¹ on u≠0 · pole flagged"),
            Primitive("exp", Math.Exp, inner => $"exp({inner})", "C∞ · monotone"),
            Primitive("log", Math.Log, inner => $"log({inner})", "domain u>0"),
            Primitive("sqrt", Math.Sqrt, inner => $"√{Wrap(inner)}", "domain u≥0"),
            Primitive("sum", u => u + 1.0, inner => $"{inner} + 1", "affine · c = 1.0"),
            Primitive("multiply", u => 2.0 * u, inner => $"2·{Wrap(inner)}", "linear · c = 2.0"),
            Primitive("clip", u => Math.Clamp(u, -1.0, 1.0), inner => $"clip({inner}, −1, 1)", "C⁰ · lo=−1 hi=1"),
            Primitive("sin", Math.Sin, inner => $"sin({inner})", "C∞ · bounded"),
            Primitive("tanh", Math.Tanh, inner => $"tanh({inner})", "C∞ · bounded"),
            Primitive("negate", u => -u, inner => $"−{Wrap(inner)}", "linear")
        ];
    }

    public LibraryFunction? FindUserFunction(string name) =>
        userFunctions.FirstOrDefault(function => function.Name == name);

    public bool IsPrimitiveName(string name) =>
        Primitives.Any(function => function.Name == name);

    public LibraryFunction SaveComposite(string name, IReadOnlyList<LibraryFunction> stages)
    {
        var components = stages.ToArray();
        var composite = new LibraryFunction
        {
            Name = name,
            Apply = x => ApplyStages(components, x),
            FormatApplied = inner => $"{name}({inner})",
            Note = $"composite · {components.Length} stage(s) · {ComposeFormula(components, "x")}",
            IsPrimitive = false,
            Components = components
        };
        var replacedIndex = userFunctions.FindIndex(function => function.Name == name);
        if (replacedIndex >= 0)
            userFunctions[replacedIndex] = composite;
        else
            userFunctions.Add(composite);
        return composite;
    }

    public static double ApplyStages(IReadOnlyList<LibraryFunction> stages, double x)
    {
        var value = x;
        foreach (var stage in stages)
            value = stage.Apply(value);
        return value;
    }

    public static string ComposeFormula(IReadOnlyList<LibraryFunction> stages, string variable)
    {
        var formula = variable;
        foreach (var stage in stages)
            formula = stage.FormatApplied(formula);
        return formula.Length > 60 ? formula[..57] + "…" : formula;
    }

    private static LibraryFunction Primitive(
        string name, Func<double, double> apply, Func<string, string> format, string note) =>
        new() { Name = name, Apply = apply, FormatApplied = format, Note = note, IsPrimitive = true };

    private static string Wrap(string inner) => inner.Length == 1 ? inner : $"({inner})";
}
