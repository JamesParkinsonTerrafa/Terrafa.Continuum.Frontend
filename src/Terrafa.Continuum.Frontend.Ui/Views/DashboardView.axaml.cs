// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Controls.Dashboard;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class DashboardView : UserControl
{
    private const double DragThreshold = 6;
    private static readonly Size DefaultTileSize = new(Dashboard.DefaultWidth, Dashboard.DefaultHeight);

    private sealed record ElementEntry(string Label, string Detail, TileKind Kind);

    private sealed record ElementSection(string Name, IReadOnlyList<ElementEntry> Entries);

    private static readonly IReadOnlyList<ElementSection> Sections =
    [
        new("TILE",
        [
            new ElementEntry("LINE CHART", "series over time · bounds as upper and lower traces", TileKind.Line),
            new ElementEntry("BAR CHART", "one bar per source · bounds as whiskers", TileKind.Bar),
            new ElementEntry("TABLE", "one row per source · bounds as a ± column", TileKind.Table)
        ])
    ];

    private readonly Dashboard board = Dashboard.Instance;
    private readonly List<DashboardTile> openEditors = [];
    private readonly HashSet<string> collapsedSections = [];
    private int activeEditorIndex = -1;

    private ElementEntry? pressedEntry;
    private Point pressOrigin;
    private bool elementDragActive;
    private Border? dragGhost;

    public DashboardView() : this(DemoData.CreateSnapshot(), _ => { })
    {
    }

    public DashboardView(DataSnapshot snapshot, Action<int> navigate)
    {
        InitializeComponent();
        Tabs.TabSelected += navigate;

        // Before the first tile is drawn: the figures a tile plots are computed by the network, and
        // building the graph is what computes them. Without this a dashboard opened first would
        // paint the values the figures were declared with.
        _ = NetworkGraph.Instance;

        FeedBadge.TimeText = snapshot.AsOf.ToString("dd-MMM-yyyy HH:mm:ss 'UTC'").ToUpperInvariant();

        Canvas.MenuProvider = BuildTileMenu;
        Canvas.TileActivated += OpenEditor;
        Canvas.TileMoved += OnTileMoved;

        EditorTabs.TabSelected += SelectEditor;
        EditorTabs.TabCloseRequested += CloseEditor;
        VarianceToggle.PointerPressed += OnVarianceTogglePressed;

        PointerMoved += (_, e) => OnElementDragMoved(e);
        PointerReleased += (_, e) => OnElementDragReleased(e);

        BuildElements();
        DrawBoard();
        SyncEditor();
        UpdateVarianceToggle();

        NoiseOverlay.Attach(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        VarianceSettings.Changed += OnVarianceChanged;

        // A figure committed on the network canvas, or a dataset mounted on DATA SOURCES, changes
        // both what this screen offers as a source and what the wired tiles are drawing.
        FigureCatalog.Instance.Changed += OnSourcesChanged;
        Workspace.Instance.Changed += OnSourcesChanged;
        board.Changed += SyncBoard;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        VarianceSettings.Changed -= OnVarianceChanged;
        FigureCatalog.Instance.Changed -= OnSourcesChanged;
        Workspace.Instance.Changed -= OnSourcesChanged;
        board.Changed -= SyncBoard;
    }

    private void OnVarianceChanged()
    {
        UpdateVarianceToggle();
        RefreshAllTiles();
        SyncEditor();
    }

    private void OnSourcesChanged()
    {
        RefreshAllTiles();
        SyncEditor();
        UpdateStatus();
    }

    /// <summary>
    /// The canvas follows the board rather than being patched alongside it: adding, removing and
    /// resetting all come through here, so there is one path that can put a tile on screen. Both
    /// this view's own edits and a reset from elsewhere call it, and it is a no-op when the canvas
    /// already shows exactly what the board holds.
    /// </summary>
    private void SyncBoard()
    {
        if (Canvas.Placements.Select(placement => placement.Tile).SequenceEqual(board.Tiles)) return;

        openEditors.RemoveAll(tile => board.Find(tile) is null);
        activeEditorIndex = Math.Clamp(activeEditorIndex, -1, openEditors.Count - 1);
        DrawBoard();
        SyncEditor();
    }

    // ── left rail ────────────────────────────────────────────────────────────────────────────

    private void BuildElements()
    {
        ElementsList.Children.Clear();
        foreach (var section in Sections)
        {
            ElementsList.Children.Add(SectionHeader(section));
            if (collapsedSections.Contains(section.Name)) continue;
            foreach (var entry in section.Entries)
                ElementsList.Children.Add(CreateElementEntry(entry));
        }
    }

    private Control SectionHeader(ElementSection section)
    {
        var collapsed = collapsedSections.Contains(section.Name);
        var caret = new TextBlock
        {
            Text = collapsed ? "▸" : "▾",
            FontSize = 10,
            Foreground = Palette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        var marker = new Rectangle
        {
            Width = 8,
            Height = 8,
            Fill = Palette.Cyan,
            VerticalAlignment = VerticalAlignment.Center
        };
        var name = new TextBlock
        {
            Text = $"{section.Name.ToLowerInvariant()} /",
            FontSize = 11,
            Foreground = Palette.Cyan,
            VerticalAlignment = VerticalAlignment.Center
        };
        var count = new TextBlock
        {
            Text = $"{section.Entries.Count}",
            FontSize = 10,
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        left.Children.Add(caret);
        left.Children.Add(marker);
        left.Children.Add(name);

        var row = new DockPanel();
        DockPanel.SetDock(count, Dock.Right);
        row.Children.Add(count);
        row.Children.Add(left);

        var shell = new Border
        {
            Margin = new Thickness(0, 6, 0, 2),
            Padding = new Thickness(2),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row
        };
        shell.PointerEntered += (_, _) => shell.Background = Palette.BgField;
        shell.PointerExited += (_, _) => shell.Background = Brushes.Transparent;
        shell.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (!collapsedSections.Remove(section.Name)) collapsedSections.Add(section.Name);
            BuildElements();
        };
        return shell;
    }

    private Border CreateElementEntry(ElementEntry entry)
    {
        var body = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Classes = { "tag" },
                    Text = entry.Label,
                    Foreground = Palette.TextSub
                },
                new TextBlock
                {
                    Classes = { "tag" },
                    Text = entry.Detail,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Palette.TextFaint
                }
            }
        };

        var shell = new Border
        {
            Margin = new Thickness(10, 0, 0, 0),
            BorderBrush = Palette.TextGhost,
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
            Padding = new Thickness(9, 7),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = body
        };
        shell.PointerEntered += (_, _) => shell.Background = Palette.BgField;
        shell.PointerExited += (_, _) => shell.Background = Brushes.Transparent;
        shell.PointerPressed += (_, e) => OnElementEntryPressed(entry, shell, e);
        shell.PointerMoved += (_, e) => OnElementDragMoved(e);
        shell.PointerReleased += (_, e) => OnElementDragReleased(e);
        return shell;
    }

    private void OnElementEntryPressed(ElementEntry entry, Border shell, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(shell).Properties.IsLeftButtonPressed) return;
        pressedEntry = entry;
        pressOrigin = e.GetPosition(this);
        elementDragActive = false;
        e.Pointer.Capture(shell);
        e.Handled = true;
    }

    private void OnElementDragMoved(PointerEventArgs e)
    {
        if (pressedEntry is null) return;
        var position = e.GetPosition(this);
        if (!elementDragActive)
        {
            var dx = position.X - pressOrigin.X;
            var dy = position.Y - pressOrigin.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < DragThreshold) return;
            elementDragActive = true;
            ShowDragGhost(pressedEntry);
        }
        MoveDragGhost(position);
    }

    private void OnElementDragReleased(PointerReleasedEventArgs e)
    {
        if (pressedEntry is null) return;
        var entry = pressedEntry;
        pressedEntry = null;
        e.Pointer.Capture(null);
        HideDragGhost();
        if (!elementDragActive) return;
        elementDragActive = false;

        var dropPoint = e.GetPosition(Canvas);
        if (dropPoint.X < 0 || dropPoint.Y < 0 ||
            dropPoint.X > Canvas.Bounds.Width || dropPoint.Y > Canvas.Bounds.Height) return;

        var world = Canvas.ViewportToWorld(dropPoint);
        CreateTile(entry.Kind, new Point(world.X - DefaultTileSize.Width / 2, world.Y - 24));
    }

    private void ShowDragGhost(ElementEntry entry)
    {
        dragGhost = new Border
        {
            Background = Palette.CanvasNoteBackdrop,
            BorderBrush = Palette.Amber,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6),
            Opacity = 0.9,
            Child = new TextBlock
            {
                Text = entry.Label,
                FontSize = 10,
                LetterSpacing = 1,
                Foreground = Palette.AmberSoft
            }
        };
        GhostLayer.Children.Add(dragGhost);
    }

    private void MoveDragGhost(Point position)
    {
        if (dragGhost is null) return;
        Avalonia.Controls.Canvas.SetLeft(dragGhost, position.X + 10);
        Avalonia.Controls.Canvas.SetTop(dragGhost, position.Y + 8);
    }

    private void HideDragGhost()
    {
        if (dragGhost is null) return;
        GhostLayer.Children.Remove(dragGhost);
        dragGhost = null;
    }

    // ── tiles ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Redraws the canvas from the board — the one place a tile reaches the screen.</summary>
    private void DrawBoard()
    {
        Canvas.Clear();
        foreach (var placement in board.Placements)
        {
            Canvas.AddTile(
                placement.Tile,
                TileView.Build(placement.Tile),
                new Point(placement.X, placement.Y),
                new Size(placement.Width, placement.Height));
        }
        Canvas.SetSelected(ActiveTile);
        UpdateStatus();
    }

    private DashboardTile CreateTile(TileKind kind, Point position)
    {
        var tile = new DashboardTile(kind, board.NextName(kind));
        board.Add(tile, position.X, position.Y, DefaultTileSize.Width, DefaultTileSize.Height);
        SyncBoard();
        OpenEditor(tile);
        return tile;
    }

    /// <summary>Keeps where the operator put a tile, so a later redraw does not shuffle the board.</summary>
    private void OnTileMoved(DashboardTile tile)
    {
        if (Canvas.Find(tile) is not { } placement) return;
        board.Place(tile, placement.Position.X, placement.Position.Y, placement.Size.Width, placement.Size.Height);
    }

    private void RefreshTile(DashboardTile tile) =>
        Canvas.Find(tile)?.SetContent(TileView.Build(tile));

    private void RefreshAllTiles()
    {
        foreach (var tile in board.Tiles) RefreshTile(tile);
    }

    private IReadOnlyList<(string Label, Action Action)> BuildTileMenu(DashboardTile tile) =>
    [
        ("EDIT TILE", () => OpenEditor(tile)),
        ("DUPLICATE", () => DuplicateTile(tile)),
        ("REMOVE FROM DASHBOARD", () => RemoveTile(tile))
    ];

    private void DuplicateTile(DashboardTile tile)
    {
        var origin = board.Find(tile);
        var copy = new DashboardTile(tile.Kind, board.NextName(tile.Kind));
        copy.Sources.AddRange(tile.Sources);
        board.Add(
            copy,
            (origin?.X ?? 0) + 28,
            (origin?.Y ?? 0) + 28,
            origin?.Width ?? DefaultTileSize.Width,
            origin?.Height ?? DefaultTileSize.Height);
        SyncBoard();
        OpenEditor(copy);
    }

    private void RemoveTile(DashboardTile tile)
    {
        board.Remove(tile);
        SyncBoard();
    }

    private void UpdateStatus()
    {
        var count = board.Placements.Count;
        var blank = board.Tiles.Count(IsBlanked);
        StatusRight.Text = blank == 0
            ? $"{count} TILE(S) · VARIANCE {(VarianceSettings.Enabled ? "ON" : "OFF")}"
            : $"{count} TILE(S) · {blank} BLANK — NO σ OR NO VALUE";
        StatusRight.Foreground = blank == 0 ? Palette.TextFaint : Palette.Amber;
    }

    /// <summary>
    /// Whether the tile is drawing nothing. Two ways to get there: a source that carries no number
    /// yet, which blanks it whatever the master switch says, and — with variance on — one that
    /// carries no σ.
    /// </summary>
    private static bool IsBlanked(DashboardTile tile)
    {
        if (!tile.IsWired) return false;
        var resolved = tile.Sources.Select(TileData.Resolve).OfType<TileSeries>().ToList();
        if (resolved.Count == 0) return false;
        if (resolved.Any(series => !series.HasValue)) return true;
        return VarianceSettings.Enabled && resolved.Any(series => !series.HasVariance);
    }

    // ── variance switch ──────────────────────────────────────────────────────────────────────

    private void OnVarianceTogglePressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        VarianceSettings.Toggle();
    }

    private void UpdateVarianceToggle()
    {
        var on = VarianceSettings.Enabled;
        VarianceToggleText.Text = on ? "[x] SHOW VARIANCE" : "[ ] SHOW VARIANCE";
        VarianceToggleText.Foreground = on ? Palette.Amber : Palette.TextMuted;
        VarianceToggle.Classes.Set("emboss", !on);
        VarianceToggle.Classes.Set("emboss-press", on);
        UpdateStatus();
    }

    // ── editor ───────────────────────────────────────────────────────────────────────────────

    private void OpenEditor(DashboardTile tile)
    {
        var index = openEditors.IndexOf(tile);
        if (index < 0)
        {
            openEditors.Add(tile);
            index = openEditors.Count - 1;
        }
        activeEditorIndex = index;
        SyncEditor();
    }

    private void SelectEditor(int index)
    {
        if (index < 0 || index >= openEditors.Count) return;
        activeEditorIndex = index;
        SyncEditor();
    }

    private void CloseEditor(int index)
    {
        if (index < 0 || index >= openEditors.Count) return;
        openEditors.RemoveAt(index);
        activeEditorIndex = openEditors.Count == 0
            ? -1
            : Math.Clamp(activeEditorIndex >= index ? activeEditorIndex - 1 : activeEditorIndex,
                0, openEditors.Count - 1);
        SyncEditor();
    }

    private DashboardTile? ActiveTile =>
        activeEditorIndex >= 0 && activeEditorIndex < openEditors.Count ? openEditors[activeEditorIndex] : null;

    /// <summary>What the open tile is wired to. Exists so the snapshot probe can assert on it.</summary>
    internal IReadOnlyList<TileSource> ActiveTileSources => ActiveTile?.Sources ?? [];

    private void SyncEditor()
    {
        EditorTabs.Labels = openEditors.Select(tile => tile.Name).ToArray();
        EditorTabs.ActiveIndex = activeEditorIndex;
        EditorTabs.IsVisible = openEditors.Count > 0;
        Canvas.SetSelected(ActiveTile);
        BuildEditorBody();
    }

    private void BuildEditorBody()
    {
        EditorBody.Children.Clear();

        if (ActiveTile is not { } tile)
        {
            EditorBody.Children.Add(new TextBlock
            {
                Text = "No tile open.\n\nDouble-click a tile on the canvas to edit its name and data sources, or drag a new element in from the left.",
                FontSize = 11,
                LineHeight = 17,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Palette.TextFaint
            });
            EditorFooter.Text = VarianceSettings.Enabled
                ? "Variance is on — tiles wired to a source without σ are drawn blank."
                : "Variance is off — every tile shows its central estimate only.";
            return;
        }

        EditorBody.Children.Add(FieldLabel("NAME"));
        EditorBody.Children.Add(BuildNameField(tile));
        EditorBody.Children.Add(FieldLabel("CHART TYPE"));
        EditorBody.Children.Add(BuildKindRow(tile));
        EditorBody.Children.Add(FieldLabel("DATA SOURCES"));
        EditorBody.Children.Add(BuildSourceList(tile));

        var wired = tile.Sources.Count;
        var resolved = tile.Sources.Select(TileData.Resolve).OfType<TileSeries>().ToList();
        var silent = resolved.Count(series => !series.HasValue);
        var bare = resolved.Count(series => !series.HasVariance);
        var asserted = resolved.Count(series => series.IsAssertedSigma);

        // No value comes first: σ is a property of a number, so reporting a missing σ for a source
        // that has no reading at all would send someone looking for the wrong thing.
        EditorFooter.Text = wired == 0
            ? "Not wired. Pick one or more measures or figures below."
            : silent > 0
                ? $"{silent} of {wired} source(s) carry no value — this tile stays blank until they do"
                : (bare, asserted, VarianceSettings.Enabled) switch
                {
                    ( > 0, _, true) => $"{bare} of {wired} source(s) carry no σ — this tile is blank while variance is on",
                    ( > 0, _, false) => $"{bare} of {wired} source(s) carry no σ · hidden while variance is off",
                    (_, > 0, _) => $"{wired} source(s) wired · {asserted} σ asserted from a figure, not carried by the tree",
                    _ => $"{wired} source(s) wired · all carry σ · bounds drawn natively"
                };
    }

    private static TextBlock FieldLabel(string text) => new()
    {
        Classes = { "tag" },
        Text = text,
        Foreground = Palette.TextFaint
    };

    private Control BuildNameField(DashboardTile tile)
    {
        var box = new TextBox { Classes = { "field" }, Text = tile.Name, Watermark = "tile name" };
        // Only the tab labels and the tile header are refreshed here — rebuilding the editor body
        // on every keystroke would replace this box mid-edit and drop focus after one character.
        box.TextChanged += (_, _) =>
        {
            tile.Name = box.Text ?? "";
            EditorTabs.Labels = openEditors.Select(open => open.Name).ToArray();
            EditorTabs.ActiveIndex = activeEditorIndex;
            RefreshTile(tile);
        };
        return new SquircleBorder
        {
            Classes = { "emboss-press" },
            Background = Palette.BgField,
            Child = box
        };
    }

    private Control BuildKindRow(DashboardTile tile)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var kind in new[] { TileKind.Line, TileKind.Bar, TileKind.Table })
        {
            var isActive = tile.Kind == kind;
            var text = new TextBlock
            {
                Text = kind.ToString().ToUpperInvariant(),
                FontSize = 10,
                LetterSpacing = 1,
                FontWeight = isActive ? FontWeight.Bold : FontWeight.Normal,
                Foreground = isActive ? Palette.Amber : Palette.TextMuted
            };
            var key = new SquircleBorder
            {
                Classes = { isActive ? "emboss-press" : "emboss" },
                Padding = new Thickness(12, 5),
                Background = Palette.EmbossSurface,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = text
            };
            var captured = kind;
            key.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                if (tile.Kind == captured) return;
                tile.Kind = captured;
                RefreshTile(tile);
                BuildEditorBody();
            };
            row.Children.Add(key);
        }
        return row;
    }

    private Control BuildSourceList(DashboardTile tile)
    {
        var list = new StackPanel { Spacing = 2 };

        if (FigureCatalog.Instance.Figures.Count > 0)
        {
            list.Children.Add(GroupHeader("dashboard figures /"));
            foreach (var figure in FigureCatalog.Instance.Figures)
            {
                list.Children.Add(SourceRow(
                    tile,
                    new TileSource(TileSourceKind.Figure, figure.Key),
                    figure.Name,
                    figure.StateNote,
                    figure.HasVariance,
                    figure.IsProvisional || !figure.HasValue));
            }
        }

        foreach (var subtree in Workspace.Instance.Subtrees)
        {
            var leaves = TileData.AvailableMeasures(subtree).ToList();
            if (leaves.Count == 0) continue;

            list.Children.Add(GroupHeader($"{subtree.Dataset.ToLowerInvariant()} /"));
            foreach (var leaf in leaves)
            {
                var reading = leaf.Reading!;
                list.Children.Add(SourceRow(
                    tile,
                    new TileSource(TileSourceKind.Measure, leaf.Path),
                    ShortPath(leaf.Path, subtree.Root.Path),
                    reading.StateNote,
                    reading.HasVariance,
                    isProvisional: !reading.HasValue));

                // The customisation route, offered only where it applies: a measure the tree gives
                // no σ for can borrow one from a figure. Not offered for a leaf with no value at
                // all — there would be nothing for the σ to be the spread of. A figure never gets
                // this either: its σ comes up the chain, and nominating one would be asserting what
                // the network computes.
                if (tile.Sources.Any(existing => existing.Matches(TileSourceKind.Measure, leaf.Path)) &&
                    reading is { HasValue: true, HasVariance: false })
                {
                    list.Children.Add(SigmaFigureRow(tile, leaf.Path));
                }
            }
        }

        if (list.Children.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "nothing mounted — open 6) DATA SOURCES",
                FontSize = 11,
                Foreground = Palette.TextFaint
            });
        }

        return list;
    }

    private static string ShortPath(string path, string rootPath) =>
        path.StartsWith(rootPath + ".", StringComparison.Ordinal) ? path[(rootPath.Length + 1)..] : path;

    private static TextBlock GroupHeader(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Margin = new Thickness(0, 8, 0, 2),
        Foreground = Palette.TextMuted
    };

    /// <summary>
    /// The σ picker for one wired measure. Shown only when the tree gave it none, so the ordinary
    /// case stays a plain checkbox and nobody is invited to override a σ the contract carries.
    /// </summary>
    private Control SigmaFigureRow(DashboardTile tile, string path)
    {
        var index = tile.Sources.FindIndex(source => source.Matches(TileSourceKind.Measure, path));
        if (index < 0) return new Panel();
        var bound = tile.Sources[index].SigmaFigureKey;

        var keys = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        keys.Children.Add(SigmaFigureKey(tile, index, null, "INTRINSIC", bound is null));

        // A figure with nothing wired into it has no number to lend, so it is not offered as one.
        foreach (var figure in FigureCatalog.Instance.Figures.Where(figure => figure.HasValue))
            keys.Children.Add(SigmaFigureKey(tile, index, figure.Key, figure.Name, bound == figure.Key));

        var caption = new TextBlock
        {
            Text = "σ FROM",
            FontSize = 9,
            LetterSpacing = 1,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = bound is null ? Palette.TextFaint : Palette.Purple
        };

        return new StackPanel
        {
            Margin = new Thickness(34, 0, 0, 2),
            Children = { caption, keys }
        };
    }

    private Control SigmaFigureKey(DashboardTile tile, int index, string? key, string label, bool isActive)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 9,
            Foreground = isActive ? (key is null ? Palette.Amber : Palette.Purple) : Palette.TextMuted
        };
        var shell = new SquircleBorder
        {
            Classes = { isActive ? "emboss-press" : "emboss" },
            Padding = new Thickness(7, 3),
            Margin = new Thickness(0, 2, 4, 0),
            Background = Palette.EmbossSurface,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = text
        };
        shell.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            tile.Sources[index] = tile.Sources[index] with { SigmaFigureKey = key };
            RefreshTile(tile);
            UpdateStatus();
            BuildEditorBody();
        };
        return shell;
    }

    private Control SourceRow(
        DashboardTile tile, TileSource source, string label, string sigmaText, bool hasVariance, bool isProvisional)
    {
        // Matched on kind and path rather than by record equality: a wired source may carry a σ
        // figure binding, and that must not make it look like a different source here.
        var existing = tile.Sources.FindIndex(candidate => candidate.Matches(source.Kind, source.Path));
        var selected = existing >= 0;
        var accent = isProvisional ? Palette.Purple : Palette.Cyan;
        var brush = selected ? accent : Palette.TextFaint;

        var check = new TextBlock { Text = selected ? "[x]" : "[ ]", FontSize = 11, Foreground = brush };
        var name = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = brush,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var sigma = new TextBlock
        {
            Text = sigmaText,
            FontSize = 10,
            Foreground = hasVariance ? Palette.TextFaint : Palette.Amber
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(check);
        left.Children.Add(name);

        var row = new DockPanel();
        DockPanel.SetDock(sigma, Dock.Right);
        row.Children.Add(sigma);
        row.Children.Add(left);

        var shell = new Border
        {
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(4, 2),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row
        };
        shell.PointerEntered += (_, _) => shell.Background = Palette.BgField;
        shell.PointerExited += (_, _) => shell.Background = Brushes.Transparent;
        shell.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (existing >= 0) tile.Sources.RemoveAt(existing);
            else tile.Sources.Add(source);
            RefreshTile(tile);
            UpdateStatus();
            BuildEditorBody();
        };
        return shell;
    }
}
