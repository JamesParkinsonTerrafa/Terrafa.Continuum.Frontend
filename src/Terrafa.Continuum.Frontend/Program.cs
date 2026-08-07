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

        // Session.Start owns the restore: it holds the session in Starting until the stored token
        // has answered, so the shell shows one loading screen instead of painting the demo seed and
        // then replacing it. Snapshot runs are excluded above, so screenshots stay on seeded state.
        AuthSession.Instance.Store = new KeychainSecretStore();
        SandboxAgent.Instance.KeyStore = new KeychainSandboxKeyStore();
        UserStateSync.Store = new HttpUserStateStore();
        UserStateSync.Start();
        Session.Instance.Start();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(AppFonts.Options)
            .LogToTrace();
}
