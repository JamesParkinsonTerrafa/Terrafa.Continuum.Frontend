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
    /// The same schema with each leaf's latest value filled in. Split from
    /// <see cref="GetSchemaAsync"/> because the two cost wildly different amounts: a schema is a
    /// catalog lookup, whereas values come from a query that can take seconds. Opening a dataset
    /// awaits the schema and renders, then awaits this and re-renders, so the structure is on
    /// screen while the values are still in flight.
    /// </summary>
    Task<DatasetSchema> GetSampleAsync(string dataset);
}
