// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Tests;

/// <summary>
/// Guards the SELECT → DASHBOARD TABLE path: row-faithful columns out of one table, the refusal
/// that stands in for a join until equality links carry one, and the wire typing that keeps row
/// sets and readings from crossing.
/// </summary>
[Collection("workspace")]
public class SelectTests
{
    private const string Parcels = "synthetic_dev.parcels";
    private const string Requirements = "synthetic_dev.contract_requirements";

    [Fact]
    public void ASingleTableSelectCommitsARowFaithfulTable()
    {
        var graph = NetworkGraph.Instance;
        graph.Reset(seedDemo: false);
        var schema = Schema(Parcels, "parcel",
            TextLeaf(Parcels, "parcel", ["TK-01", "TK-02", "TK-03"]),
            TextLeaf(Parcels, "productid", ["EN590", "JETA1", "FAME"]),
            NumberLeaf(Parcels, "volume", "bbl", [12480, 9640, 3890]),
            BooleanLeaf(Parcels, "on_spec", [true, false, true]));
        ReadingStore.Instance.Write(schema);
        Workspace.Instance.Mount(schema, schema.Root);
        try
        {
            string[] names = ["parcel", "productid", "volume", "on_spec"];
            foreach (var name in names)
                graph.PlaceMeasure($"{Parcels}.{name}", 0, 0);
            var select = graph.AddSelect(300, 0);
            foreach (var name in names)
                Assert.True(graph.Connect($"{Parcels}.{name}", select.Id));
            var sink = graph.AddTableSink("parcel_conditions", 600, 0);
            Assert.True(graph.Connect(select.Id, sink.Id));

            var table = TableCatalog.Instance.Find("parcel_conditions");
            Assert.NotNull(table);
            Assert.True(table.HasRows);
            Assert.Equal(3, table.RowCount);
            Assert.Equal(names, table.Columns.Select(column => column.Title));

            Assert.Equal(TableValueKind.Text, table.Columns[0].Kind);
            Assert.Equal(TableValueKind.Number, table.Columns[2].Kind);
            Assert.Equal("bbl", table.Columns[2].Unit);
            Assert.Equal(TableValueKind.Boolean, table.Columns[3].Kind);
            Assert.Equal([1, 0, 1], table.Columns[3].Values);
            Assert.Equal(["TK-01", "TK-02", "TK-03"], table.Columns[0].Cells);

            // The dataset's axis was selected, so it is the natural index.
            Assert.Equal("parcel", table.DefaultIndex);
            Assert.Equal(Parcels, table.Dataset);

            // Off the canvas means out of the catalogue — a table has no declared fallback.
            graph.Remove(sink.Id);
            Assert.Null(TableCatalog.Instance.Find("parcel_conditions"));
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>
    /// The R2 stand-in: two tables selected with no declared match refuse to become rows, and the
    /// note says where the match is made. The empty table travels to the tile, which shows the
    /// same words — nothing blanks silently.
    /// </summary>
    [Fact]
    public void TwoTablesWithoutAMatchAreRefusedWithTheLinkMessage()
    {
        var graph = NetworkGraph.Instance;
        graph.Reset(seedDemo: false);
        var parcels = Schema(Parcels, "parcel",
            TextLeaf(Parcels, "parcel", ["TK-01", "TK-02"]),
            NumberLeaf(Parcels, "condition_at_lift", "h", [18.1, 24.3]));
        var requirements = Schema(Requirements, "productid",
            TextLeaf(Requirements, "productid", ["EN590", "JETA1"]),
            NumberLeaf(Requirements, "required_value", "h", [20, 25]));
        ReadingStore.Instance.Write(parcels);
        ReadingStore.Instance.Write(requirements);
        Workspace.Instance.Mount(parcels, parcels.Root);
        Workspace.Instance.Mount(requirements, requirements.Root);
        try
        {
            graph.PlaceMeasure($"{Parcels}.condition_at_lift", 0, 0);
            graph.PlaceMeasure($"{Requirements}.required_value", 0, 100);
            var select = graph.AddSelect(300, 50);
            graph.Connect($"{Parcels}.condition_at_lift", select.Id);
            graph.Connect($"{Requirements}.required_value", select.Id);

            var table = graph.EvaluateSelect(select);
            Assert.False(table.HasRows);
            Assert.Contains("matching condition", table.Note);
            Assert.Contains("DATA TREE", table.Note);
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void RowSetsAndReadingsCannotCross()
    {
        var graph = NetworkGraph.Instance;
        graph.Reset(seedDemo: false);
        try
        {
            var select = graph.AddSelect(0, 0);
            var figure = graph.AddFigure("fig_typing", 300, 0);
            var sink = graph.AddTableSink("tbl_typing", 300, 100);
            var transfer = graph.AddTransfer(0, 100);

            // A row set is not a reading, and a reading is not a row set.
            Assert.False(graph.CanConnect(select.Id, figure.Id));
            Assert.False(graph.CanConnect(transfer.Id, sink.Id));
            Assert.False(graph.CanConnect(transfer.Id, select.Id));

            Assert.True(graph.Connect(select.Id, sink.Id));

            // One select per table — replacing the rows means rewiring, not stacking.
            var second = graph.AddSelect(0, 200);
            Assert.False(graph.CanConnect(second.Id, sink.Id));
        }
        finally
        {
            graph.Reset(seedDemo: true);
        }
    }

    [Fact]
    public void AGridTileSurvivesTheDashboardDocumentRoundTrip()
    {
        var board = Dashboard.Instance;
        board.Reset(seedDemo: false);
        try
        {
            var tile = new DashboardTile(TileKind.Grid, "tile.grid_1")
            {
                IndexLeaf = "parcel",
                HighlightBooleans = true
            };
            tile.Sources.Add(new TileSource(TileSourceKind.Table, "parcel_conditions"));
            board.Add(tile, 10, 20, 400, 300);

            var state = UserStateMapper.CaptureDashboard();
            board.Reset(seedDemo: false);
            UserStateMapper.ApplyDashboard(state);

            var restored = Assert.Single(board.Tiles);
            Assert.Equal(TileKind.Grid, restored.Kind);
            Assert.Equal("parcel", restored.IndexLeaf);
            Assert.True(restored.HighlightBooleans);
            var source = Assert.Single(restored.Sources);
            Assert.Equal(TileSourceKind.Table, source.Kind);
            Assert.Equal("parcel_conditions", source.Path);
        }
        finally
        {
            Dashboard.Instance.Reset(seedDemo: true);
        }
    }

    [Fact]
    public void TheIndexLeafLeadsAndSortsTheGrid()
    {
        var table = new DerivedTable
        {
            Key = "t",
            RowCount = 3,
            DefaultIndex = "parcel",
            Columns =
            [
                new TableColumnValue("volume", "bbl", TableValueKind.Number,
                    ["30", "10", "20"], [30, 10, 20], []),
                new TableColumnValue("parcel", "", TableValueKind.Text,
                    ["TK-11", "TK-02", "TK-07"], [double.NaN, double.NaN, double.NaN], [])
            ]
        };

        // A pick that is not a column falls back to the table's own default.
        Assert.Equal("parcel", DerivedTableView.ResolveIndex(table, "not_a_column"));
        Assert.Equal("volume", DerivedTableView.ResolveIndex(table, "volume"));

        Assert.Equal(["parcel", "volume"],
            DerivedTableView.OrderedColumns(table, "parcel").Select(column => column.Title));

        // Text sorts ordinally, numbers numerically — each against its own kind.
        Assert.Equal([1, 2, 0], DerivedTableView.OrderedRows(table, "parcel"));
        Assert.Equal([1, 2, 0], DerivedTableView.OrderedRows(table, "volume"));
    }

    /// <summary>
    /// The join itself: every equality link between the two tables holds at once — the composite
    /// key productid AND contractid — the base table's row order carries through, and a base row
    /// with no match is dropped and counted, not invented.
    /// </summary>
    [Fact]
    public void TwoLinkedTablesJoinOnEveryLinkAtOnce()
    {
        var graph = NetworkGraph.Instance;
        graph.Reset(seedDemo: false);
        MountJoinedFixtures();
        try
        {
            graph.PlaceMeasure($"{Parcels}.parcel", 0, 0);
            graph.PlaceMeasure($"{Parcels}.condition_at_lift", 0, 100);
            graph.PlaceMeasure($"{Requirements}.required_value", 0, 200);
            var select = graph.AddSelect(300, 100);
            graph.Connect($"{Parcels}.parcel", select.Id);
            graph.Connect($"{Parcels}.condition_at_lift", select.Id);
            graph.Connect($"{Requirements}.required_value", select.Id);

            var table = graph.EvaluateSelect(select);
            Assert.True(table.HasRows);

            // TK-04's (JETA1, C9) pairing has no requirement — inner join drops it.
            Assert.Equal(3, table.RowCount);
            Assert.Equal(["TK-01", "TK-02", "TK-03"], table.Columns[0].Cells);
            Assert.Equal([24.6, 17.8, 11.3], table.Columns[1].Values);

            // The right table's values arrive keyed, not zipped: (EN590,C2) finds 25, not the
            // row that happened to share an index.
            Assert.Equal([20, 25, 8], table.Columns[2].Values);

            Assert.Contains("inner join on 2 key(s)", table.Note);
            Assert.Contains("3/4 base rows matched", table.Note);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>
    /// The computed column — the contract_met story: the comparator evaluates per joined row from
    /// the cells, and its σ level per row comes from the operands' __sigma carrier columns.
    /// </summary>
    [Fact]
    public void AComputedColumnComparesAcrossTheJoin()
    {
        var graph = NetworkGraph.Instance;
        graph.Reset(seedDemo: false);
        MountJoinedFixtures();
        try
        {
            graph.PlaceMeasure($"{Parcels}.parcel", 0, 0);
            var conditionLeaf = graph.PlaceMeasure($"{Parcels}.condition_at_lift", 0, 100);
            var requiredLeaf = graph.PlaceMeasure($"{Requirements}.required_value", 0, 200);
            var comparator = graph.AddComparator(300, 150);
            graph.Connect(conditionLeaf.Id, comparator.Id);
            graph.Connect(requiredLeaf.Id, comparator.Id);
            var select = graph.AddSelect(600, 100);
            graph.Connect($"{Parcels}.parcel", select.Id);
            graph.Connect(comparator.Id, select.Id);

            var table = graph.EvaluateSelect(select);
            Assert.True(table.HasRows);
            Assert.Equal(3, table.RowCount);

            var computed = table.Columns[1];
            Assert.Equal(TableValueKind.Boolean, computed.Kind);
            Assert.Equal("condition_at_lift > required_value", computed.Title);
            Assert.Equal([1, 0, 1], computed.Values);
            Assert.Equal(["true", "false", "true"], computed.Cells);

            // z per row from the carrier cells: spread √(3²+4²) = 5.
            Assert.Equal(0.92, computed.SigmaLevels[0], 12);
            Assert.Equal(1.44, computed.SigmaLevels[1], 12);
            Assert.Equal(0.66, computed.SigmaLevels[2], 12);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>
    /// R3: standalone, a cross-table comparator is refused and the card says why; feeding a
    /// SELECT, the same node is legitimate — the join is its row order.
    /// </summary>
    [Fact]
    public void ACrossTableComparatorNeedsASelectToStandIn()
    {
        var graph = NetworkGraph.Instance;
        graph.Reset(seedDemo: false);
        MountJoinedFixtures();
        try
        {
            var conditionLeaf = graph.PlaceMeasure($"{Parcels}.condition_at_lift", 0, 100);
            var requiredLeaf = graph.PlaceMeasure($"{Requirements}.required_value", 0, 200);
            var comparator = graph.AddComparator(300, 150);
            graph.Connect(conditionLeaf.Id, comparator.Id);
            graph.Connect(requiredLeaf.Id, comparator.Id);

            // Standalone: no evaluation, and the checker states the reason.
            Assert.Null(graph.Evaluate(comparator));
            var objection = Assert.Single(NetworkChecker.Check(graph));
            Assert.Equal(comparator.Id, objection.NodeId);
            Assert.Contains("SELECT", objection.Message);

            // Into a select, the objection lifts.
            var select = graph.AddSelect(600, 150);
            graph.Connect(comparator.Id, select.Id);
            Assert.Empty(NetworkChecker.Check(graph));
        }
        finally
        {
            Cleanup();
        }
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Readings arrive after the network does, and the committed table has to pick them up.
    ///
    /// <para>
    /// This is the order a restored session runs in: the workspace and the network are applied
    /// from the saved documents, and only then does ReadingLoader fetch a single value. The select
    /// therefore evaluates once against a workspace holding no cells at all, reports its key
    /// columns as empty, and — until this — was never asked again. The operator saw a grid reading
    /// "a key column behind ≡ carries no cells" over a join that was wired correctly and a table
    /// that had been read in full.
    /// </para>
    /// </summary>
    [Fact]
    public void ReadingsArrivingAfterTheNetworkFillTheCommittedTable()
    {
        var graph = NetworkGraph.Instance;
        graph.Reset(seedDemo: false);

        // Mounted with structure only, the way a schema read carries no rows.
        var parcelNames = new[] { "parcel", "productid", "contractid", "condition_at_lift" };
        var parcels = Schema(Parcels, "parcel", [.. parcelNames.Select(name => Structural(Parcels, name))]);
        var requirements = Schema(Requirements, "productid",
            Structural(Requirements, "productid"),
            Structural(Requirements, "contractid"),
            Structural(Requirements, "required_value"));
        Workspace.Instance.Mount(parcels, parcels.Root);
        Workspace.Instance.Mount(requirements, requirements.Root);
        Assert.True(Workspace.Instance.AddLink(
            $"{Parcels}.productid", $"{Requirements}.productid", SubtreeLinkKind.Equality));
        Assert.True(Workspace.Instance.AddLink(
            $"{Parcels}.contractid", $"{Requirements}.contractid", SubtreeLinkKind.Equality));

        try
        {
            foreach (var name in parcelNames) graph.PlaceMeasure($"{Parcels}.{name}", 0, 0);
            graph.PlaceMeasure($"{Requirements}.required_value", 0, 0);
            var select = graph.AddSelect(300, 0);
            foreach (var name in parcelNames) Assert.True(graph.Connect($"{Parcels}.{name}", select.Id));
            Assert.True(graph.Connect($"{Requirements}.required_value", select.Id));
            var sink = graph.AddTableSink("contract", 600, 0);
            Assert.True(graph.Connect(select.Id, sink.Id));

            // Nothing has been read yet, so the table commits empty and says why.
            var before = TableCatalog.Instance.Find("contract");
            Assert.NotNull(before);
            Assert.False(before.HasRows);

            // The read lands. No structural change follows it — this is the only trigger there is.
            ReadingStore.Instance.Write(Schema(Parcels, "parcel",
                TextLeaf(Parcels, "parcel", ["TK-01", "TK-02"]),
                TextLeaf(Parcels, "productid", ["EN590", "FAME"]),
                TextLeaf(Parcels, "contractid", ["C1", "C1"]),
                NumberLeaf(Parcels, "condition_at_lift", "h", [24.6, 11.3])));
            ReadingStore.Instance.Write(Schema(Requirements, "productid",
                TextLeaf(Requirements, "productid", ["EN590", "FAME"]),
                TextLeaf(Requirements, "contractid", ["C1", "C1"]),
                NumberLeaf(Requirements, "required_value", "h", [20, 8])));

            var after = TableCatalog.Instance.Find("contract");
            Assert.NotNull(after);
            Assert.Equal(2, after.RowCount);
            Assert.Equal(5, after.Columns.Count);
            Assert.Equal(["TK-01", "TK-02"], after.Columns.Single(column => column.Title == "parcel").Cells);
        }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>A mounted leaf carrying no cells — a schema read, before any row is fetched.</summary>
    private static DataTreeNode Structural(string dataset, string name) => new()
    {
        Name = name,
        Path = $"{dataset}.{name}",
        Kind = DataNodeKind.Measure,
        Reading = new Measure { Display = "—" }
    };

    /// <summary>
    /// The parcels/requirements pair joined on (productid, contractid), with σ carriers so the
    /// computed column has levels to state. TK-04's contract pairing deliberately has no
    /// requirement row.
    /// </summary>
    private void MountJoinedFixtures()
    {
        var parcels = Schema(Parcels, "parcel",
            TextLeaf(Parcels, "parcel", ["TK-01", "TK-02", "TK-03", "TK-04"]),
            TextLeaf(Parcels, "productid", ["EN590", "EN590", "FAME", "JETA1"]),
            TextLeaf(Parcels, "contractid", ["C1", "C2", "C1", "C9"]),
            NumberLeaf(Parcels, "condition_at_lift", "h", [24.6, 17.8, 11.3, 30.0]),
            NumberLeaf(Parcels, "condition_at_lift__sigma", "h", [3, 3, 3, 3]));
        var requirements = Schema(Requirements, "productid",
            TextLeaf(Requirements, "productid", ["EN590", "EN590", "FAME"]),
            TextLeaf(Requirements, "contractid", ["C1", "C2", "C1"]),
            NumberLeaf(Requirements, "required_value", "h", [20, 25, 8]),
            NumberLeaf(Requirements, "required_value__sigma", "h", [4, 4, 4]));
        ReadingStore.Instance.Write(parcels);
        ReadingStore.Instance.Write(requirements);
        Workspace.Instance.Mount(parcels, parcels.Root);
        Workspace.Instance.Mount(requirements, requirements.Root);
        Assert.True(Workspace.Instance.AddLink(
            $"{Parcels}.productid", $"{Requirements}.productid", SubtreeLinkKind.Equality));
        Assert.True(Workspace.Instance.AddLink(
            $"{Parcels}.contractid", $"{Requirements}.contractid", SubtreeLinkKind.Equality));
    }

    private static DataTreeNode TextLeaf(string dataset, string name, string?[] cells) => new()
    {
        Name = name,
        Path = $"{dataset}.{name}",
        Kind = DataNodeKind.Measure,
        Reading = new Measure { Display = cells[^1] ?? "—", Cells = cells }
    };

    private static DataTreeNode NumberLeaf(string dataset, string name, string unit, double[] values)
    {
        var cells = values.Select(value => (string?)value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return new DataTreeNode
        {
            Name = name,
            Path = $"{dataset}.{name}",
            Kind = DataNodeKind.Measure,
            Reading = new Measure
            {
                Display = cells[^1]!,
                Value = values[^1],
                Unit = unit,
                History = values,
                Cells = cells
            }
        };
    }

    private static DataTreeNode BooleanLeaf(string dataset, string name, bool[] determinations)
    {
        var cells = determinations.Select(value => (string?)(value ? "true" : "false")).ToArray();
        return new DataTreeNode
        {
            Name = name,
            Path = $"{dataset}.{name}",
            Kind = DataNodeKind.Measure,
            Reading = new Measure
            {
                Display = cells[^1]!,
                Value = determinations[^1] ? 1 : 0,
                IsBoolean = true,
                History = determinations.Select(value => value ? 1.0 : 0).ToArray(),
                Cells = cells
            }
        };
    }

    private static DatasetSchema Schema(string dataset, string xAxis, params DataTreeNode[] leaves)
    {
        var root = new DataTreeNode
        {
            Name = dataset,
            Path = dataset,
            Kind = DataNodeKind.Object,
            Tag = "SUBTREE ROOT"
        };
        foreach (var leaf in leaves) root.Children.Add(leaf);
        return new DatasetSchema(dataset, "test", "table", "—", "—", "—", root) { XAxis = xAxis };
    }

    private static void Cleanup()
    {
        NetworkGraph.Instance.Reset(seedDemo: true);
        Workspace.Instance.Unmount(Parcels);
        Workspace.Instance.Unmount(Requirements);
        ReadingStore.Instance.Clear();
    }
}
