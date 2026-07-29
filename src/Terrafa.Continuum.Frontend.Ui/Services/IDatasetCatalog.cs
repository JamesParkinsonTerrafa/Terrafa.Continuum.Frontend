using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>Schema returned for a single dataset — the root is the dataset's own subtree.</summary>
public sealed record DatasetSchema(
    string Dataset,
    string Provider,
    string Contract,
    string Cadence,
    string Coverage,
    string Licence,
    DataTreeNode Root)
{
    /// <summary>
    /// The column every leaf's series is ordered by, relative to the dataset — "timestamp", or
    /// "reading.taken_at" for a field of a struct. Empty when the schema was read on its own, which
    /// is what a tree carrying no history means.
    /// </summary>
    public string XAxis { get; init; } = "";

    /// <summary>
    /// The most rows sharing one axis value in the fetched window. 1 is the contract a chart
    /// rests on. More means the table interleaves several series in every column — a shape to
    /// fix in the table itself; the client only detects it and declines to draw through it.
    /// </summary>
    public int RowsPerPoint { get; init; } = 1;

    public int LeafCount => Root.Descendants().Count(node => node.Kind == DataNodeKind.Measure);

    public int ObjectCount => Root.Descendants().Count(node => node.Kind == DataNodeKind.Object);
}

public interface IDatasetCatalog
{
    /// <summary>Topic → dataset names. Called once in the background at startup.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAvailableDatasetsAsync();

    /// <summary>Full schema for one dataset. Called when a dataset is opened.</summary>
    Task<DatasetSchema> GetSchemaAsync(string dataset);

    /// <summary>
    /// The same schema with each leaf carrying the readings the service returns, ordered by
    /// <paramref name="xAxis"/> — the series a chart draws, and the most recent of them as the
    /// leaf's own value. Split from <see cref="GetSchemaAsync"/> because the two cost wildly
    /// different amounts: a schema is a catalog lookup, whereas this is a real query that can take
    /// seconds. Opening a dataset awaits the schema and renders, then awaits this and re-renders,
    /// so the structure is on screen while the values are still in flight.
    /// </summary>
    /// <param name="xAxis">
    /// The column to sort on, relative to the dataset. Required rather than optional: without it
    /// the rows arrive in whatever order the engine produced them, and a line through those is a
    /// fabrication. See <see cref="SeriesAxis"/>.
    /// </param>
    Task<DatasetSchema> GetSeriesAsync(string dataset, string xAxis);
}
