// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Tests;

/// <summary>
/// Guards the sign-in path against the failures that have actually happened, all of which built and
/// ran clean and only showed up as "could not sign in" in front of a user.
///
/// <para>
/// These talk to the real user pool. That is the point: every interesting way this breaks lives in
/// the gap between the app's assumptions and the deployed pool's configuration, and a mock of
/// Cognito would agree with whatever the app believed on the day it was written. They need no
/// credentials — a deliberately wrong password proves the whole handshake ran, because Cognito can
/// only reject the SRP proof after it has accepted everything leading up to it.
/// </para>
///
/// <para>
/// They need outbound network access, so they are marked with the <c>Live</c> trait and can be
/// excluded with <c>dotnet test --filter Category!=Live</c>.
/// </para>
/// </summary>
[Trait("Category", "Live")]
public class AuthenticationTests
{
    /// <summary>
    /// The one that matters. A wrong password must come back as a *rejected credential*, not as a
    /// transport, configuration or platform error.
    ///
    /// <para>
    /// Reaching "that username and password were not accepted" means all of the following held: the
    /// region and client id resolve to a pool that exists, the app client permits
    /// <c>USER_SRP_AUTH</c>, the SRP group arithmetic and key schedule produced a well-formed
    /// claim, and the response unmarshalled. Any of those breaking produces a different message —
    /// which is what this asserts on, rather than merely asserting that it failed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task WrongPassword_IsRejectedAsCredentials_NotAsAFailureToReachThePool()
    {
        var authenticator = new CognitoAuthenticator();

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            authenticator.SignInAsync("no-such-user@terrafa.invalid", "Definitely-Not-The-Password-1!"));

        Assert.Contains("not accepted", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Each of these is a real failure mode that would otherwise hide behind a generic throw.
        Assert.DoesNotContain("could not reach", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("does not allow USER_SRP_AUTH", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("does not exist", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unmarshall", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A refresh token that was never issued must be rejected as a credential too. This covers the
    /// renewal path, which otherwise only runs after a token has been held for an hour and so never
    /// gets exercised in a test run or a demo.
    /// </summary>
    [Fact]
    public async Task GarbageRefreshToken_IsRejectedWithoutCrashing()
    {
        var authenticator = new CognitoAuthenticator();

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            authenticator.RefreshAsync("not-a-real-refresh-token"));

        Assert.StartsWith("Could not renew the session", ex.Message);
        Assert.DoesNotContain("could not reach", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unmarshall", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The deployed pool must keep allowing the flow the app implements. This asserts the negative
    /// that bit us: if someone removes <c>ALLOW_USER_SRP_AUTH</c> from the app client, or restores
    /// <c>USER_PASSWORD_AUTH</c> to the client code, sign-in stops working for everyone and the
    /// message above is the only trace.
    /// </summary>
    [Fact]
    public async Task ThePool_StillAllowsTheAuthFlowTheAppUses()
    {
        var authenticator = new CognitoAuthenticator();

        var ex = await Assert.ThrowsAsync<AuthException>(() =>
            authenticator.SignInAsync("no-such-user@terrafa.invalid", "Definitely-Not-The-Password-1!"));

        Assert.DoesNotContain("Auth flow not enabled", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("USER_SRP_AUTH", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The compiled-in deployment values. Cheap, offline, and they fail loudly if someone blanks one
/// while wiring up a local run and commits it.
/// </summary>
public class AuthOptionsTests
{
    [Fact]
    public void TheDeployedPoolValuesArePresentAndConsistent()
    {
        Assert.True(AuthOptions.IsConfigured);

        // The pool id is region-prefixed by construction, and SRP derives key material from the
        // half after the underscore — a pool id from the wrong region fails deep inside the
        // handshake rather than at the call.
        Assert.StartsWith($"{AuthOptions.Region}_", AuthOptions.UserPoolId);
        Assert.NotEqual(AuthOptions.UserPoolId, AuthOptions.ClientId);
    }

    [Fact]
    public void TheDataFeedAddressIsARoutableHttpsUrl()
    {
        Assert.True(DataFeedOptions.IsConfigured);

        var uri = new Uri(DataFeedOptions.BaseAddress);
        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);

        // 192.0.2.0/24 was the "not deployed yet" placeholder. Nothing routes it, so a build that
        // ships it can only fail to connect.
        Assert.DoesNotContain("192.0.2.", DataFeedOptions.BaseAddress);

        // The client appends "/api/...", so a trailing slash would produce a double slash.
        Assert.False(DataFeedOptions.BaseAddress.EndsWith('/'));
    }
}
