// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// The durable state store on the DataFeed service: <c>GET/PUT /api/user-state/{kind}</c>, behind
/// the same Cognito bearer token as <see cref="HttpDatasetCatalog"/>. The service resolves the
/// user from the token's <c>sub</c> claim — the client never says who it is saving for.
/// </summary>
public sealed class HttpUserStateStore : IUserStateStore, IDisposable
{
    private readonly HttpClient client;
    private readonly Func<Task<string?>> accessToken;

    public HttpUserStateStore()
        : this(new HttpClient { Timeout = DataFeedOptions.Timeout }, AuthSession.Instance.GetAccessTokenAsync)
    {
    }

    /// <param name="client">Handed in by tests. Ownership transfers — <see cref="Dispose"/> disposes it.</param>
    public HttpUserStateStore(HttpClient client, Func<Task<string?>>? accessToken = null)
    {
        this.client = client;
        this.accessToken = accessToken ?? (() => Task.FromResult<string?>(null));
    }

    public async Task<string?> GetAsync(string kind, CancellationToken cancellationToken = default)
    {
        using var request = await BuildAsync(HttpMethod.Get, kind);
        using var response = await client.SendAsync(request, cancellationToken);

        // 404 is a user who has never saved this kind. 401/403 is a sign-in that lapsed mid-load;
        // both mean "no document to apply" rather than a fault worth surfacing.
        if (response.StatusCode is HttpStatusCode.NotFound
            or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return null;

        if (!response.IsSuccessStatusCode)
            throw new DataFeedException(
                $"Could not read saved {kind}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd());

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task PutAsync(string kind, string json, CancellationToken cancellationToken = default)
    {
        using var request = await BuildAsync(HttpMethod.Put, kind);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new DataFeedException(
                $"Could not save {kind}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd());
    }

    private async Task<HttpRequestMessage> BuildAsync(HttpMethod method, string kind)
    {
        var request = new HttpRequestMessage(
            method, $"{DataFeedOptions.BaseAddress}/api/user-state/{Uri.EscapeDataString(kind)}");
        if (await accessToken() is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public void Dispose() => client.Dispose();
}
