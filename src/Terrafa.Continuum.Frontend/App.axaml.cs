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
        Palette.RegisterResources(Resources);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            IDataFeed feed = new StaticDataFeed();
            desktop.MainWindow = new MainWindow(feed);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
