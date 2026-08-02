// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Tests;

[Collection("workspace")]
public class ExportTableTests
{
    [Fact]
    public async Task SecondBuildOfSameRequest_AttachesRetainedDocumentInstantly()
    {
        var table = ExportTable.Instance;
        table.ResetForTests();
        try
        {
            await table.BuildAsync(new TableExportRequest(ExportTable.SyntheticDataset, 30_000));
            var firstDocument = table.Document;
            Assert.NotNull(firstDocument);

            await table.BuildAsync(new TableExportRequest(ExportTable.SyntheticDataset, 55_000));
            Assert.NotSame(firstDocument, table.Document);
            Assert.Equal(2, table.RetainedDocuments);

            await table.BuildAsync(new TableExportRequest(ExportTable.SyntheticDataset, 30_000));
            Assert.Same(firstDocument, table.Document);
            Assert.Equal(ExportBuildState.Ready, table.State);
            Assert.Equal(0, table.BuildMilliseconds);
            Assert.Contains("RETAINED", table.BuildNote);
            Assert.Equal(2, table.RetainedDocuments);
        }
        finally
        {
            table.ResetForTests();
        }
    }

    [Fact]
    public async Task RetentionCap_EvictsOldestDocumentAndDisposesIt()
    {
        var table = ExportTable.Instance;
        table.ResetForTests();
        try
        {
            await table.BuildAsync(new TableExportRequest(ExportTable.SyntheticDataset, 26_000));
            var oldest = table.Document!;

            for (var extra = 1; extra <= ExportTable.RetainedDocumentCap; extra++)
            {
                await table.BuildAsync(
                    new TableExportRequest(ExportTable.SyntheticDataset, 26_000 + extra * 1_000));
            }

            Assert.Equal(ExportTable.RetainedDocumentCap, table.RetainedDocuments);
            await Assert.ThrowsAnyAsync<Exception>(() =>
                oldest.ReadRowGroupAsync(0, CancellationToken.None));
        }
        finally
        {
            table.ResetForTests();
        }
    }

    [Fact]
    public async Task Clear_DisposesRetainedDocuments()
    {
        var table = ExportTable.Instance;
        table.ResetForTests();
        await table.BuildAsync(new TableExportRequest(ExportTable.SyntheticDataset, 30_000));
        var document = table.Document!;

        table.ResetForTests();

        Assert.Equal(ExportBuildState.Empty, table.State);
        Assert.Equal(0, table.RetainedDocuments);
        Assert.Null(table.Document);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            document.ReadRowGroupAsync(0, CancellationToken.None));
    }
}
