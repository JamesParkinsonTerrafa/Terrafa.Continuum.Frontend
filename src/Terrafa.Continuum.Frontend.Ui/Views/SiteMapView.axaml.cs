// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Controls.Map;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class SiteMapView : UserControl
{
    /// <summary>A value that can be stuck to the plan — a measure leaf, or a figure off the dash.</summary>
    private sealed record PinSource(
        string Id,
        string Group,
        string Title,
        string Tag,
        string TagRight,
        MapPinKind Kind,
        string Value,
        string Sigma,
        string Detail,
        double Width);

    private sealed record CatalogueRow(PinSource Source, Border Shell, TextBlock CheckBlock, TextBlock NameBlock);

    // The client's own image outlives this view: switching theme rebuilds every screen, and
    // making someone re-pick their site photo because they changed to dark mode is not on.
    private static Bitmap? sessionImage;
    private static string sessionImageName = "";

    private readonly Action<int> navigate;
    private readonly Dictionary<string, CatalogueRow> catalogue = [];
    private readonly HashSet<string> canvasHoverIds = [];
    private CatalogueRow? railDrag;
    private Border? railGhost;
    private MapPin? selectedPin;
    private TextBlock? anchorValue;

    public SiteMapView() : this(DemoContent.Create(), _ => { })
    {
    }

    public SiteMapView(DemoContent snapshot, Action<int> navigate)
    {
        this.navigate = navigate;
        InitializeComponent();
        Tabs.TabSelected += navigate;


        Plan.MenuProvider = BuildPinMenu;
        Plan.SelectionChanged += ShowSelectedPin;
        Plan.PinHoverChanged += OnPinHover;
        Plan.FileDropped += file => _ = LoadImageAsync(file);

        BuildImageActions();
        BuildLayerRows();
        BuildCatalogue(snapshot);
        SeedPlan();

        if (sessionImage is not null)
        {
            Plan.SetImage(sessionImage);
            ReportImage(sessionImageName, Palette.Green);
        }
        else
        {
            ReportPlaceholder();
        }

        ShowSelectedPin(null);
        UpdatePinCount();

        PointerMoved += (_, e) => OnRailDragMoved(e);
        PointerReleased += (_, e) => OnRailDragReleased(e);

        NoiseOverlay.Attach(this);
    }

    // ── site image ────────────────────────────────────────────────────────────────────

    private void BuildImageActions()
    {
        ImageActions.Children.Add(CommandKey("UPLOAD IMAGE", primary: true, () => _ = PickImageAsync()));
        ImageActions.Children.Add(CommandKey("RESET", primary: false, ResetImage));
    }

    private static Control CommandKey(string label, bool primary, Action action)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = TypographySettings.Size(10),
            LetterSpacing = 1,
            FontWeight = primary ? FontWeight.Bold : FontWeight.Normal,
            Foreground = primary ? Brushes.Black : Palette.TextSub
        };
        var key = new SquircleBorder
        {
            Classes = { primary ? "emboss-key" : "emboss" },
            Padding = new Thickness(14, 6),
            Background = primary ? Palette.Amber : Palette.EmbossSurface,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = text
        };
        key.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return key;
    }

    private async Task PickImageAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || !storage.CanOpen)
        {
            ReportImageProblem("no file picker on this host — drop an image onto the plan instead");
            return;
        }

        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "SELECT SITE IMAGE",
                AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll]
            });
            if (files.Count == 0) return;
            await LoadImageAsync(files[0]);
        }
        catch (Exception error)
        {
            ReportImageProblem($"picker failed — {error.Message}");
        }
    }

    internal async Task LoadImageAsync(IStorageFile file)
    {
        try
        {
            // Decoded from a copy: the browser head hands back a forward-only stream, and the
            // decoder needs to seek.
            await using var source = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            buffer.Position = 0;

            var bitmap = new Bitmap(buffer);
            sessionImage = bitmap;
            sessionImageName = file.Name;
            Plan.SetImage(bitmap);
            ReportImage($"{file.Name} · {bitmap.PixelSize.Width}×{bitmap.PixelSize.Height} px", Palette.Green);
        }
        catch (Exception error)
        {
            ReportImageProblem($"could not read {file.Name} — {error.Message}");
        }
    }

    private void ResetImage()
    {
        sessionImage = null;
        sessionImageName = "";
        Plan.SetImage(null);
        ReportPlaceholder();
    }

    private void ReportPlaceholder()
    {
        ImageStatusText.Text = "placeholder · generated site plan";
        ImageStatusText.Foreground = Palette.TextMuted;
        ImageBarText.Text = "IMAGE: PLACEHOLDER — UPLOAD OR DROP TO REPLACE";
        ImageBarText.Foreground = Palette.TextFaint;
    }

    private void ReportImage(string detail, IBrush brush)
    {
        ImageStatusText.Text = detail;
        ImageStatusText.Foreground = brush;
        ImageBarText.Text = $"IMAGE: {detail.ToUpperInvariant()}";
        ImageBarText.Foreground = Palette.TextFaint;
    }

    private void ReportImageProblem(string detail)
    {
        ImageStatusText.Text = detail;
        ImageStatusText.Foreground = Palette.Red;
        ImageBarText.Text = detail.ToUpperInvariant();
        ImageBarText.Foreground = Palette.Red;
    }

    // ── layers ────────────────────────────────────────────────────────────────────────

    private void BuildLayerRows()
    {
        AddLayerRow("facility image", Palette.TextMuted, on => Plan.ShowImage = on);
        AddLayerRow("measure zones", Palette.Cyan, on => Plan.ShowZones = on);
        AddLayerRow("pinned figures", Palette.Green, on => Plan.ShowPins = on);
        AddLayerRow("provisional flows", Palette.Purple, on => Plan.ShowFlows = on);
        AddLayerRow("labels", Palette.TextGhost, on => Plan.ShowLabels = on);
    }

    private void AddLayerRow(string label, IBrush swatch, Action<bool> apply)
    {
        var enabled = true;
        var check = new TextBlock { Text = "[x]", FontSize = TypographySettings.Size(11), Foreground = Palette.Green };
        var name = new TextBlock { Text = label, FontSize = TypographySettings.Size(11), Foreground = Palette.Text };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                check,
                new Rectangle { Width = 10, Height = 2, Fill = swatch, VerticalAlignment = VerticalAlignment.Center },
                name
            }
        };
        var shell = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row
        };
        shell.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            enabled = !enabled;
            check.Text = enabled ? "[x]" : "[ ]";
            check.Foreground = enabled ? Palette.Green : Palette.TextFaint;
            name.Foreground = enabled ? Palette.Text : Palette.TextFaint;
            apply(enabled);
        };
        LayerRows.Children.Add(shell);
    }

    // ── catalogue of pinnable values ──────────────────────────────────────────────────

    private void BuildCatalogue(DemoContent snapshot)
    {
        foreach (var group in BuildSources(snapshot).GroupBy(source => source.Group))
        {
            CatalogueList.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontSize = TypographySettings.Size(11),
                Foreground = Palette.TextMuted,
                Margin = new Thickness(12, 8, 0, 3)
            });
            foreach (var source in group) CatalogueList.Children.Add(BuildCatalogueRow(source));
        }
    }

    private static IEnumerable<PinSource> BuildSources(DemoContent snapshot)
    {
        var tree = snapshot.Tree;
        foreach (var objectNode in tree.Descendants().Where(node =>
                     node.Kind == DataNodeKind.Object &&
                     node.Children.Any(child => child.Kind == DataNodeKind.Measure)))
        {
            var group = $"{objectNode.Path[(tree.Path.Length + 1)..].Replace(".", " / ")} /";
            foreach (var measure in objectNode.Children.Where(child => child.Kind == DataNodeKind.Measure))
            {
                var reading = measure.Reading!;
                var title = LeafTitle(measure);
                yield return new PinSource(
                    measure.Path,
                    group,
                    title,
                    $"FIG · {title.Replace('.', ' ').ToUpperInvariant()}",
                    reading.SigmaKind,
                    MapPinKind.Figure,
                    reading.Display,
                    reading.SigmaDisplay,
                    reading.Detail,
                    230);
            }
        }

        foreach (var position in snapshot.Positions)
        {
            var key = Slug(position.Commodity);
            yield return new PinSource(
                $"dash.position.{key}",
                "dashboard figures /",
                $"fig.{key}",
                $"DASH · {position.Commodity}",
                "POSITION",
                MapPinKind.Figure,
                $"{position.Quantity} bbl",
                position.Sigma,
                $"position @ t · {position.Delta} on the day",
                250);
        }

        var leader = snapshot.Leaderboard[0];
        yield return new PinSource(
            "dash.log_score",
            "dashboard figures /",
            "fig.log_score",
            "DASH · LOG SCORE · L1",
            "PROPER",
            MapPinKind.Figure,
            leader.Score,
            leader.Delta,
            $"{leader.Model} · leads the board per event",
            250);

        yield return new PinSource(
            "dash.expiry_risk",
            "dashboard figures /",
            "fig.expiry_risk",
            "DASH · EXPIRY RISK",
            "L4",
            MapPinKind.Provisional,
            "λ 0.031 /d",
            "± 0.019",
            "under-determined — frailty Z not identifiable from these leaves",
            260);
    }

    private Control BuildCatalogueRow(PinSource source)
    {
        var kindMark = new TextBlock
        {
            Text = source.TagRight,
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(kindMark, Dock.Right);

        var checkBlock = new TextBlock { Text = "[ ]", FontSize = TypographySettings.Size(11) };
        var nameBlock = new TextBlock { Text = source.Title, FontSize = TypographySettings.Size(11) };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        label.Children.Add(checkBlock);
        label.Children.Add(nameBlock);

        var row = new DockPanel();
        row.Children.Add(kindMark);
        row.Children.Add(label);

        var shell = new Border
        {
            Margin = new Thickness(22, 0, 12, 0),
            Padding = new Thickness(4, 2),
            Background = Brushes.Transparent,
            Child = row
        };

        var catalogueRow = new CatalogueRow(source, shell, checkBlock, nameBlock);
        catalogue[source.Id] = catalogueRow;
        shell.PointerEntered += (_, _) =>
        {
            UpdateCatalogueRow(catalogueRow, hover: true);
            SetPinHighlight(catalogueRow, true);
        };
        shell.PointerExited += (_, _) =>
        {
            UpdateCatalogueRow(catalogueRow, hover: false);
            SetPinHighlight(catalogueRow, false);
        };
        shell.PointerPressed += (_, e) => BeginRailDrag(catalogueRow, e);
        shell.PointerMoved += (_, e) => OnRailDragMoved(e);
        shell.PointerReleased += (_, e) => OnRailDragReleased(e);
        shell.PointerCaptureLost += (_, _) => CancelRailDrag();
        UpdateCatalogueRow(catalogueRow);
        return shell;
    }

    private void UpdateCatalogueRow(CatalogueRow row, bool hover = false)
    {
        var placed = Plan.FindPin(row.Source.Id) is not null;
        var brush = placed
            ? SitePlanCanvas.AccentFor(row.Source.Kind)
            : hover ? Palette.TextSub : Palette.TextFaint;
        row.CheckBlock.Text = placed ? "[x]" : "[ ]";
        row.CheckBlock.Foreground = brush;
        row.NameBlock.Foreground = brush;
        // Lit while the pointer is on the row, and while it is on the row's pin on the plan —
        // the same light in both directions, so either side finds the other.
        row.Shell.Background = hover || canvasHoverIds.Contains(row.Source.Id)
            ? Palette.BgField
            : Brushes.Transparent;
        row.Shell.Cursor = new Cursor(placed ? StandardCursorType.Arrow : StandardCursorType.Hand);
    }

    /// <summary>Catalogue row hovered — light the pinned card on the plan, if it is placed.</summary>
    private void SetPinHighlight(CatalogueRow row, bool on)
    {
        if (Plan.FindPin(row.Source.Id) is { } pin) pin.Card.IsHighlighted = on;
    }

    /// <summary>Pin card hovered — light its catalogue row and bring it into view.</summary>
    private void OnPinHover(MapPin pin, bool hovering)
    {
        if (!catalogue.TryGetValue(pin.Id, out var row)) return;
        if (hovering) canvasHoverIds.Add(pin.Id);
        else canvasHoverIds.Remove(pin.Id);
        UpdateCatalogueRow(row);
        if (hovering) row.Shell.BringIntoView();
    }

    private static string LeafTitle(DataTreeNode measure)
    {
        var segments = measure.Path.Split('.');
        return segments.Length >= 2 ? $"{segments[^2]}.{segments[^1]}" : measure.Path;
    }

    private static string Slug(string value)
    {
        var scrubbed = new string(value.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray());
        return string.Join('_', scrubbed.Split('_', StringSplitOptions.RemoveEmptyEntries));
    }

    // ── the plan ──────────────────────────────────────────────────────────────────────

    private void SeedPlan()
    {
        var tank01 = SitePlanPlaceholder.Feature("tank_01");
        var tank02 = SitePlanPlaceholder.Feature("tank_02");
        var berth = SitePlanPlaceholder.Feature("berth");

        Plan.AddZone("TANK_01", tank01.Centre, tank01.Size, tank01.Angle, Palette.Cyan, Palette.CyanZoneFill);
        Plan.AddZone("TANK_02", tank02.Centre, tank02.Size, tank02.Angle, Palette.Cyan, Palette.CyanZoneFill);
        Plan.AddZone("BERTH · METER", berth.Centre, berth.Size, berth.Angle, Palette.Amber, Palette.AmberFill);

        SeedPin("SITE_ALPHA.tank_farm.tank_01.level", tank01.Centre, new Vector(-104, 118),
            CapacityGauge(0.71, "71% capacity · σ from Type A"));
        SeedPin("SITE_ALPHA.tank_farm.tank_02.level", tank02.Centre, new Vector(-92, 104),
            CapacityGauge(0.49, "49% capacity · β=+14 declared"));
        SeedPin("SITE_ALPHA.berth_delivery.meter.flow", berth.Centre, new Vector(-116, 86),
            ErrorEllipseRow());

        Plan.SetFlow(berth.Centre, tank01.Centre,
            "flow berth→tank_01 · UNDER-DETERMINED (L4) — drawn provisional",
            new Point(0.015, 0.60));
    }

    private void SeedPin(string id, Point anchor, Vector leader, Control extra)
    {
        if (catalogue.TryGetValue(id, out var row)) PlacePin(row.Source, anchor, leader, extra);
    }

    private MapPin PlacePin(PinSource source, Point anchor, Vector leader, Control? extra)
    {
        var pin = Plan.AddPin(source.Id, source.Title, source.Kind, BuildPinCard(source, extra), anchor, leader, source.Detail);
        if (catalogue.TryGetValue(source.Id, out var row)) UpdateCatalogueRow(row);
        UpdatePinCount();
        return pin;
    }

    private void Unpin(MapPin pin)
    {
        Plan.RemovePin(pin);
        if (catalogue.TryGetValue(pin.Id, out var row)) UpdateCatalogueRow(row);
        UpdatePinCount();
    }

    private static NodeCard BuildPinCard(PinSource source, Control? extra) => new()
    {
        Variant = source.Kind == MapPinKind.Provisional ? NodeCardVariant.Provisional : NodeCardVariant.Figure,
        TagText = source.Tag,
        TagRight = source.TagRight,
        Width = source.Width,
        ValueMain = source.Value,
        ValueAccent = source.Sigma,
        ValueSize = 15,
        Note = extra is null ? source.Detail : "",
        ExtraContent = extra,
        FillOverride = Palette.PinnedCardFill
    };

    private IReadOnlyList<(string Label, Action Action)> BuildPinMenu(MapPin pin) =>
        pin.Id.StartsWith("dash.")
            ?
            [
                ("OPEN IN DASH", () => navigate(2)),
                ("UNPIN FROM PLAN", () => Unpin(pin))
            ]
            :
            [
                ("OPEN IN NETWORK", () => navigate(0)),
                ("UNPIN FROM PLAN", () => Unpin(pin))
            ];

    private void UpdatePinCount() =>
        PinCountText.Text = $"{Plan.Pins.Count} FIGURE{(Plan.Pins.Count == 1 ? "" : "S")} PINNED";

    // ── selection readout ─────────────────────────────────────────────────────────────

    private void ShowSelectedPin(MapPin? pin)
    {
        // Dragging re-reports the same pin on every move; only the anchor changes.
        if (pin is not null && ReferenceEquals(pin, selectedPin) && anchorValue is not null)
        {
            anchorValue.Text = AnchorText(pin);
            return;
        }

        selectedPin = pin;
        anchorValue = null;
        SelectedPinRows.Children.Clear();

        if (pin is null)
        {
            SelectedPinTitle.Text = "SELECTED PIN";
            SelectedPinRows.Children.Add(new TextBlock
            {
                Text = "nothing selected — drag a value onto the plan, or click a card already pinned there.",
                FontSize = TypographySettings.Size(10),
                LineHeight = TypographySettings.Size(15),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Palette.TextFaint
            });
            return;
        }

        SelectedPinTitle.Text = $"SELECTED PIN — {pin.Title.ToUpperInvariant()}";
        SelectedPinRows.Children.Add(DetailRow("reading", $"{pin.Card.ValueMain} {pin.Card.ValueAccent}", Palette.TextBright));
        SelectedPinRows.Children.Add(DetailRow("source", pin.Id, SitePlanCanvas.AccentFor(pin.Kind)));
        anchorValue = new TextBlock
        {
            Text = AnchorText(pin),
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextBright
        };
        SelectedPinRows.Children.Add(DetailRow("anchor", anchorValue));

        if (pin.Detail.Length > 0)
        {
            SelectedPinRows.Children.Add(new TextBlock
            {
                Text = pin.Detail,
                FontSize = TypographySettings.Size(10),
                LineHeight = TypographySettings.Size(15),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Palette.TextMuted
            });
        }

        SelectedPinRows.Children.Add(new Border
        {
            BorderBrush = Palette.GridFaint,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 7, 0, 0),
            IsVisible = HintSettings.Enabled,
            Child = new TextBlock
            {
                Text = "Drag the card to move the pin — the anchor rides with it. Right-click for actions.",
                FontSize = TypographySettings.Size(10),
                LineHeight = TypographySettings.Size(15),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Palette.TextFaint
            }
        });
    }

    private static string AnchorText(MapPin pin) =>
        $"x {pin.Anchor.X * 100:0.0}% · y {pin.Anchor.Y * 100:0.0}%";

    private static Control DetailRow(string label, string value, IBrush brush) =>
        DetailRow(label, new TextBlock { Text = value, FontSize = TypographySettings.Size(10), Foreground = brush, TextWrapping = TextWrapping.Wrap });

    private static Control DetailRow(string label, Control value)
    {
        DockPanel.SetDock(value, Dock.Right);
        var row = new DockPanel();
        row.Children.Add(value);
        row.Children.Add(new TextBlock { Text = label, FontSize = TypographySettings.Size(10), Foreground = Palette.TextMuted });
        return row;
    }

    // ── dragging a value out of the rail ──────────────────────────────────────────────

    private void BeginRailDrag(CatalogueRow row, PointerPressedEventArgs e)
    {
        if (Plan.FindPin(row.Source.Id) is not null) return;
        if (!e.GetCurrentPoint(row.Shell).Properties.IsLeftButtonPressed) return;
        CancelRailDrag();
        railDrag = row;
        railGhost = BuildGhost(row.Source);
        GhostLayer.Children.Add(railGhost);
        PositionGhost(e.GetPosition(this));
        e.Pointer.Capture(row.Shell);
        e.Handled = true;
    }

    private void OnRailDragMoved(PointerEventArgs e)
    {
        if (railDrag is null) return;
        PositionGhost(e.GetPosition(this));
    }

    private void OnRailDragReleased(PointerReleasedEventArgs e)
    {
        if (railDrag is null) return;
        var row = railDrag;
        var dropPoint = e.GetPosition(Plan);
        CancelRailDrag();

        if (!Plan.ContainsPlanPoint(dropPoint)) return;
        Plan.Select(PlacePin(row.Source, Plan.ToNormalized(dropPoint), new Vector(16, 14), null));
    }

    private void CancelRailDrag()
    {
        if (railGhost is not null)
        {
            GhostLayer.Children.Remove(railGhost);
            railGhost = null;
        }
        if (railDrag is not null)
        {
            var row = railDrag;
            railDrag = null;
            UpdateCatalogueRow(row);
        }
    }

    private void PositionGhost(Point position)
    {
        if (railGhost is null) return;
        Canvas.SetLeft(railGhost, position.X + 10);
        Canvas.SetTop(railGhost, position.Y + 8);
    }

    private static Border BuildGhost(PinSource source)
    {
        var accent = SitePlanCanvas.AccentFor(source.Kind);
        return new Border
        {
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            Background = Palette.CanvasNoteBackdrop,
            Padding = new Thickness(8, 4),
            Child = new TextBlock
            {
                Text = $"{source.Title}  {source.Value}",
                FontSize = TypographySettings.Size(10),
                Foreground = accent
            }
        };
    }

    // ── card extras ───────────────────────────────────────────────────────────────────

    private static Control CapacityGauge(double fraction, string caption)
    {
        var track = new Grid
        {
            Height = 6,
            Background = Palette.BgField,
            ColumnDefinitions = new ColumnDefinitions($"{fraction:0.###}*,{1 - fraction:0.###}*")
        };
        var fill = new Rectangle { Fill = Palette.Green, Opacity = 0.75 };
        Grid.SetColumn(fill, 0);
        track.Children.Add(fill);

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(track);
        stack.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = TypographySettings.Size(9),
            Foreground = Palette.TextFaint
        });
        return stack;
    }

    private static Control ErrorEllipseRow()
    {
        var glyph = new Panel { Width = 52, Height = 34 };
        glyph.Children.Add(new Ellipse
        {
            Width = 44,
            Height = 16,
            Stroke = Palette.Amber,
            StrokeThickness = 1.2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new RotateTransform(-18)
        });
        glyph.Children.Add(new Ellipse
        {
            Width = 4,
            Height = 4,
            Fill = Palette.Amber,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(glyph);
        stack.Children.Add(new TextBlock
        {
            Text = "error ellipse — long axis = least-sure direction",
            FontSize = TypographySettings.Size(9),
            Foreground = Palette.TextFaint
        });
        return stack;
    }
}
