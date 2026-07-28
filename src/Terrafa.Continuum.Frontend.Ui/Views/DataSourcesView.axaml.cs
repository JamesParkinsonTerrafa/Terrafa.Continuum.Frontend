using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class DataSourcesView : UserControl
{
    private const double RowIndent = 14;

    private readonly IDatasetCatalog catalog;
    private readonly AuthSession session = AuthSession.Instance;
    private readonly Workspace workspace = Workspace.Instance;
    private readonly HashSet<string> collapsedTopics = [];

    private IReadOnlyDictionary<string, IReadOnlyList<string>> catalogue =
        new Dictionary<string, IReadOnlyList<string>>();
    private string query = "";
    private string? selectedDataset;
    private DatasetSchema? preview;

    /// <summary>Why the catalogue is empty or short, when it is. Null when nothing is wrong.</summary>
    private string? catalogueMessage;

    /// <summary>The dataset the preview is showing or loading — not the merely selected one.</summary>
    private string? openDataset;
    private string? previewMessage;

    public DataSourcesView() : this(DemoData.CreateSnapshot(), _ => { })
    {
    }

    public DataSourcesView(DataSnapshot snapshot, Action<int> navigate)
        : this(snapshot, navigate, StubDatasetCatalog.Instance)
    {
    }

    public DataSourcesView(DataSnapshot snapshot, Action<int> navigate, IDatasetCatalog catalog)
    {
        this.catalog = catalog;
        InitializeComponent();
        Tabs.TabSelected += navigate;

        FeedBadge.TimeText = snapshot.AsOf.ToString("dd-MMM-yyyy HH:mm:ss 'UTC'").ToUpperInvariant();
        SyncText.Text = $"CATALOGUE READING {Source}";

        SearchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            query = SearchBox.Text ?? "";
            RebuildCatalogue();
        };

        RebuildConnect();
        RenderPreview();
        RebuildMounted();
        LoadCatalogue();

        NoiseOverlay.Attach(this);
    }

    /// <summary>True when the rows came from the real service rather than the built-in demo data.</summary>
    private bool IsLive => catalog switch
    {
        SessionDatasetCatalog session => session.IsLive,
        HttpDatasetCatalog => true,
        _ => false
    };

    /// <summary>Databases the catalogue could not read, whichever catalogue is in force.</summary>
    private IReadOnlyList<string> Warnings => catalog switch
    {
        SessionDatasetCatalog session => session.Warnings,
        HttpDatasetCatalog http => http.Warnings,
        _ => []
    };

    /// <summary>Which service the screen is reading from, for the status line.</summary>
    private string Source => IsLive ? DataFeedOptions.DisplayHost.ToUpperInvariant() : "DEMO DATA";

    // ── connect / session bar ────────────────────────────────────────────────

    /// <summary>
    /// The one control on this screen that is about the session rather than the data: an invitation
    /// to connect while the catalogue is demo data, and who is connected once it is not.
    /// </summary>
    private void RebuildConnect() =>
        ConnectHost.Child = session.IsSignedIn ? ConnectedBar() : ConnectButton();

    private Control ConnectButton()
    {
        var label = new TextBlock
        {
            Text = "CONNECT REAL DATA",
            FontSize = 11,
            LetterSpacing = 1,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var button = new Border
        {
            Padding = new Thickness(12, 7),
            Background = Palette.Amber,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = label
        };
        button.PointerEntered += (_, _) => button.Background = Palette.AmberSoft;
        button.PointerExited += (_, _) => button.Background = Palette.Amber;
        button.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            ConnectDataFlow.Show(Dialog, session, OnSignedIn);
        };
        return button;
    }

    private Control ConnectedBar()
    {
        var marker = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = Palette.Cyan,
            VerticalAlignment = VerticalAlignment.Center
        };
        var who = new TextBlock
        {
            Text = session.Username ?? "connected",
            FontSize = 11,
            Foreground = Palette.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var signOut = new TextBlock
        {
            Text = "SIGN OUT",
            FontSize = 9,
            LetterSpacing = 1,
            Foreground = Palette.TextGhost,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        signOut.PointerEntered += (_, _) => signOut.Foreground = Palette.Red;
        signOut.PointerExited += (_, _) => signOut.Foreground = Palette.TextGhost;
        signOut.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            session.SignOut();
            OnSessionSwitched();
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(marker);
        left.Children.Add(who);

        var row = new DockPanel();
        DockPanel.SetDock(signOut, Dock.Right);
        row.Children.Add(signOut);
        row.Children.Add(left);

        return new Border
        {
            Padding = new Thickness(10, 7),
            Background = Palette.BgField,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            Child = row
        };
    }

    private void OnSignedIn() => OnSessionSwitched();

    /// <summary>
    /// Signing in or out replaces the entire catalogue, so nothing selected against the old one
    /// survives — the open preview least of all, since its paths belong to datasets that are no
    /// longer listed.
    /// </summary>
    private void OnSessionSwitched()
    {
        catalogue = new Dictionary<string, IReadOnlyList<string>>();
        catalogueMessage = null;
        previewMessage = null;
        preview = null;
        openDataset = null;
        selectedDataset = null;

        SyncText.Text = $"CATALOGUE READING {Source}";
        RebuildConnect();
        RenderPreview();
        RebuildMounted();
        RebuildCatalogue();
        LoadCatalogue();
    }

    /// <summary>Fired at construction — the catalogue call is the screen's only startup dependency.</summary>
    private async void LoadCatalogue()
    {
        try
        {
            catalogue = await catalog.GetAvailableDatasetsAsync();

            // A listing can succeed and still be short: the service answers 502 only when every
            // database failed, so a partial failure arrives as a 200 and has to be said out loud.
            var warnings = Warnings;
            catalogueMessage = warnings.Count == 0
                ? null
                : $"{warnings.Count} database(s) could not be read — {string.Join("; ", warnings)}";
        }
        catch (Exception ex)
        {
            catalogue = new Dictionary<string, IReadOnlyList<string>>();
            catalogueMessage = Describe(ex);
            SyncText.Text = $"CATALOGUE UNREACHABLE · {Source}";
        }
        RebuildCatalogue();
    }

    /// <summary>
    /// Opens a dataset in two passes: the schema, which is a catalog lookup and lands quickly, and
    /// then its values, which come from a query that can take seconds. Each render is guarded on
    /// the dataset still being the open one, so a slow response cannot overwrite a newer preview.
    /// </summary>
    private async void OpenSchema(string dataset)
    {
        openDataset = dataset;
        previewMessage = null;

        try
        {
            var schema = await catalog.GetSchemaAsync(dataset);
            if (openDataset != dataset) return;
            preview = schema;
            RenderPreview();

            var sampled = await catalog.GetSampleAsync(dataset);
            if (openDataset != dataset) return;
            preview = sampled;
            RenderPreview();
        }
        catch (Exception ex)
        {
            if (openDataset != dataset) return;
            // The structure may already be on screen from the first pass; keep it and report that
            // the values are what failed, rather than throwing the schema away too.
            previewMessage = Describe(ex);
            RenderPreview();
        }
    }

    /// <summary>
    /// A DataFeedException already reads as a sentence — the service writes specific messages and
    /// the client passes them through. Anything else is unexpected, so it is named as such.
    /// </summary>
    private static string Describe(Exception ex) =>
        ex is DataFeedException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";

    // ── catalogue rail ───────────────────────────────────────────────────────

    private void RebuildCatalogue()
    {
        CatalogueList.Children.Clear();
        var hits = 0;

        foreach (var (topic, datasets) in catalogue)
        {
            var matches = Rank(datasets);
            if (matches.Count == 0) continue;
            hits += matches.Count;

            var searching = query.Trim().Length > 0;
            var collapsed = !searching && collapsedTopics.Contains(topic);
            CatalogueList.Children.Add(TopicRow(topic, datasets.Count, matches.Count, collapsed));
            if (collapsed) continue;

            foreach (var dataset in matches)
                CatalogueList.Children.Add(DatasetRow(dataset));
        }

        var total = catalogue.Sum(entry => entry.Value.Count);
        CataloguePanel.Hint = query.Trim().Length > 0
            ? Plural(hits, "hit", "hits")
            : Plural(total, "dataset", "datasets");

        // A partial failure still lists something, so the note goes below the rows rather than
        // instead of them.
        if (catalogueMessage is not null && CatalogueList.Children.Count != 0)
            CatalogueList.Children.Add(Note(catalogueMessage));

        if (CatalogueList.Children.Count != 0) return;
        CatalogueList.Children.Add(catalogueMessage is not null
            ? Note(catalogueMessage)
            : new TextBlock
            {
                Text = catalogue.Count == 0 ? "loading catalogue…" : "no dataset matches that search",
                FontSize = 11,
                Margin = new Thickness(14, 12),
                Foreground = Palette.TextFaint
            });
    }

    private static Control Note(string text) => new TextBlock
    {
        Text = text,
        FontSize = 10,
        LineHeight = 15,
        Margin = new Thickness(14, 12),
        TextWrapping = TextWrapping.Wrap,
        Foreground = Palette.Red
    };

    private List<string> Rank(IReadOnlyList<string> datasets)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0) return [.. datasets];

        return
        [
            .. datasets
                .Select(dataset => (Dataset: dataset, Matched: FuzzySearch.TryMatch(dataset, trimmed, out var score), Score: score))
                .Where(entry => entry.Matched)
                .OrderByDescending(entry => entry.Score)
                .Select(entry => entry.Dataset)
        ];
    }

    private Control TopicRow(string topic, int total, int shown, bool collapsed)
    {
        var caret = new TextBlock
        {
            Text = collapsed ? "▸" : "▾",
            FontSize = 10,
            Foreground = Palette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = topic,
            FontSize = 10,
            LetterSpacing = 1,
            Foreground = Palette.TextSub,
            VerticalAlignment = VerticalAlignment.Center
        };
        var count = new TextBlock
        {
            Text = shown == total ? $"({total})" : $"({shown}/{total})",
            FontSize = 10,
            Foreground = Palette.TextGhost,
            VerticalAlignment = VerticalAlignment.Center
        };

        var mounted = catalogue[topic].Count(workspace.IsMounted);
        var mountedBlock = new TextBlock
        {
            Text = mounted > 0 ? $"{mounted} mounted" : "",
            FontSize = 10,
            Foreground = Palette.Cyan,
            VerticalAlignment = VerticalAlignment.Center
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        left.Children.Add(caret);
        left.Children.Add(label);
        left.Children.Add(count);

        var row = new DockPanel();
        DockPanel.SetDock(mountedBlock, Dock.Right);
        row.Children.Add(mountedBlock);
        row.Children.Add(left);

        var shell = new Border
        {
            Padding = new Thickness(12, 6),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row
        };
        shell.PointerEntered += (_, _) => shell.Background = Palette.BgField;
        shell.PointerExited += (_, _) => shell.Background = Brushes.Transparent;
        shell.PointerPressed += (_, e) =>
        {
            if (!collapsedTopics.Remove(topic)) collapsedTopics.Add(topic);
            RebuildCatalogue();
            e.Handled = true;
        };
        return shell;
    }

    private Control DatasetRow(string dataset)
    {
        var subtree = workspace.Find(dataset);
        IBrush accent = subtree is null ? Palette.TextGhost : SubtreeAccents.Stroke(subtree.AccentIndex);

        var marker = new Rectangle
        {
            Width = 8,
            Height = 8,
            Stroke = accent,
            StrokeThickness = 1,
            Fill = subtree is null ? Brushes.Transparent : accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        var name = new TextBlock
        {
            Text = dataset,
            FontSize = 11,
            Foreground = subtree is null ? Palette.TextMuted : Palette.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        var state = new TextBlock
        {
            Text = subtree is null ? "" : "MOUNTED",
            FontSize = 9,
            LetterSpacing = 1,
            Foreground = Palette.Cyan,
            VerticalAlignment = VerticalAlignment.Center
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        left.Children.Add(marker);
        left.Children.Add(name);

        var row = new DockPanel();
        DockPanel.SetDock(state, Dock.Right);
        row.Children.Add(state);
        row.Children.Add(left);

        var isSelected = dataset == selectedDataset;
        IBrush background = isSelected ? Palette.BgField : Brushes.Transparent;
        var shell = new Border
        {
            Padding = new Thickness(26, 5, 12, 5),
            Background = background,
            BorderBrush = isSelected ? accent : Brushes.Transparent,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row
        };
        shell.PointerEntered += (_, _) => shell.Background = Palette.BgField;
        shell.PointerExited += (_, _) => shell.Background = background;
        shell.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            selectedDataset = dataset;
            if (e.ClickCount >= 2) OpenSchema(dataset);
            RebuildCatalogue();
        };
        return shell;
    }

    // ── subtree preview ──────────────────────────────────────────────────────

    private void RenderPreview()
    {
        PreviewRows.Children.Clear();
        PreviewHeaderHost.Child = preview is null ? EmptyPreviewHeader() : PreviewHeader(preview);

        if (preview is null)
        {
            PreviewPanel.Hint = "";
            PreviewRows.Children.Add(previewMessage is not null
                ? Note(previewMessage)
                : new TextBlock
                {
                    Text = "double-click a dataset in the catalogue to fetch its schema",
                    FontSize = 11,
                    Margin = new Thickness(16, 16),
                    Foreground = Palette.TextFaint
                });
            return;
        }

        PreviewPanel.Hint = "right-click a node to add it — parents include everything beneath";
        if (previewMessage is not null) PreviewRows.Children.Add(Note(previewMessage));
        PreviewRows.Children.Add(PreviewColumnHeader());
        AppendPreviewRow(preview.Root, 0);
    }

    private Control EmptyPreviewHeader() => new TextBlock
    {
        Text = "NO DATASET OPEN",
        FontSize = 12,
        LetterSpacing = 1,
        Foreground = Palette.TextFaint
    };

    private Control PreviewHeader(DatasetSchema schema)
    {
        var subtree = workspace.Find(schema.Dataset);
        IBrush accent = subtree is null ? Palette.TextMuted : SubtreeAccents.Stroke(subtree.AccentIndex);

        var marker = new Rectangle
        {
            Width = 9,
            Height = 9,
            Stroke = accent,
            StrokeThickness = 1,
            Fill = subtree is null ? Brushes.Transparent : accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        var title = new TextBlock
        {
            Text = schema.Dataset,
            FontSize = 15,
            Foreground = Palette.TextBright,
            VerticalAlignment = VerticalAlignment.Center
        };
        var topic = new TextBlock
        {
            Text = $"· {TopicOf(schema.Dataset).ToLowerInvariant()}",
            FontSize = 12,
            Foreground = Palette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        titleRow.Children.Add(marker);
        titleRow.Children.Add(title);
        titleRow.Children.Add(topic);

        var stateChip = new Chip
        {
            Text = subtree is null ? "NOT MOUNTED" : $"MOUNTED · {subtree.LeafCount} LEAVES",
            Accent = subtree is null ? "amber" : "cyan",
            VerticalAlignment = VerticalAlignment.Center
        };

        var top = new DockPanel();
        DockPanel.SetDock(stateChip, Dock.Right);
        top.Children.Add(stateChip);
        top.Children.Add(titleRow);

        var meta = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 0) };
        meta.Children.Add(MetaCell("PROVIDER", schema.Provider));
        meta.Children.Add(MetaCell("CONTRACT", schema.Contract));
        meta.Children.Add(MetaCell("CADENCE", schema.Cadence));
        meta.Children.Add(MetaCell("COVERAGE", schema.Coverage));
        meta.Children.Add(MetaCell("LICENCE", schema.Licence));
        meta.Children.Add(MetaCell("SHAPE", $"{schema.ObjectCount} objects · {schema.LeafCount} leaves"));

        var column = new StackPanel();
        column.Children.Add(top);
        column.Children.Add(meta);
        return column;
    }

    private static Control MetaCell(string label, string value)
    {
        var stack = new StackPanel { Spacing = 3, Margin = new Thickness(0, 4, 16, 4) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            LetterSpacing = 1,
            Foreground = Palette.TextFaint
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 11,
            Foreground = Palette.Text,
            TextWrapping = TextWrapping.Wrap
        });
        return stack;
    }

    private static Control PreviewColumnHeader()
    {
        var row = new DockPanel();
        row.Children.Add(RightCell("NOTE", 250, Palette.TextFaint, 9));
        row.Children.Add(RightCell("UNIT · σ", 130, Palette.TextFaint, 9));
        row.Children.Add(RightCell("KIND", 100, Palette.TextFaint, 9));
        row.Children.Add(new TextBlock
        {
            Text = "PATH",
            FontSize = 9,
            LetterSpacing = 1,
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        });

        return new Border
        {
            Padding = new Thickness(16, 8),
            BorderBrush = Palette.RowSeparator,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = row
        };
    }

    private static TextBlock RightCell(string text, double width, IBrush brush, double size)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            Width = width,
            Foreground = brush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(block, Dock.Right);
        return block;
    }

    private void AppendPreviewRow(DataTreeNode node, int depth)
    {
        PreviewRows.Children.Add(PreviewRow(node, depth));
        foreach (var child in node.Children)
            AppendPreviewRow(child, depth + 1);
    }

    private Control PreviewRow(DataTreeNode node, int depth)
    {
        var isLeaf = node.Kind == DataNodeKind.Measure;
        var alreadyMounted = workspace.FindNode(node.Path) is not null;

        var label = new TextBlock
        {
            Text = isLeaf ? node.Name : $"{node.Name} /",
            FontSize = 11,
            Margin = new Thickness(16 + depth * RowIndent, 0, 0, 0),
            Foreground = isLeaf ? Palette.Cyan : Palette.Text,
            VerticalAlignment = VerticalAlignment.Center
        };

        var row = new DockPanel();
        row.Children.Add(RightCell(node.Reading?.Detail ?? SubtreeNote(node), 250, Palette.TextFaint, 10));
        row.Children.Add(RightCell(
            node.Reading is { } reading ? $"{reading.Display} {reading.SigmaDisplay}".Trim() : "—",
            130, Palette.TextMuted, 10));
        row.Children.Add(RightCell(isLeaf ? node.KindLabel : "OBJECT", 100, Palette.TextMuted, 10));
        row.Children.Add(label);

        IBrush background = alreadyMounted ? Palette.CyanFill : Brushes.Transparent;
        var shell = new Border
        {
            Padding = new Thickness(0, 5),
            Background = background,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row
        };
        shell.PointerEntered += (_, _) => shell.Background = Palette.BgField;
        shell.PointerExited += (_, _) => shell.Background = background;
        shell.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(shell).Properties.IsRightButtonPressed) return;
            e.Handled = true;
            MenuLayer.Show(node.Path, [("ADD TO TREE", () => ShowAddDialog(node))], e.GetPosition(MenuLayer));
        };
        return shell;
    }

    private static string SubtreeNote(DataTreeNode node)
    {
        var leaves = node.Descendants().Count(child => child.Kind == DataNodeKind.Measure);
        return leaves switch
        {
            0 => "object",
            1 => "1 leaf beneath",
            _ => $"{leaves} leaves beneath"
        };
    }

    private static string Plural(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    /// <summary>
    /// Read back out of the loaded catalogue rather than asked of the service, so it says the
    /// same thing whichever catalogue is behind it.
    /// </summary>
    private string TopicOf(string dataset) =>
        catalogue.FirstOrDefault(entry => entry.Value.Contains(dataset)).Key ?? "uncategorised";

    // ── add to tree ──────────────────────────────────────────────────────────

    private void ShowAddDialog(DataTreeNode node)
    {
        if (preview is null) return;
        var schema = preview;

        var leaves = node.Kind == DataNodeKind.Measure
            ? 1
            : node.Descendants().Count(child => child.Kind == DataNodeKind.Measure);
        var objects = node.Kind == DataNodeKind.Measure
            ? 0
            : 1 + node.Descendants().Count(child => child.Kind == DataNodeKind.Object);

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(DialogField("DATASET", schema.Dataset));
        body.Children.Add(DialogField("NODE", node.Path));
        body.Children.Add(DialogField("ADDS",
            $"{Plural(objects, "object", "objects")} · {Plural(leaves, "leaf", "leaves")} — children included recursively"));
        body.Children.Add(new TextBlock
        {
            Text = workspace.IsMounted(schema.Dataset)
                ? $"{schema.Dataset} is already mounted — this grafts the branch onto the existing subtree."
                : $"{schema.Dataset} mounts as its own subtree beside yours. Its contract is fixed; leaves may be added later, never re-shaped.",
            FontSize = 10,
            LineHeight = 15,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextFaint
        });

        Dialog.Show("ADD TO TREE", body, "ADD <GO>", () =>
        {
            workspace.Mount(schema, node);
            RenderPreview();
            RebuildCatalogue();
            RebuildMounted();
            return true;
        });
    }

    private static Control DialogField(string label, string value)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            LetterSpacing = 1,
            Foreground = Palette.TextFaint
        });
        stack.Children.Add(new Border
        {
            Padding = new Thickness(10, 6),
            Background = Palette.BgField,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = value,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Palette.Text
            }
        });
        return stack;
    }

    // ── mounted rail ─────────────────────────────────────────────────────────

    private void RebuildMounted()
    {
        MountedList.Children.Clear();
        foreach (var subtree in workspace.Subtrees)
            MountedList.Children.Add(MountedRow(subtree));

        var leaves = workspace.Subtrees.Sum(subtree => subtree.LeafCount);
        MountedSummary.Text =
            $"{workspace.Subtrees.Count} subtrees · {leaves} leaves · " +
            $"{workspace.CountLinks(SubtreeLinkKind.Equality)} equality links · " +
            $"{workspace.CountLinks(SubtreeLinkKind.Adjacency)} adjacency links.";
    }

    private Control MountedRow(MountedSubtree subtree)
    {
        var accent = SubtreeAccents.Stroke(subtree.AccentIndex);
        var marker = new Rectangle
        {
            Width = 8,
            Height = 8,
            Fill = accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        var name = new TextBlock
        {
            Text = subtree.Dataset.ToLowerInvariant() + "/",
            FontSize = 11,
            Foreground = Palette.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        var detail = new TextBlock
        {
            Text = $"{subtree.LeafCount} leaves · {subtree.Cadence}",
            FontSize = 10,
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        };

        var remove = new TextBlock
        {
            Text = "UNMOUNT",
            FontSize = 9,
            LetterSpacing = 1,
            Foreground = Palette.TextGhost,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        remove.PointerEntered += (_, _) => remove.Foreground = Palette.Red;
        remove.PointerExited += (_, _) => remove.Foreground = Palette.TextGhost;
        remove.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            workspace.Unmount(subtree.Dataset);
            RenderPreview();
            RebuildCatalogue();
            RebuildMounted();
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        left.Children.Add(marker);
        left.Children.Add(name);

        var top = new DockPanel();
        DockPanel.SetDock(remove, Dock.Right);
        top.Children.Add(remove);
        top.Children.Add(left);

        var column = new StackPanel { Spacing = 3 };
        column.Children.Add(top);
        column.Children.Add(detail);

        return new Border
        {
            Padding = new Thickness(14, 8),
            BorderBrush = Palette.RowSeparator,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = column
        };
    }
}
