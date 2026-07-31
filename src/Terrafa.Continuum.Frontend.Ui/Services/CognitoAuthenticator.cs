// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.Extensions.CognitoAuthentication;
using Amazon.Runtime;

namespace Terrafa.Continuum.Frontend.Services;

/// <param name="ExpiresIn">Lifetime of the access token in seconds, as the pool is configured.</param>
public sealed record AuthTokens(string AccessToken, string? IdToken, string? RefreshToken, int ExpiresIn);

public interface IAuthenticator
{
    /// <exception cref="AuthException">The credentials were rejected, or the pool could not be reached.</exception>
    Task<AuthTokens> SignInAsync(string username, string password);

    /// <summary>Exchanges a refresh token for a new access token, without re-prompting.</summary>
    Task<AuthTokens> RefreshAsync(string refreshToken);

    /// <summary>Best-effort revocation of a refresh token on sign-out. Never throws.</summary>
    Task RevokeAsync(string refreshToken);
}

/// <summary>A sign-in that did not succeed, with a message already fit to show the user.</summary>
public sealed class AuthException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Signs in against a Cognito user pool with <c>USER_SRP_AUTH</c>.
///
/// <para>
/// SRP rather than <c>USER_PASSWORD_AUTH</c> because that is the only user-facing flow the pool's
/// app client allows, deliberately: SRP proves knowledge of the password without ever transmitting
/// it, so the password does not reach AWS at the application layer even inside TLS. The trade is
/// that the client has to do modular exponentiation over a 3072-bit group, derive a key with HKDF
/// and assemble Cognito's particular claim signature — which is why this leans on
/// <c>Amazon.Extensions.CognitoAuthentication</c> instead of hand-rolling it.
/// </para>
///
/// <para>
/// <b>Two shims below exist solely for the browser head.</b> AWS does not support browser-wasm as a
/// target for AWSSDK.Core, and it fails there in two distinct ways that both look like unrelated
/// crashes. They are cheap, they are inert on the desktop head, and removing either one breaks
/// sign-in on the web build only — which is exactly the kind of regression nobody notices locally.
/// </para>
/// </summary>
public sealed class CognitoAuthenticator : IAuthenticator
{
    private readonly CognitoUserPool _pool;
    private readonly IAmazonCognitoIdentityProvider _provider;

    public CognitoAuthenticator()
    {
        _provider = new AmazonCognitoIdentityProviderClient(
            new AnonymousAWSCredentials(),
            new AmazonCognitoIdentityProviderConfig
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(AuthOptions.Region),

                // Shim 1. AWSSDK.Core's default factory builds a SocketsHttpHandler and sets
                // EnableMultipleHttp2Connections on it. Browser-wasm has no SocketsHttpHandler, so
                // merely constructing the client throws PlatformNotSupportedException before a
                // single byte moves. HttpClientFactory is the SDK's own override point.
                HttpClientFactory = new BrowserSafeHttpClientFactory(),
            });

        _pool = new CognitoUserPool(AuthOptions.UserPoolId, AuthOptions.ClientId, _provider);
    }

    public Task<AuthTokens> SignInAsync(string username, string password) =>
        RunAsync(
            async () =>
            {
                var user = new CognitoUser(username, AuthOptions.ClientId, _pool, _provider);
                var result = await user.StartWithSrpAuthAsync(new InitiateSrpAuthRequest { Password = password });
                return result.AuthenticationResult;
            },
            "Could not sign in");

    public Task<AuthTokens> RefreshAsync(string refreshToken) =>
        RunAsync(
            async () =>
            {
                // The SDK renews through a CognitoUser carrying the existing tokens rather than
                // taking a bare refresh token. Only the refresh token and its expiry matter here;
                // the access and id token slots are what the call is about to replace.
                var user = new CognitoUser(null, AuthOptions.ClientId, _pool, _provider)
                {
                    SessionTokens = new CognitoUserSession(null, null, refreshToken,
                        DateTime.UtcNow, DateTime.UtcNow.AddDays(30)),
                };

                var result = await user.StartWithRefreshTokenAuthAsync(
                    new InitiateRefreshTokenAuthRequest { AuthFlowType = AuthFlowType.REFRESH_TOKEN_AUTH });
                return result.AuthenticationResult;
            },
            "Could not renew the session");

    public async Task RevokeAsync(string refreshToken)
    {
        if (!AuthOptions.IsConfigured) return;
        try
        {
            await _provider.RevokeTokenAsync(new RevokeTokenRequest
            {
                Token = refreshToken,
                ClientId = AuthOptions.ClientId,
            });
        }
        catch
        {
            // Revocation is hygiene, not correctness: the stored copy is already gone, and a pool
            // whose app client has revocation disabled answers this call with an error.
        }
    }

    private static async Task<AuthTokens> RunAsync(Func<Task<AuthenticationResultType?>> call, string what)
    {
        if (!AuthOptions.IsConfigured)
            throw new AuthException("No user pool is configured in AuthOptions, so there is nothing to sign in to.");

        AuthenticationResultType? result;
        try
        {
            result = await call();
        }
        catch (Exception ex)
        {
            throw new AuthException($"{what}: {Explain(ex, what)}", ex);
        }

        if (result is not { AccessToken: { Length: > 0 } token })
            throw new AuthException($"{what}: the sign-in service returned no token.");

        return new AuthTokens(token, result.IdToken, result.RefreshToken, result.ExpiresIn ?? 0);
    }

    /// <summary>
    /// Turns an SDK exception into something worth reading. The few a person can actually act on
    /// are worth saying plainly rather than relaying "NotAuthorizedException" to someone who has
    /// mistyped a password.
    /// </summary>
    private static string Explain(Exception ex, string what) => ex switch
    {
        NotAuthorizedException => "that username and password were not accepted.",
        UserNotFoundException => "that username was not accepted.",
        UserNotConfirmedException => "that account has not been confirmed yet.",
        PasswordResetRequiredException => "that account needs its password reset before it can be used.",
        TooManyRequestsException or LimitExceededException =>
            "too many attempts have been made — wait a moment and try again.",

        // An administrator-created account sits in FORCE_CHANGE_PASSWORD until a permanent password
        // is set for it, and Cognito answers the SRP challenge with NEW_PASSWORD_REQUIRED rather
        // than tokens. Named because the fix is specific and this app deliberately cannot do it.
        InvalidParameterException p when p.Message.Contains("NEW_PASSWORD_REQUIRED", StringComparison.OrdinalIgnoreCase) =>
            "This account still has its temporary password and must have a permanent one set before it can be used here. " +
            $"Contact {AuthOptions.ContactEmail}.",

        InvalidParameterException p => $"the sign-in service rejected the request. {p.Message}",
        ResourceNotFoundException =>
            "the configured user pool client does not exist — check AuthOptions.ClientId and Region.",

        // USER_SRP_AUTH missing from the app client's explicit auth flows lands here. It is a
        // deployment fault rather than a user one, so it says so instead of blaming the password.
        AmazonCognitoIdentityProviderException a when a.Message.Contains("Auth flow not enabled", StringComparison.OrdinalIgnoreCase) =>
            "this pool's app client does not allow USER_SRP_AUTH, so the app cannot sign anyone in.",

        HttpRequestException or TaskCanceledException => $"could not reach the sign-in service. {ex.Message}",
        AmazonServiceException s => s.Message,
        _ => ex.Message,
    };
}

/// <summary>
/// Shim 2's carrier. Returns an <see cref="HttpClient"/> whose handler buffers the response body
/// before the SDK reads it.
/// </summary>
internal sealed class BrowserSafeHttpClientFactory : HttpClientFactory
{
    public override HttpClient CreateHttpClient(IClientConfig config) =>
        new(new BufferingHandler()) { Timeout = TimeSpan.FromSeconds(30) };

    public override bool UseSDKHttpClientCaching(IClientConfig config) => true;

    public override bool DisposeHttpClientsAfterUse(IClientConfig config) => false;
}

/// <summary>
/// Shim 2. AWSSDK.Core unmarshals responses with a <b>synchronous</b> <c>Stream.Read</c>, and the
/// browser's response stream only supports async reads — it throws
/// <c>NotSupportedException: net_http_synchronous_reads_not_supported</c>, surfacing as an opaque
/// "Error unmarshalling response back from AWS" on an HTTP 200. Reading the body to completion here
/// means the SDK's sync read lands on a MemoryStream instead.
///
/// <para>
/// Not a trimming problem: it reproduces identically with <c>PublishTrimmed=false</c>. Responses on
/// this path are a few kilobytes of JSON, so buffering costs nothing worth measuring.
/// </para>
/// </summary>
internal sealed class BufferingHandler() : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);

        var body = await response.Content.ReadAsByteArrayAsync(ct);
        var buffered = new ByteArrayContent(body);
        foreach (var header in response.Content.Headers)
            buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);

        response.Content = buffered;
        return response;
    }
}
