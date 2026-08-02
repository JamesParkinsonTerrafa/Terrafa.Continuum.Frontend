// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Globalization;

namespace Terrafa.Continuum.Frontend.Services;

public enum TableColumnKind
{
    Timestamp,
    Number,
    Text
}

public sealed record TableColumn(string Name, TableColumnKind Kind);

public sealed class TableColumnData
{
    private readonly long[]? timestamps;
    private readonly double[]? numbers;
    private readonly string?[]? texts;

    private TableColumnData(long[]? timestamps, double[]? numbers, string?[]? texts)
    {
        this.timestamps = timestamps;
        this.numbers = numbers;
        this.texts = texts;
    }

    public static TableColumnData FromTimestamps(long[] values) => new(values, null, null);

    public static TableColumnData FromNumbers(double[] values) => new(null, values, null);

    public static TableColumnData FromTexts(string?[] values) => new(null, null, values);

    public long[]? Timestamps => timestamps;

    public double[]? Numbers => numbers;

    public string?[]? Texts => texts;

    public int Count => timestamps?.Length ?? numbers?.Length ?? texts!.Length;

    public long ApproximateBytes =>
        timestamps is not null ? timestamps.Length * 8L
        : numbers is not null ? numbers.Length * 8L
        : texts!.Sum(text => 24L + 2L * (text?.Length ?? 0));

    public string Format(int index)
    {
        if (timestamps is not null)
        {
            return DateTimeOffset.FromUnixTimeSeconds(timestamps[index])
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        if (numbers is not null)
        {
            return numbers[index].ToString("0.###", CultureInfo.InvariantCulture);
        }

        return texts![index] ?? "";
    }
}

public sealed class TableRowGroup
{
    public TableRowGroup(int groupIndex, int firstRow, int rowCount, IReadOnlyList<TableColumnData> columns)
    {
        GroupIndex = groupIndex;
        FirstRow = firstRow;
        RowCount = rowCount;
        Columns = columns;
        ApproximateBytes = columns.Sum(column => column.ApproximateBytes);
    }

    public int GroupIndex { get; }

    public int FirstRow { get; }

    public int RowCount { get; }

    public IReadOnlyList<TableColumnData> Columns { get; }

    public long ApproximateBytes { get; }
}

/// <summary>
/// The read side of the export table's storage, shaped deliberately like a parquet file:
/// <see cref="Columns"/> is the file schema with the ascending timestamp indexer always first,
/// <see cref="RowGroupSize"/> is what the writer chunked by (only the last group may be short),
/// and <see cref="ReadRowGroupAsync"/> decodes one row group's column chunks into typed arrays.
/// The row cache above this seam holds decoded groups; implementations are the compact cold
/// store. Reads must yield rather than block the caller — the browser head is single-threaded —
/// and must tolerate being issued concurrently.
/// </summary>
public interface ITableDocument
{
    IReadOnlyList<TableColumn> Columns { get; }

    int TotalRows { get; }

    int RowGroupSize { get; }

    int RowGroupCount { get; }

    Task<TableRowGroup> ReadRowGroupAsync(int groupIndex, CancellationToken cancellationToken);
}
