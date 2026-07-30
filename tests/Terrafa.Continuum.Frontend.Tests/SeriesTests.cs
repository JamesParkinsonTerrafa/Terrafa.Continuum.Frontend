// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Net;
using System.Text;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Tests;

/// <summary>
/// Guards the path from a data response to something a chart can draw.
///
/// <para>
/// The failure these exist for built and ran clean: every leaf out of the real catalogue arrived
/// with an empty <see cref="Measure.History"/>, so a tile wired to one drew nothing and said
/// "source carries no history to plot" — a break with no exception, no log line, and no failing
/// test anywhere between the query and the tile. These assert on the numbers a leaf ends up
/// holding, which is the thing the dashboard actually consumes.
/// </para>
///
/// <para>
/// The transport is stubbed rather than live: the interesting behaviour here is the client's, and
/// the assertions are about ordering and arithmetic that a real dataset would make harder to pin
/// down, not easier.
/// </para>
/// </summary>
public class SeriesTests
{
    private const string Database = "synthetic_dev";
    private const string Table = "lig_biodiesel_calibrated";
    private const string Dataset = $"{Database}.{Table}";

    /// <summary>
    /// The one that matters: rows off the wire become the series behind the leaf, oldest first.
    ///
    /// <para>
    /// The response is newest-first because the query sorts descending — the service caps the read
    /// after ordering, so ascending would return a long table's oldest rows. Asserting the exact
    /// sequence is what catches a reversal being dropped: a chart drawn backwards looks entirely
    /// plausible, and only a monotonic fixture makes it visible.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OrderedRowsBecomeTheLeafsHistory_OldestFirst()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("cell_concentration_umol_l", "double")],
            rows:
            [
                ["400", "40.5"],
                ["300", "30.5"],
                ["200", "20.5"],
                ["100", "10.5"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var schema = await catalog.GetSeriesAsync(Dataset, "timestamp");
        var leaf = Leaf(schema, "cell_concentration_umol_l");

        Assert.Equal([10.5, 20.5, 30.5, 40.5], leaf.History);

        // The reading is the most recent row, not whichever one happened to come back first — the
        // bug this replaces took Rows[0] out of an unordered result and called it the value.
        Assert.Equal(40.5, leaf.Value);
        Assert.Equal("40.5", leaf.Display);
    }

    /// <summary>
    /// The sort must actually be asked for. Without it the service returns rows in whatever order
    /// the engine produced, and every assertion above would still pass on a fixture that happens to
    /// arrive sorted — so this checks the request rather than the result.
    /// </summary>
    [Fact]
    public async Task TheDataRequestSortsOnTheAxis_AndSelectsItFirst()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("level", "double")],
            rows: [["200", "2"], ["100", "1"]]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        await catalog.GetSeriesAsync(Dataset, "timestamp");

        var request = Assert.Single(transport.Requests, url => url.Contains("/data?"));
        Assert.Contains("orderBy=timestamp%20desc", request);

        // The axis leads the projection so the column cap can never cut the one column the
        // request cannot do without.
        Assert.Contains("?columns=timestamp&", request);
    }

    /// <summary>
    /// A text column keeps its text and carries no series — there is no number to plot. A null
    /// inside a numeric column is a missing measurement: the chart plots readings by index, so
    /// the series is simply the cells that exist, and the newest of them is the reading.
    /// </summary>
    [Fact]
    public async Task TextCarriesNoSeries_AndNullsAreSkippedNotFatal()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("grade", "varchar"), ("level", "double")],
            rows:
            [
                ["300", "EN590", "3"],
                ["200", "EN590", null],
                ["100", "EN590", "1"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var schema = await catalog.GetSeriesAsync(Dataset, "timestamp");

        var grade = Leaf(schema, "grade");
        Assert.Empty(grade.History);
        Assert.False(grade.HasValue);
        Assert.Equal("EN590", grade.Display);

        var level = Leaf(schema, "level");
        Assert.Equal([1, 3], level.History);
        Assert.Equal(3, level.Value);
    }

    /// <summary>
    /// A dataset carrying the conventional column supplies its own axis, and one that does not
    /// hands back null so the screen asks instead of guessing.
    /// </summary>
    [Fact]
    public async Task TimestampIsTheDefaultAxis_AndItsAbsenceIsReportedRatherThanGuessed()
    {
        using var withTimestamp = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("level", "double")],
            rows: []);
        using var catalogA = new HttpDatasetCatalog(withTimestamp.Client);
        Assert.Equal("timestamp", SeriesAxis.Preferred(await catalogA.GetSchemaAsync(Dataset)));

        using var without = new FakeDataFeed(
            columns: [("captured_at", "bigint"), ("level", "double")],
            rows: []);
        using var catalogB = new HttpDatasetCatalog(without.Client);
        var schema = await catalogB.GetSchemaAsync(Dataset);

        Assert.Null(SeriesAxis.Preferred(schema));

        // But both columns are offerable, so the operator has something to choose from.
        Assert.Equal(["captured_at", "level"], SeriesAxis.Candidates(schema));
    }

    /// <summary>
    /// An array column cannot be sorted on — the service answers 400 — so it must never reach the
    /// picker. Offering it would turn a pick into an error the operator could do nothing about.
    /// </summary>
    [Fact]
    public async Task ArrayColumnsAreNotOfferedAsAnAxis()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("spectrum", "array<double>")],
            rows: []);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        Assert.Equal(["timestamp"], SeriesAxis.Candidates(await catalog.GetSchemaAsync(Dataset)));
    }

    /// <summary>
    /// Only the recent end is kept. The service's own cap is far above what a hand-drawn chart
    /// should redraw per frame, and the rows that survive must be the newest ones.
    /// </summary>
    [Fact]
    public async Task OnlyTheNewestRowsAreKept()
    {
        var rows = new List<IReadOnlyList<string?>>();
        for (var i = DataFeedOptions.SeriesRows + 50; i > 0; i--)
            rows.Add([i.ToString(), i.ToString()]);

        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("level", "double")],
            rows: rows);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var leaf = Leaf(await catalog.GetSeriesAsync(Dataset, "timestamp"), "level");

        Assert.Equal(DataFeedOptions.SeriesRows, leaf.History.Count);

        // Newest kept, oldest dropped: the series ends at the highest reading, not the 240th.
        Assert.Equal(DataFeedOptions.SeriesRows + 50, leaf.History[^1]);
        Assert.Equal(51, leaf.History[0]);
    }

    /// <summary>
    /// A tile draws what the leaf holds, so the check is worth making end to end: a series that
    /// survives this far is one the dashboard can plot.
    /// </summary>
    [Fact]
    public async Task AMountedLeafResolvesToATileSeriesTheChartCanDraw()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("level", "double")],
            rows: [["300", "3"], ["200", "2"], ["100", "1"]]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var schema = await catalog.GetSeriesAsync(Dataset, "timestamp");
        var workspace = Workspace.Instance;
        workspace.Mount(schema, schema.Root);

        try
        {
            var resolved = TileData.Resolve(
                new TileSource(TileSourceKind.Measure, $"{Dataset}.level"));

            Assert.NotNull(resolved);
            Assert.Equal([1, 2, 3], resolved.History);

            // What TileView.BuildLineChart gates on — below two points it draws the "NO SERIES"
            // placeholder instead of a chart.
            Assert.True(resolved.History.Count >= 2);

            // And the axis travels with the mount, so a screen downstream can say what the x axis
            // is rather than implying the points are evenly spaced in time.
            Assert.Equal("timestamp", workspace.Find(Dataset)?.XAxis);
        }
        finally
        {
            workspace.Unmount(Dataset);
        }
    }

    /// <summary>
    /// A flat table cannot nest a σ under the reading it belongs to, so the feed spells the pairing
    /// in the column name. Until this was recognised the two arrived as unrelated columns: the
    /// reading had no variance, and every tile built on one blanked the moment variance was
    /// switched on — with a perfectly good σ sitting in the next column.
    /// </summary>
    [Fact]
    public async Task ASiblingSigmaColumnBecomesTheMeasuresVariance()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("level", "double"), ("level__sigma", "double")],
            rows:
            [
                ["300", "30", "3"],
                ["200", "20", "2"],
                ["100", "10", "1"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var schema = await catalog.GetSeriesAsync(Dataset, "timestamp");
        var level = Leaf(schema, "level");

        Assert.True(level.HasVariance);
        Assert.Equal(3, level.Sigma);
        Assert.Equal("± 3", level.SigmaDisplay);

        // Measured per reading, and carried as such — this is the σ(x) the whole sibling
        // convention exists to express, not a flat figure repeated.
        Assert.Equal([1, 2, 3], level.SigmaHistory);

        // The reading itself is untouched by the binding.
        Assert.Equal([10, 20, 30], level.History);
        Assert.Equal(30, level.Value);
    }

    /// <summary>
    /// The σ column stays in the tree — it is real data — but it is a standard deviation, not a
    /// quantity, so nothing invites anyone to plot it on its own.
    /// </summary>
    [Fact]
    public async Task TheSigmaColumnIsMarkedACarrierAndNotOfferedAsATileSource()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("level", "double"), ("level__sigma", "double")],
            rows: [["200", "20", "2"], ["100", "10", "1"]]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var schema = await catalog.GetSeriesAsync(Dataset, "timestamp");
        Assert.True(Leaf(schema, "level__sigma").IsSigmaCarrier);

        var workspace = Workspace.Instance;
        workspace.Mount(schema, schema.Root);
        try
        {
            var offered = TileData.AvailableMeasures(workspace.Find(Dataset)!)
                .Select(leaf => leaf.Name)
                .ToList();

            Assert.Contains("level", offered);
            Assert.DoesNotContain("level__sigma", offered);

            // And the tile it does offer draws bounds, which is what the master variance switch
            // blanks a tile for lacking.
            var resolved = TileData.Resolve(new TileSource(TileSourceKind.Measure, $"{Dataset}.level"));
            Assert.True(resolved!.HasVariance);
            Assert.NotEmpty(resolved.SigmaHistory);
        }
        finally
        {
            workspace.Unmount(Dataset);
        }
    }

    /// <summary>
    /// A measured σ must never be replaced by a generated one. The binder builds a series for a
    /// declared scalar σ — the demo trees rely on that — and the danger is it doing so on top of
    /// real data, which would draw a band that moved for reasons nobody measured.
    /// </summary>
    [Fact]
    public async Task AMeasuredSigmaIsNotOverwrittenByAGeneratedOne()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("level", "double"), ("level__sigma", "double")],
            rows:
            [
                ["300", "30", "7"],
                ["200", "20", "7"],
                ["100", "10", "7"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var level = Leaf(await catalog.GetSeriesAsync(Dataset, "timestamp"), "level");

        // A flat σ that was genuinely flat stays flat. Regeneration would wobble it by ±18%, so
        // three identical readings is the sharpest available assertion that none happened.
        Assert.Equal([7, 7, 7], level.SigmaHistory);
    }

    /// <summary>
    /// A σ column with a hole in it cannot produce a band, and the binder must not invent one to
    /// fill the gap. The flat figure stands instead, which is what an empty SigmaHistory means.
    /// </summary>
    [Fact]
    public async Task AGappySigmaFallsBackToTheFlatFigureRatherThanBeingInvented()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("level", "double"), ("level__sigma", "double")],
            rows:
            [
                ["300", "30", "3"],
                ["200", "20", null],
                ["100", "10", "1"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var level = Leaf(await catalog.GetSeriesAsync(Dataset, "timestamp"), "level");

        Assert.Empty(level.SigmaHistory);

        // The newest σ still reads, so the measure keeps a usable flat variance.
        Assert.True(level.HasVariance);
        Assert.Equal(3, level.Sigma);
    }

    /// <summary>
    /// The user-visible failure this round: a feed whose newest rows have not caught up yet. The
    /// series is what was measured and the reading is the last measured value — one trailing null
    /// must not throw away 239 good points, which is what an all-or-nothing parse did on the day
    /// it mattered.
    /// </summary>
    [Fact]
    public async Task TrailingNullsDoNotEraseTheSeriesOrTheReading()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("level", "double")],
            rows:
            [
                ["500", null],
                ["400", null],
                ["300", "30"],
                ["200", "20"],
                ["100", "10"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var level = Leaf(await catalog.GetSeriesAsync(Dataset, "timestamp"), "level");

        Assert.Equal([10, 20, 30], level.History);
        Assert.Equal(30, level.Value);
        Assert.Equal("30", level.Display);
    }

    /// <summary>
    /// The lig_biodiesel_calibrated shape before the table was fixed: one timestamp carrying a
    /// row per analyte and sensor. No column is a series through that — a line would thread
    /// readings from different instruments — so nothing draws and nothing quotes a "newest"
    /// value off an arbitrary tied row. The client detects and reports; the fix is the table's.
    /// </summary>
    [Fact]
    public async Task InterleavedRowsProduceNoSeriesAndNoArbitraryReading()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("analyte", "varchar"), ("value", "double")],
            rows:
            [
                ["200", "water", null],
                ["200", "methanol", "0.12"],
                ["200", "acid_number", "0.31"],
                ["100", "water", null],
                ["100", "methanol", "0.11"],
                ["100", "acid_number", "0.30"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var schema = await catalog.GetSeriesAsync(Dataset, "timestamp");

        Assert.Equal(3, schema.RowsPerPoint);

        // "value" is numeric wherever it exists — exactly the column that would have drawn a
        // plausible-looking zigzag across the three analytes.
        var value = Leaf(schema, "value");
        Assert.Empty(value.History);
        Assert.False(value.HasValue);
        Assert.Equal("—", value.Display);
        Assert.Contains("rows/point — expected one", value.Detail);
    }

    /// <summary>
    /// σ(x) must be the uncertainty of the point beside it: σ is read off exactly the rows the
    /// measure read from. A row whose value was never measured contributes neither — its σ cell
    /// does not leak in as the flat figure either.
    /// </summary>
    [Fact]
    public async Task SigmaPairsRowByRowWithTheMeasure()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("level", "double"), ("level__sigma", "double")],
            rows:
            [
                ["300", null, "0.3"],
                ["200", "20", "0.2"],
                ["100", "10", "0.1"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var level = Leaf(await catalog.GetSeriesAsync(Dataset, "timestamp"), "level");

        Assert.Equal([10, 20], level.History);
        Assert.Equal([0.1, 0.2], level.SigmaHistory);
        Assert.Equal(0.2, level.Sigma);
    }

    /// <summary>
    /// A sensor_id column declares replicate members: each sensor becomes its own subtree and
    /// each of its leaves is that one instrument's series — the ensemble shape, from one fetch.
    /// The σ sibling pairs within the member, and the member column itself is the node, not a
    /// leaf inside it.
    /// </summary>
    [Fact]
    public async Task SensorIdSplitsTheTableIntoMemberSubtrees()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("sensor_id", "varchar"), ("level", "double"), ("level__sigma", "double")],
            rows:
            [
                ["200", "LIG-02", "22", "2.2"],
                ["200", "LIG-01", "21", "2.1"],
                ["100", "LIG-02", "12", "1.2"],
                ["100", "LIG-01", "11", "1.1"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var schema = await catalog.GetSeriesAsync(Dataset, "timestamp");

        // Two rows per timestamp, yet no ties: the member split resolves them into two series.
        Assert.Equal(1, schema.RowsPerPoint);

        var one = schema.Root.Find($"{Dataset}.LIG-01.level")?.Reading;
        var two = schema.Root.Find($"{Dataset}.LIG-02.level")?.Reading;
        Assert.NotNull(one);
        Assert.NotNull(two);

        Assert.Equal([11, 21], one.History);
        Assert.Equal([12, 22], two.History);

        // σ pairs inside the member, against that sensor's own rows.
        Assert.Equal([1.1, 2.1], one.SigmaHistory);
        Assert.Equal(2.1, one.Sigma);
        Assert.True(one.HasVariance);

        // The sensor is the subtree, not a leaf of itself.
        Assert.Null(schema.Root.Find($"{Dataset}.LIG-01.sensor_id"));
    }

    /// <summary>
    /// A dataset mounted while its first fetch was still in flight went in valueless — and
    /// stayed that way, because grafting deliberately never re-shapes an existing mount. The
    /// values belong to the newest read: when the series lands, an already-mounted subtree is
    /// refreshed in place, and re-adding a node overwrites what is behind it.
    /// </summary>
    [Fact]
    public async Task AMountMadeBeforeTheSeriesLandedIsRefreshedInPlace()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("level", "double")],
            rows: [["200", "2"], ["100", "1"]]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        // The structure-only pass is what the operator sees first, and what they can mount from.
        var structure = await catalog.GetSchemaAsync(Dataset);
        var workspace = Workspace.Instance;
        workspace.Mount(structure, structure.Root);

        try
        {
            var mounted = workspace.FindNode($"{Dataset}.level");
            Assert.NotNull(mounted);
            Assert.Empty(mounted.Reading!.History);

            // The series lands; the mount is refreshed the way DataSourcesView does it.
            var series = await catalog.GetSeriesAsync(Dataset, "timestamp");
            workspace.RefreshReadings(series);

            Assert.Equal([1, 2], mounted.Reading!.History);
            Assert.Equal("timestamp", workspace.Find(Dataset)?.XAxis);

            // And a tile wired to it draws, which is the point of the whole exercise.
            var resolved = TileData.Resolve(new TileSource(TileSourceKind.Measure, $"{Dataset}.level"));
            Assert.Equal([1, 2], resolved!.History);
        }
        finally
        {
            workspace.Unmount(Dataset);
        }
    }

    private static Measure Leaf(DatasetSchema schema, string column)
    {
        var node = schema.Root.Find($"{Dataset}.{column}");
        Assert.NotNull(node);
        Assert.NotNull(node.Reading);
        return node.Reading;
    }

    /// <summary>
    /// Answers the three endpoints the catalogue calls, and records the URLs it was asked for so a
    /// test can assert on the request as well as the response.
    /// </summary>
    private sealed class FakeDataFeed : HttpMessageHandler
    {
        private readonly IReadOnlyList<(string Name, string Type)> columns;
        private readonly IReadOnlyList<IReadOnlyList<string?>> rows;

        public FakeDataFeed(
            IReadOnlyList<(string Name, string Type)> columns,
            IReadOnlyList<IReadOnlyList<string?>> rows)
        {
            this.columns = columns;
            this.rows = rows;
            Client = new HttpClient(this);
        }

        public HttpClient Client { get; }

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);

            // Matched on the path alone, and on its end: the listing route is "/api/datasets",
            // which contains "/data" as a prefix and would otherwise be answered with rows.
            var path = request.RequestUri.AbsolutePath;
            var body = path switch
            {
                _ when path.EndsWith("/schema", StringComparison.Ordinal) => Schema(),
                _ when path.EndsWith("/data", StringComparison.Ordinal) => Data(),
                _ => Listing()
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static string Listing() =>
            $$"""
              {"catalogName":"awsdatacatalog","databases":[{"database":"{{Database}}",
              "datasets":[{"name":"{{Table}}"}]}],"errors":[]}
              """;

        private string Schema()
        {
            var declared = columns.Select(column =>
                $$"""{"name":"{{column.Name}}","type":"{{column.Type}}","comment":null}""");
            return $$"""
                     {"catalogName":"awsdatacatalog","database":"{{Database}}","name":"{{Table}}",
                     "tableType":"EXTERNAL_TABLE","columns":[{{string.Join(",", declared)}}],"partitionKeys":[]}
                     """;
        }

        private string Data()
        {
            var names = columns.Select(column =>
                $$"""{"name":"{{column.Name}}","type":"{{column.Type}}"}""");
            var values = rows.Select(row =>
                "[" + string.Join(",", row.Select(cell => cell is null ? "null" : $"\"{cell}\"")) + "]");
            return $$"""
                     {"catalogName":"awsdatacatalog","database":"{{Database}}","table":"{{Table}}",
                     "columns":[{{string.Join(",", names)}}],"rows":[{{string.Join(",", values)}}],
                     "truncated":false,"queryExecutionId":"test","dataScannedBytes":0}
                     """;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Client.Dispose();
            base.Dispose(disposing);
        }
    }
}
