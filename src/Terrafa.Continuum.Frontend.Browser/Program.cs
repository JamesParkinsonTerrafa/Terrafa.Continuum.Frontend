// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using Terrafa.Continuum.Frontend;
using Terrafa.Continuum.Frontend.Themes;

[assembly: SupportedOSPlatform("browser")]

internal static class Program
{
    private static Task Main(string[] args) =>
        BuildAvaloniaApp().StartBrowserAppAsync("out");

    // No UsePlatformDetect: the browser backend is the only one linked in, and the fonts
    // have to be registered here exactly as the desktop head does it.
    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .With(AppFonts.Options);
}
