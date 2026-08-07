// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using Terrafa.Continuum.Frontend;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

[assembly: SupportedOSPlatform("browser")]

internal static class Program
{
    private static Task Main(string[] args)
    {
        AuthSession.Instance.Store = new LocalStorageSecretStore();
        SandboxAgent.Instance.KeyStore = new LocalStorageSandboxKeyStore();
        UserStateSync.Store = new HttpUserStateStore();
        UserStateSync.Start();
        // Start owns the restore, so the in-app loading screen covers it — the page's own boot
        // splash hands straight over to it rather than uncovering a demo-seeded plate.
        Session.Instance.Start();
        return BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    // No UsePlatformDetect: the browser backend is the only one linked in, and the fonts
    // have to be registered here exactly as the desktop head does it.
    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .With(AppFonts.Options);
}
