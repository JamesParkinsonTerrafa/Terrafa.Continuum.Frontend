// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Tests;

/// <summary>
/// The durable-login contract: only the refresh token is ever handed to the store, restore
/// silently succeeds or silently cleans up, and sign-out leaves nothing behind. All offline,
/// against fakes — the live pool behaviour these lean on is covered by
/// <see cref="AuthenticationTests"/>.
/// </summary>
public class DurableSessionTests
{
    private sealed class FakeAuthenticator : IAuthenticator
    {
        public AuthTokens? NextTokens { get; set; }
        public string? RevokedToken { get; private set; }
        public string? RefreshedWith { get; private set; }

        public Task<AuthTokens> SignInAsync(string username, string password) =>
            Task.FromResult(NextTokens ?? throw new AuthException("no tokens staged"));

        public Task<AuthTokens> RefreshAsync(string refreshToken)
        {
            RefreshedWith = refreshToken;
            return Task.FromResult(NextTokens ?? throw new AuthException("staged refresh failure"));
        }

        public Task RevokeAsync(string refreshToken)
        {
            RevokedToken = refreshToken;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySecretStore : ISecretStore
    {
        public StoredCredential? Credential { get; set; }

        public Task<StoredCredential?> LoadAsync() => Task.FromResult(Credential);

        public Task SaveAsync(StoredCredential credential)
        {
            Credential = credential;
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            Credential = null;
            return Task.CompletedTask;
        }
    }

    private static (AuthSession Session, FakeAuthenticator Authenticator, InMemorySecretStore Store) NewSession()
    {
        var authenticator = new FakeAuthenticator();
        var store = new InMemorySecretStore();
        var session = new AuthSession(authenticator) { Store = store };
        return (session, authenticator, store);
    }

    [Fact]
    public async Task SignIn_SavesTheRefreshTokenAndUsername()
    {
        var (session, authenticator, store) = NewSession();
        authenticator.NextTokens = new AuthTokens("access", "id", "refresh-1", 3600);

        await session.SignInAsync("someone@terrafa.com", "pw");

        Assert.Equal(new StoredCredential("someone@terrafa.com", "refresh-1"), store.Credential);
    }

    [Fact]
    public async Task SignIn_WithoutARefreshToken_SavesNothing()
    {
        var (session, authenticator, store) = NewSession();
        authenticator.NextTokens = new AuthTokens("access", "id", null, 3600);

        await session.SignInAsync("someone@terrafa.com", "pw");

        Assert.True(session.IsSignedIn);
        Assert.Null(store.Credential);
    }

    [Fact]
    public async Task Restore_SignsBackInFromTheStoredCredential()
    {
        var (session, authenticator, store) = NewSession();
        store.Credential = new StoredCredential("someone@terrafa.com", "refresh-1");
        authenticator.NextTokens = new AuthTokens("access-2", "id", null, 3600);
        var changes = 0;
        session.Changed += () => changes++;

        await session.TryRestoreAsync();

        Assert.True(session.IsSignedIn);
        Assert.Equal("someone@terrafa.com", session.Username);
        Assert.Equal("refresh-1", authenticator.RefreshedWith);
        Assert.Equal(1, changes);
        Assert.Equal("access-2", await session.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Restore_WithAnEmptyStore_DoesNothing()
    {
        var (session, _, _) = NewSession();
        var changes = 0;
        session.Changed += () => changes++;

        await session.TryRestoreAsync();

        Assert.False(session.IsSignedIn);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task Restore_WhoseRefreshIsRejected_StaysSignedOutAndClearsTheStore()
    {
        var (session, _, store) = NewSession();
        store.Credential = new StoredCredential("someone@terrafa.com", "aged-out");

        await session.TryRestoreAsync();

        Assert.False(session.IsSignedIn);
        Assert.Null(session.Username);
        Assert.Null(store.Credential);
    }

    [Fact]
    public async Task SignOut_ClearsTheStoreAndRevokesTheToken()
    {
        var (session, authenticator, store) = NewSession();
        authenticator.NextTokens = new AuthTokens("access", "id", "refresh-1", 3600);
        await session.SignInAsync("someone@terrafa.com", "pw");

        session.SignOut();
        // Store cleanup and revocation are fire-and-forget off the sign-out path; against these
        // synchronous fakes they have completed by the time the task queue drains once.
        await Task.Yield();

        Assert.False(session.IsSignedIn);
        Assert.Null(store.Credential);
        Assert.Equal("refresh-1", authenticator.RevokedToken);
    }

    [Fact]
    public async Task Renewal_ThatRotatesTheRefreshToken_ReSavesIt()
    {
        var (session, authenticator, store) = NewSession();
        // ExpiresIn of zero puts the session past its renewal point immediately, so the next
        // GetAccessTokenAsync takes the renewal path instead of returning the cached token.
        authenticator.NextTokens = new AuthTokens("access", "id", "refresh-1", 0);
        await session.SignInAsync("someone@terrafa.com", "pw");

        authenticator.NextTokens = new AuthTokens("access-2", "id", "refresh-2", 3600);
        var token = await session.GetAccessTokenAsync();

        Assert.Equal("access-2", token);
        Assert.Equal(new StoredCredential("someone@terrafa.com", "refresh-2"), store.Credential);
    }

    [Fact]
    public void AStorageValue_RoundTrips_AndGarbageReadsAsAbsent()
    {
        var credential = new StoredCredential("someone@terrafa.com", "refresh-1");

        Assert.Equal(credential, StoredCredential.FromStorageValue(credential.ToStorageValue()));
        Assert.Null(StoredCredential.FromStorageValue(null));
        Assert.Null(StoredCredential.FromStorageValue("no-separator"));
        Assert.Null(StoredCredential.FromStorageValue("username-only\n"));
    }
}
