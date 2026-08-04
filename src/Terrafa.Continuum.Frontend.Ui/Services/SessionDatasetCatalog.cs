// Copyright (c) 2026 Terrafa Limited. All rights reserved.

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// The catalogue the app actually holds: demo data until someone signs in, the real service after.
///
/// <para>
/// The swap lives here rather than in the screens so that no screen has to know a sign-in
/// happened — they ask the same object either way and get whichever catalogue is currently in
/// force. <see cref="AuthSession.Changed"/> is what makes it swap, and that event fires only when
/// the signed-in identity actually changes, so a token renewal cannot drop a warm cache.
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
        session.Changed += OnIdentityChanged;
    }

    public bool IsLive => session.IsSignedIn && DataFeedOptions.IsConfigured;

    public IReadOnlyList<string> Warnings => Current.Warnings;

    private IDatasetCatalog Current
    {
        get
        {
            if (!IsLive) return demo;
            lock (gate) return live ??= createLive();
        }
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAvailableDatasetsAsync(
        CancellationToken cancellationToken = default) =>
        Current.GetAvailableDatasetsAsync(cancellationToken);

    public Task<DatasetSchema> GetSchemaAsync(string dataset, CancellationToken cancellationToken = default) =>
        Current.GetSchemaAsync(dataset, cancellationToken);

    public Task<DatasetSchema> GetSeriesAsync(DatasetQuery query, CancellationToken cancellationToken = default) =>
        Current.GetSeriesAsync(query, cancellationToken);

    /// <summary>
    /// Drops the live catalogue when the identity changes. Its caches are keyed by dataset name and
    /// hold one account's view of the service, so carrying them across a sign-out would show the
    /// next person the last one's catalogue.
    ///
    /// <para>
    /// The reference is dropped, not disposed. A read already in flight against the old catalogue
    /// runs to completion and is discarded by whoever asked for it; tearing its transport down
    /// mid-request instead turned a good response into an <see cref="ObjectDisposedException"/>
    /// that the read path then swallowed as a failure to read.
    /// </para>
    /// </summary>
    private void OnIdentityChanged()
    {
        lock (gate) live = null;
    }

    public void Dispose()
    {
        session.Changed -= OnIdentityChanged;
        OnIdentityChanged();
    }
}
