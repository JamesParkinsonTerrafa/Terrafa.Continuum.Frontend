namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// The catalogue the app actually holds: demo data until someone signs in, the real service after.
///
/// <para>
/// The swap lives here rather than in the screens so that no screen has to know a sign-in
/// happened — they ask the same object either way and get whichever catalogue is currently in
/// force. <see cref="AuthSession.Changed"/> is what makes them re-ask.
/// </para>
/// </summary>
public sealed class SessionDatasetCatalog : IDatasetCatalog, IDisposable
{
    private readonly IDatasetCatalog demo;
    private readonly AuthSession session;
    private readonly Func<IDatasetCatalog> createLive;
    private readonly Lock gate = new();

    private IDatasetCatalog? live;

    public SessionDatasetCatalog()
        : this(StubDatasetCatalog.Instance, AuthSession.Instance, () => new HttpDatasetCatalog())
    {
    }

    public SessionDatasetCatalog(IDatasetCatalog demo, AuthSession session, Func<IDatasetCatalog> createLive)
    {
        this.demo = demo;
        this.session = session;
        this.createLive = createLive;
        session.Changed += OnSessionChanged;
    }

    /// <summary>True when reads are going to the real service rather than the built-in demo data.</summary>
    public bool IsLive => session.IsSignedIn && DataFeedOptions.IsConfigured;

    /// <summary>Databases the live service could not read. Empty on demo data, which cannot fail.</summary>
    public IReadOnlyList<string> Warnings =>
        Current is HttpDatasetCatalog http ? http.Warnings : [];

    private IDatasetCatalog Current
    {
        get
        {
            if (!IsLive) return demo;
            lock (gate) return live ??= createLive();
        }
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAvailableDatasetsAsync() =>
        Current.GetAvailableDatasetsAsync();

    public Task<DatasetSchema> GetSchemaAsync(string dataset) => Current.GetSchemaAsync(dataset);

    public Task<DatasetSchema> GetSeriesAsync(string dataset, string xAxis) =>
        Current.GetSeriesAsync(dataset, xAxis);

    /// <summary>
    /// Drops the live catalogue on any session change. Its caches are keyed by dataset name and
    /// hold one account's view of the service, so carrying them across a sign-out would show the
    /// next person the last one's catalogue.
    /// </summary>
    private void OnSessionChanged()
    {
        IDatasetCatalog? discarded;
        lock (gate)
        {
            discarded = live;
            live = null;
        }
        (discarded as IDisposable)?.Dispose();
    }

    public void Dispose()
    {
        session.Changed -= OnSessionChanged;
        OnSessionChanged();
    }
}
