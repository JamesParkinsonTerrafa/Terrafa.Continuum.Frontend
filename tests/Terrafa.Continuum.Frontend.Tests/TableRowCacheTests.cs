// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Tests;

[Collection("workspace")]
public class TableRowCacheTests
{
    [Fact]
    public async Task MissCountsThenArrivalRaisesChangedOnce()
    {
        var document = new FakeTableDocument(totalRows: 50_000, rowGroupSize: 25_000);
        using var cache = new TableRowCache(document, action => action());
        var changedCount = 0;
        cache.Changed += () => changedCount++;

        cache.OnViewport(0, 40);
        Assert.False(cache.TryGetRowGroup(0, out _));
        Assert.Equal(1, cache.Misses);
        Assert.Equal(0, changedCount);

        await document.CompleteAsync(0);
        Assert.Equal(1, changedCount);
        Assert.True(cache.TryGetRowGroup(0, out var group));
        Assert.Equal(25_000, group.RowCount);
        Assert.Equal(1, cache.Hits);
        Assert.Equal(25_000, cache.ResidentRows);
    }

    [Fact]
    public async Task SlideEvictsImmediatelyAndLoadsAhead()
    {
        var document = new FakeTableDocument(totalRows: 5_000_000, rowGroupSize: 25_000);
        using var cache = new TableRowCache(document, action => action());

        cache.OnViewport(0, 40);
        Assert.Equal(new[] { 0, 1, 2, 3 }, document.RequestedGroups);
        await document.CompleteAllAsync();
        Assert.Equal(100_000, cache.ResidentRows);

        cache.OnViewport(74_980, 40);
        Assert.Equal(new RowWindow(25_000, 125_000), cache.Window);
        Assert.Equal(75_000, cache.ResidentRows);
        Assert.False(cache.TryGetRowGroup(0, out _));
        Assert.Equal(4, document.RequestedGroups[^1]);

        await document.CompleteAllAsync();
        Assert.Equal(100_000, cache.ResidentRows);
    }

    [Fact]
    public async Task ResidencyBoundHoldsOverLongScroll()
    {
        var document = new FakeTableDocument(totalRows: 5_000_000, rowGroupSize: 25_000)
        {
            CompleteImmediately = true
        };
        using var cache = new TableRowCache(document, action => action());

        for (var firstRow = 0; firstRow <= 1_000_000; firstRow += 10_000)
        {
            cache.OnViewport(firstRow, 40);
            await document.DrainAsync();
            Assert.True(cache.ResidentRows <= 100_000);
            Assert.True(cache.Window.Contains(firstRow + 20));
        }
    }

    [Fact]
    public async Task ClearDropsStaleArrivalWithoutInstalling()
    {
        var document = new FakeTableDocument(totalRows: 50_000, rowGroupSize: 25_000);
        using var cache = new TableRowCache(document, action => action());
        cache.OnViewport(0, 40);

        var changedAfterClear = 0;
        cache.Clear();
        cache.Changed += () => changedAfterClear++;

        await document.CompleteAsync(0);
        Assert.Equal(0, changedAfterClear);
        Assert.Equal(0, cache.ResidentRows);
        Assert.False(cache.TryGetRowGroup(0, out _));
    }

    [Fact]
    public void LoadOrderPutsVisibleGroupsFirst()
    {
        var document = new FakeTableDocument(totalRows: 5_000_000, rowGroupSize: 25_000);
        using var cache = new TableRowCache(document, action => action());

        cache.OnViewport(55_000, 40);

        Assert.Equal(new[] { 2, 1, 3, 0 }, document.RequestedGroups);
    }

    private sealed class FakeTableDocument : ITableDocument
    {
        private readonly Dictionary<int, TaskCompletionSource<TableRowGroup>> pending = [];
        private readonly List<Task> issued = [];

        public FakeTableDocument(int totalRows, int rowGroupSize)
        {
            TotalRows = totalRows;
            RowGroupSize = rowGroupSize;
            Columns = [new TableColumn("timestamp", TableColumnKind.Timestamp)];
        }

        public bool CompleteImmediately { get; init; }

        public List<int> RequestedGroups { get; } = [];

        public IReadOnlyList<TableColumn> Columns { get; }

        public int TotalRows { get; }

        public int RowGroupSize { get; }

        public int RowGroupCount => (TotalRows + RowGroupSize - 1) / RowGroupSize;

        public Task<TableRowGroup> ReadRowGroupAsync(int groupIndex, CancellationToken cancellationToken)
        {
            RequestedGroups.Add(groupIndex);
            if (CompleteImmediately)
            {
                return Task.FromResult(GroupFor(groupIndex));
            }

            var source = new TaskCompletionSource<TableRowGroup>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[groupIndex] = source;
            cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
            issued.Add(source.Task);
            return source.Task;
        }

        public async Task CompleteAsync(int groupIndex)
        {
            pending[groupIndex].TrySetResult(GroupFor(groupIndex));
            pending.Remove(groupIndex);
            await Task.Yield();
        }

        public async Task CompleteAllAsync()
        {
            foreach (var groupIndex in pending.Keys.ToList())
            {
                await CompleteAsync(groupIndex);
            }
        }

        public async Task DrainAsync()
        {
            await Task.Yield();
        }

        private TableRowGroup GroupFor(int groupIndex)
        {
            var firstRow = groupIndex * RowGroupSize;
            var rowCount = Math.Min(RowGroupSize, TotalRows - firstRow);
            var timestamps = new long[rowCount];
            for (var row = 0; row < rowCount; row++)
            {
                timestamps[row] = firstRow + row;
            }

            return new TableRowGroup(
                groupIndex, firstRow, rowCount, [TableColumnData.FromTimestamps(timestamps)]);
        }
    }
}
