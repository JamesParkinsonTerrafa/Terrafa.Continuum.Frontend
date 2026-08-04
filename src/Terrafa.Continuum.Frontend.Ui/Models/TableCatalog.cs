// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Models;

public enum TableValueKind
{
    Number,
    Text,
    Boolean
}

/// <summary>
/// One column of a derived table, row-faithful: every list here has one entry per row, built from
/// <see cref="Measure.Cells"/> rather than the chart-facing series, so the columns of one table
/// actually correspond row by row.
/// </summary>
/// <param name="Cells">Display text per row — "—" where the source cell was null.</param>
/// <param name="Values">The cell as a number: the reading, or the determination as 1/0. NaN for
/// text and for null cells.</param>
/// <param name="SigmaLevels">σ level per row where the column carries one — a computed boolean
/// column. Empty for everything read straight off a table.</param>
public sealed record TableColumnValue(
    string Title,
    string Unit,
    TableValueKind Kind,
    IReadOnlyList<string> Cells,
    IReadOnlyList<double> Values,
    IReadOnlyList<double> SigmaLevels)
{
    public bool IsBoolean => Kind == TableValueKind.Boolean;
}

/// <summary>
/// A table the network has committed — the SELECT's output under a name, the way a
/// <see cref="DashboardFigure"/> is a chain's scalar under one. Carries its own explanation in
/// <see cref="Note"/>: an empty table says why it is empty, which is what the tile wired to it
/// shows rather than a silent blank.
/// </summary>
public sealed class DerivedTable
{
    /// <summary>Bare key, e.g. "parcel_conditions". <see cref="Name"/> is the "tbl." form.</summary>
    public required string Key { get; init; }

    public IReadOnlyList<TableColumnValue> Columns { get; init; } = [];

    public int RowCount { get; init; }

    /// <summary>The dataset the rows come from — the base table, once joins exist.</summary>
    public string Dataset { get; init; } = "";

    /// <summary>The column the rows are naturally keyed by — the dataset's axis when it was
    /// selected, else the first column. A tile may override it.</summary>
    public string DefaultIndex { get; init; } = "";

    public string Note { get; init; } = "";

    /// <summary>What the select wired in, for the card.</summary>
    public IReadOnlyList<string> Inputs { get; init; } = [];

    public string Name => $"tbl.{Key}";

    public bool HasRows => RowCount > 0 && Columns.Count > 0;

    public string StateNote => HasRows ? $"{RowCount} × {Columns.Count}" : "empty";
}

/// <summary>
/// Row ordering for a grid tile: the index column leads and the rows follow it. Kept beside the
/// model rather than in the tile so a test can hold it still.
/// </summary>
public static class DerivedTableView
{
    /// <summary>The table's columns with <paramref name="indexLeaf"/> first — or as they are, if
    /// the index is not one of them.</summary>
    public static IReadOnlyList<TableColumnValue> OrderedColumns(DerivedTable table, string indexLeaf)
    {
        var index = table.Columns.FirstOrDefault(column => column.Title == indexLeaf);
        if (index is null) return table.Columns;
        return [index, .. table.Columns.Where(column => column != index)];
    }

    /// <summary>
    /// Row positions sorted by the index column — numerically where it reads as numbers, by text
    /// where it does not. An index that is not a column leaves the rows in table order.
    /// </summary>
    public static IReadOnlyList<int> OrderedRows(DerivedTable table, string indexLeaf)
    {
        var positions = Enumerable.Range(0, table.RowCount).ToList();
        var index = table.Columns.FirstOrDefault(column => column.Title == indexLeaf);
        if (index is null) return positions;

        return index.Kind == TableValueKind.Text
            ? [.. positions.OrderBy(row => row < index.Cells.Count ? index.Cells[row] : "", StringComparer.Ordinal)]
            : [.. positions.OrderBy(row => row < index.Values.Count ? index.Values[row] : double.NaN)];
    }

    /// <summary>The index a tile actually uses: its own pick when valid, else the table's default.</summary>
    public static string ResolveIndex(DerivedTable table, string tileIndex) =>
        table.Columns.Any(column => column.Title == tileIndex) ? tileIndex : table.DefaultIndex;
}

/// <summary>
/// The derived tables the app knows about, shared by the screens that show them — the network
/// writes, the tile editor lists. The same shape as <see cref="FigureCatalog"/>, without the
/// declared fallback: every table is computed, so unwiring one withdraws it outright.
/// </summary>
public sealed class TableCatalog
{
    public static TableCatalog Instance { get; } = new();

    private readonly List<DerivedTable> tables = [];

    public event Action? Changed;

    private TableCatalog()
    {
    }

    public IReadOnlyList<DerivedTable> Tables => tables;

    public DerivedTable? Find(string key) => tables.FirstOrDefault(table => table.Key == key);

    public bool Contains(string key) => tables.Any(table => table.Key == key);

    public void Register(DerivedTable table)
    {
        var index = tables.FindIndex(existing => existing.Key == table.Key);
        if (index >= 0)
        {
            if (Same(tables[index], table)) return;
            tables[index] = table;
        }
        else
        {
            tables.Add(table);
        }
        Changed?.Invoke();
    }

    public void Remove(string key)
    {
        if (tables.RemoveAll(table => table.Key == key) == 0) return;
        Changed?.Invoke();
    }

    /// <summary>A key nothing is registered under yet, e.g. "table_2" from the stem "table".</summary>
    public string NextKey(string stem)
    {
        if (!Contains(stem)) return stem;
        var index = 2;
        while (Contains($"{stem}_{index}")) index++;
        return $"{stem}_{index}";
    }

    public void Reset()
    {
        if (tables.Count == 0) return;
        tables.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Whether re-registering would change anything a screen draws — recomputing the network hands
    /// every table back on each pass, and raising Changed for an identical one would loop the
    /// screens that rebuild on it.
    /// </summary>
    private static bool Same(DerivedTable left, DerivedTable right) =>
        left.RowCount == right.RowCount &&
        left.Note == right.Note &&
        left.Dataset == right.Dataset &&
        left.DefaultIndex == right.DefaultIndex &&
        left.Columns.Count == right.Columns.Count &&
        left.Inputs.SequenceEqual(right.Inputs) &&
        left.Columns.Zip(right.Columns).All(pair =>
            pair.First.Title == pair.Second.Title &&
            pair.First.Unit == pair.Second.Unit &&
            pair.First.Kind == pair.Second.Kind &&
            pair.First.Cells.SequenceEqual(pair.Second.Cells) &&
            pair.First.SigmaLevels.Count == pair.Second.SigmaLevels.Count);
}
