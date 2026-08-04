// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// Reads the values behind a restored session, once, at startup.
///
/// <para>
/// The session persists selections: which datasets are mounted, which leaves were picked, which
/// tiles and network nodes point where. It does not persist numbers. This turns those selections
/// into reads and writes the answers into <see cref="ReadingStore"/>, so the dashboard draws on
/// arrival with nothing for the operator to do.
/// </para>
///
/// <para>
/// Two sets are read, not one. The first is what the tree has mounted. The second is what the
/// tiles and the network point at, which can name a dataset this machine never mounted — a
/// dashboard saved elsewhere, or the same account on another machine. Reading both is what lets a
/// dashboard draw for whoever opens it.
/// </para>
/// </summary>
public static class ReadingLoader
{
    /// <summary>
    /// Reads every dataset the restored session refers to. One dataset failing does not stop the
    /// rest: a tile pointing at something the catalogue no longer serves says so on its own.
    /// </summary>
    public static async Task LoadAsync(IDatasetCatalog catalog)
    {
        foreach (var request in await RequestsAsync(catalog))
        {
            try
            {
                var series = await catalog.GetSeriesAsync(request.Dataset, request.XAxis, request.Paths);
                ReadingStore.Instance.Write(series);
                Workspace.Instance.SetAxis(request.Dataset, series.XAxis);
            }
            catch (Exception)
            {
                // Reported where it shows: a leaf with no value, or a tile that cannot resolve.
            }
        }
    }

    public static async Task LoadDatasetAsync(IDatasetCatalog catalog, string dataset)
    {
        if (Workspace.Instance.Find(dataset) is not { } subtree) return;
        var paths = subtree.Leaves.Select(leaf => leaf.Path).ToHashSet(StringComparer.Ordinal);
        if (paths.Count == 0) return;
        try
        {
            var axis = subtree.XAxis.Length > 0 ? subtree.XAxis : SeriesAxis.Default;
            var series = await catalog.GetSeriesAsync(dataset, axis, paths);
            ReadingStore.Instance.Write(series);
            Workspace.Instance.SetAxis(dataset, series.XAxis);
        }
        catch (Exception)
        {
        }
    }

    private sealed record Request(string Dataset, string XAxis, IReadOnlyCollection<string> Paths);

    private static async Task<IReadOnlyList<Request>> RequestsAsync(IDatasetCatalog catalog)
    {
        var wanted = new Dictionary<string, (string XAxis, HashSet<string> Paths)>(StringComparer.Ordinal);

        void Want(string dataset, string xAxis, string path)
        {
            if (!wanted.TryGetValue(dataset, out var entry))
            {
                entry = (xAxis, new HashSet<string>(StringComparer.Ordinal));
                wanted[dataset] = entry;
            }
            else if (entry.XAxis.Length == 0 && xAxis.Length > 0)
            {
                entry = (xAxis, entry.Paths);
                wanted[dataset] = entry;
            }
            entry.Paths.Add(path);
        }

        foreach (var subtree in Workspace.Instance.Subtrees)
        foreach (var leaf in subtree.Leaves)
            Want(subtree.Dataset, subtree.XAxis, leaf.Path);

        // A tile or a network node can name a dataset this machine has not mounted. Its dataset has
        // to be recognised before its path can be read, and only the catalogue knows the names.
        var referenced = Referenced().ToList();
        if (referenced.Count > 0)
        {
            foreach (var (dataset, path) in await ResolveAsync(catalog, referenced))
                Want(dataset, Workspace.Instance.Find(dataset)?.XAxis ?? "", path);
        }

        return
        [
            .. wanted
                .Where(entry => entry.Value.Paths.Count > 0)
                .Select(entry => new Request(
                    entry.Key,
                    entry.Value.XAxis.Length > 0 ? entry.Value.XAxis : SeriesAxis.Default,
                    entry.Value.Paths))
        ];
    }

    /// <summary>Leaf paths the restored dashboard and network point at.</summary>
    private static IEnumerable<string> Referenced()
    {
        foreach (var placement in Dashboard.Instance.Placements)
        foreach (var source in placement.Tile.Sources)
        {
            // A tile's σ can be asserted from a figure, which the network computes and no read
            // reaches. Only the measure sources name something a query can fetch.
            if (source.Kind != TileSourceKind.Measure) continue;
            if (source.Path.Length > 0) yield return source.Path;
        }

        foreach (var node in NetworkGraph.Instance.Nodes)
        {
            if (node.Kind != NetworkNodeKind.Measure) continue;
            if (node.Key.Length > 0) yield return node.Key;
        }
    }

    /// <summary>
    /// Splits each path into the dataset that owns it and the path itself. A dataset name contains
    /// dots of its own — "synthetic_dev.calibrated__fame_content__idc" — so the split cannot be
    /// done on the string. The longest catalogued name the path starts with is the owner.
    /// </summary>
    private static async Task<IReadOnlyList<(string Dataset, string Path)>> ResolveAsync(
        IDatasetCatalog catalog, IReadOnlyList<string> paths)
    {
        IReadOnlyList<string> names;
        try
        {
            names =
            [
                .. (await catalog.GetAvailableDatasetsAsync())
                    .SelectMany(topic => topic.Value)
                    .Distinct(StringComparer.Ordinal)
                    .OrderByDescending(name => name.Length)
            ];
        }
        catch (Exception)
        {
            return [];
        }

        var owned = new List<(string, string)>();
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            var dataset = names.FirstOrDefault(name =>
                path.Length > name.Length + 1 && path.StartsWith(name + ".", StringComparison.Ordinal));
            if (dataset is not null) owned.Add((dataset, path));
        }
        return owned;
    }
}
