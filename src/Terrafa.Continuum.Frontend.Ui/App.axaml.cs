using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;
using Terrafa.Continuum.Frontend.Views;

namespace Terrafa.Continuum.Frontend;

public class App : Application
{
    public override void Initialize()
    {
        ThemeManager.Initialize(Resources);
        HintSettings.RegisterResources(Resources);
        ButtonSettings.RegisterResources(Resources);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        IDataFeed feed = new StaticDataFeed();

        // Demo data until someone signs in on the DATA SOURCES screen, the real service after.
        // The object is the same either way, so nothing downstream has to know which is in force.
        IDatasetCatalog catalog = new SessionDatasetCatalog();

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow(feed, catalog);
                break;
            // The browser has no windows, only a root control on the page.
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new MainView(feed, catalog);
                break;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
