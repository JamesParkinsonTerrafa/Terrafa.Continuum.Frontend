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
[Collection("workspace")]
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
    /// row per analyte and sensor. The client counts the repeats and reports them, and keeps
    /// every cell it was sent.
    ///
    /// <para>
    /// It used to discard the whole read instead. That cost more than a chart: cells are the
    /// row-faithful record a grid and a join read from, and a lookup table repeats its keys by
    /// design — <c>contract_requirements</c> has thirteen rows against one productid and is not
    /// malformed for it. Dropping its cells emptied every join that crossed it. Detection is
    /// worth keeping; deciding what is fit to keep is not the read path's call.
    /// </para>
    /// </summary>
    [Fact]
    public async Task InterleavedRowsAreReportedButKeepTheirCells()
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

        // Still detected, and still said out loud on the leaf — that is what a screen reads to
        // warn that no column here is a series.
        Assert.Equal(3, schema.RowsPerPoint);

        var value = Leaf(schema, "value");
        Assert.Contains("rows/point — expected one", value.Detail);

        // Every row of the response, oldest first, nulls held in place. A join indexes cells by
        // row, so a dropped null would slide every value below it onto the wrong row.
        Assert.Equal(["0.30", "0.11", null, "0.31", "0.12", null], value.Cells);

        // The repeated column keeps its cells too. This is the one the join reads: it is a key,
        // not a series, and its repeats are the whole reason it can be joined on.
        Assert.Equal(
            ["acid_number", "methanol", "water", "acid_number", "methanol", "water"],
            Leaf(schema, "analyte").Cells);
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
    /// Parquet is columnar, so a narrower projection scans fewer bytes. A read driven by what
    /// someone selected asks for those columns and no others — plus two the request cannot work
    /// without: the axis, and the σ beside a selected leaf.
    /// </summary>
    [Fact]
    public async Task TheProjectionAsksOnlyForTheSelectedLeaves()
    {
        using var transport = new FakeDataFeed(
            columns:
            [
                ("timestamp", "bigint"),
                ("level", "double"),
                ("level__sigma", "double"),
                ("temp", "double"),
                ("grade", "varchar")
            ],
            rows: [["200", "2", "0.2", "20", "EN590"], ["100", "1", "0.1", "10", "EN590"]]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        await catalog.GetSeriesAsync(Dataset, "timestamp", [$"{Dataset}.level"]);

        var request = Assert.Single(transport.Requests, url => url.Contains("/data?"));
        Assert.Contains("columns=timestamp", request);
        Assert.Contains("columns=level", request);
        Assert.Contains("columns=level__sigma", request);

        // The columns nobody asked for stay out of the scan.
        Assert.DoesNotContain("columns=temp", request);
        Assert.DoesNotContain("columns=grade", request);
    }

    /// <summary>
    /// The column a table splits its sensors on is never selected, and never optional. Dropping it
    /// from a narrowed read would collapse the member subtrees and change the tree's shape between
    /// one read and the next.
    /// </summary>
    [Fact]
    public async Task ANarrowedReadKeepsTheMemberColumn()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("sensor_id", "varchar"), ("level", "double"), ("temp", "double")],
            rows:
            [
                ["200", "LIG-02", "22", "32"],
                ["200", "LIG-01", "21", "31"],
                ["100", "LIG-02", "12", "12"],
                ["100", "LIG-01", "11", "11"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var schema = await catalog.GetSeriesAsync(Dataset, "timestamp", [$"{Dataset}.LIG-01.level"]);

        var request = Assert.Single(transport.Requests, url => url.Contains("/data?"));
        Assert.Contains("columns=sensor_id", request);
        Assert.DoesNotContain("columns=temp", request);

        // And the split still happens, against the selected leaf's own rows.
        Assert.Equal([11, 21], schema.Root.Find($"{Dataset}.LIG-01.level")?.Reading?.History);
    }

    /// <summary>
    /// A selection that matches no column is stale, not an instruction to read nothing. Reading
    /// the axis alone would blank every leaf on screen, so the read falls back to the whole table.
    /// </summary>
    [Fact]
    public async Task ASelectionThatMatchesNothingReadsTheWholeTable()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("level", "double")],
            rows: [["200", "2"], ["100", "1"]]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        await catalog.GetSeriesAsync(Dataset, "timestamp", [$"{Dataset}.column_that_left"]);

        var request = Assert.Single(transport.Requests, url => url.Contains("/data?"));
        Assert.Contains("columns=level", request);
    }

    /// <summary>
    /// A mount holds no values. A dataset mounted while its first fetch was still in flight went in
    /// valueless and used to stay that way, because grafting deliberately never re-shapes an
    /// existing mount. Values are found by path in <see cref="ReadingStore"/> now, so the mounted
    /// node reports the read the moment it lands. Nothing walks the tree, and nothing has to be
    /// unmounted and mounted again to pick a value up.
    /// </summary>
    [Fact]
    public async Task AMountHoldsNoValues_SoOneWriteReachesIt()
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

            // The series lands. One write, and the already-mounted node reads through to it.
            var series = await catalog.GetSeriesAsync(Dataset, "timestamp");
            ReadingStore.Instance.Write(series);
            workspace.SetAxis(Dataset, series.XAxis);

            Assert.Equal([1, 2], mounted.Reading!.History);
            Assert.Equal("timestamp", workspace.Find(Dataset)?.XAxis);

            // And a tile wired to it draws, which is the point of the whole exercise.
            var resolved = TileData.Resolve(new TileSource(TileSourceKind.Measure, $"{Dataset}.level"));
            Assert.Equal([1, 2], resolved!.History);

            // The mount is not what makes it draw. Unmounting leaves the value where it is, so a
            // dashboard saved on one machine still resolves on another that never mounted this.
            workspace.Unmount(Dataset);
            var unmounted = TileData.Resolve(new TileSource(TileSourceKind.Measure, $"{Dataset}.level"));
            Assert.Equal([1, 2], unmounted!.History);
        }
        finally
        {
            workspace.Unmount(Dataset);
            ReadingStore.Instance.Clear();
        }
    }

    /// <summary>
    /// A boolean column is a determination, not a quantity: its declared type — not cell
    /// sniffing — reads the cells as a 0/1 series, the newest cell is the reading, and the
    /// display keeps the text it arrived as. No σ appears anywhere: a determination read off a
    /// table is a statement, and inventing a band around one is exactly what the app refuses.
    /// </summary>
    [Fact]
    public async Task ABooleanColumnBecomesADeterminationSeries()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("on_spec", "boolean")],
            rows:
            [
                ["300", "true"],
                ["200", "false"],
                ["100", "true"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var leaf = Leaf(await catalog.GetSeriesAsync(Dataset, "timestamp"), "on_spec");

        Assert.True(leaf.IsBoolean);
        Assert.Equal([1, 0, 1], leaf.History);
        Assert.Equal(1, leaf.Value);
        Assert.Equal("true", leaf.Display);
        Assert.Equal(["true", "false", "true"], leaf.Cells);
        Assert.False(leaf.HasVariance);
    }

    /// <summary>
    /// Every leaf keeps its raw cells, one entry per fetched row with nulls preserved. History
    /// cannot serve a join: it drops nulls per column, so its indices stop corresponding across
    /// columns the moment any column has a hole. Cells is the row-faithful record — including
    /// for text columns, which carry no series at all but whose cells are exactly what a join
    /// key is made of.
    /// </summary>
    [Fact]
    public async Task EveryLeafKeepsRowFaithfulCells_NullsAndTextIncluded()
    {
        using var transport = new FakeDataFeed(
            columns: [("timestamp", "bigint"), ("grade", "varchar"), ("level", "double")],
            rows:
            [
                ["300", "JETA1", "3"],
                ["200", "EN590", null],
                ["100", "EN590", "1"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var schema = await catalog.GetSeriesAsync(Dataset, "timestamp");

        Assert.Equal(["EN590", "EN590", "JETA1"], Leaf(schema, "grade").Cells);
        Assert.Equal(["1", null, "3"], Leaf(schema, "level").Cells);
        Assert.Equal(["100", "200", "300"], Leaf(schema, "timestamp").Cells);

        // And the chart-facing series still skips the hole, as it always has.
        Assert.Equal([1, 3], Leaf(schema, "level").History);
    }

    /// <summary>
    /// The parcels shape: a keyed table whose axis is text and whose key columns are
    /// categorical. Nothing in it is a time series, yet the numeric column still reads as one
    /// ordered by the key, and every key cell is retained row-faithfully — the shape a join
    /// runs on.
    /// </summary>
    [Fact]
    public async Task AKeyedTableWithATextAxisKeepsItsKeysAndReadsItsNumbers()
    {
        using var transport = new FakeDataFeed(
            columns: [("parcel", "varchar"), ("productid", "varchar"), ("condition_at_lift", "double")],
            rows:
            [
                ["TK-03", "FAME", "11.3"],
                ["TK-02", "EN590", "17.8"],
                ["TK-01", "EN590", "24.6"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var schema = await catalog.GetSeriesAsync(Dataset, "parcel");

        Assert.Equal(1, schema.RowsPerPoint);
        Assert.Equal(["TK-01", "TK-02", "TK-03"], Leaf(schema, "parcel").Cells);
        Assert.Equal(["EN590", "EN590", "FAME"], Leaf(schema, "productid").Cells);
        Assert.Equal([24.6, 17.8, 11.3], Leaf(schema, "condition_at_lift").History);

        var request = Assert.Single(transport.Requests, url => url.Contains("/data?"));
        Assert.Contains("orderBy=parcel%20desc", request);
    }

    /// <summary>
    /// The scalar boolean story end to end: two measured columns, compared on the network, and
    /// committed as a dashboard figure that states its determination and how firmly it holds —
    /// "true · 1σ" — with the per-row determinations behind it as its history.
    /// </summary>
    [Fact]
    public async Task TwoColumnsCompareIntoABooleanDashboardFigure()
    {
        using var transport = new FakeDataFeed(
            columns:
            [
                ("timestamp", "bigint"),
                ("level", "double"), ("level__sigma", "double"),
                ("capacity", "double"), ("capacity__sigma", "double")
            ],
            rows:
            [
                ["200", "30", "3", "25", "4"],
                ["100", "10", "3", "25", "4"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var schema = await catalog.GetSeriesAsync(Dataset, "timestamp");
        ReadingStore.Instance.Write(schema);
        var graph = NetworkGraph.Instance;
        graph.Reset(seedDemo: false);
        try
        {
            var level = graph.PlaceMeasure($"{Dataset}.level", 0, 0);
            var capacity = graph.PlaceMeasure($"{Dataset}.capacity", 0, 100);
            var comparator = graph.AddComparator(300, 50);
            graph.Connect(level.Id, comparator.Id);
            graph.Connect(capacity.Id, comparator.Id);
            var figure = graph.AddFigure("over_capacity", 600, 50);
            graph.Connect(comparator.Id, figure.Id);

            var committed = FigureCatalog.Instance.Find("over_capacity");
            Assert.NotNull(committed);
            Assert.True(committed.IsBoolean);
            Assert.Equal("true", committed.Display);
            Assert.Equal(1, committed.Value);

            // |30−25| / √(3² + 4²) = 1 — the σ level rides in the slot a ±σ would use.
            Assert.Equal("1σ", committed.SigmaDisplay);
            Assert.Equal(1, committed.SigmaLevel, 12);

            // The row before, 10 was under 25 — the history is the determination per row.
            Assert.Equal([0, 1], committed.History);
        }
        finally
        {
            graph.Reset(seedDemo: true);
            ReadingStore.Instance.Clear();
        }
    }

    /// <summary>
    /// A table with no column by the requested name has no axis — the request carries no ordering
    /// and no filter, and the rows arrive as the table gave them.
    ///
    /// <para>
    /// Pressing the first column into the role instead is what broke the contract grid: it ordered
    /// <c>contract_requirements</c> by productid, which repeats thirteen times over, and filtered
    /// out every row that column was null on. Neither was asked for, and a grid orders itself by
    /// its own index column anyway.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ATableWithNoAxisIsNeitherOrderedNorFiltered()
    {
        using var transport = new FakeDataFeed(
            columns: [("productid", "varchar"), ("contractid", "varchar"), ("required_value", "double")],
            rows:
            [
                ["PRD-UCOME", "CON-2", "42.0"],
                ["PRD-UCOME", "CON-1", "40.0"],
                ["PRD-RME", "CON-3", "38.0"]
            ]);
        using var catalog = new HttpDatasetCatalog(transport.Client);

        var schema = await catalog.GetSeriesAsync(Dataset, "timestamp");

        var request = Assert.Single(transport.Requests, url => url.Contains("/data?"));
        Assert.DoesNotContain("orderBy", request);
        Assert.DoesNotContain("filter", request);

        // Table order, not reversed: there was no descending sort to undo.
        Assert.Equal(["PRD-UCOME", "PRD-UCOME", "PRD-RME"], Leaf(schema, "productid").Cells);
        Assert.Equal(["CON-2", "CON-1", "CON-3"], Leaf(schema, "contractid").Cells);

        // The join key repeats and keeps every cell — the failure this whole change exists for.
        Assert.Equal(3, Leaf(schema, "productid").Cells.Count);
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
