// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Services;

public sealed class TableRowCache : IDisposable
{
    private readonly ITableDocument document;
    private readonly Action<Action> marshal;
    private readonly Dictionary<int, TableRowGroup> resident = [];
    private readonly Dictionary<int, CancellationTokenSource> inFlight = [];
    private RowWindow window = RowWindow.Empty;
    private int generation;

    public TableRowCache(ITableDocument document, Action<Action> marshal)
    {
        this.document = document;
        this.marshal = marshal;
    }

    public event Action? Changed;

    public RowWindow Window => window;

    public int LastFirstRow { get; private set; }

    public int LastVisibleRows { get; private set; } = 1;

    public int ResidentRows { get; private set; }

    public long ResidentBytes { get; private set; }

    public long Hits { get; private set; }

    public long Misses { get; private set; }

    public void OnViewport(int firstRow, int visibleRows)
    {
        LastFirstRow = Math.Max(firstRow, 0);
        LastVisibleRows = Math.Max(visibleRows, 1);
        var cursor = LastFirstRow + LastVisibleRows / 2;
        ApplyWindow(TableWindow.Compute(
            window, cursor, document.TotalRows, TableCacheSettings.CacheRows, TableCacheSettings.EvictionRows));
    }

    public void OnSettingsChanged() => OnViewport(LastFirstRow, LastVisibleRows);

    public bool TryGetRowGroup(int groupIndex, out TableRowGroup group)
    {
        if (resident.TryGetValue(groupIndex, out var found))
        {
            Hits++;
            group = found;
            return true;
        }

        Misses++;
        group = null!;
        return false;
    }

    public void Clear()
    {
        generation++;
        foreach (var pending in inFlight.Values)
        {
            pending.Cancel();
            pending.Dispose();
        }

        inFlight.Clear();
        resident.Clear();
        ResidentRows = 0;
        ResidentBytes = 0;
        window = RowWindow.Empty;
        Changed?.Invoke();
    }

    public void Dispose() => Clear();

    private void ApplyWindow(RowWindow next)
    {
        window = next;
        var wanted = TableWindow.GroupsFor(next, document.RowGroupSize, document.RowGroupCount).ToHashSet();

        var evicted = false;
        foreach (var group in resident.Keys.Where(group => !wanted.Contains(group)).ToList())
        {
            ResidentRows -= resident[group].RowCount;
            ResidentBytes -= resident[group].ApproximateBytes;
            resident.Remove(group);
            evicted = true;
        }

        foreach (var (group, pending) in inFlight.Where(entry => !wanted.Contains(entry.Key)).ToList())
        {
            pending.Cancel();
            pending.Dispose();
            inFlight.Remove(group);
        }

        foreach (var group in TableWindow.LoadOrder(
                     next, LastFirstRow, LastVisibleRows, document.RowGroupSize, document.RowGroupCount))
        {
            if (!resident.ContainsKey(group) && !inFlight.ContainsKey(group)) StartLoad(group);
        }

        if (evicted) Changed?.Invoke();
    }

    private void StartLoad(int groupIndex)
    {
        var pending = new CancellationTokenSource();
        inFlight[groupIndex] = pending;
        _ = LoadAsync(groupIndex, pending, generation);
    }

    private async Task LoadAsync(int groupIndex, CancellationTokenSource pending, int loadGeneration)
    {
        TableRowGroup group;
        try
        {
            group = await document.ReadRowGroupAsync(groupIndex, pending.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            marshal(() =>
            {
                if (loadGeneration == generation
                    && inFlight.TryGetValue(groupIndex, out var current)
                    && ReferenceEquals(current, pending))
                {
                    inFlight.Remove(groupIndex);
                }
            });
            return;
        }

        marshal(() =>
        {
            if (loadGeneration != generation
                || !inFlight.TryGetValue(groupIndex, out var current)
                || !ReferenceEquals(current, pending))
            {
                return;
            }

            inFlight.Remove(groupIndex);
            pending.Dispose();
            resident[groupIndex] = group;
            ResidentRows += group.RowCount;
            ResidentBytes += group.ApproximateBytes;
            Changed?.Invoke();
        });
    }
}
