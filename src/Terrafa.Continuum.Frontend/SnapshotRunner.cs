using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;
using Terrafa.Continuum.Frontend.Views;

namespace Terrafa.Continuum.Frontend;

public static class SnapshotRunner
{
    public static void Run(string outputDir)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        Directory.CreateDirectory(outputDir);
        var snapshot = new StaticDataFeed().Current;

        CaptureAllViews(outputDir, snapshot, "");
        ThemeManager.SetLight(true);
        CaptureAllViews(outputDir, snapshot, "-light");
        ThemeManager.SetLight(false);
    }

    private static void CaptureAllViews(string outputDir, DataSnapshot snapshot, string suffix)
    {
        var views = new (string Name, UserControl View)[]
        {
            ("1-netw", new NetworkView(snapshot, _ => { })),
            ("2-tfn", new TransferFunctionView(snapshot, _ => { })),
            ("3-dash", new DashboardView(snapshot, _ => { })),
            ("4-tree", new DbTreeView(snapshot, _ => { })),
            ("5-map", new SiteMapView(snapshot, _ => { }))
        };

        foreach (var (name, view) in views)
        {
            var window = new Window
            {
                Width = 1560,
                Height = 980,
                SystemDecorations = SystemDecorations.None,
                Content = view
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            using var frame = window.CaptureRenderedFrame();
            if (frame is null)
            {
                Console.Error.WriteLine($"snapshot {name}{suffix}: no frame captured");
            }
            else
            {
                frame.Save(Path.Combine(outputDir, $"{name}{suffix}.png"));
                Console.WriteLine($"snapshot {name}{suffix}: saved");
            }
            window.Close();
        }
    }
}
