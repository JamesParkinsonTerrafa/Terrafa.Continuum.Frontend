// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Terrafa.Continuum.Frontend.Controls;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Views;

public partial class CsvExportView : UserControl
{
    private static readonly (string Label, int Rows)[] RowPresets =
    [
        ("100K", 100_000),
        ("1M", 1_000_000),
        ("5M", 5_000_000)
    ];

    private string selectedDataset = ExportTable.SyntheticDataset;
    private int selectedRows = 1_000_000;
    private TableRowCache? subscribedCache;
    private CancellationTokenSource? exportCancellation;
    private TextBlock? buildStatusText;
    private TextBlock? exportStatusText;
    private TextBlock? retainedStatusText;

    public CsvExportView() : this(DemoData.CreateSnapshot(), _ => { })
    {
    }

    public CsvExportView(DataSnapshot snapshot, Action<int> navigate)
    {
        InitializeComponent();
        Tabs.TabSelected += navigate;

        _ = NetworkGraph.Instance;
        ExportTable.Instance.EnsureSeeded();

        BuildPipelinePanel();
        Grid.ViewportChanged += UpdateReadouts;
        AttachGridFromModel();
        UpdateReadouts();
        NoiseOverlay.Attach(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ExportTable.Instance.Changed += OnTableChanged;
        ResolveCacheSubscription();
        AttachGridFromModel();
        BuildPipelinePanel();
        UpdateReadouts();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ExportTable.Instance.Changed -= OnTableChanged;
        if (subscribedCache is not null)
        {
            subscribedCache.Changed -= OnCacheChanged;
            subscribedCache = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnTableChanged()
    {
        ResolveCacheSubscription();
        AttachGridFromModel();
        RefreshBuildStatus();
        UpdateReadouts();
    }

    private void OnCacheChanged()
    {
        Grid.Refresh();
        UpdateReadouts();
    }

    private void ResolveCacheSubscription()
    {
        var cache = ExportTable.Instance.Cache;
        if (ReferenceEquals(subscribedCache, cache)) return;
        if (subscribedCache is not null) subscribedCache.Changed -= OnCacheChanged;
        subscribedCache = cache;
        if (subscribedCache is not null) subscribedCache.Changed += OnCacheChanged;
    }

    private void AttachGridFromModel()
    {
        if (ExportTable.Instance is { Document: { } document, Cache: { } cache, State: ExportBuildState.Ready }
            && !Grid.IsAttachedTo(document, cache))
        {
            Grid.Attach(document, cache);
        }
    }

    // ── pipeline rail ────────────────────────────────────────────────────────────────

    private void BuildPipelinePanel()
    {
        PipelineBody.Children.Clear();

        PipelineBody.Children.Add(SectionLabel("DATASET"));
        var datasets = new List<string> { ExportTable.SyntheticDataset };
        datasets.AddRange(Workspace.Instance.Subtrees.Select(subtree => subtree.Dataset));
        foreach (var dataset in datasets.Distinct())
        {
            PipelineBody.Children.Add(Chip(
                dataset, dataset == selectedDataset, () =>
                {
                    selectedDataset = dataset;
                    BuildPipelinePanel();
                }));
        }

        PipelineBody.Children.Add(SectionLabel("ROWS"));
        var presetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (label, rows) in RowPresets)
        {
            presetRow.Children.Add(Chip(label, rows == selectedRows, () =>
            {
                selectedRows = rows;
                BuildPipelinePanel();
            }));
        }

        PipelineBody.Children.Add(presetRow);

        PipelineBody.Children.Add(SectionLabel("FILTER"));
        var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        filterRow.Children.Add(new TextBlock
        {
            Text = TableExportBuilder.StubFilter,
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextSub,
            VerticalAlignment = VerticalAlignment.Center
        });
        filterRow.Children.Add(new TextBlock
        {
            Text = "[STUB]",
            FontSize = TypographySettings.Size(9),
            Foreground = Palette.Amber,
            VerticalAlignment = VerticalAlignment.Center
        });
        PipelineBody.Children.Add(filterRow);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        actions.Children.Add(CommandKey("BUILD", primary: true, () =>
            _ = ExportTable.Instance.BuildAsync(new TableExportRequest(selectedDataset, selectedRows))));
        actions.Children.Add(CommandKey("STRESS TEST", primary: false, () =>
        {
            selectedRows = RowPresets[^1].Rows;
            BuildPipelinePanel();
            _ = ExportTable.Instance.BuildAsync(new TableExportRequest(selectedDataset, selectedRows));
        }));
        actions.Children.Add(CommandKey("EXPORT CSV", primary: false, () => _ = ExportCsvAsync()));
        PipelineBody.Children.Add(actions);

        buildStatusText = new TextBlock
        {
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextMuted,
            TextWrapping = TextWrapping.Wrap
        };
        PipelineBody.Children.Add(buildStatusText);

        exportStatusText = new TextBlock
        {
            FontSize = TypographySettings.Size(10),
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap
        };
        PipelineBody.Children.Add(exportStatusText);

        retainedStatusText = new TextBlock
        {
            FontSize = TypographySettings.Size(9),
            Foreground = Palette.TextFaint,
            TextWrapping = TextWrapping.Wrap
        };
        PipelineBody.Children.Add(retainedStatusText);

        RefreshBuildStatus();
    }

    private void RefreshBuildStatus()
    {
        if (buildStatusText is null) return;
        var table = ExportTable.Instance;
        buildStatusText.Text = table.State switch
        {
            ExportBuildState.Building => $"BUILDING · {table.BuildProgress:P0} · {table.Request?.RowCount:N0} ROWS",
            ExportBuildState.Ready =>
                $"READY · {table.Document?.TotalRows:N0} ROWS · {table.BuildMilliseconds:N0} MS · {table.BuildNote}",
            ExportBuildState.Failed => table.BuildNote,
            _ => "NO TABLE — PICK A DATASET AND BUILD"
        };

        if (retainedStatusText is null) return;
        retainedStatusText.Text = table.RetainedDocuments > 1
            ? $"RETAINED {table.RetainedDocuments} PARQUET DOCS · {table.RetainedBytes / 1048576.0:0.0} MB — REBUILDING ONE ATTACHES INSTANTLY"
            : "";
    }

    private static TextBlock SectionLabel(string label) => new()
    {
        Text = label,
        FontSize = TypographySettings.Size(9),
        LetterSpacing = 1,
        Foreground = Palette.TextFaint,
        Margin = new Thickness(0, 4, 0, 0)
    };

    private static Control Chip(string label, bool isSelected, Action select)
    {
        var chip = new SquircleBorder
        {
            Classes = { isSelected ? "emboss-press" : "emboss" },
            Padding = new Thickness(10, 4),
            Background = Palette.EmbossSurface,
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = label,
                FontSize = TypographySettings.Size(10),
                LetterSpacing = 1,
                Foreground = isSelected ? Palette.TextBright : Palette.TextSub
            }
        };
        chip.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            select();
        };
        return chip;
    }

    private static Control CommandKey(string label, bool primary, Action action)
    {
        var key = new SquircleBorder
        {
            Classes = { primary ? "emboss-key" : "emboss" },
            Padding = new Thickness(14, 6),
            Background = primary ? Palette.Amber : Palette.EmbossSurface,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = label,
                FontSize = TypographySettings.Size(10),
                LetterSpacing = 1,
                FontWeight = primary ? FontWeight.Bold : FontWeight.Normal,
                Foreground = primary ? Brushes.Black : Palette.TextSub
            }
        };
        key.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return key;
    }

    // ── readouts ─────────────────────────────────────────────────────────────────────

    private void UpdateReadouts()
    {
        var table = ExportTable.Instance;
        var cache = table.Cache;
        if (table.Document is null || cache is null)
        {
            StatusRight.Text = "NO TABLE";
            return;
        }

        StatusRight.Text =
            $"ROWS {table.Document.TotalRows:N0}" +
            $" · CACHED {cache.ResidentRows:N0} ({cache.ResidentBytes / 1048576.0:0.0} MB)" +
            $" · PARQUET {table.ParquetBytes / 1048576.0:0.0} MB" +
            $" · HIT {cache.Hits:N0} / MISS {cache.Misses:N0}";
    }

    // ── csv export ───────────────────────────────────────────────────────────────────

    private async Task ExportCsvAsync()
    {
        if (ExportTable.Instance is not { Document: { } document, State: ExportBuildState.Ready })
        {
            ReportExport("no table yet — build one first");
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || !storage.CanSave)
        {
            ReportExport("no file picker on this host — the grid is the preview");
            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "EXPORT CSV",
            SuggestedFileName = $"{selectedDataset.ToLowerInvariant()}-export.csv",
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });
        if (file is null)
        {
            ReportExport("export cancelled");
            return;
        }

        exportCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        exportCancellation = cancellation;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, Encoding.UTF8) { NewLine = "\r\n" };
            await writer.WriteLineAsync(string.Join(',', document.Columns.Select(column => CsvField(column.Name))));

            var line = new StringBuilder();
            for (var group = 0; group < document.RowGroupCount; group++)
            {
                var rowGroup = await document.ReadRowGroupAsync(group, cancellation.Token);
                for (var row = 0; row < rowGroup.RowCount; row++)
                {
                    line.Clear();
                    for (var column = 0; column < rowGroup.Columns.Count; column++)
                    {
                        if (column > 0) line.Append(',');
                        line.Append(CsvField(rowGroup.Columns[column].Format(row)));
                    }

                    await writer.WriteLineAsync(line, cancellation.Token);
                }

                ReportExport($"writing csv · {(group + 1) * 100 / document.RowGroupCount}%");
                await Task.Yield();
            }

            ReportExport($"csv written · {document.TotalRows:N0} rows · {file.Name}");
        }
        catch (OperationCanceledException)
        {
            ReportExport("export cancelled");
        }
        catch (Exception ex)
        {
            ReportExport($"export failed — {ex.Message}");
        }
    }

    private void ReportExport(string status)
    {
        if (exportStatusText is not null) exportStatusText.Text = status.ToUpperInvariant();
    }

    private static string CsvField(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0) return value;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
