using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// Which column a dataset's readings are ordered by.
///
/// <para>
/// A chart needs its rows in a defined order and Athena has none of its own: a read with no
/// ORDER BY returns whatever the engine happened to produce, and the service applies its row cap
/// after ordering, so an unordered read is not even a stable sample of the same rows twice.
/// Drawing a line through them would invent an x axis. The axis is therefore settled before any
/// values are asked for, and the service sorts on it.
/// </para>
/// </summary>
public static class SeriesAxis
{
    /// <summary>
    /// The column taken as the axis without asking anyone. Nearly every table in the lake carries
    /// one, so the common case is that nobody has to choose.
    /// </summary>
    public const string Default = "timestamp";

    /// <summary>
    /// The column declaring replicate members. A table of twelve sensors reads the same quantity
    /// twelve times per axis point, and the tree gives each sensor its own subtree of leaves —
    /// the ensemble shape the demo's MET_ENSEMBLE already draws — so every leaf is one
    /// instrument's real series. A named convention like <see cref="Default"/>: no guessing.
    /// </summary>
    public const string Member = "sensor_id";

    /// <summary>A leaf's path relative to its dataset — "level", not "topic.table.level".</summary>
    public static string Relative(string dataset, string path) =>
        path.Length > dataset.Length + 1 && path.StartsWith(dataset, StringComparison.Ordinal)
            ? path[(dataset.Length + 1)..]
            : path;

    /// <summary>
    /// The columns that can carry the axis. A struct is not a leaf here to begin with, and arrays
    /// and maps have no ordering — the service rejects both with a 400 — so offering one would turn
    /// a pick into an error the operator could do nothing about.
    /// </summary>
    public static IReadOnlyList<string> Candidates(DatasetSchema schema) =>
    [
        .. schema.Root.Descendants()
            .Where(node => node.Kind == DataNodeKind.Measure && node.Reading is { IsVector: false })
            .Select(node => Relative(schema.Dataset, node.Path))
    ];

    /// <summary>
    /// The axis a dataset takes on its own, or null when someone has to choose one. A top-level
    /// <see cref="Default"/> wins over a field of that name inside a struct: both are plausible,
    /// but only the first is the convention, and picking the buried one silently would order a
    /// whole dataset by something nobody looked at.
    /// </summary>
    public static string? Preferred(DatasetSchema schema)
    {
        var candidates = Candidates(schema);
        return candidates.FirstOrDefault(path => Matches(path, Default))
            ?? candidates.FirstOrDefault(path => Matches(Leaf(path), Default));
    }

    private static string Leaf(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot >= 0 ? path[(dot + 1)..] : path;
    }

    private static bool Matches(string path, string name) =>
        path.Equals(name, StringComparison.OrdinalIgnoreCase);
}
