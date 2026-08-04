// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Models;

/// <summary>
/// Evaluates a SELECT node into a <see cref="DerivedTable"/>.
///
/// <para>
/// Columns come from <see cref="Measure.Cells"/> — the row-faithful record — never from the
/// chart-facing series, whose indices stop corresponding the moment a column has a hole. Rows
/// from two tables align only through the workspace's equality links (≡, declared on the TREE
/// screen): every link between the selected tables must hold at once, which is how a composite
/// key — productid AND contractid — is spelled. The join is inner, and the base table's row
/// order carries through; a base row without a match is dropped and counted in the note.
/// </para>
///
/// <para>
/// A comparator wired into the select is a computed column, evaluated per joined row against the
/// cells — not against its own series, which would silently misalign. Its σ level per row comes
/// from the operands' "__sigma" carrier cells where the table states them, else the flat σ.
/// </para>
/// </summary>
public static class SelectEvaluation
{
    public static DerivedTable Evaluate(NetworkGraph graph, NetworkNode select, string key)
    {
        var entries = graph.InputsOf(select.Id)
            .Select(graph.Find)
            .OfType<NetworkNode>()
            .Where(node => node.Kind is NetworkNodeKind.Measure or NetworkNodeKind.Compare)
            .ToList();
        if (entries.Count == 0)
            return Empty(key, "no columns — wire leaves into the select");

        // Every leaf the rows depend on, in wiring order: wired columns and computed-column
        // operands alike. The join must cover all of them.
        var dependencies = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.Kind == NetworkNodeKind.Measure)
            {
                dependencies.Add(entry.Key);
                continue;
            }
            foreach (var port in NetworkGraph.ComparePorts)
            {
                if (graph.SourceOnPort(entry, port) is not { } sourceId ||
                    graph.Find(sourceId) is not { Kind: NetworkNodeKind.Measure } leaf)
                {
                    return Empty(key,
                        $"a computed column's {port} port must carry a leaf of the selected tables");
                }
                dependencies.Add(leaf.Key);
            }
        }

        var inputs = entries.Select(graph.Title).ToList();
        var datasets = dependencies
            .Select(NetworkGraph.DatasetOf)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var join = Join(key, datasets, dependencies, inputs);
        if (join.Objection is { } objection) return Empty(key, objection, inputs);

        var columns = new List<TableColumnValue>();
        var starved = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.Kind == NetworkNodeKind.Measure)
            {
                if (Workspace.ReadingAt(entry.Key) is not { } reading || reading.Cells.Count == 0)
                {
                    starved.Add(NetworkGraph.LeafName(entry.Key));
                    continue;
                }
                columns.Add(LeafColumn(entry.Key, reading, join));
            }
            else
            {
                if (ComputedColumn(graph, entry, join) is not { } computed)
                    return Empty(key, "cannot compare unlike units in a computed column", inputs);
                columns.Add(computed);
            }
        }

        if (columns.Count == 0)
            return Empty(key, "no column carries cells — sample the dataset on 6) DATA SOURCES", inputs);

        var axis = Workspace.Instance.Find(join.BaseDataset)?.XAxis is { Length: > 0 } xAxis
            ? NetworkGraph.LeafName(xAxis)
            : "";

        var note = datasets.Count == 1
            ? $"{join.Rows.Count} row(s) · {columns.Count} column(s) · from {join.BaseDataset}"
            : $"{join.Rows.Count} row(s) · inner join on {join.KeyCount} key(s) · " +
              $"{join.MatchedBaseRows}/{join.BaseRows} base rows matched";
        if (starved.Count > 0) note += $" · no cells behind {string.Join(", ", starved)}";

        return new DerivedTable
        {
            Key = key,
            Columns = columns,
            RowCount = join.Rows.Count,
            Dataset = join.BaseDataset,
            DefaultIndex = columns.Any(column => column.Title == axis)
                ? axis
                : columns[0].Title,
            Note = note,
            Inputs = inputs
        };
    }

    // ── the join ─────────────────────────────────────────────────────────────

    private sealed class JoinResult
    {
        public List<int[]> Rows { get; } = [];
        public Dictionary<string, int> Slots { get; } = new(StringComparer.Ordinal);
        public string BaseDataset { get; set; } = "";
        public string? Objection { get; set; }
        public int KeyCount { get; set; }
        public int BaseRows { get; set; }
        public int MatchedBaseRows { get; set; }

        public int RowOf(int[] row, string dataset) => row[Slots[dataset]];
    }

    /// <summary>
    /// Builds the joined row tuples: one <c>int[]</c> per result row, holding each dataset's row
    /// index. Datasets fold in one at a time in wiring order, each attached by every equality
    /// link between it and what is already joined — all of them at once, ANDed.
    /// </summary>
    private static JoinResult Join(
        string key, IReadOnlyList<string> datasets, IReadOnlyList<string> dependencies, IReadOnlyList<string> inputs)
    {
        var result = new JoinResult { BaseDataset = datasets[0] };
        for (var slot = 0; slot < datasets.Count; slot++) result.Slots[datasets[slot]] = slot;

        var rowCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var dataset in datasets)
        {
            var counts = dependencies
                .Where(path => NetworkGraph.DatasetOf(path) == dataset)
                .Select(path => Workspace.ReadingAt(path)?.Cells.Count ?? 0)
                .Where(count => count > 0)
                .ToList();
            rowCounts[dataset] = counts.Count == 0 ? 0 : counts.Min();
        }

        result.BaseRows = rowCounts[result.BaseDataset];
        for (var row = 0; row < result.BaseRows; row++)
        {
            var tuple = new int[datasets.Count];
            Array.Fill(tuple, -1);
            tuple[0] = row;
            result.Rows.Add(tuple);
        }
        result.MatchedBaseRows = result.BaseRows;
        if (datasets.Count == 1) return result;

        var joined = new HashSet<string>(StringComparer.Ordinal) { result.BaseDataset };
        foreach (var dataset in datasets.Skip(1))
        {
            // Each link is (already-joined leaf, this dataset's leaf) once oriented; a link whose
            // far side is not joined yet waits for its own dataset's turn.
            var conditions = new List<(IReadOnlyList<string?> JoinedCells, string JoinedDataset, IReadOnlyList<string?> MineCells)>();
            foreach (var link in Workspace.Instance.Links)
            {
                if (link.Kind != SubtreeLinkKind.Equality) continue;
                var leftDataset = NetworkGraph.DatasetOf(link.LeftPath);
                var rightDataset = NetworkGraph.DatasetOf(link.RightPath);

                var (mine, other, otherDataset) =
                    leftDataset == dataset && joined.Contains(rightDataset) ? (link.LeftPath, link.RightPath, rightDataset)
                    : rightDataset == dataset && joined.Contains(leftDataset) ? (link.RightPath, link.LeftPath, leftDataset)
                    : (null, null, "");
                if (mine is null) continue;

                if (Workspace.ReadingAt(mine)?.Cells is not { Count: > 0 } mineCells ||
                    Workspace.ReadingAt(other!)?.Cells is not { Count: > 0 } otherCells)
                {
                    result.Objection = $"a key column behind ≡ carries no cells — sample {NetworkGraph.LeafName(mine)}";
                    return result;
                }
                conditions.Add((otherCells, otherDataset, mineCells));
            }

            if (conditions.Count == 0)
            {
                result.Objection =
                    "if data from two tables is selected, a matching condition must be " +
                    "included — link their key columns (≡) in 2) DATA TREE";
                return result;
            }
            result.KeyCount += conditions.Count;

            var mineRows = rowCounts[dataset];
            var slot = result.Slots[dataset];
            var expanded = new List<int[]>();
            foreach (var tuple in result.Rows)
            {
                for (var mineRow = 0; mineRow < mineRows; mineRow++)
                {
                    var matches = conditions.All(condition =>
                        KeysEqual(
                            Cell(condition.JoinedCells, result.RowOf(tuple, condition.JoinedDataset)),
                            Cell(condition.MineCells, mineRow)));
                    if (!matches) continue;
                    var grown = (int[])tuple.Clone();
                    grown[slot] = mineRow;
                    expanded.Add(grown);
                }
            }
            result.Rows.Clear();
            result.Rows.AddRange(expanded);
            joined.Add(dataset);
        }

        result.MatchedBaseRows = result.Rows.Select(tuple => tuple[0]).Distinct().Count();
        return result;
    }

    private static string? Cell(IReadOnlyList<string?> cells, int row) =>
        row >= 0 && row < cells.Count ? cells[row] : null;

    /// <summary>Null never equals null — a row with no key belongs to no match, as in SQL.</summary>
    private static bool KeysEqual(string? left, string? right) =>
        left is not null && right is not null &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);

    // ── columns ──────────────────────────────────────────────────────────────

    private static TableColumnValue LeafColumn(string path, Measure reading, JoinResult join)
    {
        var kind = reading.IsBoolean
            ? TableValueKind.Boolean
            : reading.HasValue || reading.History.Count > 0
                ? TableValueKind.Number
                : TableValueKind.Text;

        var dataset = NetworkGraph.DatasetOf(path);
        var cells = new List<string>(join.Rows.Count);
        var values = new List<double>(join.Rows.Count);
        foreach (var tuple in join.Rows)
        {
            var cell = Cell(reading.Cells, join.RowOf(tuple, dataset));
            cells.Add(cell is { Length: > 0 } ? cell : "—");
            values.Add(cell is null
                ? double.NaN
                : kind switch
                {
                    TableValueKind.Boolean => MeasureNumerics.ParseBoolean(cell),
                    TableValueKind.Number => MeasureNumerics.ParseValue(cell).Value,
                    _ => double.NaN
                });
        }

        return new TableColumnValue(
            NetworkGraph.LeafName(path),
            kind == TableValueKind.Number ? reading.Unit : "",
            kind,
            cells,
            values,
            SigmaLevels: []);
    }

    /// <summary>
    /// The comparator as a column: determination and σ level per joined row, from the operands'
    /// cells at that row. Null when the operands' units disagree — the same refusal the
    /// standalone comparator makes.
    /// </summary>
    private static TableColumnValue? ComputedColumn(NetworkGraph graph, NetworkNode comparator, JoinResult join)
    {
        if (FunctionLibrary.Instance.Find(comparator.Operator) is not { } operation) return null;
        if (graph.SourceOnPort(comparator, NetworkGraph.ComparePortA) is not { } aId ||
            graph.SourceOnPort(comparator, NetworkGraph.ComparePortB) is not { } bId) return null;
        if (graph.Find(aId) is not { } aLeaf || graph.Find(bId) is not { } bLeaf) return null;
        if (Workspace.ReadingAt(aLeaf.Key) is not { } a || Workspace.ReadingAt(bLeaf.Key) is not { } b) return null;
        if (a.Unit.Length > 0 && b.Unit.Length > 0 && a.Unit != b.Unit) return null;

        var aDataset = NetworkGraph.DatasetOf(aLeaf.Key);
        var bDataset = NetworkGraph.DatasetOf(bLeaf.Key);
        var aSigma = SigmaCells(aLeaf.Key);
        var bSigma = SigmaCells(bLeaf.Key);

        var cells = new List<string>(join.Rows.Count);
        var values = new List<double>(join.Rows.Count);
        var levels = new List<double>(join.Rows.Count);
        foreach (var tuple in join.Rows)
        {
            var aRow = join.RowOf(tuple, aDataset);
            var bRow = join.RowOf(tuple, bDataset);
            var aValue = ParsedCell(a.Cells, aRow);
            var bValue = ParsedCell(b.Cells, bRow);

            if (double.IsNaN(aValue) || double.IsNaN(bValue))
            {
                cells.Add("—");
                values.Add(double.NaN);
                levels.Add(double.NaN);
                continue;
            }

            var (determination, level) = TransferMath.CompareValues(
                operation,
                aValue, RowSigma(aSigma, aRow, a.Sigma),
                bValue, RowSigma(bSigma, bRow, b.Sigma));
            cells.Add(MeasureNumerics.FormatBoolean(determination));
            values.Add(determination);
            levels.Add(level);
        }

        return new TableColumnValue(
            operation.FormatApplied([NetworkGraph.LeafName(aLeaf.Key), NetworkGraph.LeafName(bLeaf.Key)]),
            "",
            TableValueKind.Boolean,
            cells,
            values,
            levels);
    }

    private static double ParsedCell(IReadOnlyList<string?> cells, int row) =>
        Cell(cells, row) is { } cell ? MeasureNumerics.ParseValue(cell).Value : double.NaN;

    /// <summary>The "__sigma" carrier's cells beside a leaf, when the table states one.</summary>
    private static IReadOnlyList<string?> SigmaCells(string path) =>
        Workspace.ReadingAt(path + MeasureNumerics.SigmaSuffix)?.Cells ?? [];

    /// <summary>σ at one row: the carrier's cell where it reads, else the flat figure — and NaN
    /// stays NaN, which is what makes the vacuous regime reach the cell.</summary>
    private static double RowSigma(IReadOnlyList<string?> sigmaCells, int row, double flat)
    {
        if (Cell(sigmaCells, row) is not { } cell) return flat;
        var (value, _) = MeasureNumerics.ParseValue(cell);
        return double.IsNaN(value) ? flat : Math.Abs(value);
    }

    private static DerivedTable Empty(string key, string note, IReadOnlyList<string>? inputs = null) => new()
    {
        Key = key,
        Note = note,
        Inputs = inputs ?? []
    };
}
