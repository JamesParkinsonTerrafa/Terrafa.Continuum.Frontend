namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// The Cognito user pool that issues the tokens the DataFeed service accepts.
///
/// <para>
/// <b>Fill in <see cref="Region"/> and <see cref="ClientId"/> once the pool is applied.</b> Both
/// are blank by default, which leaves <see cref="IsConfigured"/> false and makes the CONNECT REAL
/// DATA dialog say the pool is not set up rather than offer a sign-in box that cannot work.
/// </para>
///
/// <para>
/// Neither value is a secret. The client is the public one the Terraform describes
/// (<c>cognito_generate_client_secret = false</c>), so there is nothing here that a user of the
/// app could not read out of the network tab anyway — which is exactly why this flow is safe to
/// run from a wasm bundle. The user pool is what enforces the password.
/// </para>
///
/// <para>
/// The pool's app client needs <c>ALLOW_USER_PASSWORD_AUTH</c> in its explicit auth flows: the
/// hosted-UI redirect the Terraform's callback URLs describe is a different flow from the in-app
/// username and password box this screen shows, and a client can offer both.
/// </para>
/// </summary>
public static class AuthOptions
{
    /// <summary>AWS region of the user pool, e.g. <c>eu-west-2</c>.</summary>
    public static string Region { get; } = Environment.GetEnvironmentVariable("TERRAFA_COGNITO_REGION") ?? "";

    /// <summary>The user pool <b>app client</b> id — not the pool id, which this flow never sends.</summary>
    public static string ClientId { get; } = Environment.GetEnvironmentVariable("TERRAFA_COGNITO_CLIENT_ID") ?? "";

    public static bool IsConfigured => Region.Length > 0 && ClientId.Length > 0;

    /// <summary>
    /// The regional Cognito endpoint. Public, and takes no AWS credentials for this flow.
    ///
    /// <para>
    /// <c>TERRAFA_COGNITO_ENDPOINT</c> overrides it, which exists so the sign-in flow can be run
    /// against a local stand-in. It is a <b>development override only</b>: it redirects where
    /// passwords are sent, so it has no business being set on a real machine. Nothing reads it on
    /// the browser head, which has no environment.
    /// </para>
    /// </summary>
    public static string Endpoint { get; } =
        Environment.GetEnvironmentVariable("TERRAFA_COGNITO_ENDPOINT") is { Length: > 0 } development
            ? development
            : $"https://cognito-idp.{Region}.amazonaws.com/";

    /// <summary>Where to send people who have no account yet.</summary>
    public const string ContactEmail = "info@terrafa.uk";

    /// <summary>
    /// Renew this long before the token actually expires, so a request cannot leave with a token
    /// that lapses in flight.
    /// </summary>
    public static TimeSpan RefreshMargin { get; } = TimeSpan.FromMinutes(2);
}
