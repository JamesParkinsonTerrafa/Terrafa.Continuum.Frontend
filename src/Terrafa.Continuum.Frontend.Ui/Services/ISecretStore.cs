// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Services;

public sealed record StoredCredential(string Username, string RefreshToken)
{
    public string ToStorageValue() => $"{Username}\n{RefreshToken}";

    public static StoredCredential? FromStorageValue(string? value)
    {
        if (value is null) return null;
        var separator = value.IndexOf('\n');
        if (separator < 0) return null;
        var username = value[..separator];
        var refreshToken = value[(separator + 1)..];
        if (refreshToken.Length == 0) return null;
        return new StoredCredential(username, refreshToken);
    }
}

public interface ISecretStore
{
    Task<StoredCredential?> LoadAsync();
    Task SaveAsync(StoredCredential credential);
    Task ClearAsync();
}

public sealed class NullSecretStore : ISecretStore
{
    public Task<StoredCredential?> LoadAsync() => Task.FromResult<StoredCredential?>(null);
    public Task SaveAsync(StoredCredential credential) => Task.CompletedTask;
    public Task ClearAsync() => Task.CompletedTask;
}
