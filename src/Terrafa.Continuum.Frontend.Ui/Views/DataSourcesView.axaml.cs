// Copyright (c) 2026 Terrafa Limited. All rights reserved.

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
    private const double CheckColumn = 68;

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

    /// <summary>
    /// The preview row the pointer settled on. Nothing follows from a selection on its own — it is
    /// there so the operator can find the row they mean with a harmless click, and see it stay
    /// found, before the right-click that acts on it.
    /// </summary>
    private string? selectedNode;

    /// <summary>
    /// The column the open dataset is being read along — the pick, not the outcome. Null while
    /// nobody has settled on one, which is a state the screen shows rather than guesses its way out
    /// of; see <see cref="SeriesAxis"/> for why an unordered read is not worth drawing. What the
    /// read actually managed is <see cref="DatasetSchema.XAxis"/> on the schema that comes back —
    /// a table with no such column is read unordered and says so.
    /// </summary>
    private string? xAxis;

    /// <summary>The read in flight, cancelled when a newer one supersedes it.</summary>
    private CancellationTokenSource? reading;

    public DataSourcesView() : this(_ => { })
    {
    }

    public DataSourcesView(Action<int> navigate)
        : this(navigate, StubDatasetCatalog.Instance)
    {
    }

    public DataSourcesView(Action<int> navigate, IDatasetCatalog catalog)
    {
        this.catalog = catalog;
        InitializeComponent();
        Tabs.TabSelected += navigate;

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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Whatever is in flight is for a screen that is going away.
        reading?.Cancel();
        reading = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>True when the rows came from the real service rather than the built-in demo data.</summary>
    private bool IsLive => catalog.IsLive;

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
            FontSize = TypographySettings.Size(11),
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
            // Nothing to run afterwards: signing in is an identity change, and the session rebuilds
            // every screen against the catalogue that replaces this one.
            ConnectDataFlow.Show(Dialog, session, () => { });
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
            FontSize = TypographySettings.Size(11),
            Foreground = Palette.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var signOut = new TextBlock
        {
            Text = "SIGN OUT",
            FontSize = TypographySettings.Size(9),
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
            // Nothing else to do here. Signing out is an identity change, and the session rebuilds
            // every screen — including this one — against the catalogue that replaces this one.
            session.SignOut();
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

    /// <summary>Fired at construction — the catalogue call is the screen's only startup dependency.</summary>
    private async void LoadCatalogue()
    {
        var request = Restart();
        try
        {
            catalogue = await catalog.GetAvailableDatasetsAsync(request.Token);

            // A listing can succeed and still be short: the service answers 502 only when every
            // database failed, so a partial failure arrives as a 200 and has to be said out loud.
            var warnings = catalog.Warnings;
            catalogueMessage = warnings.Count == 0
                ? null
                : $"{warnings.Count} database(s) could not be read — {string.Join("; ", warnings)}";
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            catalogue = new Dictionary<string, IReadOnlyList<string>>();
            catalogueMessage = ReadingLoader.Describe(ex);
            SyncText.Text = $"CATALOGUE UNREACHABLE · {Source}";
        }
        RebuildCatalogue();
    }

    /// <summary>
    /// Cancels whatever this screen last asked for and hands back the token for the new request.
    /// Opening a dataset, changing its axis and reloading the catalogue all supersede each other:
    /// only the most recent one is still wanted, and it used to be guarded by comparing strings on
    /// arrival — which stopped a stale answer being drawn but let it finish being fetched.
    /// </summary>
    private CancellationTokenSource Restart()
    {
        reading?.Cancel();
        return reading = new CancellationTokenSource();
    }

    /// <summary>
    /// Opens a dataset in two passes: the schema, which is a catalog lookup and lands quickly, and
    /// then its values, which come from a query that can take seconds. Each render is guarded on
    /// the dataset still being the open one, so a slow response cannot overwrite a newer preview.
    ///
    /// <para>
    /// The second pass needs an x axis, and only runs once there is one. A dataset carrying a
    /// <see cref="SeriesAxis.Default"/> column supplies its own and nobody is asked; anything else
    /// waits for a pick rather than spending a billed query on rows in an order that would not
    /// mean anything.
    /// </para>
    /// </summary>
    private async void OpenSchema(string dataset)
    {
        var request = Restart();
        openDataset = dataset;
        previewMessage = null;
        xAxis = null;
        // The old selection names a path in the tree being replaced.
        selectedNode = null;

        try
        {
            preview = await catalog.GetSchemaAsync(dataset, request.Token);

            // Demo trees are written with their series already in them and there is nothing to
            // order — the axis exists because Athena has no inherent row order, not because a
            // chart needs one named.
            if (!IsLive)
            {
                RenderPreview();
                return;
            }

            xAxis = SeriesAxis.Preferred(preview);
            RenderPreview();
            if (xAxis is { } axis) await LoadSeries(dataset, axis, request.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // The structure may already be on screen from the first pass; keep it and report that
            // the values are what failed, rather than throwing the schema away too.
            previewMessage = ReadingLoader.Describe(ex);
            RenderPreview();
        }
    }

    /// <summary>
    /// Reads the dataset ordered by <paramref name="axis"/> and publishes it. The read itself is
    /// <see cref="ReadingLoader.ReadAsync"/> — the same one the restore uses — so the store write
    /// and the recorded axis cannot come apart from the fetch. What is left here is the screen's
    /// own business: which preview to draw.
    /// </summary>
    private async Task LoadSeries(string dataset, string axis, CancellationToken cancellationToken)
    {
        try
        {
            preview = await ReadingLoader.ReadAsync(
                catalog, new DatasetQuery(dataset, axis), cancellationToken);
            previewMessage = null;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            previewMessage = ReadingLoader.Describe(ex);
        }
        RenderPreview();
    }

    /// <summary>
    /// Re-reads the open dataset ordered by a different column. The whole subtree moves together:
    /// every leaf's series is indexed by the same axis, which is what makes two of them comparable
    /// on one chart.
    /// </summary>
    private async void ChooseAxis(string dataset, string axis)
    {
        var request = Restart();
        xAxis = axis;
        previewMessage = null;
        RenderPreview();
        await LoadSeries(dataset, axis, request.Token);
    }

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
                FontSize = TypographySettings.Size(11),
                Margin = new Thickness(14, 12),
                Foreground = Palette.TextFaint
            });
    }

    private static Control Note(string text) => new TextBlock
    {
        Text = text,
        FontSize = TypographySettings.Size(10),
        LineHeight = TypographySettings.Size(15),
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
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = topic,
            FontSize = TypographySettings.Size(10),
            LetterSpacing = 1,
            Foreground = Palette.TextSub,
            VerticalAlignment = VerticalAlignment.Center
        };
        var count = new TextBlock
        {
            Text = shown == total ? $"({total})" : $"({shown}/{total})",
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextGhost,
            VerticalAlignment = VerticalAlignment.Center
        };

        var mounted = catalogue[topic].Count(workspace.IsMounted);
        var mountedBlock = new TextBlock
        {
            Text = mounted > 0 ? $"{mounted} mounted" : "",
            FontSize = TypographySettings.Size(10),
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
            FontSize = TypographySettings.Size(11),
            Foreground = subtree is null ? Palette.TextMuted : Palette.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        var state = new TextBlock
        {
            Text = subtree is null ? "" : "MOUNTED",
            FontSize = TypographySettings.Size(9),
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
                    FontSize = TypographySettings.Size(11),
                    Margin = new Thickness(16, 16),
                    Foreground = Palette.TextFaint
                });
            return;
        }

        PreviewPanel.Hint = selectedNode is null
            ? "tick a box to add a node, untick to remove — parents include everything beneath"
            : $"{selectedNode} picked — tick its box to add, untick to remove, parents included";
        if (previewMessage is not null) PreviewRows.Children.Add(Note(previewMessage));
        PreviewRows.Children.Add(PreviewColumnHeader());
        AppendPreviewRow(preview.Root, 0);
    }

    private Control EmptyPreviewHeader() => new TextBlock
    {
        Text = "NO DATASET OPEN",
        FontSize = TypographySettings.Size(12),
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
            FontSize = TypographySettings.Size(15),
            Foreground = Palette.TextBright,
            VerticalAlignment = VerticalAlignment.Center
        };
        var topic = new TextBlock
        {
            Text = $"· {TopicOf(schema.Dataset).ToLowerInvariant()}",
            FontSize = TypographySettings.Size(12),
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
        if (IsLive)
        {
            column.Children.Add(XAxisRow(schema));
            if (schema.RowsPerPoint > 1) column.Children.Add(TiesNote(schema));
        }
        return column;
    }

    /// <summary>
    /// Said once, in the header, when the table breaks the one-row-per-point contract. There is
    /// nothing to click: interleaved rows are the table's shape to fix upstream, not something
    /// this client untangles.
    /// </summary>
    private static Control TiesNote(DatasetSchema schema)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 4, 0, 0)
        };
        row.Children.Add(new TextBlock
        {
            Text = "SERIES",
            FontSize = TypographySettings.Size(9),
            LetterSpacing = 1,
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = $"{schema.RowsPerPoint} rows per axis point — a chart needs one; fix the table, and these leaves will carry series",
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.Amber,
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }

    /// <summary>
    /// The x axis control: what the dataset's rows are sorted by, and the way to change it. Sitting
    /// in the header rather than on a tile because it is a property of the read — every leaf in the
    /// subtree shares it, and two leaves ordered differently could not go on one chart.
    /// </summary>
    private Control XAxisRow(DatasetSchema schema)
    {
        var candidates = SeriesAxis.Candidates(schema);

        var label = new TextBlock
        {
            Text = "X AXIS",
            FontSize = TypographySettings.Size(9),
            LetterSpacing = 1,
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 4, 0, 0)
        };
        row.Children.Add(label);

        if (candidates.Count == 0)
        {
            row.Children.Add(new TextBlock
            {
                Text = "no orderable column — this dataset has no series to plot",
                FontSize = TypographySettings.Size(10),
                Foreground = Palette.TextFaint,
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        var chip = new Chip
        {
            Text = xAxis is { } axis ? axis.ToUpperInvariant() : "SELECT AN X AXIS",
            Accent = xAxis is null ? "amber" : "cyan",
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        chip.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            ShowAxisDialog(schema, candidates);
        };
        row.Children.Add(chip);

        // Three states, and the middle one is the one that used to be invisible: read in full,
        // read but windowed, or not yet ordered. A windowed read is amber, because everything
        // downstream — a chart, a grid, a join — will otherwise present it as the whole table.
        row.Children.Add(new TextBlock
        {
            Text = xAxis is null
                ? "rows arrive unordered — pick the column the readings run along"
                : schema.Truncated
                    ? $"sorted by {xAxis} · newest {schema.WindowRows} rows read — the table holds more"
                    : $"sorted by {xAxis} · all {schema.WindowRows} rows read",
            FontSize = TypographySettings.Size(10),
            Foreground = xAxis is null || schema.Truncated ? Palette.Amber : Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        });

        return row;
    }

    /// <summary>
    /// The axis picker. A dialog rather than a context menu because a wide table offers dozens of
    /// columns and the menu neither scrolls nor fits them.
    /// </summary>
    private void ShowAxisDialog(DatasetSchema schema, IReadOnlyList<string> candidates)
    {
        var chosen = xAxis;
        var rows = new StackPanel { Spacing = 1 };
        var entries = new List<(string Path, Border Row)>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var entry = new Border
            {
                Padding = new Thickness(10, 6),
                Background = candidate == chosen ? Palette.BgField : Brushes.Transparent,
                BorderBrush = candidate == chosen ? Palette.Cyan : Palette.Border,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = candidate,
                    FontSize = TypographySettings.Size(11),
                    Foreground = candidate == chosen ? Palette.TextBright : Palette.Text
                }
            };

            var path = candidate;
            entry.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                chosen = path;
                foreach (var (other, control) in entries)
                {
                    var selected = other == path;
                    control.Background = selected ? Palette.BgField : Brushes.Transparent;
                    control.BorderBrush = selected ? Palette.Cyan : Palette.Border;
                    ((TextBlock)control.Child!).Foreground = selected ? Palette.TextBright : Palette.Text;
                }
            };

            entries.Add((candidate, entry));
            rows.Children.Add(entry);
        }

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(DialogField("DATASET", schema.Dataset));
        body.Children.Add(new TextBlock
        {
            Text = "Athena returns rows in no particular order, so the service is asked to sort on " +
                   "this column. Every leaf in the subtree is read along the same axis.",
            FontSize = TypographySettings.Size(10),
            LineHeight = TypographySettings.Size(15),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextFaint
        });
        body.Children.Add(new ScrollViewer
        {
            MaxHeight = 280,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = rows
        });

        Dialog.Show("ORDER BY", body, "SET AXIS <GO>", () =>
        {
            if (chosen is not { } axis) return false;
            ChooseAxis(schema.Dataset, axis);
            return true;
        });
    }

    private static Control MetaCell(string label, string value)
    {
        var stack = new StackPanel { Spacing = 3, Margin = new Thickness(0, 4, 16, 4) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = TypographySettings.Size(9),
            LetterSpacing = 1,
            Foreground = Palette.TextFaint
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = TypographySettings.Size(11),
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

        var selected = new TextBlock
        {
            Text = "SELECTED",
            FontSize = TypographySettings.Size(9),
            LetterSpacing = 1,
            Width = CheckColumn,
            Foreground = Palette.TextFaint,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(selected, Dock.Left);
        row.Children.Add(selected);

        row.Children.Add(new TextBlock
        {
            Text = "PATH",
            FontSize = TypographySettings.Size(9),
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
            FontSize = TypographySettings.Size(size),
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
        var isSelected = node.Path == selectedNode;

        var label = new TextBlock
        {
            Text = isLeaf ? node.Name : $"{node.Name} /",
            FontSize = TypographySettings.Size(11),
            Margin = new Thickness(depth * RowIndent, 0, 0, 0),
            Foreground = isLeaf ? Palette.Cyan : Palette.Text,
            VerticalAlignment = VerticalAlignment.Center
        };

        var row = new DockPanel();
        row.Children.Add(RightCell(node.Reading?.Detail ?? SubtreeNote(node), 250, Palette.TextFaint, 10));
        row.Children.Add(RightCell(
            node.Reading is { } reading ? $"{reading.Display} {reading.SigmaDisplay}".Trim() : "—",
            130, Palette.TextMuted, 10));
        row.Children.Add(RightCell(isLeaf ? node.KindLabel : "OBJECT", 100, Palette.TextMuted, 10));
        row.Children.Add(CheckCell(node, alreadyMounted));
        row.Children.Add(label);

        // Mounted keeps its tint even while picked — the bar down the left side is what says picked,
        // so the two states are never in competition for the same background.
        IBrush background = alreadyMounted ? Palette.CyanFill
            : isSelected ? Palette.BgField
            : Brushes.Transparent;
        var shell = new Border
        {
            Padding = new Thickness(0, 5),
            Background = background,
            // Carried by every row, transparent unless selected, so highlighting one cannot nudge
            // the column of names sideways.
            BorderBrush = isSelected ? Palette.Amber : Brushes.Transparent,
            BorderThickness = new Thickness(2, 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row
        };
        shell.PointerEntered += (_, _) => shell.Background = Palette.BgField;
        shell.PointerExited += (_, _) => shell.Background = background;
        shell.PointerPressed += (_, e) =>
        {
            var properties = e.GetCurrentPoint(shell).Properties;
            if (!properties.IsLeftButtonPressed && !properties.IsRightButtonPressed) return;
            e.Handled = true;

            // Read before the rebuild below detaches this row: the layer the menu is placed on
            // outlives it, but the row the position is measured from does not.
            var point = e.GetPosition(MenuLayer);
            Select(node.Path);
            if (!properties.IsRightButtonPressed) return;
            MenuLayer.Show(node.Path, alreadyMounted
                ? [("REMOVE FROM TREE", () => ShowRemoveDialog(node))]
                : [("ADD TO TREE", () => ShowAddDialog(node))], point);
        };
        return shell;
    }

    /// <summary>
    /// Moves the highlight. Right-click selects the row it lands on as well, so the menu and the
    /// highlight can never name two different nodes.
    /// </summary>
    private void Select(string path)
    {
        if (selectedNode == path) return;
        selectedNode = path;
        RenderPreview();
    }

    /// <summary>
    /// The box in the SELECTED column: the short way to do what the right-click menu does, since a
    /// node worth adding is usually one the eye has already found in this column. Ticked means the
    /// node is in the tree, and clicking a ticked box takes it back out.
    /// </summary>
    private Control CheckCell(DataTreeNode node, bool alreadyMounted)
    {
        var idle = alreadyMounted ? Palette.Cyan : Palette.TextGhost;
        var box = new TextBlock
        {
            Text = alreadyMounted ? "[x]" : "[ ]",
            FontSize = TypographySettings.Size(11),
            Foreground = idle,
            VerticalAlignment = VerticalAlignment.Center
        };
        // Transparent rather than unset, so the whole column width takes the click and not just the
        // three characters of the box.
        var cell = new Border
        {
            Width = CheckColumn,
            Margin = new Thickness(14, 0, 0, 0),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = box
        };
        DockPanel.SetDock(cell, Dock.Left);

        // Red on the way out, amber on the way in — the same reading as UNMOUNT on the right rail.
        cell.PointerEntered += (_, _) => box.Foreground = alreadyMounted ? Palette.Red : Palette.Amber;
        cell.PointerExited += (_, _) => box.Foreground = idle;
        cell.PointerPressed += (_, e) =>
        {
            // Right-click belongs to the row beneath: the menu says the same thing as the box, and
            // an operator aiming at it should not be blocked by having hit the column.
            if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Select(node.Path);
            if (alreadyMounted) ShowRemoveDialog(node);
            else ShowAddDialog(node);
        };
        return cell;
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

    // ── add to & remove from tree ────────────────────────────────────────────

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
            FontSize = TypographySettings.Size(10),
            LineHeight = TypographySettings.Size(15),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextFaint
        });

        Dialog.Show("ADD TO TREE", body, "ADD <GO>", () =>
        {
            workspace.Mount(schema, node);
            _ = ReadingLoader.LoadDatasetAsync(catalog, schema.Dataset);
            RenderPreview();
            RebuildCatalogue();
            RebuildMounted();
            return true;
        });
    }

    /// <summary>
    /// The counterpart to <see cref="ShowAddDialog"/>, and it asks for the same reason: a node is
    /// rarely alone. What goes is counted off the mount rather than the schema — the mount holds
    /// only what was picked, which can be a good deal less than the branch in front of you.
    /// </summary>
    private void ShowRemoveDialog(DataTreeNode node)
    {
        if (preview is null) return;
        var schema = preview;

        var going = workspace.RemovalFootprint(node.Path);
        if (going.Count == 0) return;

        var leaves = going.Count(gone => gone.Kind == DataNodeKind.Measure);
        var objects = going.Count - leaves;
        var unmounts = workspace.SubtreeOf(node.Path) is { } subtree &&
                       going.Any(gone => gone.Path == subtree.Root.Path);
        var severed = workspace.Links.Count(link =>
            going.Any(gone => gone.Path == link.LeftPath || gone.Path == link.RightPath));

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(DialogField("DATASET", schema.Dataset));
        body.Children.Add(DialogField("NODE", node.Path));
        body.Children.Add(DialogField("REMOVES",
            $"{Plural(objects, "object", "objects")} · {Plural(leaves, "leaf", "leaves")}" +
            (severed > 0 ? $" · {Plural(severed, "link", "links")} severed" : "")));
        body.Children.Add(new TextBlock
        {
            Text = unmounts
                ? $"Nothing else of {schema.Dataset} is in the tree, so the subtree unmounts with it. " +
                  "The catalogue is untouched — it can be added again from this screen."
                : "An object left holding nothing goes too. The catalogue is untouched — anything " +
                  "removed can be added again from this screen.",
            FontSize = TypographySettings.Size(10),
            LineHeight = TypographySettings.Size(15),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextFaint
        });

        Dialog.Show("REMOVE FROM TREE", body, "REMOVE <GO>", () =>
        {
            workspace.RemoveNode(node.Path);
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
            FontSize = TypographySettings.Size(9),
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
                FontSize = TypographySettings.Size(11),
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

        // What the session tried to read on the way in and could not. It is said here because a
        // dataset that failed to read looks exactly like an empty one everywhere else — a blank
        // tile, and no way to tell "nothing there" from "could not reach it".
        foreach (var failure in Session.Instance.ReadFailures)
            MountedList.Children.Add(Note($"{failure.Dataset} — {failure.Message}"));

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
            FontSize = TypographySettings.Size(11),
            Foreground = Palette.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        var detail = new TextBlock
        {
            Text = $"{subtree.LeafCount} leaves · {subtree.Cadence}",
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        };

        var remove = new TextBlock
        {
            Text = "UNMOUNT",
            FontSize = TypographySettings.Size(9),
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
