// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Models;

/// <summary>
/// Every value the app has read, keyed by leaf path.
///
/// <para>
/// A tree node holds identity: a name, a path, a place in a shape. It does not hold a number. The
/// number lives here and is found by path, so one write reaches every screen at once. Mounting
/// copies nodes, and it used to copy their values with them — a leaf then reported whatever it was
/// built with until someone unmounted and mounted the dataset again. Nothing copies a value now,
/// so nothing can hold a stale one.
/// </para>
///
/// <para>
/// The store is filled at startup, for the selections the session restored. It is not a cache with
/// a clock behind it. When the feed becomes push-based, the pushes write here and every screen
/// follows.
/// </para>
/// </summary>
/// <param name="Rows">Rows kept per column for this dataset.</param>
/// <param name="Truncated">
/// Whether the table holds more rows than were read — the service hitting its own cap, or the
/// query's window keeping only part of what arrived.
/// </param>
public sealed record ReadWindow(int Rows, bool Truncated);

public sealed class ReadingStore
{
    public static ReadingStore Instance { get; } = new();

    private readonly Dictionary<string, Measure> readings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReadWindow> windows = new(StringComparer.Ordinal);

    public event Action? Changed;

    private ReadingStore()
    {
    }

    public int Count => readings.Count;

    /// <summary>The value at a path, or null when nothing has been read for it.</summary>
    public Measure? Find(string path) => readings.GetValueOrDefault(path);

    /// <summary>
    /// How much of a dataset the last read of it saw, or null when it has not been read.
    ///
    /// <para>
    /// Kept beside the values because it is a property of the read, and everything downstream works
    /// from the cells rather than the schema that produced them. Without it a join over the newest
    /// 240 rows of a 10,000-row table could only report "240/240 base rows matched" — complete
    /// success, over a window, with nothing to say a window existed.
    /// </para>
    /// </summary>
    public ReadWindow? WindowOf(string dataset) => windows.GetValueOrDefault(dataset);

    /// <summary>
    /// Takes every value a fetched schema carries. Paths the schema does not mention keep what they
    /// had: a narrowed read asks for some columns, and it must not blank the rest.
    /// </summary>
    public void Write(DatasetSchema schema)
    {
        var written = false;
        foreach (var node in schema.Root.Descendants())
        {
            if (node.Kind != DataNodeKind.Measure) continue;
            if (node.DeclaredReading is not { } reading) continue;
            readings[node.Path] = reading;
            written = true;
        }

        // Only a read that carried rows says anything about the window. A structure-only schema is
        // written on the way to a mount and must not report this dataset as having been read.
        if (schema.WindowRows > 0)
            windows[schema.Dataset] = new ReadWindow(schema.WindowRows, schema.Truncated);

        if (written) Changed?.Invoke();
    }

    /// <summary>
    /// Drops one dataset's values. Called when a session ends: values read for one account must not
    /// still be on screen under the next one.
    /// </summary>
    public void Remove(string dataset)
    {
        var prefix = dataset + ".";
        var gone = readings.Keys.Where(path => path.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        windows.Remove(dataset);
        if (gone.Count == 0) return;

        foreach (var path in gone) readings.Remove(path);
        Changed?.Invoke();
    }

    public void Clear()
    {
        windows.Clear();
        if (readings.Count == 0) return;
        readings.Clear();
        Changed?.Invoke();
    }
}
