// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Tests;

/// <summary>
/// Guards the comparator: the determination, the σ level behind it, and the graph mechanics that
/// wire one. The σ level is the point — a bare true/false out of measured inputs would claim a
/// certainty the inputs never had, so every determination states how many σ it holds by, or that
/// it cannot say.
/// </summary>
[Collection("workspace")]
public class ComparatorTests
{
    private static LibraryFunction Op(string name) => FunctionLibrary.Instance.Find(name)!;

    private static TransferInput Input(
        double value, double sigma, string unit = "",
        double[]? history = null, double[]? sigmaHistory = null) =>
        new("input", value, sigma, unit, history ?? [], sigmaHistory ?? []);

    [Fact]
    public void TheSigmaLevelIsTheMarginInSigmaUnits()
    {
        var result = TransferMath.EvaluateComparison(Op("greater_than"), Input(10, 3), Input(4, 4));

        Assert.NotNull(result);
        Assert.True(result.IsBoolean);
        Assert.Equal(1, result.Value);

        // |10−4| / √(3² + 4²) = 6/5 — the degree greater, in σ units.
        Assert.Equal(1.2, result.SigmaLevel, 12);

        // Firmness is not variance: the determination carries no σ of its own.
        Assert.True(double.IsNaN(result.Sigma));
    }

    /// <summary>
    /// NaN σ means unknown variance, not zero — the same house rule the transfers follow. The
    /// determination still states, bare; the level is withheld rather than computed from a
    /// variance nobody measured. This is the vacuous regime.
    /// </summary>
    [Fact]
    public void AnUnknownSigmaWithholdsTheLevelRatherThanInventingIt()
    {
        var result = TransferMath.EvaluateComparison(Op("greater_than"), Input(10, 3), Input(4, double.NaN));

        Assert.NotNull(result);
        Assert.Equal(1, result.Value);
        Assert.True(double.IsNaN(result.SigmaLevel));
        Assert.Contains("no σ level", result.Note);
    }

    /// <summary>
    /// Strict and non-strict differ exactly on the tie, and a tie between exact inputs is exact —
    /// an infinite level, printed as such rather than as a number pretending to be one.
    /// </summary>
    [Fact]
    public void ExactInputsMakeTheDeterminationExact()
    {
        var strict = TransferMath.EvaluateComparison(Op("greater_than"), Input(5, 0), Input(5, 0));
        var nonStrict = TransferMath.EvaluateComparison(Op("greater_equal"), Input(5, 0), Input(5, 0));

        Assert.Equal(0, strict!.Value);
        Assert.Equal(1, nonStrict!.Value);
        Assert.True(double.IsPositiveInfinity(strict.SigmaLevel));
        Assert.Equal("exact", MeasureNumerics.FormatSigmaLevel(strict.SigmaLevel));
    }

    [Fact]
    public void UnlikeUnitsAreRefusedNotCompared()
    {
        Assert.Null(TransferMath.EvaluateComparison(Op("greater_than"), Input(18, 1, "bbl"), Input(20, 1, "h")));

        var objection = TransferMath.ComparisonObjection(Input(18, 1, "bbl"), Input(20, 1, "h"));
        Assert.NotNull(objection);
        Assert.Contains("like units only", objection);
    }

    [Fact]
    public void TheComparisonRunsRowByRowAndCarriesALevelPerRow()
    {
        var result = TransferMath.EvaluateComparison(
            Op("greater_than"),
            Input(10, 3, history: [1, 10]),
            Input(5, 4, history: [5, 5]));

        Assert.Equal([0, 1], result!.History);
        Assert.NotNull(result.SigmaLevelHistory);
        Assert.Equal(0.8, result.SigmaLevelHistory![0], 12);
        Assert.Equal(1.0, result.SigmaLevelHistory[1], 12);
    }

    [Fact]
    public void AComparatorWiresItsPortsInOrderSwapsThemAndRefusesAThird()
    {
        var graph = NetworkGraph.Instance;
        graph.Reset(seedDemo: false);
        try
        {
            var left = graph.PlaceMeasure("t.a", 0, 0);
            var right = graph.PlaceMeasure("t.b", 0, 100);
            var third = graph.PlaceMeasure("t.c", 0, 200);
            var comparator = graph.AddComparator(300, 50);

            Assert.True(graph.Connect(left.Id, comparator.Id));
            Assert.True(graph.Connect(right.Id, comparator.Id));
            Assert.Equal(NetworkGraph.ComparePortA, graph.PortOf(left.Id, comparator.Id));
            Assert.Equal(NetworkGraph.ComparePortB, graph.PortOf(right.Id, comparator.Id));

            // Two roles, so a third wire has nowhere to land.
            Assert.False(graph.CanConnect(third.Id, comparator.Id));

            graph.SwapCompareWires(comparator);
            Assert.Equal(NetworkGraph.ComparePortB, graph.PortOf(left.Id, comparator.Id));
            Assert.Equal(NetworkGraph.ComparePortA, graph.PortOf(right.Id, comparator.Id));
        }
        finally
        {
            graph.Reset(seedDemo: true);
        }
    }

    [Fact]
    public void AComparatorSurvivesTheNetworkDocumentRoundTrip()
    {
        var graph = NetworkGraph.Instance;
        graph.Reset(seedDemo: false);
        try
        {
            var comparator = graph.AddComparator(100, 100);
            graph.CycleOperator(comparator);
            Assert.Equal("greater_equal", comparator.Operator);

            var state = UserStateMapper.CaptureNetwork();
            graph.Reset(seedDemo: false);
            UserStateMapper.ApplyNetwork(state);

            var restored = Assert.Single(graph.Nodes, node => node.Kind == NetworkNodeKind.Compare);
            Assert.Equal(comparator.Id, restored.Id);
            Assert.Equal("greater_equal", restored.Operator);

            // The counter resumes past the load, so a new comparator cannot collide with it.
            Assert.NotEqual(restored.Id, graph.AddComparator(0, 0).Id);
        }
        finally
        {
            graph.Reset(seedDemo: true);
        }
    }

    /// <summary>
    /// R1: a categorical leaf wired into a transfer is a category error the card must state. An
    /// unsampled leaf stays quiet — it may yet read as numbers — which is the distinction between
    /// "will never be a quantity" and "is not one yet".
    /// </summary>
    [Fact]
    public void TheCheckerObjectsToACategoricalLeafButNotAnUnsampledOne()
    {
        var graph = NetworkGraph.Instance;
        graph.Reset(seedDemo: false);

        var root = new DataTreeNode { Name = "cat_ds", Path = "cat_ds", Kind = DataNodeKind.Object };
        root.Children.Add(new DataTreeNode
        {
            Name = "productid",
            Path = "cat_ds.productid",
            Kind = DataNodeKind.Measure,
            Reading = new Measure { Display = "EN590", Cells = ["EN590", "FAME"] }
        });
        root.Children.Add(new DataTreeNode
        {
            Name = "pending",
            Path = "cat_ds.pending",
            Kind = DataNodeKind.Measure,
            Reading = new Measure { Display = "—" }
        });
        ReadingStore.Instance.Write(new DatasetSchema("cat_ds", "test", "table", "—", "—", "—", root));

        try
        {
            var categorical = graph.PlaceMeasure("cat_ds.productid", 0, 0);
            var unsampled = graph.PlaceMeasure("cat_ds.pending", 0, 100);
            var transfer = graph.AddTransfer(300, 50);
            graph.Connect(categorical.Id, transfer.Id);
            graph.Connect(unsampled.Id, transfer.Id);

            var objection = Assert.Single(NetworkChecker.Check(graph));
            Assert.Equal(transfer.Id, objection.NodeId);
            Assert.Contains("categorical", objection.Message);
        }
        finally
        {
            graph.Reset(seedDemo: true);
            ReadingStore.Instance.Clear();
        }
    }

    /// <summary>R1's other half: a determination is not a quantity a transfer can combine.</summary>
    [Fact]
    public void TheCheckerObjectsToADeterminationWiredIntoATransfer()
    {
        var graph = NetworkGraph.Instance;
        graph.Reset(seedDemo: false);

        var root = new DataTreeNode { Name = "bool_ds", Path = "bool_ds", Kind = DataNodeKind.Object };
        root.Children.Add(new DataTreeNode
        {
            Name = "on_spec",
            Path = "bool_ds.on_spec",
            Kind = DataNodeKind.Measure,
            Reading = new Measure { Display = "true", Value = 1, IsBoolean = true }
        });
        ReadingStore.Instance.Write(new DatasetSchema("bool_ds", "test", "table", "—", "—", "—", root));

        try
        {
            var determination = graph.PlaceMeasure("bool_ds.on_spec", 0, 0);
            var transfer = graph.AddTransfer(300, 50);
            graph.Connect(determination.Id, transfer.Id);

            var objection = Assert.Single(NetworkChecker.Check(graph));
            Assert.Equal(transfer.Id, objection.NodeId);
            Assert.Contains("determination", objection.Message);
        }
        finally
        {
            graph.Reset(seedDemo: true);
            ReadingStore.Instance.Clear();
        }
    }
}
