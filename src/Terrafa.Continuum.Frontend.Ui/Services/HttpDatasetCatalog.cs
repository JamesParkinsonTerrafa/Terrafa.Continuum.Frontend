using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// The catalogue, read from the Terrafa Continuum DataFeed service over HTTP.
///
/// <para>
/// The service speaks in Athena terms — databases holding tables of typed columns — and this
/// screen speaks in topics holding datasets of measures, so the mapping is the substance of this
/// class: a database becomes a topic, a table becomes a dataset, and a column becomes either a
/// leaf or, when its type is a struct, an object whose fields are leaves. That last case is why
/// the tree is worth building at all — the service resolves dotted <c>parent.child</c> paths
/// against struct fields, so a path in this tree is a path the data endpoint accepts verbatim.
/// </para>
/// </summary>
public sealed class HttpDatasetCatalog : IDatasetCatalog, IDisposable
{
    private readonly HttpClient client;
    private readonly Lock gate = new();

    /// <summary>Dataset name as published to the UI → the pair it addresses on the service.</summary>
    private readonly Dictionary<string, (string Database, string Table)> routes = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Task<DatasetSchema>> schemas = new(StringComparer.Ordinal);

    /// <summary>Keyed by dataset <i>and</i> axis: re-ordering a read is a different read.</summary>
    private readonly Dictionary<(string Dataset, string XAxis), Task<DatasetSchema>> series = [];

    private readonly Dictionary<string, DatasetSchemaResponse> rawSchemas = new(StringComparer.Ordinal);

    private Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>? listing;

    private readonly Func<Task<string?>> accessToken;

    public HttpDatasetCatalog()
        : this(new HttpClient { Timeout = DataFeedOptions.Timeout }, AuthSession.Instance.GetAccessTokenAsync)
    {
    }

    /// <param name="client">
    /// Handed in by tests. Ownership transfers — <see cref="Dispose"/> disposes it.
    /// </param>
    /// <param name="accessToken">
    /// Asked once per request rather than captured at construction, so a token renewed mid-session
    /// is picked up without rebuilding the catalogue. Returns null when signed out, which sends no
    /// Authorization header at all.
    /// </param>
    public HttpDatasetCatalog(HttpClient client, Func<Task<string?>>? accessToken = null)
    {
        this.client = client;
        this.accessToken = accessToken ?? (() => Task.FromResult<string?>(null));
    }

    /// <summary>
    /// Databases the service could not read, from the most recent listing. The service answers
    /// 502 only when *every* database failed, so a partial failure arrives as a normal 200 with
    /// this filled in — silently dropping it would show a short catalogue as if it were complete.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAvailableDatasetsAsync()
    {
        // MainView warms this while the first screen builds and DataSourcesView asks for it again
        // on entry. Sharing the task means the second caller waits on the first call rather than
        // starting a second one.
        lock (gate)
        {
            if (listing is null || listing.IsFaulted)
                listing = LoadCatalogueAsync();
            return listing;
        }
    }

    public Task<DatasetSchema> GetSchemaAsync(string dataset)
    {
        lock (gate)
        {
            if (schemas.TryGetValue(dataset, out var cached) && !cached.IsFaulted) return cached;
            var loading = LoadSchemaAsync(dataset);
            schemas[dataset] = loading;
            return loading;
        }
    }

    public Task<DatasetSchema> GetSeriesAsync(string dataset, string xAxis)
    {
        if (!DataFeedOptions.SampleValues) return GetSchemaAsync(dataset);

        // Keyed on the pair rather than a joined string: both parts can contain a dot, so there
        // is no separator that is guaranteed not to appear in either.
        var key = (dataset, xAxis);

        lock (gate)
        {
            if (series.TryGetValue(key, out var cached) && !cached.IsFaulted) return cached;
            var loading = LoadSeriesAsync(dataset, xAxis);
            series[key] = loading;
            return loading;
        }
    }

    // ── catalogue ────────────────────────────────────────────────────────────

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogueAsync()
    {
        var response = await GetAsync(
            "/api/datasets?includeColumns=false",
            DataFeedJson.Default.AvailableDatasetsResponse,
            "list the datasets");

        var databases = response.Databases ?? [];

        var catalogue = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var routing = new Dictionary<string, (string, string)>(StringComparer.Ordinal);

        foreach (var database in databases)
        {
            if (string.IsNullOrWhiteSpace(database.Database)) continue;

            var names = new List<string>();
            foreach (var dataset in database.Datasets ?? [])
            {
                if (string.IsNullOrWhiteSpace(dataset.Name)) continue;

                // Always qualified, never just the table name. A table name is only unique within
                // its database, and the UI keys a dataset by name alone — mounting, path building
                // and cross-subtree links all go through it. Qualifying only the names that happen
                // to collide today would mean a dataset silently changing identity the day some
                // unrelated database gains a table of the same name, renaming it out from under an
                // existing mount. The database is in the topic header directly above either way.
                var published = $"{database.Database}.{dataset.Name}";
                names.Add(published);
                routing[published] = (database.Database, dataset.Name);
            }

            // The topic headers are rendered as small caps labels beside the stub's own
            // "MARKET & PRICING"; an Athena database is conventionally lower case, so it is
            // upper-cased for display only. Routing uses the spelling the service gave us.
            catalogue[database.Database.ToUpperInvariant()] = names;
        }

        lock (gate)
        {
            routes.Clear();
            foreach (var (name, route) in routing) routes[name] = route;
        }

        Warnings =
        [
            .. (response.Errors ?? [])
                .Select(error => $"{error.Database ?? "unknown database"}: {error.Message ?? "could not be read"}")
        ];

        return catalogue;
    }

    // ── schema ───────────────────────────────────────────────────────────────

    private async Task<DatasetSchema> LoadSchemaAsync(string dataset) =>
        BuildSchema(dataset, await GetRawSchemaAsync(dataset), readings: null, xAxis: "");

    private async Task<DatasetSchemaResponse> GetRawSchemaAsync(string dataset)
    {
        lock (gate)
        {
            if (rawSchemas.TryGetValue(dataset, out var cached)) return cached;
        }

        var (database, table) = await RouteAsync(dataset);

        var response = await GetAsync(
            $"/api/datasets/{Segment(database)}/{Segment(table)}/schema",
            DataFeedJson.Default.DatasetSchemaResponse,
            $"read the schema of {dataset}");

        lock (gate) rawSchemas[dataset] = response;
        return response;
    }

    /// <summary>
    /// The database and table a published dataset name addresses, loading the catalogue first if a
    /// schema was asked for before the listing landed. Kept as a lookup rather than split back out
    /// of the name, so a database or table containing a dot cannot be cut in the wrong place.
    /// </summary>
    private async Task<(string Database, string Table)> RouteAsync(string dataset)
    {
        lock (gate)
        {
            if (routes.TryGetValue(dataset, out var known)) return known;
        }

        await GetAvailableDatasetsAsync();

        lock (gate)
        {
            if (routes.TryGetValue(dataset, out var loaded)) return loaded;
        }

        throw new DataFeedException($"'{dataset}' is not in the catalogue.");
    }

    /// <param name="readings">
    /// Column path → that column's values across the ordered rows, oldest first. Null before any
    /// query has run, which is the structure-only tree.
    /// </param>
    private static DatasetSchema BuildSchema(
        string dataset,
        DatasetSchemaResponse response,
        IReadOnlyDictionary<string, IReadOnlyList<string?>>? readings,
        string xAxis)
    {
        var root = new DataTreeNode
        {
            Name = dataset,
            Path = dataset,
            Kind = DataNodeKind.Object,
            Tag = "SUBTREE ROOT"
        };

        // A sensor_id column declares replicate members: twelve sensors reading the same
        // quantity are twelve series, and each gets its own subtree of leaves built from its own
        // rows of the one fetch. Without members, one row per axis value is the contract the
        // pipeline rests on — a table carrying more interleaves several series in every column,
        // and a line through that would join readings from different instruments. Its leaves
        // stay structural and say why; the fix is the table's, not this client's.
        var members = readings is not null ? MemberPartition(readings) : null;
        var rowsPerPoint = 1;

        if (members is not null)
        {
            // The σ pass after this loop resolves leaves by their member-prefixed paths, so it
            // needs every member's cells in one dictionary. The keys are disjoint by
            // construction — each carries its member's name.
            var merged = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, memberReadings) in members)
            foreach (var (path, cells) in memberReadings)
                merged[path] = cells;
            readings = merged;

            foreach (var (member, memberReadings) in members)
            {
                var node = new DataTreeNode
                {
                    Name = member,
                    Path = $"{dataset}.{member}",
                    Kind = DataNodeKind.Object,
                    Tag = "SENSOR"
                };

                var memberRows = RowsPerPoint(memberReadings, $"{member}.{xAxis}");
                rowsPerPoint = Math.Max(rowsPerPoint, memberRows);
                var memberNote = memberRows > 1 ? $"{memberRows} rows/point — expected one" : "";

                foreach (var column in response.Columns ?? [])
                {
                    if (SeriesAxis.Member.Equals(column.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    Append(node, column, dataset, memberRows > 1 ? null : memberReadings, memberNote, isPartitionKey: false, depth: 0);
                }
                foreach (var column in response.PartitionKeys ?? [])
                    Append(node, column, dataset, memberRows > 1 ? null : memberReadings, memberNote, isPartitionKey: true, depth: 0);

                root.Children.Add(node);
            }
        }
        else
        {
            readings = readings is not null ? Tail(readings) : null;
            rowsPerPoint = readings is not null ? RowsPerPoint(readings, xAxis) : 1;
            var tieNote = rowsPerPoint > 1 ? $"{rowsPerPoint} rows/point — expected one" : "";
            if (rowsPerPoint > 1) readings = null;

            // Partition keys are selectable and filterable exactly like ordinary columns — Athena
            // just reports them separately — so they belong in the tree, tagged for what they are.
            foreach (var column in response.Columns ?? [])
                Append(root, column, dataset, readings, tieNote, isPartitionKey: false, depth: 0);
            foreach (var column in response.PartitionKeys ?? [])
                Append(root, column, dataset, readings, tieNote, isPartitionKey: true, depth: 0);
        }

        // Folds a "<name>__sigma" column into the measure beside it. Athena carries no uncertainty
        // of its own, so a σ column beside a reading is the only way this feed can state one. Done
        // here rather than in MeasureNumerics.BindSigmaLeaves because the pairing is row-by-row,
        // and the rows are only in hand on this side of the tree.
        BindSiblingSigma(root, dataset, readings);

        var created = response.CreatedAt;
        var accessed = response.LastAccessedAt;

        return new DatasetSchema(
            Dataset: dataset,
            Provider: response.Database ?? response.CatalogName ?? "athena",
            Contract: Humanise(response.TableType) is { Length: > 0 } type ? type : "table",
            // The service publishes no cadence or licence: neither is in an Athena catalog, so
            // there is nothing honest to put here until something upstream records them.
            Cadence: "—",
            Coverage: Coverage(created, accessed),
            Licence: "—",
            Root: root)
        {
            XAxis = xAxis,
            RowsPerPoint = rowsPerPoint
        };
    }

    private static void Append(
        DataTreeNode parent,
        DatasetColumn column,
        string dataset,
        IReadOnlyDictionary<string, IReadOnlyList<string?>>? readings,
        string tieNote,
        bool isPartitionKey,
        int depth)
    {
        if (string.IsNullOrWhiteSpace(column.Name)) return;

        var path = $"{parent.Path}.{column.Name}";
        var type = column.Type ?? "";

        // A struct becomes an object whose fields are addressable; every other type — including
        // arrays and maps, which a dotted path cannot enter — stays a leaf.
        if (HiveType.IsStruct(type) && depth < HiveType.MaxDepth)
        {
            var node = new DataTreeNode
            {
                Name = column.Name,
                Path = path,
                Kind = DataNodeKind.Object,
                Tag = isPartitionKey ? "PARTITION" : ""
            };
            foreach (var (fieldName, fieldType) in HiveType.StructFields(type))
                Append(node, new DatasetColumn(fieldName, fieldType, null), dataset, readings, tieNote, isPartitionKey, depth + 1);

            parent.Children.Add(node);
            return;
        }

        var isVector = HiveType.IsArray(type);
        var columnPath = SeriesAxis.Relative(dataset, path);
        var cells = readings is not null && readings.TryGetValue(columnPath, out var found) ? found : [];

        // The whole transformation a value undergoes on its way to a chart: the column's non-null
        // cells, parsed, in row order. The chart plots readings by index, so a skipped null is a
        // missing measurement, not a closed gap. A column that does not read as numbers keeps its
        // text and carries no series, and the newest non-null cell is the leaf's reading either
        // way — a feed whose latest rows have not caught up still reads as its last measurement.
        var measured = new List<double>(cells.Count);
        string? latest = null;
        var numeric = true;
        foreach (var cell in cells)
        {
            if (cell is null) continue;
            latest = cell;
            if (!numeric) continue;
            var (parsed, _) = MeasureNumerics.ParseValue(cell);
            if (double.IsNaN(parsed)) numeric = false;
            else measured.Add(parsed);
        }

        IReadOnlyList<double> history = numeric && measured.Count >= 2 ? measured : [];
        var (value, unit) = MeasureNumerics.ParseValue(latest ?? "");

        parent.Children.Add(new DataTreeNode
        {
            Name = column.Name,
            Path = path,
            Kind = DataNodeKind.Measure,
            Tag = isPartitionKey ? "PARTITION" : isVector ? "VECTOR" : "",
            Reading = new Measure
            {
                Display = cells.Count > 0 ? Format(latest) : "—",
                // Athena carries no uncertainty of its own; a __sigma sibling adds one after the
                // tree is built. Leaving these blank until then is the honest reading.
                SigmaDisplay = "",
                SigmaKind = "",
                Detail = Detail(
                    type, column.Comment, isPartitionKey,
                    attempted: readings is not null,
                    sampled: cells.Count > 0,
                    points: history.Count,
                    tieNote),
                IsVector = isVector,
                Value = value,
                Unit = unit,
                History = history
            }
        });

        static string Detail(
            string type, string? comment, bool isPartitionKey,
            bool attempted, bool sampled, int points, string tieNote)
        {
            var parts = new List<string>(4);
            if (type.Length > 0) parts.Add(type);
            if (isPartitionKey) parts.Add("partition key");
            // Whether a chart will draw at all, said where the operator is already looking. A
            // column that read fine but has no series is the case that would otherwise only show
            // up as an empty tile two screens later.
            if (points > 1) parts.Add($"{points} points");
            if (tieNote.Length > 0) parts.Add(tieNote);
            if (!string.IsNullOrWhiteSpace(comment)) parts.Add(comment.Trim());
            // Only after a query actually ran does a missing value mean anything — before that it
            // just means the values have not been asked for yet. This is also what marks the
            // leaves past MaxSampleColumns, which the query deliberately left out.
            else if (attempted && !sampled) parts.Add("no sample");
            return parts.Count == 0 ? "column" : string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// Splits the fetched rows by <see cref="SeriesAxis.Member"/>, each member's cells keyed the
    /// way its subtree's leaves will look them up — "LIG-01.level". Null when the table has no
    /// member column, or only one member: a table already per-sensor stays flat, its sensor_id a
    /// constant leaf. One fetch, split locally: no extra queries.
    /// </summary>
    private static List<(string Member, Dictionary<string, IReadOnlyList<string?>> Readings)>? MemberPartition(
        IReadOnlyDictionary<string, IReadOnlyList<string?>> readings)
    {
        if (!readings.TryGetValue(SeriesAxis.Member, out var sensors)) return null;

        var indices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < sensors.Count; i++)
        {
            if (sensors[i] is not { Length: > 0 } sensor) continue;
            if (!indices.TryGetValue(sensor, out var rows))
            {
                rows = [];
                indices[sensor] = rows;
            }
            rows.Add(i);
        }
        if (indices.Count < 2) return null;

        var members = new List<(string, Dictionary<string, IReadOnlyList<string?>>)>(indices.Count);
        foreach (var (member, rows) in indices.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var kept = rows.Count > DataFeedOptions.SeriesRows
                ? rows.Skip(rows.Count - DataFeedOptions.SeriesRows).ToList()
                : rows;

            var slice = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, cells) in readings)
            {
                if (path.Equals(SeriesAxis.Member, StringComparison.OrdinalIgnoreCase)) continue;
                slice[$"{member}.{path}"] = [.. kept.Select(row => row < cells.Count ? cells[row] : null)];
            }
            members.Add((member, slice));
        }
        return members;
    }

    /// <summary>The newest <see cref="DataFeedOptions.SeriesRows"/> rows of each column.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string?>> Tail(
        IReadOnlyDictionary<string, IReadOnlyList<string?>> readings)
    {
        var capped = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, cells) in readings)
        {
            capped[path] = cells.Count > DataFeedOptions.SeriesRows
                ? [.. cells.Skip(cells.Count - DataFeedOptions.SeriesRows)]
                : cells;
        }
        return capped;
    }

    /// <summary>Longest run of one axis value in the ordered rows — rows per point.</summary>
    private static int RowsPerPoint(
        IReadOnlyDictionary<string, IReadOnlyList<string?>> readings, string xAxis)
    {
        if (!readings.TryGetValue(xAxis, out var axis) || axis.Count == 0) return 1;

        var widest = 1;
        var run = 1;
        for (var i = 1; i < axis.Count; i++)
        {
            run = axis[i] is not null && axis[i] == axis[i - 1] ? run + 1 : 1;
            if (run > widest) widest = run;
        }
        return widest;
    }

    /// <summary>
    /// Marks every "&lt;name&gt;__sigma" leaf as its neighbour's σ carrier and folds its numbers
    /// into that neighbour — the flat-table spelling of what
    /// <see cref="MeasureNumerics.BindSigmaLeaves"/> does for a "sigma" child in a nested tree.
    /// σ(x) pairs row-by-row: the carrier's cell on each row the measure read from. Where the
    /// carrier is missing or unreadable on any of those rows, the flat newest σ stands alone,
    /// which <see cref="Measure.SigmaHistory"/> being empty already means.
    /// </summary>
    private static void BindSiblingSigma(
        DataTreeNode parent,
        string dataset,
        IReadOnlyDictionary<string, IReadOnlyList<string?>>? readings)
    {
        foreach (var node in parent.Children)
        {
            BindSiblingSigma(node, dataset, readings);

            if (node.Kind != DataNodeKind.Measure || node.Reading is not { } reading) continue;
            if (reading.IsSigmaCarrier) continue;

            var carrier = parent.Children.FirstOrDefault(candidate =>
                candidate.Kind == DataNodeKind.Measure &&
                candidate.Name.Equals(node.Name + MeasureNumerics.SigmaSuffix, StringComparison.OrdinalIgnoreCase));
            if (carrier?.Reading is not { } carrierReading) continue;

            // Marked even before any values exist, so a picker never offers a standard deviation
            // as though it were a quantity — the tree keeps the leaf, sources skip it.
            carrier.Reading = AsCarrier(carrierReading);

            if (!carrierReading.HasValue) continue;

            var sigmaHistory = PairedSigma(dataset, node, carrier, readings, reading.History.Count);
            var sigma = Math.Abs(sigmaHistory.Count > 0 ? sigmaHistory[^1] : carrierReading.Value);

            node.Reading = new Measure
            {
                Display = reading.Display,
                SigmaDisplay = sigma > 0 ? $"± {MeasureNumerics.FormatSigma(sigma)}" : "",
                SigmaKind = reading.SigmaKind,
                Detail = reading.Detail,
                Selected = reading.Selected,
                IsNew = reading.IsNew,
                IsVector = reading.IsVector,
                Value = reading.Value,
                Sigma = sigma,
                Unit = reading.Unit,
                History = reading.History,
                SigmaHistory = sigmaHistory
            };
        }
    }

    /// <summary>The carrier's value on each row the measure read from — or nothing.</summary>
    private static IReadOnlyList<double> PairedSigma(
        string dataset,
        DataTreeNode node,
        DataTreeNode carrier,
        IReadOnlyDictionary<string, IReadOnlyList<string?>>? readings,
        int points)
    {
        if (points == 0 || readings is null) return [];
        if (!readings.TryGetValue(SeriesAxis.Relative(dataset, node.Path), out var values)) return [];
        if (!readings.TryGetValue(SeriesAxis.Relative(dataset, carrier.Path), out var sigmas)) return [];

        var paired = new List<double>(points);
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is null) continue;
            if (i >= sigmas.Count || sigmas[i] is not { } cell) return [];
            var (sigma, _) = MeasureNumerics.ParseValue(cell);
            if (double.IsNaN(sigma)) return [];
            paired.Add(Math.Abs(sigma));
        }
        return paired.Count == points ? paired : [];
    }

    private static Measure AsCarrier(Measure source) => new()
    {
        Display = source.Display,
        SigmaDisplay = source.SigmaDisplay,
        SigmaKind = source.SigmaKind,
        Detail = source.Detail,
        Selected = source.Selected,
        IsNew = source.IsNew,
        IsVector = source.IsVector,
        Value = source.Value,
        Sigma = source.Sigma,
        Unit = source.Unit,
        History = source.History,
        SigmaHistory = source.SigmaHistory,
        IsSigmaCarrier = true
    };

    // ── series ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-reads the schema with live readings folded in, ordered by <paramref name="xAxis"/>. It is
    /// a second tree rather than a mutation of the first because a node's reading is fixed at
    /// construction — and that is worth keeping, since a mounted subtree holds those same nodes.
    /// </summary>
    private async Task<DatasetSchema> LoadSeriesAsync(string dataset, string xAxis)
    {
        var response = await GetRawSchemaAsync(dataset);
        var structure = BuildSchema(dataset, response, readings: null, xAxis);

        var leaves = structure.Root
            .Descendants()
            .Where(node => node.Kind == DataNodeKind.Measure)
            .Select(node => SeriesAxis.Relative(dataset, node.Path));

        // The axis leads the projection so it always survives the column cap: it is the one column
        // the request cannot do without, and on a wide table it would otherwise be cut.
        var columns = new List<string> { xAxis };
        columns.AddRange(leaves.Where(path => !path.Equals(xAxis, StringComparison.OrdinalIgnoreCase)));
        if (columns.Count > DataFeedOptions.MaxSampleColumns)
            columns.RemoveRange(DataFeedOptions.MaxSampleColumns, columns.Count - DataFeedOptions.MaxSampleColumns);

        var (database, table) = await RouteAsync(dataset);

        var query = new StringBuilder($"/api/datasets/{Segment(database)}/{Segment(table)}/data");
        for (var i = 0; i < columns.Count; i++)
            query.Append(i == 0 ? '?' : '&').Append("columns=").Append(Uri.EscapeDataString(columns[i]));

        // A row without an axis value cannot be placed on the axis, and with the sort descending
        // it would come back ahead of every row that can. The service filters them out itself.
        query.Append("&filter=").Append(Uri.EscapeDataString($"{xAxis} IS NOT NULL"));

        // Descending, and reversed below. The service caps the read after ordering, so ascending
        // would return the oldest rows of a long table and the chart would draw its distant past.
        query.Append("&orderBy=").Append(Uri.EscapeDataString($"{xAxis} desc"));

        var data = await GetAsync(
            query.ToString(),
            DataFeedJson.Default.DatasetDataResponse,
            $"read {dataset} ordered by {xAxis}");

        var rows = data.Rows ?? [];
        if (rows.Count == 0) return structure;

        // Newest first on the wire, oldest first here: a chart reads left to right, and the last
        // point is the one the tree shows as the reading. The whole response is kept at this
        // stage — a member table spreads its rows across sensors, so the per-series cap is
        // applied after the split, not before it.
        var ordered = rows.Reverse().ToList();

        // Keyed off the response's own column list, which reports the resolved path in the
        // catalog's spelling — that, not the order we asked in, is what the values line up with.
        var names = data.Columns ?? [];
        var readings = new Dictionary<string, IReadOnlyList<string?>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i].Name is not { Length: > 0 } name) continue;
            var column = i;
            readings[name] = [.. ordered.Select(row => column < row.Count ? row[column] : null)];
        }

        return BuildSchema(dataset, response, readings, xAxis);
    }

    private static string Format(string? value)
    {
        if (value is null) return "null";

        var trimmed = value.Trim();
        if (trimmed.Length == 0) return "—";
        // The reading column is a fixed-width cell that ellipsises anyway; cutting a long struct
        // or JSON rendering here keeps one wide value from dominating the row.
        return trimmed.Length <= 40 ? trimmed : trimmed[..39] + "…";
    }

    private static string Humanise(string? tableType) =>
        (tableType ?? "").Replace('_', ' ').ToLowerInvariant();

    private static string Coverage(DateTime? created, DateTime? lastAccessed) => (created, lastAccessed) switch
    {
        ({ } from, { } to) => $"{from:yyyy-MM} → {to:yyyy-MM}",
        ({ } from, null) => $"{from:yyyy-MM} → live",
        (null, { } to) => $"… → {to:yyyy-MM}",
        _ => "—"
    };

    // ── transport ────────────────────────────────────────────────────────────

    private static string Segment(string value) => Uri.EscapeDataString(value);

    /// <param name="what">
    /// What the call was for, in the infinitive — it goes straight into the message a user reads,
    /// as "could not {what}".
    /// </param>
    private async Task<T> GetAsync<T>(string path, JsonTypeInfo<T> typeInfo, string what)
    {
        var url = DataFeedOptions.BaseAddress + path;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (await accessToken() is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A browser refusing the request for CORS reasons also lands here, indistinguishable
            // from an unreachable host — hence naming the address rather than guessing the cause.
            throw new DataFeedException(
                $"Could not {what}: no answer from {DataFeedOptions.DisplayHost}. {ex.Message}", ex);
        }

        using (response)
        {
            // The service rejecting the token is not a fault the caller can fix by retrying, and
            // it reads nothing like the RFC 7807 bodies the other failures produce, so it is
            // called out as the sign-in problem it is.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new DataFeedException(
                    $"Could not {what}: the service rejected the sign-in. Sign in again, and if that " +
                    $"does not help contact {AuthOptions.ContactEmail}.");
            }

            if (!response.IsSuccessStatusCode)
                throw new DataFeedException($"Could not {what}: {await DescribeAsync(response)}");

            try
            {
                return await response.Content.ReadFromJsonAsync(typeInfo)
                    ?? throw new DataFeedException($"Could not {what}: the service returned an empty body.");
            }
            catch (JsonException ex)
            {
                throw new DataFeedException($"Could not {what}: unreadable response. {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Turns a failure response into a sentence. Every failure path in the service answers with
    /// RFC 7807, and those messages are specific — which databases are configured, which columns
    /// exist — so they are worth surfacing rather than replacing with a status code.
    /// </summary>
    private static async Task<string> DescribeAsync(HttpResponseMessage response)
    {
        var status = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
        try
        {
            if (await response.Content.ReadFromJsonAsync(DataFeedJson.Default.ProblemDetails) is { } problem)
            {
                var written = string.Join(" — ", new[] { problem.Title, problem.Detail }
                    .Where(part => !string.IsNullOrWhiteSpace(part)));
                if (written.Length > 0) return $"{written} ({status})";
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException)
        {
            // Not a problem+json body — the status alone is all there is to report.
        }
        return status;
    }

    public void Dispose() => client.Dispose();
}
