using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Terrafa.Continuum.Frontend.Services;

/// <param name="ExpiresIn">Lifetime of the access token in seconds, as the pool is configured.</param>
public sealed record AuthTokens(string AccessToken, string? IdToken, string? RefreshToken, int ExpiresIn);

public interface IAuthenticator
{
    /// <exception cref="AuthException">The credentials were rejected, or the pool could not be reached.</exception>
    Task<AuthTokens> SignInAsync(string username, string password);

    /// <summary>Exchanges a refresh token for a new access token, without re-prompting.</summary>
    Task<AuthTokens> RefreshAsync(string refreshToken);
}

/// <summary>A sign-in that did not succeed, with a message already fit to show the user.</summary>
public sealed class AuthException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Signs in against a Cognito user pool with <c>USER_PASSWORD_AUTH</c>, which is the flow that
/// fits accounts an administrator creates and hands out — the user never self-registers, and there
/// is no hosted-UI redirect to bounce through.
///
/// <para>
/// This talks to the Cognito JSON API directly rather than through the AWS SDK. The call takes no
/// AWS credentials and no request signing, so the SDK would contribute nothing but megabytes to a
/// wasm bundle that has to be downloaded before the app starts.
/// </para>
/// </summary>
public sealed class CognitoAuthenticator(HttpClient client) : IAuthenticator
{
    private const string TargetHeader = "X-Amz-Target";
    private const string InitiateAuth = "AWSCognitoIdentityProviderService.InitiateAuth";

    public CognitoAuthenticator() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
    {
    }

    public Task<AuthTokens> SignInAsync(string username, string password) =>
        AuthenticateAsync(
            new CognitoAuthRequest("USER_PASSWORD_AUTH", AuthOptions.ClientId,
                new Dictionary<string, string> { ["USERNAME"] = username, ["PASSWORD"] = password }),
            "Could not sign in");

    public Task<AuthTokens> RefreshAsync(string refreshToken) =>
        AuthenticateAsync(
            new CognitoAuthRequest("REFRESH_TOKEN_AUTH", AuthOptions.ClientId,
                new Dictionary<string, string> { ["REFRESH_TOKEN"] = refreshToken }),
            "Could not renew the session");

    private async Task<AuthTokens> AuthenticateAsync(CognitoAuthRequest body, string what)
    {
        if (!AuthOptions.IsConfigured)
            throw new AuthException("No user pool is configured in AuthOptions, so there is nothing to sign in to.");

        // Serialised up front rather than streamed: a JsonContent has no length until it is
        // written, so it goes out chunked with no Content-Length, and the AWS endpoints expect a
        // length on a POST. Buffering costs nothing here — the body is two short strings.
        var content = new StringContent(
            JsonSerializer.Serialize(body, CognitoJson.Default.CognitoAuthRequest), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-amz-json-1.1");

        using var request = new HttpRequestMessage(HttpMethod.Post, AuthOptions.Endpoint)
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation(TargetHeader, InitiateAuth);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new AuthException($"{what}: could not reach the sign-in service. {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw new AuthException($"{what}: {await ExplainAsync(response)}");

            CognitoAuthResponse? result;
            try
            {
                result = await response.Content.ReadFromJsonAsync(CognitoJson.Default.CognitoAuthResponse);
            }
            catch (JsonException ex)
            {
                throw new AuthException($"{what}: the sign-in service returned an unreadable response.", ex);
            }

            // A challenge is a 200 with no tokens in it. The one that actually turns up is
            // NEW_PASSWORD_REQUIRED, which is the state an administrator-created account sits in
            // until its temporary password is replaced — so it is named rather than lumped in with
            // a generic failure, because the fix is specific and this app cannot do it.
            if (result?.ChallengeName is { Length: > 0 } challenge)
            {
                throw new AuthException(challenge == "NEW_PASSWORD_REQUIRED"
                    ? "This account still has its temporary password and must have a permanent one set before it can be used here. " +
                      $"Contact {AuthOptions.ContactEmail}."
                    : $"{what}: the account needs to complete '{challenge}', which this app cannot do.");
            }

            if (result?.AuthenticationResult is not { AccessToken: { Length: > 0 } token } tokens)
                throw new AuthException($"{what}: the sign-in service returned no token.");

            return new AuthTokens(token, tokens.IdToken, tokens.RefreshToken, tokens.ExpiresIn);
        }
    }

    /// <summary>
    /// Turns a Cognito error into something worth reading. Its wire errors carry the failure in a
    /// <c>__type</c> field, and the few that a person can actually act on are worth saying plainly
    /// rather than relaying "NotAuthorizedException" to someone mistyping a password.
    /// </summary>
    private static async Task<string> ExplainAsync(HttpResponseMessage response)
    {
        CognitoError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync(CognitoJson.Default.CognitoError);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException)
        {
            // Not a Cognito error body; fall through to the status code.
        }

        // __type arrives either bare or namespaced as "com.amazonaws...#NotAuthorizedException".
        var kind = error?.Type is { Length: > 0 } raw ? raw[(raw.IndexOf('#') + 1)..] : "";

        return kind switch
        {
            "NotAuthorizedException" => "that username and password were not accepted.",
            "UserNotFoundException" => "that username was not accepted.",
            "UserNotConfirmedException" => "that account has not been confirmed yet.",
            "PasswordResetRequiredException" => "that account needs its password reset before it can be used.",
            "TooManyRequestsException" or "LimitExceededException" =>
                "too many attempts have been made — wait a moment and try again.",
            "InvalidParameterException" => $"the sign-in service rejected the request. {error?.Message}",
            "ResourceNotFoundException" =>
                "the configured user pool client does not exist — check AuthOptions.ClientId and Region.",
            _ => error?.Message is { Length: > 0 } message
                ? message
                : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd()
        };
    }
}

// Cognito's JSON protocol is PascalCase on the wire, unlike the DataFeed service, so these get
// their own context rather than inheriting the camelCase policy of DataFeedJson.

internal sealed record CognitoAuthRequest(
    string AuthFlow,
    string ClientId,
    Dictionary<string, string> AuthParameters);

internal sealed record CognitoAuthResponse(
    CognitoAuthenticationResult? AuthenticationResult,
    string? ChallengeName);

internal sealed record CognitoAuthenticationResult(
    string? AccessToken,
    string? IdToken,
    string? RefreshToken,
    int ExpiresIn);

internal sealed record CognitoError(
    [property: JsonPropertyName("__type")] string? Type,
    [property: JsonPropertyName("message")] string? Message);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CognitoAuthRequest))]
[JsonSerializable(typeof(CognitoAuthResponse))]
[JsonSerializable(typeof(CognitoError))]
internal sealed partial class CognitoJson : JsonSerializerContext;
