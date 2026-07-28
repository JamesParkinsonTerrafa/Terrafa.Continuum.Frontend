namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// Where the DataFeed service lives and how hard this client leans on it.
///
/// <para>
/// <b>To point the app at a deployed service, change <see cref="BaseAddress"/> below.</b> It is
/// currently a placeholder in 192.0.2.0/24 — the range RFC 5737 reserves for documentation, which
/// nothing routes. That is deliberate: an unset address can only ever fail to connect, it can
/// never quietly reach some other machine that happens to answer on that port.
/// </para>
///
/// <para>
/// While the address is the placeholder, <see cref="IsConfigured"/> is false and the app keeps
/// running against <see cref="StubDatasetCatalog"/> exactly as it does today. Filling in a real
/// address is the only step needed to switch the catalogue over to the live service.
/// </para>
/// </summary>
public static class DataFeedOptions
{
    /// <summary>The reserved address that means "not deployed yet". Compared against, never dialled.</summary>
    private const string Placeholder = "http://192.0.2.10:8080";

    /// <summary>
    /// Root of the DataFeed service, without the <c>/api</c> suffix — the client appends the
    /// route itself. Overridden at run time by <c>TERRAFA_DATAFEED_URL</c> on the desktop head,
    /// which has an environment to read; the browser head has none, so there it is this constant.
    /// </summary>
    public static string BaseAddress { get; } =
        Environment.GetEnvironmentVariable("TERRAFA_DATAFEED_URL") is { Length: > 0 } fromEnvironment
            ? fromEnvironment.TrimEnd('/')
            : Placeholder;

    /// <summary>False while the address is still the placeholder — the app then stays on the stub.</summary>
    public static bool IsConfigured => BaseAddress != Placeholder;

    /// <summary>Host and port only, for showing which service a screen is reading from.</summary>
    public static string DisplayHost =>
        Uri.TryCreate(BaseAddress, UriKind.Absolute, out var uri) ? uri.Authority : BaseAddress;

    /// <summary>
    /// Ceiling on a single call. The catalog endpoints are metadata reads and answer quickly; the
    /// data endpoint runs a real Athena query and the service allows it up to 60 s by default, so
    /// this sits above that and lets the service's own timeout produce the 504 rather than the
    /// client giving up first and losing the reason.
    /// </summary>
    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Whether opening a dataset also fetches a row of live values for its leaves. Each fetch is
    /// one billed Athena query, so this is the switch to reach for if browsing the catalogue turns
    /// out to cost more than it is worth. Off still leaves the full schema browsable.
    /// </summary>
    public static bool SampleValues { get; } = true;

    /// <summary>
    /// Most columns one sample query asks for. A wide table would otherwise put hundreds of
    /// <c>columns=</c> pairs in the query string and scan every one of them; leaves past this
    /// keep their placeholder reading and say so.
    /// </summary>
    public static int MaxSampleColumns { get; } = 64;
}
