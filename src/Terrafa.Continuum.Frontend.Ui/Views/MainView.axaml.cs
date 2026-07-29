using Avalonia;
using Avalonia.Controls;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

/// <summary>
/// The application, minus any window. Both heads host this: the desktop one wraps it in a
/// <see cref="MainWindow"/>, the browser one hands it straight to the single-view lifetime.
/// </summary>
public partial class MainView : UserControl
{
    private const int ScreenCount = 6;

    private readonly IDataFeed feed;
    private readonly IDatasetCatalog catalog;
    private readonly Dictionary<int, UserControl> screens = [];
    private int activeIndex;

    public MainView() : this(new StaticDataFeed())
    {
    }

    public MainView(IDataFeed feed) : this(feed, StubDatasetCatalog.Instance)
    {
    }

    public MainView(IDataFeed feed, IDatasetCatalog catalog)
    {
        this.feed = feed;
        this.catalog = catalog;
        InitializeComponent();

        // Build the network before any screen does. The graph is what computes the dashboard
        // figures, so whichever screen opens first must find them already derived rather than
        // showing the values they were declared with until someone visits NETW.
        _ = NetworkGraph.Instance;

        // Warm the catalogue while the first screen builds — DATA SOURCES then opens populated.
        _ = WarmCatalogue();

        SwitchTo(0);
        AddHandler(
            Avalonia.Input.InputElement.PointerPressedEvent,
            (_, e) => Console.WriteLine(
                $"[diag] press at {e.GetPosition(this)} source={e.Source?.GetType().Name}"),
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    // These replace the Window.Closed teardown the desktop shell used to do. They must stay
    // symmetric: the browser attaches and detaches this view while laying out the page, so
    // subscribing once in the constructor and unsubscribing on the first detach silently
    // killed the settings flyout and contact dialog — both are raised through static events.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeManager.Changed += RebuildScreens;
        Workspace.Instance.Changed += InvalidateInactiveScreens;
        FigureCatalog.Instance.Changed += InvalidateInactiveScreens;
        NetworkGraph.Instance.Changed += InvalidateInactiveScreens;
        AuthSession.Instance.Changed += OnSessionChanged;
        SettingsFlyout.ToggleRequested += ToggleSettings;
        ContactDialog.ShowRequested += ShowContact;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ThemeManager.Changed -= RebuildScreens;
        Workspace.Instance.Changed -= InvalidateInactiveScreens;
        FigureCatalog.Instance.Changed -= InvalidateInactiveScreens;
        NetworkGraph.Instance.Changed -= InvalidateInactiveScreens;
        AuthSession.Instance.Changed -= OnSessionChanged;
        SettingsFlyout.ToggleRequested -= ToggleSettings;
        ContactDialog.ShowRequested -= ShowContact;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Signing in or out swaps the whole catalogue underneath the app, so the workspace is emptied
    /// with it — a subtree mounted from the demo catalogue is meaningless against a real one, and
    /// vice versa. The network and the board go with it: both are built out of leaves that are
    /// about to stop existing, and a figure derived from the demo tree must not survive into a real
    /// session. Resetting raises Changed, which drops the screens that are not on show; the DATA
    /// SOURCES screen refreshes itself, since that is where the change was made.
    /// </summary>
    private void OnSessionChanged()
    {
        var seedDemo = !AuthSession.Instance.IsSignedIn;
        Workspace.Instance.Reset(seedDemo);
        NetworkGraph.Instance.Reset(seedDemo);
        Dashboard.Instance.Reset(seedDemo);
    }

    /// <summary>
    /// Prefetch only. A failure is deliberately dropped here: DataSourcesView makes the same call
    /// and reports it on screen, and leaving it unobserved on a discarded task would instead
    /// surface as an UnobservedTaskException from the finaliser thread.
    /// </summary>
    private async Task WarmCatalogue()
    {
        try
        {
            await catalog.GetAvailableDatasetsAsync();
        }
        catch (Exception)
        {
            // Reported by the screen that needs it.
        }
    }

    private void ToggleSettings() => Settings.Toggle();

    private void ShowContact()
    {
        Settings.Hide();
        Contact.Show();
    }

    private UserControl CreateScreen(int index)
    {
        var snapshot = feed.Current;
        return index switch
        {
            0 => new NetworkView(snapshot, SwitchTo),
            1 => new TransferFunctionView(snapshot, SwitchTo),
            2 => new DashboardView(snapshot, SwitchTo),
            3 => new DbTreeView(snapshot, SwitchTo),
            4 => new SiteMapView(snapshot, SwitchTo),
            _ => new DataSourcesView(snapshot, SwitchTo, catalog)
        };
    }

    private void RebuildScreens()
    {
        screens.Clear();
        SwitchTo(activeIndex);
    }

    /// <summary>A mount or link changes what every other screen shows — drop them so they rebuild on entry.</summary>
    private void InvalidateInactiveScreens()
    {
        foreach (var index in screens.Keys.Where(index => index != activeIndex).ToList())
            screens.Remove(index);
    }

    private void SwitchTo(int index)
    {
        if (index < 0 || index >= ScreenCount) return;
        if (!screens.TryGetValue(index, out var screen))
        {
            screen = CreateScreen(index);
            screens[index] = screen;
        }
        activeIndex = index;
        ViewHost.Content = screen;
    }
}
