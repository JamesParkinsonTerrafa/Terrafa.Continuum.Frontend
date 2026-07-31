// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Runtime.InteropServices.JavaScript;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend;

/// <summary>
/// The browser head's credential store: <c>localStorage</c>, the only durable storage a wasm page
/// has. The trade the app previously refused — a token readable by any script on the origin — is
/// accepted here for the refresh token alone: it is revocable server-side, useless without the
/// pool's client id, and never the bearer token the API actually honours.
/// </summary>
internal sealed partial class LocalStorageSecretStore : ISecretStore
{
    private const string Key = "terrafa.auth";

    public Task<StoredCredential?> LoadAsync() =>
        Task.FromResult(StoredCredential.FromStorageValue(GetItem(Key)));

    public Task SaveAsync(StoredCredential credential)
    {
        SetItem(Key, credential.ToStorageValue());
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        RemoveItem(Key);
        return Task.CompletedTask;
    }

    [JSImport("globalThis.localStorage.getItem")]
    private static partial string? GetItem(string key);

    [JSImport("globalThis.localStorage.setItem")]
    private static partial void SetItem(string key, string value);

    [JSImport("globalThis.localStorage.removeItem")]
    private static partial void RemoveItem(string key);
}
