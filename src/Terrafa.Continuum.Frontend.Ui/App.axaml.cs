// Copyright (c) 2026 Terrafa Limited. All rights reserved.

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
        TypographySettings.RegisterResources(Resources);
        HintSettings.RegisterResources(Resources);
        BuilderModeSettings.RegisterResources(Resources);
        ButtonSettings.RegisterResources(Resources);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var content = DemoContent.Create();

        // Demo data until someone signs in, the real service after. The session owns it, so the
        // screens, the restore and the probe all read through the one object rather than each
        // reaching for their own and disagreeing about which is in force.
        var catalog = Session.Instance.Catalog;

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow(content, catalog);
                break;
            // The browser has no windows, only a root control on the page.
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new MainView(content, catalog);
                break;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
