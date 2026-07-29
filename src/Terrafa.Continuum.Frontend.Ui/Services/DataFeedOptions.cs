namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// Where the DataFeed service lives and how hard this client leans on it.
///
/// <para>
/// The address is the deployed API Gateway stage, compiled in for the same reason the Cognito
/// values in <see cref="AuthOptions"/> are: the browser head has no environment, so a default that
/// only ever came from <c>TERRAFA_DATAFEED_URL</c> left the web build pointing at nothing. The
/// variable still overrides on the desktop head.
/// </para>
///
/// <para>
/// The gateway rejects an unauthenticated request itself, before the function is invoked, so the
/// address being public gives away only that the service exists.
/// </para>
/// </summary>
public static class DataFeedOptions
{
    /// <summary>
    /// Root of the DataFeed service, without the <c>/api</c> suffix — the client appends the
    /// route itself.
    /// </summary>
    public static string BaseAddress { get; } =
        Environment.GetEnvironmentVariable("TERRAFA_DATAFEED_URL") is { Length: > 0 } fromEnvironment
            ? fromEnvironment.TrimEnd('/')
            : "https://0ncy4qt6v1.execute-api.eu-north-1.amazonaws.com";

    /// <summary>
    /// Whether the catalogue should read from the live service at all. Always true now that a real
    /// address is compiled in; kept because the call sites branch on it to fall back to
    /// <see cref="StubDatasetCatalog"/>, which is still what runs when the user has not signed in.
    /// </summary>
    public static bool IsConfigured => BaseAddress.Length > 0;

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
    /// Most columns one query asks for. A wide table would otherwise put hundreds of
    /// <c>columns=</c> pairs in the query string and scan every one of them; leaves past this
    /// keep their placeholder reading and say so. The x axis is always inside the cap — it leads
    /// the projection, because it is the one column the request cannot do without.
    /// </summary>
    public static int MaxSampleColumns { get; } = 64;

    /// <summary>
    /// Most points a leaf's series keeps, taken from the recent end.
    ///
    /// <para>
    /// The service caps a read at its own MaxRows and applies that cap <i>after</i> ordering, so
    /// asking for ascending order on a long table would hand back its oldest rows and the chart
    /// would draw that dataset's distant past. The query therefore sorts descending and the rows
    /// are reversed here, which makes both cuts — the service's and this one — keep the present.
    /// </para>
    /// </summary>
    public static int SeriesRows { get; } = 240;
}
