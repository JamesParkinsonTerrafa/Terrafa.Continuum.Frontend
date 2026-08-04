// Copyright (c) 2026 Terrafa Limited. All rights reserved.

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

    // Leaves the catalogue on the stub — the constructor SnapshotRunner uses, where a render must
    // not depend on a service being reachable.
    public MainWindow(IDataFeed feed) : this(feed, StubDatasetCatalog.Instance)
    {
    }

    public MainWindow(IDataFeed feed, IDatasetCatalog catalog)
    {
        InitializeComponent();
        root = new MainView(feed, catalog);
        Content = root;
    }

    // Reached by SnapshotRunner in the desktop head, which drives the real controls.
    internal ContentControl ViewHost => root.ViewHost;

    internal void ShowScreen(int index) => root.ShowScreen(index);

    internal SettingsFlyout Settings => root.Settings;

    internal ContactDialog Contact => root.Contact;
}
