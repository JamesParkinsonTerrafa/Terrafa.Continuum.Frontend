// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia.Threading;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// Who is signed in, and the token that proves it. One per app, like
/// <see cref="Models.Workspace"/> — the screens read it rather than passing it around, because
/// signing in changes what every one of them shows.
///
/// <para>
/// Access and id tokens are held in memory only. The refresh token — revocable server-side, and
/// useless without the pool's client id — is handed to <see cref="Store"/> so a restart can renew
/// the session without a login box; each head supplies the safest store it has (the keychain on
/// desktop, localStorage on the web head), and the default stores nothing.
/// </para>
/// </summary>
public sealed class AuthSession(IAuthenticator authenticator)
{
    public static AuthSession Instance { get; } = new(new CognitoAuthenticator());

    private readonly Lock gate = new();
    private AuthTokens? tokens;
    private DateTimeOffset renewAfter;
    private Task<string?>? renewing;

    /// <summary>The identity the last <see cref="Changed"/> announced. See <see cref="RaiseChanged"/>.</summary>
    private string? announced;

    public ISecretStore Store { get; set; } = new NullSecretStore();

    /// <summary>
    /// Raised when, and only when, <b>the signed-in identity changes</b> — signed out to someone,
    /// someone to signed out, or one account to another. A token restore counts, because from the
    /// app's point of view a restored session is a sign-in.
    ///
    /// <para>
    /// A renewal deliberately does not raise it, and neither does re-signing in as whoever is
    /// already signed in. This event used to fire for all of those, and every subscriber had to
    /// work out which one it had been: one of them got it wrong and reset the workspace to the demo
    /// seed on a routine token renewal, then saved the seed over the operator's real work. The
    /// event now carries one meaning so nothing has to infer it.
    /// </para>
    /// </summary>
    public event Action? Changed;

    public string? Username { get; private set; }

    public bool IsSignedIn
    {
        get { lock (gate) return tokens is not null; }
    }

    /// <summary>Who is signed in, or null. This is what <see cref="Changed"/> announces a change in.</summary>
    public string? Identity
    {
        get { lock (gate) return tokens is null ? null : Username ?? ""; }
    }

    /// <exception cref="AuthException">The credentials were rejected, or the pool could not be reached.</exception>
    public async Task SignInAsync(string username, string password)
    {
        var issued = await authenticator.SignInAsync(username, password);

        lock (gate)
        {
            tokens = issued;
            renewAfter = Expiry(issued);
            renewing = null;
        }
        Username = username;
        if (issued.RefreshToken is not null)
            await SaveCredentialAsync(new StoredCredential(username, issued.RefreshToken));
        RaiseChanged();
    }

    public async Task TryRestoreAsync()
    {
        StoredCredential? credential;
        try
        {
            credential = await Store.LoadAsync();
        }
        catch
        {
            return;
        }
        if (credential is null) return;

        lock (gate)
        {
            if (tokens is not null) return;
        }

        AuthTokens issued;
        try
        {
            issued = await authenticator.RefreshAsync(credential.RefreshToken);
        }
        catch
        {
            await ClearCredentialAsync();
            return;
        }

        lock (gate)
        {
            if (tokens is not null) return;
            tokens = issued with { RefreshToken = issued.RefreshToken ?? credential.RefreshToken };
            renewAfter = Expiry(issued);
            renewing = null;
        }
        Username = credential.Username;
        if (issued.RefreshToken is not null && issued.RefreshToken != credential.RefreshToken)
            await SaveCredentialAsync(credential with { RefreshToken = issued.RefreshToken });
        RaiseChanged();
    }

    public void SignOut()
    {
        string? refreshToken;
        lock (gate)
        {
            if (tokens is null) return;
            refreshToken = tokens.RefreshToken;
            tokens = null;
            renewing = null;
        }
        Username = null;
        _ = ForgetCredentialAsync(refreshToken);
        RaiseChanged();
    }

    /// <summary>
    /// A token good for the next request, renewed first if it is about to lapse. Null when signed
    /// out — callers send no Authorization header rather than an empty one, so an API that does
    /// not require auth still answers.
    /// </summary>
    public Task<string?> GetAccessTokenAsync()
    {
        Task<string?> renewal;
        lock (gate)
        {
            if (tokens is null) return Task.FromResult<string?>(null);
            if (DateTimeOffset.UtcNow < renewAfter) return Task.FromResult<string?>(tokens.AccessToken);

            // Past the renewal point. Share one renewal between concurrent callers — the catalogue
            // and a sample query routinely overlap, and refreshing twice can invalidate the first
            // result depending on how the pool is configured to rotate.
            renewal = renewing ??= RenewAsync();
        }
        return renewal;
    }

    private async Task<string?> RenewAsync()
    {
        string? refreshToken;
        lock (gate) refreshToken = tokens?.RefreshToken;

        if (refreshToken is null)
        {
            // Nothing to renew with — an expired session is a signed-out one.
            SignOut();
            return null;
        }

        try
        {
            var issued = await authenticator.RefreshAsync(refreshToken);
            lock (gate)
            {
                // Cognito does not reissue the refresh token on this flow unless rotation is
                // enabled, so the old one is kept when nothing new arrives.
                tokens = issued with { RefreshToken = issued.RefreshToken ?? refreshToken };
                renewAfter = Expiry(issued);
                renewing = null;
            }
            if (issued.RefreshToken is not null && issued.RefreshToken != refreshToken && Username is not null)
                await SaveCredentialAsync(new StoredCredential(Username, issued.RefreshToken));
            return issued.AccessToken;
        }
        catch (AuthException)
        {
            // The refresh token has been revoked or has aged out. Signing out is the honest
            // outcome: the screens drop back to stub data and the connect button reappears.
            SignOut();
            return null;
        }
    }

    private async Task SaveCredentialAsync(StoredCredential credential)
    {
        try
        {
            await Store.SaveAsync(credential);
        }
        catch
        {
        }
    }

    private async Task ClearCredentialAsync()
    {
        try
        {
            await Store.ClearAsync();
        }
        catch
        {
        }
    }

    private async Task ForgetCredentialAsync(string? refreshToken)
    {
        await ClearCredentialAsync();
        if (refreshToken is not null)
            await authenticator.RevokeAsync(refreshToken);
    }

    /// <summary>
    /// Announces an identity change, once. The suppression here is what makes the event mean one
    /// thing: every path that alters the session calls this, and only a genuine change gets out.
    /// </summary>
    private void RaiseChanged()
    {
        lock (gate)
        {
            var identity = tokens is null ? null : Username ?? "";
            if (identity == announced) return;
            announced = identity;
        }

        var handlers = Changed;
        if (handlers is null) return;
        if (Dispatcher.UIThread.CheckAccess()) handlers();
        else Dispatcher.UIThread.Post(() => handlers());
    }

    private static DateTimeOffset Expiry(AuthTokens issued) =>
        DateTimeOffset.UtcNow
        + TimeSpan.FromSeconds(Math.Max(issued.ExpiresIn, 0))
        - AuthOptions.RefreshMargin;
}
