using Avalonia;

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
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
