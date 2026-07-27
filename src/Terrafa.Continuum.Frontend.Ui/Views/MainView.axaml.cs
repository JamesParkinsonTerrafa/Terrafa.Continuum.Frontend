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

        // Warm the catalogue while the first screen builds — DATA SOURCES then opens populated.
        _ = catalog.GetAvailableDatasetsAsync();

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
        SettingsFlyout.ToggleRequested += ToggleSettings;
        ContactDialog.ShowRequested += ShowContact;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ThemeManager.Changed -= RebuildScreens;
        Workspace.Instance.Changed -= InvalidateInactiveScreens;
        SettingsFlyout.ToggleRequested -= ToggleSettings;
        ContactDialog.ShowRequested -= ShowContact;
        base.OnDetachedFromVisualTree(e);
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
