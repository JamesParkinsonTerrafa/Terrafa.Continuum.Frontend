using Avalonia.Controls;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class MainWindow : Window
{
    private readonly IDataFeed feed;
    private readonly List<UserControl> screens = [];
    private int activeIndex;

    public MainWindow() : this(new StaticDataFeed())
    {
    }

    public MainWindow(IDataFeed feed)
    {
        this.feed = feed;
        InitializeComponent();
        BuildScreens();
        SwitchTo(0);
        ThemeManager.Changed += RebuildScreens;
        Closed += (_, _) => ThemeManager.Changed -= RebuildScreens;
    }

    private void BuildScreens()
    {
        screens.Clear();
        var snapshot = feed.Current;
        screens.Add(new NetworkView(snapshot, SwitchTo));
        screens.Add(new TransferFunctionView(snapshot, SwitchTo));
        screens.Add(new DashboardView(snapshot, SwitchTo));
        screens.Add(new DbTreeView(snapshot, SwitchTo));
        screens.Add(new SiteMapView(snapshot, SwitchTo));
    }

    private void RebuildScreens()
    {
        BuildScreens();
        SwitchTo(activeIndex);
    }

    private void SwitchTo(int index)
    {
        if (index < 0 || index >= screens.Count) return;
        activeIndex = index;
        ViewHost.Content = screens[index];
    }
}
