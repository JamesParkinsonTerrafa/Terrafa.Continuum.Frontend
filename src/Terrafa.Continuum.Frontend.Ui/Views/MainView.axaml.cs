using Avalonia;
using Avalonia.Controls;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

/// <summary>
/// The application, minus any window. Both heads host this: the desktop one wraps it in a
/// <see cref="MainWindow"/>, the browser one hands it straight to the single-view lifetime.
/// </summary>
public partial class MainView : UserControl
{
    private readonly IDataFeed feed;
    private readonly List<UserControl> screens = [];
    private int activeIndex;

    public MainView() : this(new StaticDataFeed())
    {
    }

    public MainView(IDataFeed feed)
    {
        this.feed = feed;
        InitializeComponent();
        BuildScreens();
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
        SettingsFlyout.ToggleRequested += ToggleSettings;
        ContactDialog.ShowRequested += ShowContact;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ThemeManager.Changed -= RebuildScreens;
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
