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
    private readonly Dictionary<string, Task<DatasetSchema>> samples = new(StringComparer.Ordinal);
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

    public Task<DatasetSchema> GetSampleAsync(string dataset)
    {
        if (!DataFeedOptions.SampleValues) return GetSchemaAsync(dataset);

        lock (gate)
        {
            if (samples.TryGetValue(dataset, out var cached) && !cached.IsFaulted) return cached;
            var loading = LoadSampleAsync(dataset);
            samples[dataset] = loading;
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
        BuildSchema(dataset, await GetRawSchemaAsync(dataset), values: null);

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

    private static DatasetSchema BuildSchema(
        string dataset,
        DatasetSchemaResponse response,
        IReadOnlyDictionary<string, string?>? values)
    {
        var root = new DataTreeNode
        {
            Name = dataset,
            Path = dataset,
            Kind = DataNodeKind.Object,
            Tag = "SUBTREE ROOT"
        };

        // Partition keys are selectable and filterable exactly like ordinary columns — Athena just
        // reports them separately — so they belong in the tree, tagged for what they are.
        foreach (var column in response.Columns ?? [])
            Append(root, column, dataset, values, isPartitionKey: false, depth: 0);
        foreach (var column in response.PartitionKeys ?? [])
            Append(root, column, dataset, values, isPartitionKey: true, depth: 0);

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
            Root: root);
    }

    private static void Append(
        DataTreeNode parent,
        DatasetColumn column,
        string dataset,
        IReadOnlyDictionary<string, string?>? values,
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
                Append(node, new DatasetColumn(fieldName, fieldType, null), dataset, values, isPartitionKey, depth + 1);

            parent.Children.Add(node);
            return;
        }

        var isVector = HiveType.IsArray(type);
        var columnPath = path[(dataset.Length + 1)..];

        string? raw = null;
        var hasValue = values is not null && values.TryGetValue(columnPath, out raw);

        parent.Children.Add(new DataTreeNode
        {
            Name = column.Name,
            Path = path,
            Kind = DataNodeKind.Measure,
            Tag = isPartitionKey ? "PARTITION" : isVector ? "VECTOR" : "",
            Reading = new Measure
            {
                Display = hasValue ? Format(raw) : "—",
                // Athena carries no uncertainty of its own. Leaving these blank is the honest
                // reading: the UI prints nothing rather than a sigma nobody measured.
                SigmaDisplay = "",
                SigmaKind = "",
                Detail = Detail(type, column.Comment, isPartitionKey, attempted: values is not null, hasValue),
                IsVector = isVector
            }
        });

        static string Detail(string type, string? comment, bool isPartitionKey, bool attempted, bool sampled)
        {
            var parts = new List<string>(3);
            if (type.Length > 0) parts.Add(type);
            if (isPartitionKey) parts.Add("partition key");
            if (!string.IsNullOrWhiteSpace(comment)) parts.Add(comment.Trim());
            // Only after a sample query actually ran does a missing value mean anything — before
            // that it just means the values have not been asked for yet. This is also what marks
            // the leaves past MaxSampleColumns, which the query deliberately left out.
            else if (attempted && !sampled) parts.Add("no sample");
            return parts.Count == 0 ? "column" : string.Join(" · ", parts);
        }
    }

    // ── sampled values ───────────────────────────────────────────────────────

    /// <summary>
    /// Re-reads the schema with one row of live values folded in. It is a second tree rather than
    /// a mutation of the first because a node's reading is fixed at construction — and that is
    /// worth keeping, since a mounted subtree holds those same nodes.
    /// </summary>
    private async Task<DatasetSchema> LoadSampleAsync(string dataset)
    {
        var response = await GetRawSchemaAsync(dataset);
        var structure = BuildSchema(dataset, response, values: null);

        var columns = structure.Root
            .Descendants()
            .Where(node => node.Kind == DataNodeKind.Measure)
            .Select(node => node.Path[(dataset.Length + 1)..])
            .Take(DataFeedOptions.MaxSampleColumns)
            .ToList();

        // The data endpoint requires at least one column, and a dataset of nothing but structs
        // this walk could not enter would otherwise send it none.
        if (columns.Count == 0) return structure;

        var (database, table) = await RouteAsync(dataset);

        var query = new StringBuilder($"/api/datasets/{Segment(database)}/{Segment(table)}/data");
        for (var i = 0; i < columns.Count; i++)
            query.Append(i == 0 ? '?' : '&').Append("columns=").Append(Uri.EscapeDataString(columns[i]));

        var data = await GetAsync(
            query.ToString(),
            DataFeedJson.Default.DatasetDataResponse,
            $"read values from {dataset}");

        var row = data.Rows?.FirstOrDefault();
        if (row is null) return structure;

        // Keyed off the response's own column list, which reports the resolved path in the
        // catalog's spelling — that, not the order we asked in, is what the values line up with.
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var names = data.Columns ?? [];
        for (var i = 0; i < names.Count && i < row.Count; i++)
        {
            if (names[i].Name is { Length: > 0 } name) values[name] = row[i];
        }

        return BuildSchema(dataset, response, values);
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
