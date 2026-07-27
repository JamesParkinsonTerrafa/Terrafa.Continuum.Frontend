using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Controls.Charts;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class DbTreeView : UserControl
{
    private const double CardWidth = 140;
    private const double ObjectCardHeight = 48;
    private const double MeasureCardHeight = 64;
    private const double ColumnGap = 14;
    private const double RowHeight = 82;
    private const double BandHeaderHeight = 30;
    private const double BandGap = 34;
    private const double CanvasMargin = 24;

    private sealed record Placement(DataTreeNode Node, MountedSubtree Subtree, double X, double Y)
    {
        public double Height => Node.Kind == DataNodeKind.Measure ? MeasureCardHeight : ObjectCardHeight;
    }

    /// <summary>One band's geometry: where each node sits and which edge feeds it.</summary>
    private sealed class BandLayout
    {
        public List<(DataTreeNode Node, int Row, double Centre)> Nodes { get; } = [];
        public List<(string From, string To)> Containment { get; } = [];
        public double Width { get; set; }
        public int Rows { get; set; }
    }

    private readonly Workspace workspace = Workspace.Instance;
    private readonly Dictionary<string, Placement> placements = [];
    private readonly List<(string From, string To)> containmentEdges = [];
    private readonly HashSet<SubtreeLinkKind> hiddenLinkKinds = [];

    public DbTreeView() : this(DemoData.CreateSnapshot(), _ => { })
    {
    }

    public DbTreeView(DataSnapshot snapshot, Action<int> navigate)
    {
        InitializeComponent();
        Tabs.TabSelected += navigate;

        ReplayText.Text = $"REPLAY TEST ✓  {workspace.Subtrees.Count} subtrees folded · CI 14:00 UTC";

        BuildCanvas();
        BuildSidePanel();
        BuildEventLog(snapshot);
        BuildLegend();

        NoiseOverlay.Attach(this);
    }

    // ── canvas ───────────────────────────────────────────────────────────────

    private void BuildCanvas()
    {
        TreeCanvas.Children.Clear();
        TreeCanvas.Children.Add(Edges);
        placements.Clear();
        containmentEdges.Clear();

        var y = CanvasMargin;
        var widest = 0.0;

        foreach (var subtree in workspace.VisibleSubtrees)
        {
            TreeCanvas.Children.Add(BandHeader(subtree, y));
            y += BandHeaderHeight;

            var band = LayoutBand(subtree.Root);
            foreach (var (node, row, centre) in band.Nodes)
            {
                var position = new Point(centre - CardWidth / 2, y + row * RowHeight);
                placements[node.Path] = new Placement(node, subtree, position.X, position.Y);
                TreeCanvas.Children.Add(BuildCard(node, subtree, position));
            }
            containmentEdges.AddRange(band.Containment);

            widest = Math.Max(widest, band.Width);
            y += band.Rows * RowHeight + BandGap;
        }

        if (!workspace.VisibleSubtrees.Any())
        {
            TreeCanvas.Children.Add(EmptyNote());
            y += 60;
        }

        var width = Math.Max(widest + CanvasMargin, 880);
        var height = Math.Max(y, 560);
        TreeCanvas.Width = width;
        TreeCanvas.Height = height;
        Edges.Width = width;
        Edges.Height = height;
        Edges.Edges = BuildEdges();

        ShapeText.Text =
            $"{workspace.Subtrees.Count} SUBTREES · {workspace.Subtrees.Sum(subtree => subtree.LeafCount)} LEAVES";
        Tabs.HintText =
            $"{workspace.Subtrees.Count} subtrees · {workspace.Links.Count} cross-subtree links · contracts held";
    }

    private static BandLayout LayoutBand(DataTreeNode root)
    {
        var band = new BandLayout();
        var cursor = CanvasMargin;
        PlaceBranch(root, 0, ref cursor, band);
        band.Width = cursor;
        band.Rows = band.Nodes.Max(entry => entry.Row) + 1;
        return band;
    }

    /// <summary>
    /// Objects fan out horizontally and centre over their children; measures stack vertically in
    /// one column so a band stays as narrow as the number of objects that own leaves.
    /// </summary>
    private static double PlaceBranch(DataTreeNode node, int row, ref double cursor, BandLayout band)
    {
        var objects = node.Children.Where(child => child.Kind == DataNodeKind.Object).ToList();
        var measures = node.Children.Where(child => child.Kind == DataNodeKind.Measure).ToList();

        var first = double.MaxValue;
        var last = double.MinValue;

        foreach (var child in objects)
        {
            var childCentre = PlaceBranch(child, row + 1, ref cursor, band);
            first = Math.Min(first, childCentre);
            last = Math.Max(last, childCentre);
            band.Containment.Add((node.Path, child.Path));
        }

        if (measures.Count > 0)
        {
            var column = cursor + CardWidth / 2;
            cursor += CardWidth + ColumnGap;
            first = Math.Min(first, column);
            last = Math.Max(last, column);

            var previous = node.Path;
            var measureRow = row + 1;
            foreach (var measure in measures)
            {
                band.Nodes.Add((measure, measureRow, column));
                band.Containment.Add((previous, measure.Path));
                previous = measure.Path;
                measureRow++;
            }
        }

        if (first > last)
        {
            first = last = cursor + CardWidth / 2;
            cursor += CardWidth + ColumnGap;
        }

        var centre = (first + last) / 2;
        band.Nodes.Add((node, row, centre));
        return centre;
    }

    private Control BandHeader(MountedSubtree subtree, double y)
    {
        var accent = SubtreeAccents.Stroke(subtree.AccentIndex);
        var marker = new Rectangle
        {
            Width = 8,
            Height = 8,
            Fill = accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        var title = new TextBlock
        {
            Text = $"SUBTREE · {subtree.Dataset.ToLowerInvariant()}/",
            FontSize = 11,
            LetterSpacing = 1,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        var detail = new TextBlock
        {
            Text = $"contract {subtree.Contract} · {subtree.LeafCount} leaves · 𝓕ₜ {subtree.Cadence}",
            FontSize = 10,
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(marker);
        row.Children.Add(title);
        row.Children.Add(detail);

        Canvas.SetLeft(row, CanvasMargin);
        Canvas.SetTop(row, y);
        return row;
    }

    private static Control EmptyNote()
    {
        var note = new TextBlock
        {
            Text = "no subtrees visible — mount a dataset on 6) DATA SOURCES, or re-enable one on the left",
            FontSize = 11,
            Foreground = Palette.TextFaint
        };
        Canvas.SetLeft(note, CanvasMargin);
        Canvas.SetTop(note, CanvasMargin);
        return note;
    }

    private Control BuildCard(DataTreeNode node, MountedSubtree subtree, Point position)
    {
        var isRoot = node.Path == subtree.Root.Path;
        var card = new NodeCard
        {
            Width = CardWidth,
            TagText = isRoot ? "OBJECT · ROOT" : node.KindLabel,
            TagRight = node.IsNew ? "+NEW" : "",
            Title = node.Name,
            TitleSize = isRoot ? 13 : 12,
            Note = node.Kind == DataNodeKind.Measure ? BuildLeafNote(node.Reading!) : "",
            Variant = ResolveVariant(node),
            AccentOverride = SubtreeAccents.Stroke(subtree.AccentIndex),
            FillOverride = SubtreeAccents.Fill(subtree.AccentIndex),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsRightButtonPressed) return;
            e.Handled = true;
            MenuLayer.Show(node.Path, BuildNodeMenu(node, subtree), e.GetPosition(MenuLayer));
        };

        Canvas.SetLeft(card, position.X);
        Canvas.SetTop(card, position.Y);
        return card;
    }

    private IReadOnlyList<(string Label, Action Action)> BuildNodeMenu(DataTreeNode node, MountedSubtree subtree)
    {
        var items = new List<(string, Action)> { ("LINK TO…", () => ShowLinkDialog(node)) };

        var attached = workspace.Links
            .Where(link => link.LeftPath == node.Path || link.RightPath == node.Path)
            .ToList();
        foreach (var link in attached)
        {
            var other = link.LeftPath == node.Path ? link.RightPath : link.LeftPath;
            items.Add(($"REMOVE {link.Label} → {ShortPath(other)}", () =>
            {
                workspace.RemoveLink(link);
                Refresh();
            }));
        }

        if (node.Path == subtree.Root.Path)
        {
            items.Add(("UNMOUNT SUBTREE", () =>
            {
                workspace.Unmount(subtree.Dataset);
                Refresh();
            }));
        }
        return items;
    }

    private static string BuildLeafNote(Measure reading)
    {
        if (reading.SigmaDisplay.Length == 0) return reading.Detail;
        var sigmaSuffix = reading.SigmaKind is "σ(x)" or "Σ aniso" ? $" · {reading.SigmaKind}" : "";
        return $"{reading.Display} {reading.SigmaDisplay}{sigmaSuffix}";
    }

    private static NodeCardVariant ResolveVariant(DataTreeNode node)
    {
        if (node.IsNew) return NodeCardVariant.NewNode;
        return node.Kind == DataNodeKind.Measure ? NodeCardVariant.Measure : NodeCardVariant.ObjectNode;
    }

    // ── edges ────────────────────────────────────────────────────────────────

    private List<Edge> BuildEdges()
    {
        var edges = new List<Edge>();

        foreach (var (fromPath, toPath) in containmentEdges)
        {
            if (!placements.TryGetValue(fromPath, out var from)) continue;
            if (!placements.TryGetValue(toPath, out var to)) continue;

            edges.Add(new Edge
            {
                From = new Point(from.X + CardWidth / 2, from.Y + from.Height),
                To = new Point(to.X + CardWidth / 2, to.Y),
                Stroke = Palette.TextGhost,
                Thickness = to.Node.Kind == DataNodeKind.Measure ? 1 : 1.5
            });
        }

        foreach (var link in workspace.Links)
        {
            if (hiddenLinkKinds.Contains(link.Kind)) continue;
            if (!placements.TryGetValue(link.LeftPath, out var left)) continue;
            if (!placements.TryGetValue(link.RightPath, out var right)) continue;

            var from = left.X <= right.X ? left : right;
            var to = left.X <= right.X ? right : left;
            var fromY = from.Y + from.Height / 2;
            var toY = to.Y + to.Height / 2;

            edges.Add(new Edge
            {
                From = new Point(from.X + CardWidth, fromY),
                To = new Point(to.X, toY),
                Stroke = link.Kind == SubtreeLinkKind.Equality ? Palette.Purple : Palette.Amber,
                Thickness = 1,
                Dashes = link.Kind == SubtreeLinkKind.Equality ? [2, 4] : [5, 4],
                BendControl1 = new Point(from.X + CardWidth + 70, fromY),
                BendControl2 = new Point(to.X - 70, toY)
            });
        }

        return edges;
    }

    // ── link dialog ──────────────────────────────────────────────────────────

    private void ShowLinkDialog(DataTreeNode source)
    {
        var sourceSubtree = workspace.SubtreeOf(source.Path);
        var candidates = workspace.Subtrees
            .Where(subtree => subtree != sourceSubtree)
            .SelectMany(subtree => subtree.Root.Descendants().Select(node => (Subtree: subtree, Node: node)))
            .ToList();

        var kind = SubtreeLinkKind.Equality;
        string? target = null;

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(DialogField("SOURCE", source.Path));

        var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var kindButtons = new List<(SubtreeLinkKind Kind, Border Shell, TextBlock Text)>();

        void ApplyKind(SubtreeLinkKind selected)
        {
            kind = selected;
            foreach (var (buttonKind, shell, text) in kindButtons)
            {
                var active = buttonKind == selected;
                var accent = buttonKind == SubtreeLinkKind.Equality ? Palette.Purple : Palette.Amber;
                shell.BorderBrush = active ? accent : Palette.Border;
                shell.Background = active ? Palette.BgField : Brushes.Transparent;
                text.Foreground = active ? accent : Palette.TextMuted;
            }
        }

        foreach (var option in new[] { SubtreeLinkKind.Equality, SubtreeLinkKind.Adjacency })
        {
            var text = new TextBlock { FontSize = 10, LetterSpacing = 1 };
            text.Text = option == SubtreeLinkKind.Equality
                ? "≡ EQUALITY — same underlying thing"
                : "→ ADJACENCY — context, not containment";
            var shell = new Border
            {
                Padding = new Thickness(10, 6),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = text
            };
            var chosen = option;
            shell.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                ApplyKind(chosen);
            };
            kindButtons.Add((option, shell, text));
            kindRow.Children.Add(shell);
        }
        ApplyKind(kind);

        body.Children.Add(LabelledBlock("LINK KIND", kindRow));

        var list = new StackPanel();
        var targetRows = new List<(string Path, Border Shell, TextBlock Text)>();

        void ApplyTarget(string path)
        {
            target = path;
            foreach (var (rowPath, shell, text) in targetRows)
            {
                var active = rowPath == path;
                shell.Background = active ? Palette.BgField : Brushes.Transparent;
                text.Foreground = active ? Palette.Amber : Palette.TextMuted;
            }
        }

        foreach (var (subtree, node) in candidates)
        {
            var text = new TextBlock
            {
                Text = $"{node.Path}  ·  {(node.Kind == DataNodeKind.Measure ? "measure" : "object")}",
                FontSize = 10,
                Foreground = Palette.TextMuted
            };
            var marker = new Rectangle
            {
                Width = 7,
                Height = 7,
                Fill = SubtreeAccents.Stroke(subtree.AccentIndex),
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(marker);
            row.Children.Add(text);

            var shell = new Border
            {
                Padding = new Thickness(10, 5),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = row
            };
            var path = node.Path;
            shell.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                ApplyTarget(path);
            };
            targetRows.Add((path, shell, text));
            list.Children.Add(shell);
        }

        if (candidates.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "no other subtree is mounted — mount one on 6) DATA SOURCES first",
                FontSize = 10,
                Margin = new Thickness(10, 8),
                Foreground = Palette.TextFaint
            });
        }

        body.Children.Add(LabelledBlock("TARGET", new ScrollViewer
        {
            MaxHeight = 220,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = list
        }));

        Dialog.Show("LINK TO", body, "LINK <GO>", () =>
        {
            if (target is null) return false;
            workspace.AddLink(source.Path, target, kind);
            Refresh();
            return true;
        }, width: 560);
    }

    private static Control DialogField(string label, string value) =>
        LabelledBlock(label, new TextBlock
        {
            Text = value,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.Text
        });

    private static Control LabelledBlock(string label, Control content)
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
            Child = content
        });
        return stack;
    }

    // ── side panel ───────────────────────────────────────────────────────────

    private void Refresh()
    {
        BuildCanvas();
        BuildSidePanel();
        BuildLegend();
    }

    private void BuildSidePanel()
    {
        SubtreeToggles.Children.Clear();
        foreach (var subtree in workspace.Subtrees)
            SubtreeToggles.Children.Add(SubtreeToggleRow(subtree));

        if (workspace.Subtrees.Count == 0)
        {
            SubtreeToggles.Children.Add(new TextBlock
            {
                Text = "nothing mounted yet",
                FontSize = 10,
                Margin = new Thickness(14, 8),
                Foreground = Palette.TextFaint
            });
        }

        LinkToggles.Children.Clear();
        LinkToggles.Children.Add(LinkKindRow(SubtreeLinkKind.Equality));
        LinkToggles.Children.Add(LinkKindRow(SubtreeLinkKind.Adjacency));
        LinkToggles.Children.Add(new Border
        {
            Padding = new Thickness(14, 4),
            Child = new TextBlock
            {
                Text = "[ ] containment          in-subtree only",
                FontSize = 10,
                Foreground = Palette.TextGhost
            }
        });

        LinkList.Children.Clear();
        foreach (var link in workspace.Links)
            LinkList.Children.Add(LinkRow(link));
    }

    private Control SubtreeToggleRow(MountedSubtree subtree)
    {
        var accent = SubtreeAccents.Stroke(subtree.AccentIndex);
        var check = new TextBlock
        {
            Text = subtree.Visible ? "[x]" : "[ ]",
            FontSize = 11,
            Foreground = subtree.Visible ? accent : Palette.TextGhost,
            VerticalAlignment = VerticalAlignment.Center
        };
        var marker = new Rectangle
        {
            Width = 8,
            Height = 8,
            Fill = subtree.Visible ? accent : Brushes.Transparent,
            Stroke = accent,
            StrokeThickness = 1,
            VerticalAlignment = VerticalAlignment.Center
        };
        var name = new TextBlock
        {
            Text = subtree.Dataset.ToLowerInvariant() + "/",
            FontSize = 11,
            Foreground = subtree.Visible ? Palette.Text : Palette.TextGhost,
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(check);
        header.Children.Add(marker);
        header.Children.Add(name);

        var detail = new TextBlock
        {
            Text = $"{subtree.Cadence} · {subtree.LeafCount} leaves",
            FontSize = 10,
            Margin = new Thickness(29, 2, 0, 0),
            Foreground = Palette.TextFaint
        };

        var column = new StackPanel();
        column.Children.Add(header);
        column.Children.Add(detail);

        var shell = new Border
        {
            Padding = new Thickness(14, 6),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = column
        };
        shell.PointerEntered += (_, _) => shell.Background = Palette.BgField;
        shell.PointerExited += (_, _) => shell.Background = Brushes.Transparent;
        shell.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            workspace.SetVisible(subtree, !subtree.Visible);
            Refresh();
        };
        return shell;
    }

    private Control LinkKindRow(SubtreeLinkKind kind)
    {
        var shown = !hiddenLinkKinds.Contains(kind);
        var accent = kind == SubtreeLinkKind.Equality ? Palette.Purple : Palette.Amber;
        var label = kind == SubtreeLinkKind.Equality ? "equality" : "adjacency";

        var check = new TextBlock
        {
            Text = shown ? "[x]" : "[ ]",
            FontSize = 11,
            Foreground = shown ? accent : Palette.TextGhost,
            VerticalAlignment = VerticalAlignment.Center
        };
        var name = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = shown ? Palette.Text : Palette.TextGhost,
            VerticalAlignment = VerticalAlignment.Center
        };
        var count = new TextBlock
        {
            Text = workspace.CountLinks(kind).ToString(),
            FontSize = 10,
            Foreground = Palette.TextFaint,
            VerticalAlignment = VerticalAlignment.Center
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(check);
        left.Children.Add(name);

        var row = new DockPanel();
        DockPanel.SetDock(count, Dock.Right);
        row.Children.Add(count);
        row.Children.Add(left);

        var shell = new Border
        {
            Padding = new Thickness(14, 5),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = row
        };
        shell.PointerEntered += (_, _) => shell.Background = Palette.BgField;
        shell.PointerExited += (_, _) => shell.Background = Brushes.Transparent;
        shell.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (!hiddenLinkKinds.Remove(kind)) hiddenLinkKinds.Add(kind);
            Refresh();
        };
        return shell;
    }

    private Control LinkRow(SubtreeLink link)
    {
        var accent = link.Kind == SubtreeLinkKind.Equality ? Palette.Purple : Palette.Amber;
        var text = new TextBlock
        {
            FontSize = 10,
            LineHeight = 14,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.TextMuted
        };
        text.Inlines =
        [
            new Run($"{link.Symbol} ") { Foreground = accent },
            new Run($"{ShortPath(link.LeftPath)} ↔ {ShortPath(link.RightPath)}")
        ];

        var shell = new Border
        {
            Margin = new Thickness(14, 2),
            Padding = new Thickness(8, 5),
            Background = Brushes.Transparent,
            BorderBrush = Palette.RowSeparator,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = text
        };
        shell.PointerEntered += (_, _) => shell.Background = Palette.BgField;
        shell.PointerExited += (_, _) => shell.Background = Brushes.Transparent;
        shell.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            workspace.RemoveLink(link);
            Refresh();
        };
        return shell;
    }

    private static string ShortPath(string path)
    {
        var segments = path.Split('.');
        return segments.Length >= 2 ? $"{segments[0].ToLowerInvariant()}…{segments[^1]}" : path;
    }

    // ── event log & legend ───────────────────────────────────────────────────

    private void BuildEventLog(DataSnapshot snapshot)
    {
        foreach (var entry in snapshot.Events)
        {
            var idBrush = entry.Accent switch
            {
                "red" => Palette.Red,
                "green" => Palette.Green,
                _ => Palette.Cyan
            };

            var header = new TextBlock { FontSize = 10, LineHeight = 15 };
            header.Inlines =
            [
                new Run(entry.Time + " ") { Foreground = Palette.TextFaint },
                new Run(entry.Id + " ") { Foreground = idBrush },
                new Run(entry.Kind) { Foreground = Palette.Text }
            ];
            var detail = new TextBlock
            {
                Text = entry.Detail,
                FontSize = 10,
                LineHeight = 15,
                Foreground = Palette.TextMuted,
                TextWrapping = TextWrapping.Wrap
            };

            var body = new StackPanel();
            body.Children.Add(header);
            body.Children.Add(detail);

            EventRows.Children.Add(new Border
            {
                BorderBrush = Palette.RowSeparator,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 7),
                Child = body
            });
        }

        EventRows.Children.Add(new Border
        {
            Padding = new Thickness(12, 7),
            Child = new TextBlock
            {
                Text = $"… {snapshot.EventCount - snapshot.Events.Count:N0} earlier events retained",
                FontSize = 10,
                Foreground = Palette.TextGhost
            }
        });
    }

    private void BuildLegend()
    {
        LegendPanel.Children.Clear();
        foreach (var subtree in workspace.Subtrees)
        {
            LegendPanel.Children.Add(NetworkView.LegendRow(
                SubtreeAccents.Stroke(subtree.AccentIndex),
                SubtreeAccents.Fill(subtree.AccentIndex),
                false,
                subtree.Dataset.ToLowerInvariant()));
        }
        LegendPanel.Children.Add(LineLegendRow(Palette.Purple, "EQUALITY — cross-subtree"));
        LegendPanel.Children.Add(LineLegendRow(Palette.Amber, "ADJACENCY — cross-subtree"));
        LegendPanel.Children.Add(new TextBlock
        {
            Text = "containment stays inside one dataset",
            FontSize = 9,
            Foreground = Palette.TextFaint,
            Margin = new Thickness(0, 2, 0, 0)
        });
    }

    private static Control LineLegendRow(IBrush stroke, string text)
    {
        var swatch = new Line
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(14, 0),
            Stroke = stroke,
            StrokeThickness = 1,
            StrokeDashArray = [2, 2],
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(swatch);
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 9,
            LetterSpacing = 0.5,
            Foreground = Palette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }
}
