// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Parquet;
using Parquet.Schema;
using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Services;

public sealed record TableExportRequest(string Dataset, int RowCount);

/// <summary>
/// The seam where the real fetch slots in: an implementation hands back the input table one
/// row-group's worth at a time — timestamp first, then measure columns, then a text column —
/// returning null when exhausted. A future DataFeedRowSource pages
/// GET /api/datasets/{db}/{table}/data here once the service grows a continuation token
/// (Truncated and QueryExecutionId already come back on every response and go unread).
/// Today's only implementation is the deterministic synthetic source below.
/// </summary>
public interface IRowBatchSource
{
    IReadOnlyList<TableColumn> Columns { get; }

    Task<IReadOnlyList<TableColumnData>?> ReadNextGroupAsync(int maxRows, CancellationToken cancellationToken);
}

public sealed class SyntheticRowSource : IRowBatchSource
{
    private const long EpochStart = 1_577_836_800;
    private const long StepSeconds = 60;

    private readonly TableColumn[] columns;
    private readonly ColumnGenerator[] generators;
    private readonly int totalRows;
    private int nextRow;

    public SyntheticRowSource(string dataset, int rowCount)
    {
        totalRows = Math.Max(rowCount, 0);
        var measureNames = MeasureNamesFor(dataset);
        columns =
        [
            new TableColumn("timestamp", TableColumnKind.Timestamp),
            .. measureNames.Select(name => new TableColumn(name, TableColumnKind.Number)),
            new TableColumn("status", TableColumnKind.Text)
        ];
        generators = measureNames
            .Select(name => ColumnGenerator.For($"{dataset}.{name}"))
            .Append(ColumnGenerator.For($"{dataset}.status"))
            .ToArray();
    }

    public IReadOnlyList<TableColumn> Columns => columns;

    public Task<IReadOnlyList<TableColumnData>?> ReadNextGroupAsync(
        int maxRows, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = Math.Min(maxRows, totalRows - nextRow);
        if (rows <= 0) return Task.FromResult<IReadOnlyList<TableColumnData>?>(null);

        var data = new TableColumnData[columns.Length];
        var timestamps = new long[rows];
        for (var row = 0; row < rows; row++)
        {
            timestamps[row] = EpochStart + (nextRow + row) * StepSeconds;
        }

        data[0] = TableColumnData.FromTimestamps(timestamps);
        for (var i = 0; i < generators.Length - 1; i++)
        {
            data[1 + i] = TableColumnData.FromNumbers(generators[i].NextValues(rows));
        }

        data[^1] = TableColumnData.FromTexts(generators[^1].NextStatuses(rows));
        nextRow += rows;
        return Task.FromResult<IReadOnlyList<TableColumnData>?>(data);
    }

    private static string[] MeasureNamesFor(string dataset)
    {
        var leaves = Workspace.Instance.Find(dataset)?.Leaves
            .Select(leaf => leaf.Name)
            .Where(name => name.Length > 0)
            .Distinct()
            .Take(8)
            .ToArray();
        return leaves is { Length: > 0 }
            ? leaves
            : Enumerable.Range(1, 8).Select(i => $"m{i}").ToArray();
    }

    private sealed class ColumnGenerator
    {
        private uint state;
        private double drift;
        private readonly double baseValue;
        private readonly double scale;

        private ColumnGenerator(uint seed)
        {
            state = seed;
            baseValue = 20 + seed % 1000 / 10.0;
            scale = Math.Max(baseValue * 0.006, 0.01);
        }

        public static ColumnGenerator For(string seedText) => new(Fnv1a(seedText));

        public double[] NextValues(int rows)
        {
            var values = new double[rows];
            for (var row = 0; row < rows; row++)
            {
                drift = drift * 0.999 + (NextUnit(ref state) - 0.5) * scale * 0.9;
                values[row] = baseValue + drift + (NextUnit(ref state) - 0.5) * scale;
            }

            return values;
        }

        public string?[] NextStatuses(int rows)
        {
            var values = new string?[rows];
            for (var row = 0; row < rows; row++)
            {
                var draw = NextUnit(ref state);
                values[row] = draw < 0.02 ? "CAL" : draw < 0.07 ? "DRIFT" : "OK";
            }

            return values;
        }

        private static uint Fnv1a(string text)
        {
            var hash = 2166136261u;
            foreach (var character in text)
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return hash == 0 ? 1u : hash;
        }

        private static double NextUnit(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state / (double)uint.MaxValue;
        }
    }
}

public static class TableExportBuilder
{
    public const int RowGroupSize = 25_000;
    public const CompressionMethod Codec = CompressionMethod.Snappy;
    public const string StubFilter = "timestamp IS NOT NULL";

    public static Task<ParquetTableDocument> BuildAsync(
        TableExportRequest request, IProgress<double>? progress, CancellationToken cancellationToken) =>
        BuildAsync(new SyntheticRowSource(request.Dataset, request.RowCount), request, progress, cancellationToken);

    public static async Task<ParquetTableDocument> BuildAsync(
        IRowBatchSource source,
        TableExportRequest request,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var figureKeys = NetworkGraph.Instance.Nodes
            .Where(node => node.Kind == NetworkNodeKind.Figure)
            .Select(node => node.Key)
            .Distinct()
            .ToArray();
        var figureMagnitudes = figureKeys
            .Select(key => FigureCatalog.Instance.Find(key) is { HasValue: true } figure
                ? Math.Abs(figure.Value)
                : 100.0)
            .ToArray();

        var sourceColumns = source.Columns;
        if (sourceColumns.Count == 0 || sourceColumns[0].Kind != TableColumnKind.Timestamp)
        {
            throw new InvalidOperationException("row source must lead with the timestamp indexer");
        }

        var measureIndexes = Enumerable.Range(0, sourceColumns.Count)
            .Where(i => sourceColumns[i].Kind == TableColumnKind.Number)
            .ToArray();

        var fields = new List<DataField>();
        var writeOrder = new List<(int SourceIndex, int FigureIndex)>();
        for (var i = 0; i < sourceColumns.Count; i++)
        {
            if (sourceColumns[i].Kind == TableColumnKind.Text) continue;
            fields.Add(sourceColumns[i].Kind == TableColumnKind.Timestamp
                ? new DataField<long>(sourceColumns[i].Name)
                : new DataField<double>(sourceColumns[i].Name));
            writeOrder.Add((i, -1));
        }

        for (var f = 0; f < figureKeys.Length; f++)
        {
            fields.Add(new DataField<double>($"fig.{figureKeys[f]}"));
            writeOrder.Add((-1, f));
        }

        for (var i = 0; i < sourceColumns.Count; i++)
        {
            if (sourceColumns[i].Kind != TableColumnKind.Text) continue;
            fields.Add(new DataField<string>(sourceColumns[i].Name));
            writeOrder.Add((i, -1));
        }

        var schema = new ParquetSchema(fields.Cast<Field>().ToArray());
        var stream = new MemoryStream();
        try
        {
            var options = new ParquetOptions { CompressionMethod = Codec };
            await using (var writer = await ParquetWriter.CreateAsync(
                             schema, stream, options, cancellationToken: cancellationToken))
            {
                var writtenRows = 0;
                while (true)
                {
                    var batch = await source.ReadNextGroupAsync(RowGroupSize, cancellationToken);
                    if (batch is null) break;

                    var figureColumns = ComputeStubFigureColumns(
                        batch, measureIndexes, figureMagnitudes);

                    using (var rowGroup = writer.CreateRowGroup())
                    {
                        for (var w = 0; w < writeOrder.Count; w++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var (sourceIndex, figureIndex) = writeOrder[w];
                            if (figureIndex >= 0)
                            {
                                await rowGroup.WriteAsync(
                                    (DataField<double>)fields[w],
                                    new ReadOnlyMemory<double>(figureColumns[figureIndex]),
                                    cancellationToken: cancellationToken);
                            }
                            else if (batch[sourceIndex].Timestamps is { } timestamps)
                            {
                                await rowGroup.WriteAsync(
                                    (DataField<long>)fields[w],
                                    new ReadOnlyMemory<long>(timestamps),
                                    cancellationToken: cancellationToken);
                            }
                            else if (batch[sourceIndex].Numbers is { } numbers)
                            {
                                await rowGroup.WriteAsync(
                                    (DataField<double>)fields[w],
                                    new ReadOnlyMemory<double>(numbers),
                                    cancellationToken: cancellationToken);
                            }
                            else
                            {
                                await rowGroup.WriteAsync(fields[w], batch[sourceIndex].Texts!);
                            }
                        }
                    }

                    writtenRows += batch[0].Count;
                    progress?.Report(Math.Min((double)writtenRows / Math.Max(request.RowCount, 1), 1));
                    await Task.Yield();
                }
            }

            return await ParquetTableDocument.OpenAsync(stream, cancellationToken);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    // STUB: per-row figures are a deterministic affine mix of the row's measure cells rescaled
    // to the figure's declared magnitude. The real implementation walks the network per row
    // through TransferMath.Evaluate and is deliberately deferred.
    private static double[][] ComputeStubFigureColumns(
        IReadOnlyList<TableColumnData> batch,
        int[] measureIndexes,
        double[] figureMagnitudes)
    {
        var rows = batch[0].Count;
        var figures = new double[figureMagnitudes.Length][];
        for (var f = 0; f < figureMagnitudes.Length; f++)
        {
            var column = new double[rows];
            var magnitude = figureMagnitudes[f] <= 0 ? 100.0 : figureMagnitudes[f];
            if (measureIndexes.Length == 0)
            {
                Array.Fill(column, magnitude);
            }
            else
            {
                var weightTotal = 0.0;
                for (var m = 0; m < measureIndexes.Length; m++)
                {
                    weightTotal += f + m + 1;
                }

                for (var row = 0; row < rows; row++)
                {
                    var mix = 0.0;
                    for (var m = 0; m < measureIndexes.Length; m++)
                    {
                        mix += (f + m + 1) * batch[measureIndexes[m]].Numbers![row];
                    }

                    column[row] = magnitude * mix / (weightTotal * 100.0);
                }
            }

            figures[f] = column;
        }

        return figures;
    }
}
