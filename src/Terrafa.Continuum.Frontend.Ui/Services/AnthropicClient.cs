// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>An Anthropic API answer that was not 2xx, carrying what the service said went wrong.</summary>
public sealed class AnthropicApiException(HttpStatusCode status, string message)
    : Exception($"{(int)status} — {message}")
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>
/// The Managed Agents API, spoken directly over HTTP. Hand-rolled rather than the Anthropic SDK
/// because this assembly runs on both heads: everything here must survive the trimmer and WASM
/// AOT, so all JSON goes through <see cref="JsonNode"/> — no reflection serialization anywhere.
///
/// <para>
/// One request surface for both heads. The browser talks to api.anthropic.com from the page
/// itself, which Anthropic's CORS policy allows only when the request owns up to it — the
/// <c>anthropic-dangerous-direct-browser-access</c> header. That is the accepted trade here for
/// the same reason the auth refresh token sits in localStorage: the key is the operator's own,
/// entered by them, revocable by them.
/// </para>
/// </summary>
public sealed class AnthropicClient(string apiKey)
{
    private const string BaseUrl = "https://api.anthropic.com";
    private const string Beta = "managed-agents-2026-04-01";

    /// <summary>
    /// No client-level timeout: the event stream is a connection that is meant to stay open for
    /// the life of a session. Plain calls get their deadline per-request instead.
    /// </summary>
    private static readonly HttpClient Shared = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(45);

    // ── environments and agents (find-or-create, so no id has to be persisted) ───────

    public async Task<string> EnsureEnvironmentAsync(string name, CancellationToken cancellationToken)
    {
        var listing = await CallAsync(HttpMethod.Get, "/v1/environments?limit=100", null, cancellationToken);
        var existing = Find(listing, name);
        if (existing is not null) return existing;

        var created = await CallAsync(HttpMethod.Post, "/v1/environments", new JsonObject
        {
            ["name"] = name,
            ["config"] = new JsonObject
            {
                ["type"] = "cloud",
                ["networking"] = new JsonObject { ["type"] = "unrestricted" }
            }
        }, cancellationToken);
        return created["id"]!.GetValue<string>();
    }

    /// <summary>
    /// The agent by name, created if absent and updated in place when its configuration has
    /// drifted from what this build would create. The comparison is a hash carried in the agent's
    /// own metadata, so an unchanged app relaunching does not mint a new agent version every time.
    /// </summary>
    public async Task<string> EnsureAgentAsync(
        string name, JsonObject configuration, CancellationToken cancellationToken)
    {
        var hash = Fingerprint(configuration.ToJsonString());
        configuration["name"] = name;
        configuration["metadata"] = new JsonObject { ["config_hash"] = hash };

        var listing = await CallAsync(HttpMethod.Get, "/v1/agents?limit=100", null, cancellationToken);
        foreach (var entry in listing["data"]!.AsArray())
        {
            if (entry?["name"]?.GetValue<string>() != name) continue;
            var id = entry["id"]!.GetValue<string>();
            if (entry["metadata"]?["config_hash"]?.GetValue<string>() == hash) return id;
            await CallAsync(HttpMethod.Post, $"/v1/agents/{id}", configuration, cancellationToken);
            return id;
        }

        var created = await CallAsync(HttpMethod.Post, "/v1/agents", configuration, cancellationToken);
        return created["id"]!.GetValue<string>();
    }

    private static string? Find(JsonNode listing, string name)
    {
        foreach (var entry in listing["data"]!.AsArray())
        {
            if (entry?["name"]?.GetValue<string>() == name)
                return entry["id"]!.GetValue<string>();
        }
        return null;
    }

    // ── sessions and events ──────────────────────────────────────────────────────────

    public async Task<string> CreateSessionAsync(
        string agentId, string environmentId, string title, CancellationToken cancellationToken)
    {
        var created = await CallAsync(HttpMethod.Post, "/v1/sessions", new JsonObject
        {
            ["agent"] = agentId,
            ["environment_id"] = environmentId,
            ["title"] = title
        }, cancellationToken);
        return created["id"]!.GetValue<string>();
    }

    public Task SendEventsAsync(string sessionId, JsonArray events, CancellationToken cancellationToken) =>
        CallAsync(HttpMethod.Post, $"/v1/sessions/{sessionId}/events",
            new JsonObject { ["events"] = events }, cancellationToken);

    /// <summary>One page of past events, oldest first — the catch-up read a reconnect does.</summary>
    public async Task<List<JsonNode>> ListEventsAsync(string sessionId, CancellationToken cancellationToken)
    {
        var listing = await CallAsync(
            HttpMethod.Get, $"/v1/sessions/{sessionId}/events?limit=1000", null, cancellationToken);
        var events = new List<JsonNode>();
        foreach (var entry in listing["data"]!.AsArray())
        {
            if (entry is not null) events.Add(entry);
        }
        return events;
    }

    /// <summary>
    /// The session's live event stream. Yields one parsed event per SSE message and completes only
    /// when the server closes the connection or the token cancels; the caller owns reconnection.
    /// </summary>
    public async IAsyncEnumerable<JsonNode> StreamEventsAsync(
        string sessionId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = NewRequest(HttpMethod.Get, $"/v1/sessions/{sessionId}/events/stream");
        // The browser's fetch buffers the whole response unless streaming is asked for by name —
        // and an SSE response never ends, so buffering it is a hang, not a slowdown.
        request.Options.Set(new HttpRequestOptionsKey<bool>("WebAssemblyEnableStreamingResponse"), true);

        using var response = await Shared.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await ThrowIfErrorAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var data = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    var parsed = TryParse(data.ToString());
                    data.Clear();
                    if (parsed is not null) yield return parsed;
                }
                continue;
            }
            if (line[0] == ':') continue; // heartbeat comment
            if (line.StartsWith("data:", StringComparison.Ordinal))
                data.Append(line.AsSpan(5).TrimStart());
        }
    }

    private static JsonNode? TryParse(string payload)
    {
        try
        {
            return JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── transport ────────────────────────────────────────────────────────────────────

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Add("anthropic-beta", Beta);
        request.Headers.Add("anthropic-dangerous-direct-browser-access", "true");
        return request;
    }

    private async Task<JsonNode> CallAsync(
        HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CallTimeout);

        using var request = NewRequest(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await Shared.SendAsync(request, deadline.Token);
        await ThrowIfErrorAsync(response, deadline.Token);
        var text = await response.Content.ReadAsStringAsync(deadline.Token);
        return JsonNode.Parse(text) ?? new JsonObject();
    }

    private static async Task ThrowIfErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = TryParse(text)?["error"]?["message"]?.GetValue<string>()
            ?? (text.Length > 0 ? text : response.ReasonPhrase ?? "request failed");
        throw new AnthropicApiException(response.StatusCode, message);
    }

    /// <summary>
    /// A stable content hash with no dependency on the crypto stack, which is not uniformly
    /// available under browser WASM. FNV-1a is plenty: this only answers "did the agent config
    /// this build carries change since the agent was last written".
    /// </summary>
    internal static string Fingerprint(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var unit in text)
        {
            hash = (hash ^ unit) * 1099511628211UL;
        }
        return hash.ToString("x16");
    }
}
