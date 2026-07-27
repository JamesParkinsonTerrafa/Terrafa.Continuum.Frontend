using Avalonia.Controls;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Views;

/// <summary>
/// Desktop shell. Everything the app does lives in <see cref="MainView"/>; this only
/// supplies the window the browser head does not have.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainView root;

    public MainWindow() : this(new StaticDataFeed())
    {
    }

    public MainWindow(IDataFeed feed)
    {
        InitializeComponent();
        root = new MainView(feed);
        Content = root;
    }

    // Reached by SnapshotRunner in the desktop head, which drives the real controls.
    internal ContentControl ViewHost => root.ViewHost;

    internal SettingsFlyout Settings => root.Settings;

    internal ContactDialog Contact => root.Contact;
}
