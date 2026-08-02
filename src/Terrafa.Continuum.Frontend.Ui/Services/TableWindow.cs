// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Services;

public readonly record struct RowWindow(int Start, int End)
{
    public static readonly RowWindow Empty = new(0, 0);

    public int Rows => End - Start;

    public bool IsEmpty => End <= Start;

    public bool Contains(int row) => row >= Start && row < End;
}

public static class TableWindow
{
    public static int EffectiveEviction(int cacheRows, int evictionRows) =>
        Math.Clamp(evictionRows, 1, Math.Max(1, cacheRows / 4));

    public static RowWindow Compute(
        RowWindow current, int cursor, int totalRows, int cacheRows, int evictionRows)
    {
        if (totalRows <= 0 || cacheRows <= 0) return RowWindow.Empty;
        if (totalRows <= cacheRows) return new RowWindow(0, totalRows);

        var eviction = EffectiveEviction(cacheRows, evictionRows);
        var maxStart = totalRows - cacheRows;
        cursor = Math.Clamp(cursor, 0, totalRows - 1);

        if (current.IsEmpty) return new RowWindow(0, cacheRows);

        var farBehind = cursor < current.Start - cacheRows;
        var farAhead = cursor >= current.End + cacheRows;
        if (farBehind || farAhead)
        {
            var centered = AlignDown(cursor - cacheRows / 2, eviction);
            var start = Math.Clamp(centered, 0, maxStart);
            return new RowWindow(start, start + cacheRows);
        }

        var slidStart = current.Start;
        while (cursor >= slidStart + cacheRows - eviction && slidStart < maxStart)
        {
            slidStart = Math.Min(slidStart + eviction, maxStart);
        }

        while (cursor < slidStart + eviction && slidStart > 0)
        {
            slidStart = Math.Max(slidStart - eviction, 0);
        }

        return new RowWindow(slidStart, slidStart + cacheRows);
    }

    public static IReadOnlyList<int> GroupsFor(RowWindow window, int rowGroupSize, int rowGroupCount)
    {
        if (window.IsEmpty || rowGroupSize <= 0 || rowGroupCount <= 0) return [];
        var first = Math.Clamp(window.Start / rowGroupSize, 0, rowGroupCount - 1);
        var last = Math.Clamp((window.End - 1) / rowGroupSize, 0, rowGroupCount - 1);
        return Enumerable.Range(first, last - first + 1).ToArray();
    }

    public static IReadOnlyList<int> LoadOrder(
        RowWindow window, int firstVisibleRow, int visibleRows, int rowGroupSize, int rowGroupCount)
    {
        var groups = GroupsFor(window, rowGroupSize, rowGroupCount);
        if (groups.Count == 0 || rowGroupSize <= 0) return groups;

        var visibleFirst = Math.Max(firstVisibleRow, 0) / rowGroupSize;
        var visibleLast = Math.Max(firstVisibleRow + Math.Max(visibleRows, 1) - 1, 0) / rowGroupSize;

        return groups
            .OrderBy(group => group >= visibleFirst && group <= visibleLast
                ? 0
                : group < visibleFirst ? visibleFirst - group : group - visibleLast)
            .ToArray();
    }

    private static int AlignDown(int value, int step) =>
        value <= 0 ? 0 : value - value % step;
}
