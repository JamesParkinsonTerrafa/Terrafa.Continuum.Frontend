// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;
using Xunit;

namespace Terrafa.Continuum.Frontend.Tests;

/// <summary>
/// A signed-in restore rebuilds mounts from the live catalogue, which serves no demo dataset.
/// The demo site is seeded on every machine, so a saved mount the catalogue cannot answer for
/// must keep the tree the machine already has — the day this slipped, one rebuilt live dataset
/// swapped the demo mount away and every tile wired to it read SOURCE MISSING.
/// </summary>
public class RestoreFallbackTests
{
    /// <summary>A catalogue behaving like the live service: no demo datasets, one live dataset.</summary>
    private sealed class LiveLikeCatalog : IDatasetCatalog
    {
        public bool IsLive => true;

        public IReadOnlyList<string> Warnings => [];

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAvailableDatasetsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["synthetic"] = ["synthetic_dev.parcels"]
                });

        public Task<DatasetSchema> GetSchemaAsync(string dataset, CancellationToken cancellationToken = default) =>
            dataset == "synthetic_dev.parcels"
                ? Task.FromResult(Schema(dataset))
                : throw new InvalidOperationException($"{dataset} is not served");

        public Task<DatasetSchema> GetSeriesAsync(DatasetQuery query, CancellationToken cancellationToken = default) =>
            GetSchemaAsync(query.Dataset, cancellationToken);

        private static DatasetSchema Schema(string dataset)
        {
            var root = new DataTreeNode { Name = dataset, Path = dataset, Kind = DataNodeKind.Object, Tag = "ROOT" };
            root.Children.Add(new DataTreeNode
            {
                Name = "volume",
                Path = $"{dataset}.volume",
                Kind = DataNodeKind.Measure,
                Reading = new Measure { Display = "12480 bbl", Value = 12480, Unit = "bbl" }
            });
            return new DatasetSchema(dataset, "probe", "table", "—", "—", "—", root) { XAxis = "parcel" };
        }
    }

    [Fact]
    public async Task SignedInRestore_DemoWiredTiles_StillResolve()
    {
        // Machine A: the seeded demo workspace + dashboard were captured to the account.
        Workspace.Instance.Reset(seedDemo: true);
        Dashboard.Instance.Reset(seedDemo: true);
        var savedWorkspace = UserStateMapper.CaptureWorkspace();
        var savedDashboard = UserStateMapper.CaptureDashboard();

        // Machine B, signed in: the live catalogue serves none of the demo datasets.
        var catalog = new LiveLikeCatalog();
        await UserStateMapper.ApplyWorkspaceAsync(savedWorkspace, catalog);
        UserStateMapper.ApplyDashboard(savedDashboard);
        await ReadingLoader.LoadAsync(catalog);

        try
        {
            Assert.True(Workspace.Instance.IsMounted("SITE_ALPHA"),
                "the demo mount was dropped by the restore swap");
            Assert.NotNull(Workspace.ReadingAt("SITE_ALPHA.tank_farm.tank_01.level"));
        }
        finally
        {
            Workspace.Instance.Reset(seedDemo: true);
            Dashboard.Instance.Reset(seedDemo: true);
            NetworkGraph.Instance.Reset(seedDemo: true);
        }
    }

    [Fact]
    public async Task SignedInRestore_WithOneLiveMount_KeepsDemoWiredTilesAlive()
    {
        // Machine A: the seeded demo mount plus one live dataset were captured to the account.
        Workspace.Instance.Reset(seedDemo: true);
        Dashboard.Instance.Reset(seedDemo: true);
        var savedDashboard = UserStateMapper.CaptureDashboard();
        var savedWorkspace = new WorkspaceState(1,
            [
                new MountState("SITE_ALPHA", "", true, ["SITE_ALPHA.tank_farm.tank_01.level"]),
                new MountState("synthetic_dev.parcels", "parcel", true, ["synthetic_dev.parcels.volume"])
            ],
            null);

        // Machine B, signed in: the live catalogue serves the live dataset but no demo one.
        var catalog = new LiveLikeCatalog();
        await UserStateMapper.ApplyWorkspaceAsync(savedWorkspace, catalog);
        UserStateMapper.ApplyDashboard(savedDashboard);
        await ReadingLoader.LoadAsync(catalog);

        try
        {
            Assert.NotNull(Workspace.ReadingAt("SITE_ALPHA.tank_farm.tank_01.level"));
        }
        finally
        {
            Workspace.Instance.Reset(seedDemo: true);
            Dashboard.Instance.Reset(seedDemo: true);
            NetworkGraph.Instance.Reset(seedDemo: true);
        }
    }

    [Fact]
    public async Task DemoRestore_AgainstStubCatalog_StillResolves()
    {
        Workspace.Instance.Reset(seedDemo: true);
        Dashboard.Instance.Reset(seedDemo: true);
        var savedWorkspace = UserStateMapper.CaptureWorkspace();

        await UserStateMapper.ApplyWorkspaceAsync(savedWorkspace, StubDatasetCatalog.Instance);
        await ReadingLoader.LoadAsync(StubDatasetCatalog.Instance);

        try
        {
            Assert.NotNull(Workspace.ReadingAt("SITE_ALPHA.tank_farm.tank_01.level"));
        }
        finally
        {
            Workspace.Instance.Reset(seedDemo: true);
            Dashboard.Instance.Reset(seedDemo: true);
            NetworkGraph.Instance.Reset(seedDemo: true);
        }
    }
}
