// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend;

/// <summary>
/// Reads a dataset through the live signed-in path and prints what came back, without starting a
/// window. The session is restored from the keychain exactly as the app restores it, so this
/// exercises the real catalogue against the real service rather than a stub.
///
/// <para>
/// It exists because the failures worth chasing here are all in what the cells hold — a column
/// that arrived empty, a row order nobody asked for — and none of that is visible from a
/// screenshot of a tile that says "empty table". Run as
/// <c>dotnet run --project src/Terrafa.Continuum.Frontend -- --probe &lt;dataset&gt;</c>.
/// </para>
/// </summary>
internal static class DataProbe
{
    public static async Task RunAsync(string dataset)
    {
        AuthSession.Instance.Store = new KeychainSecretStore();
        await AuthSession.Instance.TryRestoreAsync();
        Console.WriteLine($"signed in: {AuthSession.Instance.IsSignedIn} ({AuthSession.Instance.Username ?? "—"})");
        if (!AuthSession.Instance.IsSignedIn) return;

        using var catalog = new SessionDatasetCatalog();
        Console.WriteLine($"live: {catalog.IsLive}");

        try
        {
            var schema = await catalog.GetSeriesAsync(dataset, SeriesAxis.Default);
            Console.WriteLine($"axis: '{schema.XAxis}' · rows/point: {schema.RowsPerPoint}");
            foreach (var leaf in schema.Root.Descendants().Where(node => node.Kind == DataNodeKind.Measure))
            {
                var reading = leaf.Reading;
                var sample = reading is { Cells.Count: > 0 } ? string.Join(", ", reading.Cells.Take(3)) : "";
                Console.WriteLine(
                    $"  {leaf.Path}: cells={reading?.Cells.Count ?? 0} history={reading?.History.Count ?? 0} [{sample}]");
            }
        }
        catch (Exception error)
        {
            Console.WriteLine($"FAILED: {error.Message}");
        }

        // The saved session, restored the way the app restores it, then every derived table
        // recomputed from it — which is precisely what a grid tile draws.
        UserStateSync.Store = new HttpUserStateStore();
        UserStateSync.Catalog = catalog;
        await UserStateSync.LoadAllAsync();

        Console.WriteLine();
        foreach (var subtree in Workspace.Instance.Subtrees)
        {
            var withCells = subtree.Leaves.Count(leaf => Workspace.ReadingAt(leaf.Path)?.Cells.Count > 0);
            Console.WriteLine(
                $"mount {subtree.Dataset}: axis='{subtree.XAxis}' leaves={subtree.LeafCount} withCells={withCells}");
        }
        foreach (var link in Workspace.Instance.Links)
            Console.WriteLine($"link {link.LeftPath} {link.Symbol} {link.RightPath}");

        Console.WriteLine();
        foreach (var table in TableCatalog.Instance.Tables)
            Console.WriteLine($"table {table.Name}: {table.StateNote} · index='{table.DefaultIndex}' · {table.Note}");
    }
}
