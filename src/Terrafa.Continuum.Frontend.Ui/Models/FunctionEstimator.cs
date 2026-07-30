// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Models;

public sealed record FittedModel(Func<double, double> Predict, string Summary, bool CarriesNaN);

public sealed class FunctionEstimator
{
    public required string Name { get; init; }
    public required string Group { get; init; }
    public required string Note { get; init; }
    public required string DisplayFormula { get; init; }
    public required Func<IReadOnlyList<double>, IReadOnlyList<double>, FittedModel> FitSeries { get; init; }

    public string ArityLabel => "HIGHER-ORDER";

    public string SignatureText => "(x[], y[]) → f(x)";
}
