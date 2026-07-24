using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Controls.Diagram;
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
        CaptureInteractionProbe(outputDir);
        CaptureTransferFunctionProbe(outputDir);
        ThemeManager.SetLight(true);
        CaptureAllViews(outputDir, snapshot, "-light");
        ThemeManager.SetLight(false);
        CaptureSettingsProbe(outputDir);
    }

    private static void CaptureSettingsProbe(string outputDir)
    {
        var window = new MainWindow(new StaticDataFeed())
        {
            Width = 1280,
            Height = 840,
            SystemDecorations = SystemDecorations.None
        };
        window.Show();
        Pump();

        var topBar = window.GetVisualDescendants().OfType<TerminalTopBar>().First();
        var button = topBar.SettingsButton;
        var buttonPoint = button.TranslatePoint(
            new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(buttonPoint, MouseButton.Left);
        window.MouseUp(buttonPoint, MouseButton.Left);
        Pump();

        var flyout = window.Settings;
        var grainRow = flyout.GrainToggleRow;
        var grainPoint = grainRow.TranslatePoint(
            new Point(grainRow.Bounds.Width / 2, grainRow.Bounds.Height / 2), window)!.Value;
        window.MouseDown(grainPoint, MouseButton.Left);
        window.MouseUp(grainPoint, MouseButton.Left);
        Pump();

        flyout.IntensitySlider.Value = 24;
        flyout.SlopeSlider.Value = 0.8;
        flyout.WarpSlider.Value = 34;
        NoiseOverlay.RebuildNow();
        Pump();

        using var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine("snapshot 0-settings: no frame captured");
        }
        else
        {
            frame.Save(Path.Combine(outputDir, "0-settings.png"));
            Console.WriteLine("snapshot 0-settings: saved");
        }
        window.Close();
    }

    private static void CaptureInteractionProbe(string outputDir)
    {
        var window = new MainWindow(new StaticDataFeed())
        {
            Width = 1280,
            Height = 840,
            SystemDecorations = SystemDecorations.None
        };
        window.Show();
        Pump();

        var view = (NetworkView)window.ViewHost.Content!;
        var diagram = view.Diagram;
        Point ToWindow(Point worldPoint) =>
            diagram.TranslatePoint(diagram.WorldToViewport(worldPoint), window)!.Value;

        var leafLevel01 = diagram.Nodes.First(node => node.Id.EndsWith("tank_01.level"));
        var leafLevel02 = diagram.Nodes.First(node => node.Id.EndsWith("tank_02.level"));
        var transfer1 = diagram.Nodes.First(node => node.Id == "transfer:t1");
        var transfer2 = diagram.Nodes.First(node => node.Id == "transfer:t2");

        var flowShell = view.MeasureList.Children.OfType<Border>()
            .First(shell => shell.Child is DockPanel row &&
                row.Children.OfType<StackPanel>().Any(label =>
                    label.Children.OfType<TextBlock>().Any(text => text.Text == "flow")));
        var railStart = flowShell.TranslatePoint(
            new Point(flowShell.Bounds.Width / 2, flowShell.Bounds.Height / 2), window)!.Value;
        var railDrop = ToWindow(new Point(680, 700));
        window.MouseDown(railStart, MouseButton.Left);
        window.MouseMove(new Point(railStart.X + 40, railStart.Y + 30));
        window.MouseMove(railDrop);
        window.MouseUp(railDrop, MouseButton.Left);
        Pump();

        var dragStart = ToWindow(diagram.NodeCenter(leafLevel01));
        var dragEnd = new Point(dragStart.X + 60, dragStart.Y + 45);
        window.MouseDown(dragStart, MouseButton.Left);
        window.MouseMove(new Point(dragStart.X + 30, dragStart.Y + 20));
        window.MouseMove(dragEnd);
        window.MouseUp(dragEnd, MouseButton.Left);
        Pump();

        var connectFrom = ToWindow(diagram.PortAnchor(leafLevel02, PortSide.Right)) + new Point(-3, 0);
        var connectTo = ToWindow(diagram.PortAnchor(transfer2, PortSide.Left)) + new Point(3, 0);
        window.MouseDown(connectFrom, MouseButton.Left);
        window.MouseMove(new Point((connectFrom.X + connectTo.X) / 2, (connectFrom.Y + connectTo.Y) / 2));
        window.MouseMove(connectTo);
        window.MouseUp(connectTo, MouseButton.Left);
        Pump();

        var panStart = ToWindow(new Point(1150, 780));
        var panEnd = new Point(panStart.X - 80, panStart.Y - 55);
        window.MouseDown(panStart, MouseButton.Left);
        window.MouseMove(new Point(panStart.X - 40, panStart.Y - 30));
        window.MouseMove(panEnd);
        window.MouseUp(panEnd, MouseButton.Left);
        Pump();

        var menuPoint = ToWindow(diagram.NodeCenter(transfer1));
        window.MouseDown(menuPoint, MouseButton.Right);
        window.MouseUp(menuPoint, MouseButton.Right);
        Pump();

        using var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine("snapshot 1-netw-interact: no frame captured");
        }
        else
        {
            frame.Save(Path.Combine(outputDir, "1-netw-interact.png"));
            Console.WriteLine("snapshot 1-netw-interact: saved");
        }
        window.Close();
    }

    private static void CaptureTransferFunctionProbe(string outputDir)
    {
        var view = new TransferFunctionView(new StaticDataFeed().Current, _ => { });
        var window = new Window
        {
            Width = 1560,
            Height = 980,
            SystemDecorations = SystemDecorations.None,
            Content = view
        };
        window.Show();
        Pump();

        Point Center(Visual visual) =>
            visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window)!.Value;

        void Click(Point point)
        {
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Pump();
        }

        Border LibraryEntry(string name)
        {
            var entry = view.LibraryList.Children.OfType<Border>()
                .First(candidate => candidate.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text?.StartsWith($"{name}:") == true));
            entry.BringIntoView();
            Pump();
            return entry;
        }

        void OpenCreateFunctionMenu()
        {
            var libraryPoint = Center(view.LibraryList);
            window.MouseDown(libraryPoint, MouseButton.Right);
            window.MouseUp(libraryPoint, MouseButton.Right);
            Pump();
            var createItem = view.Overlay.GetVisualDescendants().OfType<TextBlock>()
                .First(text => text.Text == "CREATE FUNCTION");
            Click(Center(createItem));
        }

        OpenCreateFunctionMenu();
        Console.WriteLine($"tfn probe: blank stack has {view.StageRows.Count} stages");

        Click(Center(LibraryEntry("exp")));
        Click(Center(LibraryEntry("sum")));
        Console.WriteLine($"tfn probe: after adds, {view.StageRows.Count} stages");

        var secondRow = view.StageRows[1];
        var reorderStart = Center(secondRow);
        var reorderEnd = new Point(reorderStart.X, reorderStart.Y - 110);
        window.MouseDown(reorderStart, MouseButton.Left);
        window.MouseMove(new Point(reorderStart.X, reorderStart.Y - 55));
        window.MouseMove(reorderEnd);
        window.MouseUp(reorderEnd, MouseButton.Left);
        Pump();
        Console.WriteLine($"tfn probe: after reorder, {view.StackFooter.Text}");

        Click(Center(view.SaveButton));
        Console.WriteLine($"tfn probe: library entries after save = {view.LibraryList.Children.Count}");

        OpenCreateFunctionMenu();
        Click(Center(LibraryEntry("fn_1")));
        Console.WriteLine($"tfn probe: after composite add, {view.StackFooter.Text}");

        using var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine("snapshot 2-tfn-interact: no frame captured");
        }
        else
        {
            frame.Save(Path.Combine(outputDir, "2-tfn-interact.png"));
            Console.WriteLine("snapshot 2-tfn-interact: saved");
        }
        window.Close();
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
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
