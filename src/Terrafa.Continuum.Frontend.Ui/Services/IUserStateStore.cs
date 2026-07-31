// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// Where a signed-in user's durable state lives, one JSON document per
/// <see cref="UserStateKinds"/> kind. Payloads stay raw strings so the transport knows nothing
/// about the shapes — (de)serialisation lives beside the DTOs, and a test store is a dictionary.
/// </summary>
public interface IUserStateStore
{
    /// <summary>The stored document, or null when the user has never saved this kind.</summary>
    Task<string?> GetAsync(string kind, CancellationToken cancellationToken = default);

    Task PutAsync(string kind, string json, CancellationToken cancellationToken = default);
}

/// <summary>The default store: nothing persists. Mirrors <see cref="NullSecretStore"/>.</summary>
public sealed class NullUserStateStore : IUserStateStore
{
    public Task<string?> GetAsync(string kind, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task PutAsync(string kind, string json, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
