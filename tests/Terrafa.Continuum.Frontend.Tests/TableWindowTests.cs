// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Tests;

public class TableWindowTests
{
    private const int Total = 5_000_000;
    private const int Cache = 100_000;
    private const int Eviction = 25_000;

    private static RowWindow Compute(RowWindow current, int cursor) =>
        TableWindow.Compute(current, cursor, Total, Cache, Eviction);

    [Fact]
    public void EntryFillsFromRowZero()
    {
        Assert.Equal(new RowWindow(0, Cache), Compute(RowWindow.Empty, 0));
    }

    [Fact]
    public void WorkedExample_100kCache_25kEviction_SlidesAt75k()
    {
        var window = Compute(RowWindow.Empty, 0);
        Assert.Equal(new RowWindow(0, 100_000), window);

        window = Compute(window, 50_000);
        Assert.Equal(new RowWindow(0, 100_000), window);

        window = Compute(window, 74_999);
        Assert.Equal(new RowWindow(0, 100_000), window);

        window = Compute(window, 75_000);
        Assert.Equal(new RowWindow(25_000, 125_000), window);
    }

    [Fact]
    public void BackwardSlide_IsSymmetric()
    {
        var window = new RowWindow(25_000, 125_000);

        Assert.Equal(window, Compute(window, 50_000));
        Assert.Equal(new RowWindow(0, 100_000), Compute(window, 49_999));
    }

    [Fact]
    public void DeadBand_NoChangeBetween50kAnd99k()
    {
        var window = new RowWindow(25_000, 125_000);
        foreach (var cursor in new[] { 50_000, 62_500, 75_000, 87_500, 99_999 })
        {
            Assert.Equal(window, Compute(window, cursor));
        }
    }

    [Fact]
    public void JumpFarOutsideWindow_RecentersAligned()
    {
        var window = Compute(new RowWindow(0, 100_000), 4_900_000);

        Assert.True(window.Contains(4_900_000));
        Assert.Equal(0, window.Start % Eviction);
        Assert.Equal(Cache, window.Rows);
        Assert.Equal(new RowWindow(4_850_000, 4_950_000), window);
    }

    [Fact]
    public void ClampsAtTail_WindowStopsAtTotalRows()
    {
        var window = Compute(new RowWindow(0, 100_000), Total - 1);
        Assert.Equal(new RowWindow(Total - Cache, Total), window);

        var slid = window;
        for (var cursor = Total - Cache; cursor < Total; cursor += 10_000)
        {
            slid = Compute(slid, cursor);
            Assert.True(slid.End <= Total);
        }

        Assert.Equal(new RowWindow(Total - Cache, Total), slid);
    }

    [Fact]
    public void ClampsAtZero()
    {
        var window = Compute(new RowWindow(25_000, 125_000), 0);
        Assert.Equal(new RowWindow(0, 100_000), window);
    }

    [Fact]
    public void TotalSmallerThanCache_WholeTableResident_NeverEvicts()
    {
        var window = TableWindow.Compute(RowWindow.Empty, 0, 2_500, Cache, Eviction);
        Assert.Equal(new RowWindow(0, 2_500), window);

        Assert.Equal(window, TableWindow.Compute(window, 2_499, 2_500, Cache, Eviction));
    }

    [Fact]
    public void EffectiveEviction_CappedAtQuarterOfCache()
    {
        Assert.Equal(25_000, TableWindow.EffectiveEviction(100_000, 60_000));
        Assert.Equal(25_000, TableWindow.EffectiveEviction(100_000, 25_000));
        Assert.Equal(5_000, TableWindow.EffectiveEviction(100_000, 5_000));

        var window = TableWindow.Compute(new RowWindow(0, 100_000), 75_000, Total, 100_000, 60_000);
        Assert.Equal(new RowWindow(25_000, 125_000), window);
    }

    [Fact]
    public void MultiChunkCatchUp_FastScrollSlidesSeveralChunks()
    {
        var window = Compute(new RowWindow(0, 100_000), 160_000);

        Assert.Equal(new RowWindow(100_000, 200_000), window);
        Assert.True(window.Contains(160_000));
    }

    [Fact]
    public void GroupsFor_PartialOverlapIncludesEdgeGroups()
    {
        var groups = TableWindow.GroupsFor(new RowWindow(30_000, 130_000), 25_000, 200);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, groups);

        Assert.Empty(TableWindow.GroupsFor(RowWindow.Empty, 25_000, 200));

        var lastShortGroup = TableWindow.GroupsFor(new RowWindow(40_000, 60_000), 25_000, 3);
        Assert.Equal(new[] { 1, 2 }, lastShortGroup);
    }

    [Fact]
    public void LoadOrder_VisibleGroupsFirst()
    {
        var order = TableWindow.LoadOrder(
            new RowWindow(0, 100_000), firstVisibleRow: 55_000, visibleRows: 40, rowGroupSize: 25_000, rowGroupCount: 200);

        Assert.Equal(2, order[0]);
        Assert.Equal(new[] { 2, 1, 3, 0 }, order);
    }

    [Fact]
    public void WindowAlignment_StartAlignsToEvictionChunks()
    {
        var window = Compute(new RowWindow(0, 100_000), 3_141_592);

        Assert.Equal(0, window.Start % Eviction);
        Assert.True(window.Contains(3_141_592));
    }
}
