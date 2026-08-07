// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Runtime.InteropServices.JavaScript;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend;

/// <summary>
/// The browser head's home for the operator's Anthropic API key: <c>localStorage</c>, the only
/// durable storage the page has. The same trade the auth store already accepts — readable by any
/// script on the origin — taken here for a key the operator supplied themselves and can revoke
/// from their Anthropic console at any time.
/// </summary>
internal sealed partial class LocalStorageSandboxKeyStore : ISandboxKeyStore
{
    private const string Key = "terrafa.anthropic";

    public Task<string?> LoadAsync() => Task.FromResult(GetItem(Key));

    public Task SaveAsync(string apiKey)
    {
        SetItem(Key, apiKey);
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
