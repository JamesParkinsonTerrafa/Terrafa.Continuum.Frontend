// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>A dataset the app meant to read and could not, with the reason already fit to show.</summary>
public sealed record ReadFailure(string Dataset, string Message);

/// <summary>
/// The read path: selections in, values in <see cref="ReadingStore"/> out.
///
/// <para>
/// <see cref="ReadAsync"/> is the only place a value enters the app. Everything that wants one — a
/// screen opening a dataset, a restore filling in what a saved session refers to, a dataset just
/// mounted — goes through it, so the three steps that must happen together (fetch, write to the
/// store, record the axis the read actually used) cannot come apart. They were written out
/// separately in three places before this, which is two more than the rule allows.
/// </para>
///
/// <para>
/// The session persists selections: which datasets are mounted, which leaves were picked, which
/// tiles and network nodes point where. It does not persist numbers. <see cref="LoadAsync"/> turns
/// those selections into reads, so a restored dashboard draws on arrival with nothing for the
/// operator to do. Two sets are read, not one: what the tree has mounted, and what the tiles and
/// the network point at — which can name a dataset this machine never mounted, a dashboard saved
/// elsewhere or the same account on another machine. Reading both is what lets a dashboard draw
/// for whoever opens it.
/// </para>
///
/// <para>
/// Failures are returned rather than swallowed. A dataset that cannot be read is a fact the
/// operator can act on — the catalogue moved, the service is down, the sign-in lapsed — and it used
/// to vanish into an empty catch, leaving a blank tile and no way to tell an empty dataset from an
/// unreachable one. Cancellation is not a failure and propagates: it means a newer session
/// superseded this one, and the answer is no longer wanted.
/// </para>
/// </summary>
public static class ReadingLoader
{
    /// <summary>
    /// Reads one dataset and publishes it. Values are found by path, so this one write reaches the
    /// preview, every mount of the dataset and every tile wired to it — nothing walks a tree to
    /// hand them out.
    /// </summary>
    /// <exception cref="DataFeedException">The service could not answer.</exception>
    /// <exception cref="OperationCanceledException">A newer read superseded this one.</exception>
    public static async Task<DatasetSchema> ReadAsync(
        IDatasetCatalog catalog, DatasetQuery query, CancellationToken cancellationToken = default)
    {
        var series = await catalog.GetSeriesAsync(query, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        ReadingStore.Instance.Write(series);

        // The axis the read used, not the one it was asked for: a table with no such column is read
        // unordered and says so. The subtree keeps it so a screen downstream can state what a
        // chart's x axis is rather than implying the points are evenly spaced in time.
        Workspace.Instance.SetAxis(query.Dataset, series.XAxis);
        return series;
    }

    /// <summary>
    /// Reads every dataset the restored session refers to. One dataset failing does not stop the
    /// rest — each is reported on its own, and a tile pointing at something the catalogue no longer
    /// serves says so where it is drawn.
    /// </summary>
    public static async Task<IReadOnlyList<ReadFailure>> LoadAsync(
        IDatasetCatalog catalog, CancellationToken cancellationToken = default)
    {
        var failures = new List<ReadFailure>();
        foreach (var query in await PlanAsync(catalog, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ReadAsync(catalog, query, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new ReadFailure(query.Dataset, Describe(ex)));
            }
        }
        return failures;
    }

    /// <summary>
    /// Reads one dataset that has just been mounted, against the leaves the mount holds. Null when
    /// it read, or when there was nothing to read.
    /// </summary>
    public static async Task<ReadFailure?> LoadDatasetAsync(
        IDatasetCatalog catalog, string dataset, CancellationToken cancellationToken = default)
    {
        if (Workspace.Instance.Find(dataset) is not { } subtree) return null;

        var paths = subtree.Leaves.Select(leaf => leaf.Path).ToHashSet(StringComparer.Ordinal);
        if (paths.Count == 0) return null;

        var axis = subtree.XAxis.Length > 0 ? subtree.XAxis : SeriesAxis.Default;
        try
        {
            await ReadAsync(catalog, new DatasetQuery(dataset, axis, paths), cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ReadFailure(dataset, Describe(ex));
        }
    }

    /// <summary>
    /// A DataFeedException already reads as a sentence — the service writes specific messages and
    /// the client passes them through. Anything else is unexpected, so it is named as such.
    /// </summary>
    public static string Describe(Exception ex) =>
        ex is DataFeedException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";

    // ── working out what to read ─────────────────────────────────────────────

    private static async Task<IReadOnlyList<DatasetQuery>> PlanAsync(
        IDatasetCatalog catalog, CancellationToken cancellationToken)
    {
        var wanted = new Dictionary<string, (string Axis, HashSet<string> Paths)>(StringComparer.Ordinal);

        void Want(string dataset, string axis, string path)
        {
            if (!wanted.TryGetValue(dataset, out var entry))
            {
                entry = (axis, new HashSet<string>(StringComparer.Ordinal));
                wanted[dataset] = entry;
            }
            else if (entry.Axis.Length == 0 && axis.Length > 0)
            {
                entry = (axis, entry.Paths);
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
            foreach (var (dataset, path) in await ResolveAsync(catalog, referenced, cancellationToken))
                Want(dataset, Workspace.Instance.Find(dataset)?.XAxis ?? "", path);
        }

        return
        [
            .. wanted
                .Where(entry => entry.Value.Paths.Count > 0)
                .Select(entry => new DatasetQuery(
                    entry.Key,
                    entry.Value.Axis.Length > 0 ? entry.Value.Axis : SeriesAxis.Default,
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
    ///
    /// <para>
    /// An unreachable catalogue yields nothing rather than throwing: the mounted datasets above are
    /// still worth reading, and each of those reports its own failure if it has one.
    /// </para>
    /// </summary>
    private static async Task<IReadOnlyList<(string Dataset, string Path)>> ResolveAsync(
        IDatasetCatalog catalog, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> names;
        try
        {
            names =
            [
                .. (await catalog.GetAvailableDatasetsAsync(cancellationToken))
                    .SelectMany(topic => topic.Value)
                    .Distinct(StringComparer.Ordinal)
                    .OrderByDescending(name => name.Length)
            ];
        }
        catch (OperationCanceledException)
        {
            throw;
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
