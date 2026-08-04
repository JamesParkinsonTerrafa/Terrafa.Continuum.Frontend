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
        var probeIndex = Array.IndexOf(args, "--probe");
        if (probeIndex >= 0 && probeIndex + 1 < args.Length)
        {
            DataProbe.RunAsync(args[probeIndex + 1]).GetAwaiter().GetResult();
            return;
        }

        // The restore runs concurrently with startup, and deliberately does not have to win: if the
        // stored token lands first, Session.Start finds an identity and establishes it; if it lands
        // after, AuthSession.Changed brings it through the same transition. Both converge on the
        // same state, which is the point of putting reset, load and read in one method.
        // Snapshot runs are excluded above, so screenshots stay on seeded state.
        AuthSession.Instance.Store = new KeychainSecretStore();
        UserStateSync.Store = new HttpUserStateStore();
        UserStateSync.Start();
        Session.Instance.Start();
        _ = AuthSession.Instance.TryRestoreAsync();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(AppFonts.Options)
            .LogToTrace();
}
