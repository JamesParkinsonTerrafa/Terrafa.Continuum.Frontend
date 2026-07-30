// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// Who is signed in, and the token that proves it. One per app, like
/// <see cref="Models.Workspace"/> — the screens read it rather than passing it around, because
/// signing in changes what every one of them shows.
///
/// <para>
/// The token is held in memory only. Nothing is written to disk, so closing the app signs out:
/// persisting it would mean putting a bearer token somewhere a wasm bundle shares with every
/// other page on its origin, for the sake of skipping a login box.
/// </para>
/// </summary>
public sealed class AuthSession(IAuthenticator authenticator)
{
    public static AuthSession Instance { get; } = new(new CognitoAuthenticator());

    private readonly Lock gate = new();
    private AuthTokens? tokens;
    private DateTimeOffset renewAfter;
    private Task<string?>? renewing;

    /// <summary>Raised on sign-in and sign-out. Screens rebuild against the catalogue this selects.</summary>
    public event Action? Changed;

    public string? Username { get; private set; }

    public bool IsSignedIn
    {
        get { lock (gate) return tokens is not null; }
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
        Changed?.Invoke();
    }

    public void SignOut()
    {
        lock (gate)
        {
            if (tokens is null) return;
            tokens = null;
            renewing = null;
        }
        Username = null;
        Changed?.Invoke();
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
                // Cognito does not reissue the refresh token on this flow, so the old one is kept.
                tokens = issued with { RefreshToken = issued.RefreshToken ?? refreshToken };
                renewAfter = Expiry(issued);
                renewing = null;
            }
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

    private static DateTimeOffset Expiry(AuthTokens issued) =>
        DateTimeOffset.UtcNow
        + TimeSpan.FromSeconds(Math.Max(issued.ExpiresIn, 0))
        - AuthOptions.RefreshMargin;
}
