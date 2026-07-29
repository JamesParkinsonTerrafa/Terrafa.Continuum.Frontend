namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// The Cognito user pool that issues the tokens the DataFeed service accepts.
///
/// <para>
/// These are the deployed values, compiled in rather than configured. None of them is a secret:
/// the client is the public one the Terraform describes
/// (<c>cognito_generate_client_secret = false</c>), and every one of them travels in the clear on
/// the first request any signed-in browser makes. A browser app cannot authenticate without
/// knowing which pool to authenticate against — an OIDC client has to carry its own issuer and
/// client id — so there is nothing here to hide. The user pool is what enforces the password.
/// </para>
///
/// <para>
/// Compiled in rather than read from the environment because the browser head has no environment
/// to read: <c>Environment.GetEnvironmentVariable</c> returns null under wasm no matter what the
/// deploy sets, so an environment-only default left the web build permanently unconfigured. The
/// variables below still override on the desktop head, which does have one.
/// </para>
///
/// <para>
/// Sign-in uses <c>USER_SRP_AUTH</c>, which is the only user-facing flow the pool's app client
/// allows — see <see cref="CognitoAuthenticator"/>. The password is never sent, so the values here
/// being public costs nothing.
/// </para>
/// </summary>
public static class AuthOptions
{
    /// <summary>AWS region of the user pool.</summary>
    public static string Region { get; } =
        Environment.GetEnvironmentVariable("TERRAFA_COGNITO_REGION") is { Length: > 0 } region
            ? region
            : "eu-north-1";

    /// <summary>The user pool <b>app client</b> id.</summary>
    public static string ClientId { get; } =
        Environment.GetEnvironmentVariable("TERRAFA_COGNITO_CLIENT_ID") is { Length: > 0 } client
            ? client
            : "2lroc37l2gjoi6nnvbitpm4m57";

    /// <summary>
    /// The pool id. Unlike the client id this one is needed only because SRP derives part of its
    /// key material from the pool's short name — the half after the underscore.
    /// </summary>
    public static string UserPoolId { get; } =
        Environment.GetEnvironmentVariable("TERRAFA_COGNITO_USER_POOL_ID") is { Length: > 0 } pool
            ? pool
            : "eu-north-1_dgRWlrr7C";

    public static bool IsConfigured => Region.Length > 0 && ClientId.Length > 0 && UserPoolId.Length > 0;

    /// <summary>Where to send people who have no account yet.</summary>
    public const string ContactEmail = "info@terrafa.uk";

    /// <summary>
    /// Renew this long before the token actually expires, so a request cannot leave with a token
    /// that lapses in flight.
    /// </summary>
    public static TimeSpan RefreshMargin { get; } = TimeSpan.FromMinutes(2);
}
