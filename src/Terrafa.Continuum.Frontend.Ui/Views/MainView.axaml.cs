// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
    private const int ScreenCount = 7;

    /// <summary>
    /// The screen indices <see cref="CreateScreen"/> builds, named where anything outside this
    /// class has to say which one it means. These are build indices, not strip positions — the
    /// nav order is the operator's to rearrange and says nothing about what a screen is.
    /// </summary>
    public const int NetworkScreen = 0;

    public const int MapScreen = 4;

    /// <summary>The screen the app opens on.</summary>
    public const int LandingScreen = MapScreen;

    private const double PlateWidth = 1560;
    private const double PlateHeight = 980;

    /// <summary>
    /// Where the window fit stops growing the plate. Past this the extra space stays empty and
    /// the plate holds to the top-left — that is the "resize within limits" contract.
    /// </summary>
    private const double MaxFitScale = 1.6;

    /// <summary>Floor on the drawn scale, so a tiny window clips the plate rather than pulping it.</summary>
    private const double MinDrawnScale = 0.45;

    private readonly IDataFeed feed;
    private readonly IDatasetCatalog catalog;
    private readonly Dictionary<int, UserControl> screens = [];
    private readonly ScaleTransform plateScale = new();
    private int activeIndex;
    private string? sessionIdentity;

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

        Plate.RenderTransform = plateScale;
        SizeChanged += (_, _) => ApplyPlateScale();

        // Build the network before any screen does. The graph is what computes the dashboard
        // figures, so whichever screen opens first must find them already derived rather than
        // showing the values they were declared with until someone visits NETW.
        _ = NetworkGraph.Instance;

        // Warm the catalogue while the first screen builds — DATA SOURCES then opens populated.
        // Signed out there is no live catalogue to warm, so the prefetch waits for sign-in;
        // OnSessionChanged fires it once the session arrives.
        if (AuthSession.Instance.IsSignedIn) _ = WarmCatalogue();

        SwitchTo(LandingScreen);
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
        TypographySettings.Changed += RebuildScreens;
        UiScaleSettings.Changed += ApplyPlateScale;
        ApplyPlateScale();
        Workspace.Instance.Changed += InvalidateInactiveScreens;
        FigureCatalog.Instance.Changed += InvalidateInactiveScreens;
        NetworkGraph.Instance.Changed += InvalidateInactiveScreens;
        sessionIdentity = CurrentSessionIdentity();
        AuthSession.Instance.Changed += OnSessionChanged;
        SettingsFlyout.ToggleRequested += ToggleSettings;
        SettingsFlyout.SignInRequested += ShowConnect;
        ContactDialog.ShowRequested += ShowContact;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ThemeManager.Changed -= RebuildScreens;
        TypographySettings.Changed -= RebuildScreens;
        UiScaleSettings.Changed -= ApplyPlateScale;
        Workspace.Instance.Changed -= InvalidateInactiveScreens;
        FigureCatalog.Instance.Changed -= InvalidateInactiveScreens;
        NetworkGraph.Instance.Changed -= InvalidateInactiveScreens;
        AuthSession.Instance.Changed -= OnSessionChanged;
        SettingsFlyout.ToggleRequested -= ToggleSettings;
        SettingsFlyout.SignInRequested -= ShowConnect;
        ContactDialog.ShowRequested -= ShowContact;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Signing in or out swaps the whole catalogue underneath the app, so one account's mounts,
    /// figures and readings must not survive into the next session — the workspace, network and
    /// board all reset. Everything resets to the demo seed in both directions, so a signed-in restore
    /// applies over the same state it would find at startup — ApplyWorkspaceAsync's KeepExisting
    /// fallback needs the demo mount still present to keep saved tiles wired to it alive, and an
    /// account with nothing saved lands on the seeded demo rather than a blank board. Every screen
    /// is rebuilt, the one on show included — the session can change from the settings flyout over
    /// any screen, not just DATA SOURCES.
    ///
    /// <para>
    /// Only a change of identity resets. The session raises Changed for a token restore and a
    /// renewal too, and those arrive after the restored documents have been applied — resetting on
    /// one wipes the operator's work back to the seed and saves the seed over it.
    /// </para>
    /// </summary>
    private void OnSessionChanged()
    {
        var identity = CurrentSessionIdentity();
        if (identity == sessionIdentity) return;
        sessionIdentity = identity;

        Workspace.Instance.Reset(seedDemo: true);
        NetworkGraph.Instance.Reset(seedDemo: true);
        Dashboard.Instance.Reset(seedDemo: true);
        RebuildScreens();
        if (AuthSession.Instance.IsSignedIn) _ = WarmCatalogue();
    }

    private static string? CurrentSessionIdentity() =>
        AuthSession.Instance.IsSignedIn ? AuthSession.Instance.Username ?? "" : null;

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

    /// <summary>
    /// The plate's drawn scale: the window fit, capped so a big window pins the plate to the
    /// top-left instead of inflating it without end, times the operator's UI SCALE setting.
    /// </summary>
    private void ApplyPlateScale()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var fit = Math.Min(Bounds.Width / PlateWidth, Bounds.Height / PlateHeight);
        var scale = Math.Max(Math.Min(fit, MaxFitScale) * UiScaleSettings.Scale, MinDrawnScale);
        plateScale.ScaleX = scale;
        plateScale.ScaleY = scale;
    }

    /// <summary>
    /// Opens a screen by build index. The nav keys go through the same path, so a caller that
    /// needs a particular screen — SnapshotRunner drives several that only exist on NETWORK —
    /// asks for it rather than assuming where the app happens to land.
    /// </summary>
    internal void ShowScreen(int index) => SwitchTo(index);

    private void ToggleSettings() => Settings.Toggle();

    private void ShowConnect()
    {
        Settings.Hide();
        ConnectDataFlow.Show(ConnectDialog, AuthSession.Instance, () => { });
    }

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
            5 => new DataSourcesView(snapshot, SwitchTo, catalog),
            _ => new CsvExportView(snapshot, SwitchTo)
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
