using Avalonia.Controls;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class MainWindow : Window
{
    private const int ScreenCount = 6;

    private readonly IDataFeed feed;
    private readonly IDatasetCatalog catalog;
    private readonly Dictionary<int, UserControl> screens = [];
    private int activeIndex;

    public MainWindow() : this(new StaticDataFeed())
    {
    }

    public MainWindow(IDataFeed feed) : this(feed, StubDatasetCatalog.Instance)
    {
    }

    public MainWindow(IDataFeed feed, IDatasetCatalog catalog)
    {
        this.feed = feed;
        this.catalog = catalog;
        InitializeComponent();

        // Warm the catalogue while the first screen builds — DATA SOURCES then opens populated.
        _ = catalog.GetAvailableDatasetsAsync();

        SwitchTo(0);
        ThemeManager.Changed += RebuildScreens;
        Workspace.Instance.Changed += InvalidateInactiveScreens;
        SettingsFlyout.ToggleRequested += ToggleSettings;
        ContactDialog.ShowRequested += ShowContact;
        Closed += (_, _) =>
        {
            ThemeManager.Changed -= RebuildScreens;
            Workspace.Instance.Changed -= InvalidateInactiveScreens;
            SettingsFlyout.ToggleRequested -= ToggleSettings;
            ContactDialog.ShowRequested -= ShowContact;
        };
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
