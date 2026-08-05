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

    private readonly DemoContent content;
    private readonly IDatasetCatalog catalog;
    private readonly Dictionary<int, UserControl> screens = [];
    private readonly ScaleTransform plateScale = new();
    private int activeIndex;

    public MainView() : this(DemoContent.Create())
    {
    }

    public MainView(DemoContent content) : this(content, StubDatasetCatalog.Instance)
    {
    }

    public MainView(DemoContent content, IDatasetCatalog catalog)
    {
        this.content = content;
        this.catalog = catalog;
        InitializeComponent();

        Plate.RenderTransform = plateScale;
        SizeChanged += (_, _) => ApplyPlateScale();

        // Build the network before any screen does. The graph is what computes the dashboard
        // figures, so whichever screen opens first must find them already derived rather than
        // showing the values they were declared with until someone visits NETW.
        _ = NetworkGraph.Instance;

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
        Session.Instance.Changed += OnSessionChanged;
        // The session is usually already starting by the time this view is attached, and it will
        // not announce that again — so the cover is put up from the phase as it stands.
        Boot.Follow(Session.Instance.Phase);
        SettingsFlyout.ToggleRequested += ToggleSettings;
        SettingsFlyout.SignInRequested += ShowConnect;
        ContactDialog.ShowRequested += ShowContact;
        // The tour card sits on a screen but moves between them, so the shell is what walks it on.
        TourGuide.NavigateRequested += SwitchTo;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ThemeManager.Changed -= RebuildScreens;
        TypographySettings.Changed -= RebuildScreens;
        UiScaleSettings.Changed -= ApplyPlateScale;
        Workspace.Instance.Changed -= InvalidateInactiveScreens;
        FigureCatalog.Instance.Changed -= InvalidateInactiveScreens;
        NetworkGraph.Instance.Changed -= InvalidateInactiveScreens;
        Session.Instance.Changed -= OnSessionChanged;
        SettingsFlyout.ToggleRequested -= ToggleSettings;
        SettingsFlyout.SignInRequested -= ShowConnect;
        ContactDialog.ShowRequested -= ShowContact;
        TourGuide.NavigateRequested -= SwitchTo;
        base.OnDetachedFromVisualTree(e);
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

    private UserControl CreateScreen(int index) => index switch
    {
        0 => new NetworkView(content, SwitchTo),
        1 => new TransferFunctionView(SwitchTo),
        2 => new DashboardView(SwitchTo),
        3 => new DbTreeView(content, SwitchTo),
        4 => new SiteMapView(content, SwitchTo),
        5 => new DataSourcesView(SwitchTo, catalog),
        _ => new CsvExportView(SwitchTo)
    };

    /// <summary>
    /// Throws every screen away and rebuilds the one on show. Called for a theme or type change,
    /// and for a session change — the session can turn over from the settings flyout on any screen,
    /// not just DATA SOURCES, and it replaces the catalogue every screen reads from.
    /// </summary>
    private void RebuildScreens()
    {
        screens.Clear();
        SwitchTo(activeIndex);
    }

    /// <summary>
    /// The cover follows the session, and the screens under it are rebuilt against whatever it has
    /// arrived at. Both in one handler because they are one event: the screens are only ever
    /// rebuilt where the cover is up, so the swap happens behind it rather than in front of the
    /// operator.
    /// </summary>
    private void OnSessionChanged()
    {
        Boot.Follow(Session.Instance.Phase);
        RebuildScreens();
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
