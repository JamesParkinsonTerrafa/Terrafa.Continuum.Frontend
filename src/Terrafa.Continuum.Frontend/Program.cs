// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var snapshotIndex = Array.IndexOf(args, "--snapshot");
        if (snapshotIndex >= 0 && snapshotIndex + 1 < args.Length)
        {
            SnapshotRunner.Run(args[snapshotIndex + 1]);
            return;
        }
        // Restore runs concurrently with startup: MainView rebuilds on AuthSession.Changed, so a
        // session that lands after the first frame swaps demo data for live just like a sign-in.
        // Snapshot runs are excluded above so screenshots stay deterministic.
        AuthSession.Instance.Store = new KeychainSecretStore();
        UserStateSync.Store = new HttpUserStateStore();
        UserStateSync.Start();
        _ = AuthSession.Instance.TryRestoreAsync();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(AppFonts.Options)
            .LogToTrace();
}
