using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Controls.Diagram;
using Terrafa.Continuum.Frontend.Models;
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
            .With(AppFonts.Options)
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        Directory.CreateDirectory(outputDir);
        var snapshot = new StaticDataFeed().Current;

        CaptureAllViews(outputDir, snapshot, "");
        CaptureInteractionProbe(outputDir);
        CaptureTransferFunctionProbe(outputDir);
        CaptureMapProbe(outputDir);
        CaptureMapUploadProbe(outputDir);
        CaptureDashboardProbe(outputDir);
        HintSettings.SetEnabled(false);
        CaptureAllViews(outputDir, snapshot, "-nohints");
        HintSettings.SetEnabled(true);
        ThemeManager.SetLight(false);
        CaptureAllViews(outputDir, snapshot, "-dark");
        ThemeManager.SetLight(true);
        CaptureSettingsProbe(outputDir);
        CaptureContactProbe(outputDir);

        // Mount a second dataset last — it mutates the shared workspace the earlier captures rely on.
        CaptureDataSourcesProbe(outputDir, snapshot);
        CaptureTreeLinkProbe(outputDir, snapshot);
    }

    private static void CaptureDataSourcesProbe(string outputDir, DataSnapshot snapshot)
    {
        var view = new DataSourcesView(snapshot, _ => { });
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

        view.SearchBox.Text = "brnt";
        Pump();
        Console.WriteLine($"data probe: fuzzy 'brnt' → {view.CatalogueList.Children.Count} rows, " +
                          $"hint '{view.CataloguePanel.Hint}'");
        view.SearchBox.Text = "";
        Pump();

        var datasetRow = RowContaining(view.CatalogueList, "ICE_BRENT");
        var rowPoint = Center(datasetRow);
        window.MouseDown(rowPoint, MouseButton.Left);
        window.MouseUp(rowPoint, MouseButton.Left);
        window.MouseDown(rowPoint, MouseButton.Left);
        window.MouseUp(rowPoint, MouseButton.Left);
        Pump();
        Console.WriteLine($"data probe: preview rows = {view.PreviewRows.Children.Count}");

        var schemaRoot = RowContaining(view.PreviewRows, "ICE_BRENT /");
        var schemaPoint = Center(schemaRoot);
        window.MouseDown(schemaPoint, MouseButton.Right);
        window.MouseUp(schemaPoint, MouseButton.Right);
        Pump();

        ClickText(window, view.MenuLayer, "ADD TO TREE");
        Console.WriteLine($"data probe: dialog open = {view.Dialog.IsVisible}");
        ClickText(window, view.Dialog, "ADD <GO>");
        Console.WriteLine($"data probe: subtrees mounted = {Workspace.Instance.Subtrees.Count}");

        Save(window, outputDir, "6-data-interact");
        window.Close();
    }

    private static void CaptureTreeLinkProbe(string outputDir, DataSnapshot snapshot)
    {
        var view = new DbTreeView(snapshot, _ => { });
        var window = new Window
        {
            Width = 1560,
            Height = 980,
            SystemDecorations = SystemDecorations.None,
            Content = view
        };
        window.Show();
        Pump();

        var card = window.GetVisualDescendants().OfType<NodeCard>().First(node => node.Title == "m1_settle");
        // The embedded fonts lay the tree out taller than the viewport, so this card sits below
        // the fold — clicking its untranslated position would hit the status bar instead.
        card.BringIntoView();
        Pump();
        var cardPoint = card.TranslatePoint(new Point(card.Bounds.Width / 2, 12), window)!.Value;
        window.MouseDown(cardPoint, MouseButton.Right);
        window.MouseUp(cardPoint, MouseButton.Right);
        Pump();

        ClickText(window, view.MenuLayer, "LINK TO…");
        ClickText(window, view.Dialog, "SITE_ALPHA.tank_farm.tank_01.grade @ intake", startsWith: true);
        ClickText(window, view.Dialog, "LINK <GO>");
        Console.WriteLine($"tree probe: links = {Workspace.Instance.Links.Count}");

        Save(window, outputDir, "4-tree-interact");
        window.Close();
    }

    private static Border RowContaining(Panel host, string text) =>
        host.Children.OfType<Border>().First(row =>
            row.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == text));

    private static void ClickText(Window window, Visual host, string text, bool startsWith = false)
    {
        var block = host.GetVisualDescendants().OfType<TextBlock>()
            .First(candidate => startsWith
                ? candidate.Text?.StartsWith(text, StringComparison.Ordinal) == true
                : candidate.Text == text);
        block.BringIntoView();
        Pump();
        var point = block.TranslatePoint(new Point(block.Bounds.Width / 2, block.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Pump();
    }

    private static void Save(Window window, string outputDir, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine($"snapshot {name}: no frame captured");
            return;
        }
        frame.Save(Path.Combine(outputDir, $"{name}.png"));
        Console.WriteLine($"snapshot {name}: saved");
    }

    private static void CaptureContactProbe(string outputDir)
    {
        var window = new MainWindow(new StaticDataFeed())
        {
            Width = 1280,
            Height = 840,
            SystemDecorations = SystemDecorations.None
        };
        window.Show();
        Pump();

        var brand = window.GetVisualDescendants().OfType<TerminalTopBar>().First().BrandButton;
        var brandPoint = brand.TranslatePoint(
            new Point(brand.Bounds.Width / 2, brand.Bounds.Height / 2), window)!.Value;
        window.MouseDown(brandPoint, MouseButton.Left);
        window.MouseUp(brandPoint, MouseButton.Left);
        Pump();

        using var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine("snapshot 0-contact: no frame captured");
        }
        else
        {
            frame.Save(Path.Combine(outputDir, "0-contact.png"));
            Console.WriteLine("snapshot 0-contact: saved");
        }
        window.Close();
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

        void ExpandSection(Border row)
        {
            var point = row.TranslatePoint(
                new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Pump();
        }

        ExpandSection(flyout.AppearanceToggleRow);
        ExpandSection(flyout.ButtonToggleRow);
        ExpandSection(flyout.GrainToggleRow);

        flyout.SaturationSlider.Value = AppearanceSettings.NodeSaturation;
        flyout.NodeCornerRadiusSlider.Value = AppearanceSettings.NodeCornerRadius;
        flyout.HighlightSaturationSlider.Value = AppearanceSettings.HighlightSaturation;
        flyout.HighlightBrightnessSlider.Value = AppearanceSettings.HighlightBrightness;
        flyout.IdleEmbossSlider.Value = ButtonSettings.IdleEmbossStrength;
        flyout.CornerRadiusSlider.Value = ButtonSettings.CornerRadius;
        flyout.IntensitySlider.Value = 24;
        flyout.SlopeSlider.Value = 0.8;
        flyout.WarpSlider.Value = 34;
        NoiseOverlay.RebuildNow();
        Pump();

        Capture(window, outputDir, "0-settings");
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

        Capture(window, outputDir, "1-netw-interact");
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

        void OpenMenu(Visual over)
        {
            var point = Center(over);
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            Pump();
        }

        void ClickMenuItem(string label)
        {
            var item = view.Overlay.GetVisualDescendants().OfType<TextBlock>()
                .First(text => text.Text == label);
            Click(Center(item));
        }

        void ClickDialogButton(string label)
        {
            var button = view.DialogHost.GetVisualDescendants().OfType<TextBlock>()
                .First(text => text.Text == label);
            Click(Center(button));
        }

        string DialogMessage() => string.Join(" | ", view.DialogHost.GetVisualDescendants()
            .OfType<TextBlock>().Select(text => text.Text));

        void CreateFunctionTab()
        {
            OpenMenu(view.LibraryList);
            ClickMenuItem("CREATE FUNCTION");
        }

        void TypeName(string name)
        {
            view.NameBox.Text = name;
            Pump();
        }

        CreateFunctionTab();
        Console.WriteLine($"tfn probe: blank stack has {view.StageRows.Count} stages");

        Click(Center(LibraryEntry("exp")));
        Console.WriteLine($"tfn probe: left click added {view.StageRows.Count} stages (expected 0)");

        OpenMenu(LibraryEntry("exp"));
        ClickMenuItem("ADD TO COMPOSITION STACK");

        var dragEntry = LibraryEntry("sum");
        var dragStart = Center(dragEntry);
        var dragDrop = Center(view.StackHost);
        window.MouseDown(dragStart, MouseButton.Left);
        window.MouseMove(new Point(dragStart.X + 30, dragStart.Y + 10));
        window.MouseMove(dragDrop);
        window.MouseUp(dragDrop, MouseButton.Left);
        Pump();
        Console.WriteLine($"tfn probe: after menu add + drag, {view.StageRows.Count} stages");

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

        CreateFunctionTab();
        OpenMenu(LibraryEntry("fn_1"));
        ClickMenuItem("ADD TO COMPOSITION STACK");
        Console.WriteLine($"tfn probe: after composite add, {view.StackFooter.Text}");

        OpenMenu(view.StageRows[0]);
        Capture(window, outputDir, "2-tfn-interact");
        ClickMenuItem("REMOVE FROM STACK");
        Console.WriteLine($"tfn probe: after menu remove, {view.StageRows.Count} stages");

        OpenMenu(LibraryEntry("fn_1"));
        ClickMenuItem("ADD TO COMPOSITION STACK");
        TypeName("fn_1");
        Click(Center(view.SaveButton));
        Console.WriteLine($"tfn probe: overwrite dialog = {DialogMessage()}");
        Capture(window, outputDir, "2-tfn-dialog");
        ClickDialogButton("CANCEL");

        var closeGlyph = view.StackTabs.GetVisualDescendants().OfType<TextBlock>()
            .Last(text => text.Text == "×");
        Click(Center(closeGlyph));
        Console.WriteLine($"tfn probe: close prompt = {DialogMessage()}");
        ClickDialogButton("DISCARD");
        Console.WriteLine($"tfn probe: tabs after close = {view.StackTabs.Labels.Count}");

        window.Close();
    }

    /// <summary>
    /// Drags a tile out of the element rail onto the canvas — which lands empty and opens its
    /// editor — then flips the master variance switch. Those two frames are the only way to see
    /// the states the dashboard is built around: an unwired tile, and every tile with its bounds
    /// suppressed for prototyping.
    /// </summary>
    private static void CaptureDashboardProbe(string outputDir)
    {
        var view = new DashboardView(new StaticDataFeed().Current, _ => { });
        var window = new Window
        {
            Width = 1560,
            Height = 980,
            SystemDecorations = SystemDecorations.None,
            Content = view
        };
        window.Show();
        Pump();

        Point Centre(Visual visual) =>
            visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window)!.Value;

        var lineEntry = view.ElementsList.GetVisualDescendants().OfType<TextBlock>()
            .First(text => text.Text == "LINE CHART");
        var from = Centre(lineEntry);
        var to = new Point(760, 700);

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X + 40, from.Y + 40));
        window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Pump();

        Console.WriteLine($"dash probe: tiles = {view.Canvas.Placements.Count}, editors = {view.EditorTabs.Labels.Count}");

        // tank_03 reports but is not uncertainty-characterised, so wiring it blanks the tile — and
        // the σ picker only appears for exactly that case.
        void ClickSourceRow(string label)
        {
            var row = view.EditorBody.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(text => text.Text == label);
            if (row is null)
            {
                Console.Error.WriteLine($"dash probe: no source row '{label}'");
                return;
            }
            var point = Centre(row);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Pump();
        }

        ClickSourceRow("tank_farm.tank_03.level");
        Console.WriteLine($"dash probe: wired = {string.Join(",", view.ActiveTileSources.Select(s => s.Path))}");
        Capture(window, outputDir, "3-dash-interact");

        // The σ keys repeat the figure names the source list already shows, so they are located
        // through the "σ FROM" caption rather than by label alone.
        var sigmaGroup = view.EditorBody.GetVisualDescendants().OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Children.Count > 0 &&
                                     panel.Children[0] is TextBlock { Text: "σ FROM" });
        if (sigmaGroup is null)
        {
            Console.Error.WriteLine("dash probe: σ picker not shown");
        }
        else
        {
            var key = sigmaGroup.GetVisualDescendants().OfType<TextBlock>()
                .First(text => text.Text == "fig.total_inventory");
            var point = Centre(key);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Pump();

            var bound = view.ActiveTileSources.FirstOrDefault(s => s.Path.EndsWith("tank_03.level"));
            Console.WriteLine($"dash probe: σ bound to {bound?.SigmaFigureKey ?? "(none)"}");
            Capture(window, outputDir, "3-dash-sigma-bound");
        }

        var toggle = Centre(view.VarianceToggle);
        window.MouseDown(toggle, MouseButton.Left);
        window.MouseUp(toggle, MouseButton.Left);
        Pump();

        Console.WriteLine($"dash probe: variance enabled = {VarianceSettings.Enabled}");
        Capture(window, outputDir, "3-dash-novariance");

        VarianceSettings.SetEnabled(true);
        window.Close();

        ProbeSigmaLeafBinding();
    }

    /// <summary>
    /// MET_ENSEMBLE states σ as a child leaf rather than inline, and it is not mounted by default,
    /// so that route is asserted against the schema directly rather than through a frame.
    /// </summary>
    private static void ProbeSigmaLeafBinding()
    {
        var schema = StubDatasetCatalog.Instance.GetSchemaAsync("MET_ENSEMBLE").GetAwaiter().GetResult();
        var member = schema.Root.Find("MET_ENSEMBLE.members.m01_temp")?.Reading;
        var carrier = schema.Root.Find("MET_ENSEMBLE.members.m01_temp.sigma")?.Reading;

        Console.WriteLine(
            $"sigma-leaf probe: m01_temp σ = {member?.Sigma:0.###} · " +
            $"σ(x) points = {member?.SigmaHistory.Count ?? 0} · " +
            $"variance = {member?.HasVariance} · carrier hidden = {carrier?.IsSigmaCarrier}");
    }

    /// <summary>Drags a dashboard figure out of the rail onto the plan, moves it, and opens the
    /// pin menu — the whole point of the map screen, so it gets a frame of its own.</summary>
    private static void CaptureMapProbe(string outputDir)
    {
        var view = new SiteMapView(new StaticDataFeed().Current, _ => { });
        var window = new Window
        {
            Width = 1560,
            Height = 980,
            SystemDecorations = SystemDecorations.None,
            Content = view
        };
        window.Show();
        Pump();

        Point Centre(Visual visual) =>
            visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window)!.Value;

        void Drag(Point from, Point to)
        {
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
            window.MouseMove(to);
            window.MouseUp(to, MouseButton.Left);
            Pump();
        }

        void Click(Point point)
        {
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Pump();
        }

        var zoneLayerRow = view.LayerRows.Children.OfType<Border>().ElementAt(1);
        Click(Centre(zoneLayerRow));
        Console.WriteLine($"map probe: zone layer after toggle = {view.Plan.ShowZones} (expected False)");
        Click(Centre(zoneLayerRow));

        var railRow = view.CatalogueList.Children.OfType<Border>()
            .First(shell => shell.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.Text == "fig.diesel_en590"));
        railRow.BringIntoView();
        Pump();

        var dropPoint = new Point(1160, 790);
        Drag(Centre(railRow), dropPoint);
        Console.WriteLine($"map probe: {view.Plan.Pins.Count} pins after rail drop (expected 4)");

        var dropped = view.Plan.Pins[^1];
        Console.WriteLine($"map probe: dropped pin anchored at {dropped.Anchor}");

        Drag(Centre(dropped.Card), new Point(1210, 700));
        Console.WriteLine($"map probe: after drag, anchor {dropped.Anchor}, selected {view.Plan.Selected?.Id}");

        var menuPoint = Centre(dropped.Card);
        window.MouseDown(menuPoint, MouseButton.Right);
        window.MouseUp(menuPoint, MouseButton.Right);
        Pump();

        Capture(window, outputDir, "5-map-interact");
        window.Close();
    }

    /// <summary>Pushes a file through the same path the picker and the drop target use, then
    /// checks the pins are still on the same piece of ground after the image underneath changed
    /// shape — portrait here, against the landscape placeholder.</summary>
    private static void CaptureMapUploadProbe(string outputDir)
    {
        var view = new SiteMapView(new StaticDataFeed().Current, _ => { });
        var window = new Window
        {
            Width = 1560,
            Height = 980,
            SystemDecorations = SystemDecorations.None,
            Content = view
        };
        window.Show();
        Pump();

        var before = view.Plan.Pins.Select(pin => pin.Anchor).ToArray();
        var path = Path.Combine(Path.GetTempPath(), "terrafa-client-photo.png");
        WriteClientPhoto(path);

        var file = Await(window.StorageProvider.TryGetFileFromPathAsync(new Uri(path)));
        if (file is null)
        {
            Console.Error.WriteLine("snapshot 5-map-upload: no file from the storage provider");
            window.Close();
            return;
        }

        Await(view.LoadImageAsync(file));
        Pump();

        var after = view.Plan.Pins.Select(pin => pin.Anchor).ToArray();
        Console.WriteLine($"map probe: anchors held across upload = {before.SequenceEqual(after)}");
        Console.WriteLine($"map probe: plan rect fitted to {view.Plan.PlanRect}");

        Capture(window, outputDir, "5-map-upload");
        window.Close();
    }

    /// <summary>A stand-in for the client's own photo — portrait, and obviously not the plan.</summary>
    private static void WriteClientPhoto(string path)
    {
        using var target = new RenderTargetBitmap(new PixelSize(1200, 1500));
        using (var context = target.CreateDrawingContext())
        {
            context.FillRectangle(new SolidColorBrush(Color.Parse("#5A6B4A")), new Rect(0, 0, 1200, 1500));
            var road = new SolidColorBrush(Color.Parse("#CFC4A8"));
            for (var x = 150.0; x < 1200; x += 300) context.FillRectangle(road, new Rect(x, 0, 26, 1500));
            for (var y = 200.0; y < 1500; y += 320) context.FillRectangle(road, new Rect(0, y, 1200, 26));
            context.FillRectangle(new SolidColorBrush(Color.Parse("#8E8E8A")), new Rect(210, 430, 620, 520));
            context.FillRectangle(new SolidColorBrush(Color.Parse("#3B4A53")), new Rect(0, 1180, 1200, 320));
        }
        target.Save(path);
    }

    private static T? Await<T>(Task<T?> task) where T : class
    {
        WaitFor(task);
        return task.IsCompleted ? task.GetAwaiter().GetResult() : null;
    }

    private static void Await(Task task)
    {
        WaitFor(task);
        if (task.IsCompleted) task.GetAwaiter().GetResult();
    }

    private static void WaitFor(Task task)
    {
        for (var attempt = 0; attempt < 400 && !task.IsCompleted; attempt++)
        {
            Pump();
            Thread.Sleep(5);
        }
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void Capture(Window window, string outputDir, string name)
    {
        using var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine($"snapshot {name}: no frame captured");
            return;
        }
        frame.Save(Path.Combine(outputDir, $"{name}.png"));
        Console.WriteLine($"snapshot {name}: saved");
    }

    private static void CaptureAllViews(string outputDir, DataSnapshot snapshot, string suffix)
    {
        var views = new (string Name, UserControl View)[]
        {
            ("1-netw", new NetworkView(snapshot, _ => { })),
            ("2-tfn", new TransferFunctionView(snapshot, _ => { })),
            ("3-dash", new DashboardView(snapshot, _ => { })),
            ("4-tree", new DbTreeView(snapshot, _ => { })),
            ("5-map", new SiteMapView(snapshot, _ => { })),
            ("6-data", new DataSourcesView(snapshot, _ => { }))
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
            Pump();

            Capture(window, outputDir, $"{name}{suffix}");
            window.Close();
        }
    }
}
