// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Tests;

[Collection("workspace")]
public class TableDocumentTests
{
    private static Task<ParquetTableDocument> BuildAsync(int rows, CancellationToken cancellationToken = default) =>
        TableExportBuilder.BuildAsync(
            new TableExportRequest(ExportTable.SyntheticDataset, rows), null, cancellationToken);

    [Fact]
    public async Task BuildIsDeterministic_SameRequestTwiceYieldsIdenticalCells()
    {
        await using var first = await BuildAsync(60_000);
        await using var second = await BuildAsync(60_000);

        Assert.Equal(first.ParquetBytes, second.ParquetBytes);

        foreach (var group in new[] { 0, 2 })
        {
            var firstGroup = await first.ReadRowGroupAsync(group, CancellationToken.None);
            var secondGroup = await second.ReadRowGroupAsync(group, CancellationToken.None);
            for (var column = 0; column < first.Columns.Count; column++)
            {
                foreach (var row in new[] { 0, 1, firstGroup.RowCount - 1 })
                {
                    Assert.Equal(
                        firstGroup.Columns[column].Format(row),
                        secondGroup.Columns[column].Format(row));
                }
            }
        }
    }

    [Fact]
    public async Task SchemaShape_TimestampLeads_AscendsAcrossGroups_OneColumnPerFigureNode()
    {
        await using var document = await BuildAsync(60_000);

        Assert.Equal(TableColumnKind.Timestamp, document.Columns[0].Kind);
        Assert.Equal("timestamp", document.Columns[0].Name);

        var expectedFigures = NetworkGraph.Instance.Nodes
            .Where(node => node.Kind == NetworkNodeKind.Figure)
            .Select(node => $"fig.{node.Key}")
            .Distinct()
            .OrderBy(name => name)
            .ToArray();
        var actualFigures = document.Columns
            .Where(column => column.Name.StartsWith("fig.", StringComparison.Ordinal))
            .Select(column => column.Name)
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(expectedFigures, actualFigures);
        Assert.NotEmpty(actualFigures);

        var groupZero = await document.ReadRowGroupAsync(0, CancellationToken.None);
        var groupOne = await document.ReadRowGroupAsync(1, CancellationToken.None);
        var zeroTimestamps = groupZero.Columns[0].Timestamps!;
        var oneTimestamps = groupOne.Columns[0].Timestamps!;

        for (var row = 1; row < zeroTimestamps.Length; row++)
        {
            Assert.Equal(60, zeroTimestamps[row] - zeroTimestamps[row - 1]);
        }

        Assert.Equal(60, oneTimestamps[0] - zeroTimestamps[^1]);
    }

    [Fact]
    public async Task GroupPartitioning_60kSplitsAs25k25k10k()
    {
        await using var document = await BuildAsync(60_000);

        Assert.Equal(60_000, document.TotalRows);
        Assert.Equal(25_000, document.RowGroupSize);
        Assert.Equal(3, document.RowGroupCount);

        var groups = new List<TableRowGroup>();
        for (var group = 0; group < document.RowGroupCount; group++)
        {
            groups.Add(await document.ReadRowGroupAsync(group, CancellationToken.None));
        }

        Assert.Equal(new[] { 25_000, 25_000, 10_000 }, groups.Select(group => group.RowCount));
        Assert.Equal(new[] { 0, 25_000, 50_000 }, groups.Select(group => group.FirstRow));
    }

    [Fact]
    public async Task OutOfOrderReads_YieldIdenticalCells()
    {
        await using var document = await BuildAsync(60_000);

        var lastFirst = await document.ReadRowGroupAsync(2, CancellationToken.None);
        var zeroAfter = await document.ReadRowGroupAsync(0, CancellationToken.None);
        var lastAgain = await document.ReadRowGroupAsync(2, CancellationToken.None);

        Assert.Equal(0, zeroAfter.FirstRow);
        for (var column = 0; column < document.Columns.Count; column++)
        {
            Assert.Equal(lastFirst.Columns[column].Format(0), lastAgain.Columns[column].Format(0));
            Assert.Equal(
                lastFirst.Columns[column].Format(lastFirst.RowCount - 1),
                lastAgain.Columns[column].Format(lastAgain.RowCount - 1));
        }
    }

    [Fact]
    public async Task CancellationMidBuild_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelOnFirstReport(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TableExportBuilder.BuildAsync(
                new TableExportRequest(ExportTable.SyntheticDataset, 100_000), progress, cancellation.Token));
    }

    [Fact]
    public async Task CancellationMidRead_Throws()
    {
        await using var document = await BuildAsync(30_000);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            document.ReadRowGroupAsync(0, new CancellationToken(canceled: true)));
    }

    private sealed class CancelOnFirstReport(CancellationTokenSource cancellation) : IProgress<double>
    {
        public void Report(double value) => cancellation.Cancel();
    }
}
