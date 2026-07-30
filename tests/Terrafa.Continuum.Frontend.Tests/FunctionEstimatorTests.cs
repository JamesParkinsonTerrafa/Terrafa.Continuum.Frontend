using Terrafa.Continuum.Analytics.Regression;
using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Tests;

[Collection("function library")]
public class FunctionEstimatorTests
{
    private static readonly FunctionLibrary Library = FunctionLibrary.Instance;

    private static FunctionEstimator Linear =>
        Library.FindEstimator("fit_linear") ?? throw new InvalidOperationException("fit_linear missing from library");

    private static TransferInput Series(string label, double value, string unit, double[] history) =>
        new(label, value, double.NaN, unit, history, []);

    [Fact]
    public void RegressionGroupCarriesTheLinearEstimator()
    {
        Assert.Contains(FunctionLibrary.RegressionGroup, FunctionLibrary.EstimatorGroups);
        Assert.DoesNotContain(FunctionLibrary.RegressionGroup, FunctionLibrary.PlannedGroups);
        Assert.Equal(["fit_linear"], Library.EstimatorsInGroup(FunctionLibrary.RegressionGroup).Select(e => e.Name));
        Assert.Equal("HIGHER-ORDER", Linear.ArityLabel);
        Assert.Equal("(x[], y[]) → f(x)", Linear.SignatureText);
    }

    [Fact]
    public void FitSeriesReturnsThePredictor()
    {
        var model = Linear.FitSeries([1.0, 2.0, 3.0, 4.0], [5.0, 7.0, 9.0, 11.0]);

        Assert.Equal(13.0, model.Predict(5.0), 10);
        Assert.False(model.CarriesNaN);
        Assert.Contains("ŷ = 2·x + 3", model.Summary);
        Assert.Contains("R² 1", model.Summary);
        Assert.Contains("n 4", model.Summary);
    }

    [Fact]
    public void NegativeInterceptKeepsItsSignInTheSummary()
    {
        var model = Linear.FitSeries([0.0, 1.0, 2.0], [-3.0, -1.0, 1.0]);

        Assert.Contains("ŷ = 2·x − 3", model.Summary);
        Assert.Equal(1.0, model.Predict(2.0), 10);
    }

    [Fact]
    public void NaNTrainingReadingPropagates()
    {
        var model = Linear.FitSeries([0.0, 1.0, 2.0], [1.0, double.NaN, 3.0]);

        Assert.True(model.CarriesNaN);
        Assert.True(double.IsNaN(model.Predict(0.0)));
        Assert.Contains("carries NaN", model.Summary);
    }

    [Fact]
    public void EvaluateEstimatorPredictsFromWiredInputs()
    {
        var result = TransferMath.EvaluateEstimator(
            Linear,
            Series("ts", 4.0, "s", [1.0, 2.0, 3.0, 4.0]),
            Series("level", 11.0, "bbl", [5.0, 7.0, 9.0, 11.0]),
            Series("probe", 5.0, "s", [1.0, 5.0]));

        Assert.NotNull(result);
        Assert.Equal(13.0, result.Value, 10);
        Assert.Equal("bbl", result.Unit);
        Assert.True(double.IsNaN(result.Sigma));
        Assert.True(result.Linearised);
        Assert.Equal([5.0, 13.0], result.History.Select(value => Math.Round(value, 9)));
        Assert.Contains("ŷ = 2·x + 3", result.Note);
        Assert.Contains("refit on every recompute", result.Note);
        Assert.Contains("σ not derived", result.Note);
    }

    [Fact]
    public void MissingPortIsObjectedNotEvaluated()
    {
        var x = Series("ts", 4.0, "s", [1.0, 2.0]);
        var y = Series("level", 7.0, "bbl", [5.0, 7.0]);

        Assert.Null(TransferMath.EvaluateEstimator(Linear, x, y, null));
        var objection = TransferMath.EstimatorObjection(x, y, null);
        Assert.NotNull(objection);
        Assert.Contains("predict", objection);
    }

    [Fact]
    public void MismatchedTrainingSeriesAreRefused()
    {
        var x = Series("ts", 2.0, "s", [1.0, 2.0]);
        var y = Series("level", 9.0, "bbl", [5.0, 7.0, 9.0]);
        var predict = Series("probe", 1.0, "s", [1.0]);

        Assert.Null(TransferMath.EvaluateEstimator(Linear, x, y, predict));
        Assert.Contains("differ", TransferMath.EstimatorObjection(x, y, predict));
    }

    [Fact]
    public void TooFewTrainingPointsAreRefused()
    {
        var x = Series("ts", 1.0, "s", [1.0]);
        var y = Series("level", 5.0, "bbl", [5.0]);
        var predict = Series("probe", 1.0, "s", [1.0]);

        Assert.Null(TransferMath.EvaluateEstimator(Linear, x, y, predict));
        Assert.Contains("not a fit", TransferMath.EstimatorObjection(x, y, predict));
    }
}

[Collection("function library")]
public class EstimatorNetworkTests
{
    private static readonly NetworkGraph Graph = NetworkGraph.Instance;
    private static readonly Workspace SharedWorkspace = Workspace.Instance;

    private static string LeafPath(string suffix) =>
        $"{SharedWorkspace.Subtrees[0].Root.Path}.tank_farm.{suffix}";

    private static NetworkNode BuildWiredRegressor(out string xPath, out string yPath, out string predictPath)
    {
        xPath = LeafPath("tank_01.temp");
        yPath = LeafPath("tank_01.level");
        predictPath = LeafPath("tank_02.level");
        Graph.PlaceMeasure(xPath, 0, 0);
        Graph.PlaceMeasure(yPath, 0, 100);
        Graph.PlaceMeasure(predictPath, 0, 200);
        var regressor = Graph.AddEstimator("fit_linear", 300, 100);
        Graph.Connect(xPath, regressor.Id);
        Graph.Connect(yPath, regressor.Id);
        Graph.Connect(predictPath, regressor.Id);
        return regressor;
    }

    [Fact]
    public void PortsFollowWiringOrderAndCapAtThree()
    {
        Graph.Reset(seedDemo: false);
        try
        {
            var regressor = BuildWiredRegressor(out var xPath, out var yPath, out var predictPath);

            Assert.Equal(xPath, Graph.SourceOnPort(regressor, NetworkGraph.EstimatorPortX));
            Assert.Equal(yPath, Graph.SourceOnPort(regressor, NetworkGraph.EstimatorPortY));
            Assert.Equal(predictPath, Graph.SourceOnPort(regressor, NetworkGraph.EstimatorPortPredict));

            var fourth = LeafPath("tank_01.spoilage");
            Graph.PlaceMeasure(fourth, 0, 300);
            Assert.False(Graph.CanConnect(fourth, regressor.Id));

            Graph.Remove(xPath);
            Assert.Null(Graph.SourceOnPort(regressor, NetworkGraph.EstimatorPortX));
            Assert.True(Graph.Connect(fourth, regressor.Id));
            Assert.Equal(fourth, Graph.SourceOnPort(regressor, NetworkGraph.EstimatorPortX));
        }
        finally
        {
            Graph.Reset(seedDemo: true);
        }
    }

    [Fact]
    public void EvaluateRefitsFromTheWiredSeries()
    {
        Graph.Reset(seedDemo: false);
        try
        {
            var regressor = BuildWiredRegressor(out var xPath, out var yPath, out var predictPath);
            var x = SharedWorkspace.FindNode(xPath)!.Reading!;
            var y = SharedWorkspace.FindNode(yPath)!.Reading!;
            var predict = SharedWorkspace.FindNode(predictPath)!.Reading!;
            Assert.Equal(x.History.Count, y.History.Count);
            var fit = LinearRegression.Fit(x.History, y.History);

            var result = Graph.Evaluate(regressor);

            Assert.NotNull(result);
            Assert.Equal(fit.Predict(predict.Value), result.Value, 9);
            Assert.Equal(y.Unit, result.Unit);
            Assert.True(double.IsNaN(result.Sigma));
            Assert.Equal(predict.History.Select(fit.Predict).ToList(), result.History);

            Graph.SwapTrainingWires(regressor);
            var swappedFit = LinearRegression.Fit(y.History, x.History);
            var swapped = Graph.Evaluate(regressor);
            Assert.NotNull(swapped);
            Assert.Equal(swappedFit.Predict(predict.Value), swapped.Value, 9);
            Assert.Equal(x.Unit, swapped.Unit);
        }
        finally
        {
            Graph.Reset(seedDemo: true);
        }
    }

    [Fact]
    public void RotatePortRolesCyclesEveryWire()
    {
        Graph.Reset(seedDemo: false);
        try
        {
            var regressor = BuildWiredRegressor(out var xPath, out var yPath, out var predictPath);

            Graph.RotatePortRoles(regressor);

            Assert.Equal(predictPath, Graph.SourceOnPort(regressor, NetworkGraph.EstimatorPortX));
            Assert.Equal(xPath, Graph.SourceOnPort(regressor, NetworkGraph.EstimatorPortY));
            Assert.Equal(yPath, Graph.SourceOnPort(regressor, NetworkGraph.EstimatorPortPredict));
        }
        finally
        {
            Graph.Reset(seedDemo: true);
        }
    }

    [Fact]
    public void EstimatorTransferCommitsAPredictedFigure()
    {
        Graph.Reset(seedDemo: false);
        try
        {
            var regressor = BuildWiredRegressor(out var xPath, out var yPath, out var predictPath);
            var figure = Graph.AddFigure("test_predicted_level", 600, 100);
            Graph.Connect(regressor.Id, figure.Id);

            var x = SharedWorkspace.FindNode(xPath)!.Reading!;
            var y = SharedWorkspace.FindNode(yPath)!.Reading!;
            var predict = SharedWorkspace.FindNode(predictPath)!.Reading!;
            var fit = LinearRegression.Fit(x.History, y.History);

            var committed = FigureCatalog.Instance.Find("test_predicted_level");
            Assert.NotNull(committed);
            Assert.True(committed.HasValue);
            Assert.Equal(fit.Predict(predict.Value), committed.Value, 9);
            Assert.Contains("refit on every recompute", committed.Note);
        }
        finally
        {
            Graph.Reset(seedDemo: true);
        }
    }

    [Fact]
    public void CycleOpsRefuseEstimatorTransfers()
    {
        Graph.Reset(seedDemo: false);
        try
        {
            var regressor = Graph.AddEstimator("fit_linear", 0, 0);

            Graph.CycleStage(regressor);
            Graph.CycleCombiner(regressor);

            Assert.Equal("", regressor.Stage);
            Assert.Equal(TransferCombiner.Sum, regressor.Combiner);
            Assert.True(regressor.IsEstimator);
        }
        finally
        {
            Graph.Reset(seedDemo: true);
        }
    }
}
