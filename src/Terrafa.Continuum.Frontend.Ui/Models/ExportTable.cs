// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Diagnostics;
using Avalonia.Threading;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Models;

public enum ExportBuildState
{
    Empty,
    Building,
    Ready,
    Failed
}

public sealed class ExportTable
{
    public const string SyntheticDataset = "SYNTHETIC";
    public const int SeedRows = 2_500;
    public const int RetainedDocumentCap = 4;

    public static ExportTable Instance { get; } = new();

    private readonly List<(TableExportRequest Request, ParquetTableDocument Document)> retained = [];
    private CancellationTokenSource? buildCancellation;

    private ExportTable()
    {
        AuthSession.Instance.Changed += Clear;
        TableCacheSettings.Changed += OnCacheSettingsChanged;
    }

    public event Action? Changed;

    public ITableDocument? Document { get; private set; }

    public TableRowCache? Cache { get; private set; }

    public TableExportRequest? Request { get; private set; }

    public ExportBuildState State { get; private set; }

    public double BuildProgress { get; private set; }

    public long BuildMilliseconds { get; private set; }

    public string BuildNote { get; private set; } = "";

    public long ParquetBytes => (Document as ParquetTableDocument)?.ParquetBytes ?? 0;

    public int RetainedDocuments => retained.Count;

    public long RetainedBytes => retained.Sum(entry => entry.Document.ParquetBytes);

    public void EnsureSeeded()
    {
        if (Document is not null || State == ExportBuildState.Building) return;
        _ = BuildAsync(new TableExportRequest(SyntheticDataset, SeedRows));
    }

    public async Task BuildAsync(TableExportRequest request)
    {
        buildCancellation?.Cancel();
        buildCancellation = null;
        if (TryAttachRetained(request)) return;

        var cancellation = new CancellationTokenSource();
        buildCancellation = cancellation;

        Request = request;
        State = ExportBuildState.Building;
        BuildProgress = 0;
        BuildNote = $"BUILDING {request.RowCount:N0} ROWS";
        RaiseChanged();

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var document = await TableExportBuilder.BuildAsync(
                request, new InlineProgress(OnBuildProgress), cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                await document.DisposeAsync();
                return;
            }

            ReplaceDocument(document);
            Retain(request, document);
            BuildMilliseconds = stopwatch.ElapsedMilliseconds;
            BuildProgress = 1;
            State = ExportBuildState.Ready;
            BuildNote = $"{request.Dataset} · {TableExportBuilder.StubFilter} [STUB]";
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(buildCancellation, cancellation))
            {
                State = Document is null ? ExportBuildState.Empty : ExportBuildState.Ready;
            }
        }
        catch (Exception ex)
        {
            State = ExportBuildState.Failed;
            BuildNote = $"BUILD FAILED — {ex.Message}";
        }

        RaiseChanged();
    }

    public void Clear()
    {
        buildCancellation?.Cancel();
        buildCancellation = null;
        Cache?.Dispose();
        Cache = null;
        foreach (var entry in retained) _ = entry.Document.DisposeAsync();
        retained.Clear();
        Document = null;
        Request = null;
        State = ExportBuildState.Empty;
        BuildProgress = 0;
        BuildMilliseconds = 0;
        BuildNote = "";
        RaiseChanged();
    }

    public void ResetForTests()
    {
        Clear();
    }

    private bool TryAttachRetained(TableExportRequest request)
    {
        var index = retained.FindIndex(entry => entry.Request == request);
        if (index < 0) return false;

        var entry = retained[index];
        retained.RemoveAt(index);
        retained.Insert(0, entry);

        Request = request;
        if (!ReferenceEquals(Document, entry.Document))
        {
            ReplaceDocument(entry.Document);
        }

        BuildMilliseconds = 0;
        BuildProgress = 1;
        State = ExportBuildState.Ready;
        BuildNote = $"{request.Dataset} · {TableExportBuilder.StubFilter} [STUB] · RETAINED";
        RaiseChanged();
        return true;
    }

    private void Retain(TableExportRequest request, ParquetTableDocument document)
    {
        retained.RemoveAll(entry => entry.Request == request);
        retained.Insert(0, (request, document));
        for (var index = retained.Count - 1; index >= RetainedDocumentCap; index--)
        {
            if (ReferenceEquals(retained[index].Document, Document)) continue;
            _ = retained[index].Document.DisposeAsync();
            retained.RemoveAt(index);
        }
    }

    private void ReplaceDocument(ITableDocument document)
    {
        Cache?.Dispose();
        Document = document;
        Cache = new TableRowCache(document, RunOnUiThread);
    }

    private void OnBuildProgress(double progress)
    {
        if (State != ExportBuildState.Building) return;
        BuildProgress = progress;
        RaiseChanged();
    }

    private void OnCacheSettingsChanged() => Cache?.OnSettingsChanged();

    private void RaiseChanged()
    {
        if (Changed is not { } handlers) return;
        RunOnUiThread(handlers);
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
