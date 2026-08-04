// Copyright (c) 2026 Terrafa Limited. All rights reserved.

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
    /// is what a tree carrying no history means, and also what a table with no column by the asked
    /// name reports: the read is the authority on the axis, not the request.
    /// </summary>
    public string XAxis { get; init; } = "";

    /// <summary>
    /// The most rows sharing one axis value in the fetched window. 1 is the contract a chart
    /// rests on. More means the table interleaves several series in every column — a shape to
    /// fix in the table itself; the client only detects it and declines to draw through it.
    /// </summary>
    public int RowsPerPoint { get; init; } = 1;

    /// <summary>
    /// True when this read did not see every row the table holds — either the service hit its own
    /// cap and said so, or more rows arrived than the query's window kept.
    ///
    /// <para>
    /// Worth carrying because a windowed read is indistinguishable from a complete one everywhere
    /// downstream. A join over the newest 240 rows of a 10,000-row table reported "240/240 base
    /// rows matched": total success, over a window, with nothing to say a window existed.
    /// </para>
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>Rows kept per column. 0 for a tree that carries no rows at all.</summary>
    public int WindowRows { get; init; }

    public int LeafCount => Root.Descendants().Count(node => node.Kind == DataNodeKind.Measure);

    public int ObjectCount => Root.Descendants().Count(node => node.Kind == DataNodeKind.Object);
}

/// <summary>
/// One read of one dataset: which rows, in what order, projected to which columns.
///
/// <para>
/// A record rather than three loose arguments because these travel together everywhere — the
/// screen's read, the restore's read and the cache key are all the same three facts — and because
/// the feed is heading towards a subscribe model whose historic pull takes exactly this shape plus
/// a date range. When that lands, the range is a member here and no call site changes.
/// </para>
/// </summary>
/// <param name="Axis">
/// The column to sort on, relative to the dataset. Required rather than optional: without it the
/// rows arrive in whatever order the engine produced them, and a line through those is a
/// fabrication. See <see cref="SeriesAxis"/>. A dataset with no such column is read unordered and
/// says so in <see cref="DatasetSchema.XAxis"/>.
/// </param>
/// <param name="Paths">
/// Full paths of the leaves to read, or null to read the whole table. Parquet is columnar, so a
/// narrower projection scans fewer bytes: reading the leaves someone actually selected costs less
/// than reading all of them. A σ carrier beside a wanted leaf comes along with it, and so does the
/// axis. Leaves outside the list arrive with no value.
/// </param>
/// <param name="MaxRows">
/// Most rows to keep per column, from the recent end. Named for the service's own cap so the word
/// means one thing end to end.
///
/// <para>
/// It bounds what is transferred and held, not what is scanned — see
/// <see cref="DataFeedOptions.SeriesRows"/> for why a row cap cannot be a cost control here. The
/// default is deliberately small so that browsing a catalogue during development cannot pull a
/// large table into a browser heap; a caller that genuinely wants the rows asks for them.
/// </para>
/// </param>
public sealed record DatasetQuery(
    string Dataset,
    string Axis,
    IReadOnlyCollection<string>? Paths = null,
    int MaxRows = DataFeedOptions.SeriesRows)
{
    /// <summary>
    /// Everything about this read that makes it a different read, as one comparable value. The row
    /// cap belongs in here: a cache keyed without it hands a caller that asked for five thousand
    /// rows the two hundred and forty a previous caller settled for, and reports it as a complete
    /// answer.
    /// </summary>
    public (string Dataset, string Axis, string Projection, int MaxRows) CacheKey =>
        (Dataset,
            Axis,
            Paths is { Count: > 0 } paths ? string.Join('\n', paths.Order(StringComparer.Ordinal)) : "",
            MaxRows);
}

public interface IDatasetCatalog
{
    /// <summary>
    /// True when reads go to the real service rather than the built-in demo data. On the interface
    /// rather than inferred from the implementation type, so a screen showing which source it is
    /// reading from does not have to know which implementations exist.
    /// </summary>
    bool IsLive { get; }

    /// <summary>
    /// Databases the most recent listing could not read. The service answers 502 only when *every*
    /// database failed, so a partial failure arrives as a normal 200 with this filled in — dropping
    /// it silently would show a short catalogue as if it were complete. Empty on demo data, which
    /// cannot fail.
    /// </summary>
    IReadOnlyList<string> Warnings { get; }

    /// <summary>Topic → dataset names. Called once in the background at startup.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAvailableDatasetsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Full schema for one dataset. Called when a dataset is opened.</summary>
    Task<DatasetSchema> GetSchemaAsync(string dataset, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same schema with each leaf carrying the readings the service returns, ordered by the
    /// query's axis — the series a chart draws, and the most recent of them as the leaf's own
    /// value. Split from <see cref="GetSchemaAsync"/> because the two cost wildly different
    /// amounts: a schema is a catalog lookup, whereas this is a real query that can take seconds.
    /// Opening a dataset awaits the schema and renders, then awaits this and re-renders, so the
    /// structure is on screen while the values are still in flight.
    /// </summary>
    Task<DatasetSchema> GetSeriesAsync(DatasetQuery query, CancellationToken cancellationToken = default);
}
