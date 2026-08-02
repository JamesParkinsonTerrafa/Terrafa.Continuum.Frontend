// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Parquet;
using Parquet.Schema;

namespace Terrafa.Continuum.Frontend.Services;

public sealed class ParquetTableDocument : ITableDocument, IAsyncDisposable
{
    private readonly MemoryStream stream;
    private readonly ParquetReader reader;
    private readonly IReadOnlyList<DataField> fields;
    private readonly IReadOnlyList<int> groupFirstRows;
    private readonly SemaphoreSlim readGate = new(1, 1);

    private ParquetTableDocument(
        MemoryStream stream,
        ParquetReader reader,
        IReadOnlyList<DataField> fields,
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<int> groupFirstRows,
        int totalRows,
        int rowGroupSize)
    {
        this.stream = stream;
        this.reader = reader;
        this.fields = fields;
        this.groupFirstRows = groupFirstRows;
        Columns = columns;
        TotalRows = totalRows;
        RowGroupSize = rowGroupSize;
    }

    public IReadOnlyList<TableColumn> Columns { get; }

    public int TotalRows { get; }

    public int RowGroupSize { get; }

    public int RowGroupCount => groupFirstRows.Count;

    public long ParquetBytes => stream.Length;

    public static async Task<ParquetTableDocument> OpenAsync(
        MemoryStream stream, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        var reader = await ParquetReader.CreateAsync(stream, cancellationToken: cancellationToken);
        try
        {
            var fields = reader.Schema.DataFields;
            if (fields.Length == 0)
            {
                throw new InvalidOperationException("parquet document has no columns");
            }

            var columns = new TableColumn[fields.Length];
            for (var i = 0; i < fields.Length; i++)
            {
                columns[i] = new TableColumn(fields[i].Name, ColumnKindOf(fields[i], i));
            }

            var groupFirstRows = new int[reader.RowGroupCount];
            var totalRows = 0;
            var rowGroupSize = 0;
            for (var group = 0; group < reader.RowGroupCount; group++)
            {
                using var groupReader = reader.OpenRowGroupReader(group);
                groupFirstRows[group] = totalRows;
                var rowCount = checked((int)groupReader.RowCount);
                totalRows = checked(totalRows + rowCount);
                if (group == 0) rowGroupSize = rowCount;
            }

            return new ParquetTableDocument(
                stream, reader, fields, columns, groupFirstRows, totalRows, rowGroupSize);
        }
        catch
        {
            await reader.DisposeAsync();
            throw;
        }
    }

    public async Task<TableRowGroup> ReadRowGroupAsync(int groupIndex, CancellationToken cancellationToken)
    {
        await Task.Yield();
        await readGate.WaitAsync(cancellationToken);
        try
        {
            using var groupReader = reader.OpenRowGroupReader(groupIndex);
            var rowCount = checked((int)groupReader.RowCount);
            var columns = new TableColumnData[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                columns[i] = await ReadColumnAsync(groupReader, fields[i], rowCount, cancellationToken);
            }

            return new TableRowGroup(groupIndex, groupFirstRows[groupIndex], rowCount, columns);
        }
        finally
        {
            readGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await reader.DisposeAsync();
        await stream.DisposeAsync();
        readGate.Dispose();
    }

    private static async Task<TableColumnData> ReadColumnAsync(
        ParquetRowGroupReader groupReader, DataField field, int rowCount, CancellationToken cancellationToken)
    {
        if (field.ClrType == typeof(long))
        {
            var values = new long[rowCount];
            await groupReader.ReadAsync(field, values.AsMemory(), cancellationToken: cancellationToken);
            return TableColumnData.FromTimestamps(values);
        }

        if (field.ClrType == typeof(double))
        {
            var values = new double[rowCount];
            await groupReader.ReadAsync(field, values.AsMemory(), cancellationToken: cancellationToken);
            return TableColumnData.FromNumbers(values);
        }

        if (field.ClrType == typeof(string))
        {
            var values = new string?[rowCount];
            await groupReader.ReadAsync(field, values.AsMemory(), cancellationToken: cancellationToken);
            return TableColumnData.FromTexts(values);
        }

        throw new InvalidOperationException(
            $"column '{field.Name}' has unsupported type {field.ClrType.Name}; the export schema is long/double/string only");
    }

    private static TableColumnKind ColumnKindOf(DataField field, int index)
    {
        if (field.ClrType == typeof(long))
        {
            return index == 0
                ? TableColumnKind.Timestamp
                : throw new InvalidOperationException(
                    $"long column '{field.Name}' outside position 0; the timestamp indexer must lead");
        }

        if (field.ClrType == typeof(double)) return TableColumnKind.Number;
        if (field.ClrType == typeof(string)) return TableColumnKind.Text;
        throw new InvalidOperationException(
            $"column '{field.Name}' has unsupported type {field.ClrType.Name}; the export schema is long/double/string only");
    }
}
