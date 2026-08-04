// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// The catalogue, read from the Terrafa Continuum DataFeed service over HTTP.
///
/// <para>
/// This class does three things and no more: it speaks HTTP, it maps a published dataset name to
/// the database/table pair that addresses it, and it caches each answer so two screens asking the
/// same question share one call. Turning a response into a tree is
/// <see cref="DatasetSchemaBuilder"/>'s job — that logic has nothing to do with transport and used
/// to be the larger half of this file.
/// </para>
///
/// <para>
/// Cached tasks are started without a cancellation token and awaited per-caller through
/// <see cref="Task{T}.WaitAsync(CancellationToken)"/>. One caller giving up therefore cannot
/// cancel a fetch another caller is still waiting on, which is what cancelling the shared task
/// itself would do.
/// </para>
/// </summary>
public sealed class HttpDatasetCatalog : IDatasetCatalog
{
    /// <summary>
    /// One client for the life of the process. Deliberately not disposed with the catalogue: a
    /// sign-out drops this object, and disposing the client under a read still in flight is how a
    /// perfectly good response used to come back as <see cref="ObjectDisposedException"/> and get
    /// swallowed as "could not read". A client per sign-in is a socket-exhaustion smell besides.
    /// </summary>
    private static readonly HttpClient Shared = new() { Timeout = DataFeedOptions.Timeout };

    private readonly HttpClient client;
    private readonly Lock gate = new();

    /// <summary>Dataset name as published to the UI → the pair it addresses on the service.</summary>
    private Dictionary<string, (string Database, string Table)> routes = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Task<DatasetSchema>> schemas = new(StringComparer.Ordinal);

    /// <summary>Keyed by everything that makes a read a different read — see
    /// <see cref="DatasetQuery.CacheKey"/>.</summary>
    private readonly Dictionary<
        (string Dataset, string Axis, string Projection, int MaxRows), Task<DatasetSchema>> series = [];

    private readonly Dictionary<string, DatasetSchemaResponse> rawSchemas = new(StringComparer.Ordinal);

    private Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>? listing;

    private IReadOnlyList<string> warnings = [];

    private readonly Func<Task<string?>> accessToken;

    public HttpDatasetCatalog()
        : this(Shared, AuthSession.Instance.GetAccessTokenAsync)
    {
    }

    /// <param name="client">
    /// Handed in by tests. Ownership does <b>not</b> transfer — whoever created it disposes it.
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

    public bool IsLive => true;

    public IReadOnlyList<string> Warnings
    {
        get { lock (gate) return warnings; }
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAvailableDatasetsAsync(
        CancellationToken cancellationToken = default)
    {
        // The session warms this while the first screen builds and DataSourcesView asks for it
        // again on entry. Sharing the task means the second caller waits on the first call rather
        // than starting a second one.
        Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> pending;
        lock (gate)
        {
            if (listing is null || listing.IsFaulted) listing = LoadCatalogueAsync();
            pending = listing;
        }
        return pending.WaitAsync(cancellationToken);
    }

    public Task<DatasetSchema> GetSchemaAsync(string dataset, CancellationToken cancellationToken = default)
    {
        Task<DatasetSchema> pending;
        lock (gate)
        {
            if (schemas.TryGetValue(dataset, out var cached) && !cached.IsFaulted) pending = cached;
            else schemas[dataset] = pending = LoadSchemaAsync(dataset);
        }
        return pending.WaitAsync(cancellationToken);
    }

    public Task<DatasetSchema> GetSeriesAsync(DatasetQuery query, CancellationToken cancellationToken = default)
    {
        if (!DataFeedOptions.SampleValues) return GetSchemaAsync(query.Dataset, cancellationToken);

        // Keyed on the parts rather than one joined string: each can contain a dot, so there is no
        // separator guaranteed not to appear in any of them. The row cap is part of it — see
        // DatasetQuery.CacheKey for what leaving it out would cost.
        var key = query.CacheKey;

        Task<DatasetSchema> pending;
        lock (gate)
        {
            if (series.TryGetValue(key, out var cached) && !cached.IsFaulted) pending = cached;
            else series[key] = pending = LoadSeriesAsync(query);
        }
        return pending.WaitAsync(cancellationToken);
    }

    // ── catalogue ────────────────────────────────────────────────────────────

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogueAsync()
    {
        var response = await GetAsync(
            "/api/datasets?includeColumns=false",
            DataFeedJson.Default.AvailableDatasetsResponse,
            "list the datasets");

        var catalogue = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var routing = new Dictionary<string, (string, string)>(StringComparer.Ordinal);

        foreach (var database in response.Databases ?? [])
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

        var reported = (response.Errors ?? [])
            .Select(error => $"{error.Database ?? "unknown database"}: {error.Message ?? "could not be read"}")
            .ToList();

        lock (gate)
        {
            // Swapped whole rather than cleared and refilled. A concurrent RouteAsync landing
            // between a Clear and its repopulation saw an empty map and reported a perfectly good
            // dataset as "not in the catalogue".
            routes = routing;
            warnings = reported;
        }

        return catalogue;
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

    // ── schema ───────────────────────────────────────────────────────────────

    private async Task<DatasetSchema> LoadSchemaAsync(string dataset) =>
        DatasetSchemaBuilder.Build(dataset, await GetRawSchemaAsync(dataset), readings: null, xAxis: "");

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

    // ── series ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-reads the schema with live readings folded in, ordered by the query's axis. It is a
    /// second tree rather than a mutation of the first because a node's reading is fixed at
    /// construction — and that is worth keeping, since a mounted subtree holds those same nodes.
    /// </summary>
    private async Task<DatasetSchema> LoadSeriesAsync(DatasetQuery query)
    {
        var dataset = query.Dataset;
        var response = await GetRawSchemaAsync(dataset);

        // Built without an axis first, because whether the asked-for axis exists is a question
        // about this tree. A structure-only tree has the same shape either way — only the axis it
        // reports differs — so the answer is stamped on rather than built twice.
        var structure = DatasetSchemaBuilder.Build(dataset, response, readings: null, xAxis: "", query.MaxRows);
        var leaves = DatasetSchemaBuilder.Leaves(structure);

        // A table with no column by this name has no axis, rather than some other column pressed
        // into the role. Naming one anyway ordered a lookup table by whichever column happened to
        // come first and filtered out every row that column was null on — a reordering and a
        // silent cut, neither of them asked for. Rows come back in table order instead, and a grid
        // orders them by its own index column, which is where the operator picks it.
        var axis = leaves.Contains(query.Axis, StringComparer.OrdinalIgnoreCase) ? query.Axis : "";
        if (axis.Length > 0) structure = structure with { XAxis = axis };

        if (DatasetSchemaBuilder.Narrow(dataset, leaves, query.Paths) is { Count: > 0 } narrowed)
            leaves = narrowed;

        // The axis leads the projection so it always survives the column cap: it is the one column
        // the request cannot do without, and on a wide table it would otherwise be cut.
        var columns = new List<string>();
        if (axis.Length > 0) columns.Add(axis);
        columns.AddRange(leaves.Where(path => !path.Equals(axis, StringComparison.OrdinalIgnoreCase)));
        if (columns.Count > DataFeedOptions.MaxSampleColumns)
            columns.RemoveRange(DataFeedOptions.MaxSampleColumns, columns.Count - DataFeedOptions.MaxSampleColumns);
        if (columns.Count == 0) return structure;

        var (database, table) = await RouteAsync(dataset);

        var url = new StringBuilder($"/api/datasets/{Segment(database)}/{Segment(table)}/data");
        for (var i = 0; i < columns.Count; i++)
            url.Append(i == 0 ? '?' : '&').Append("columns=").Append(Uri.EscapeDataString(columns[i]));

        if (axis.Length > 0)
        {
            // A row without an axis value cannot be placed on the axis, and with the sort descending
            // it would come back ahead of every row that can. The service filters them out itself.
            url.Append("&filter=").Append(Uri.EscapeDataString($"{axis} IS NOT NULL"));

            // Descending, and reversed when the rows are read. The service caps the read after
            // ordering, so ascending would return the oldest rows of a long table and the chart
            // would draw its distant past.
            url.Append("&orderBy=").Append(Uri.EscapeDataString($"{axis} desc"));
        }

        // The window, asked for rather than applied on arrival. The service clamps it to its own
        // configured ceiling and reports the cut as `truncated`, so this narrows a read and can
        // never widen one. A deployment that predates the parameter ignores it and sends its own
        // cap instead, which the client then windows locally exactly as it did before — so this is
        // safe to ship in either order.
        url.Append("&maxRows=").Append(query.MaxRows);

        var data = await GetAsync(
            url.ToString(),
            DataFeedJson.Default.DatasetDataResponse,
            axis.Length > 0 ? $"read {dataset} ordered by {axis}" : $"read {dataset}");

        if (data.Rows is not { Count: > 0 }) return structure;

        var readings = DatasetSchemaBuilder.ReadingsOf(data, ordered: axis.Length > 0);

        // data.Truncated is the service saying it hit its own cap. It has come back on every
        // response since the service was written and was read by nobody, so a windowed read looked
        // exactly like a complete one everywhere downstream.
        return DatasetSchemaBuilder.Build(dataset, response, readings, axis, query.MaxRows, data.Truncated);
    }

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
}
