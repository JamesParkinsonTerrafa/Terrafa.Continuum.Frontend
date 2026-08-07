// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// Where the operator's Anthropic API key lives between runs. A separate store from
/// <see cref="ISecretStore"/> rather than a second slot on it: that interface is the auth
/// session's, its value has a shape (username + refresh token), and the two keys have different
/// lifecycles — signing out of Terrafa should not discard the operator's own Anthropic key.
/// Each head assigns its implementation next to the auth store, in Program.
/// </summary>
public interface ISandboxKeyStore
{
    Task<string?> LoadAsync();
    Task SaveAsync(string apiKey);
    Task ClearAsync();
}

public sealed class NullSandboxKeyStore : ISandboxKeyStore
{
    public Task<string?> LoadAsync() => Task.FromResult<string?>(null);
    public Task SaveAsync(string apiKey) => Task.CompletedTask;
    public Task ClearAsync() => Task.CompletedTask;
}
