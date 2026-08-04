// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Net;
using System.Text.Json;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Tests;

/// <summary>
/// The durable-state contract: every kind round-trips through its wire shape, a document that no
/// longer resolves degrades to skipping rather than throwing, and the sync layer saves exactly
/// what was edited — nothing while applying, nothing signed out, retried after a failed write.
/// All offline, against an in-memory store; the HTTP store is exercised against a stub handler.
/// </summary>
[Collection("workspace")]
public class UserStateTests
{
    private sealed class InMemoryUserStateStore : IUserStateStore
    {
        public Dictionary<string, string> Documents { get; } = new(StringComparer.Ordinal);
        public List<string> Puts { get; } = [];
        public bool FailPuts { get; set; }

        public Task<string?> GetAsync(string kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(Documents.GetValueOrDefault(kind));

        public Task PutAsync(string kind, string json, CancellationToken cancellationToken = default)
        {
            if (FailPuts) throw new DataFeedException($"staged failure saving {kind}");
            Documents[kind] = json;
            Puts.Add(kind);
            return Task.CompletedTask;
        }
    }

    // ── settings ─────────────────────────────────────────────────────────────

    [Fact]
    public void Settings_RoundTrip()
    {
        UiScaleSettings.SetScale(1.2);
        SnapSettings.SetEnabled(false);
        BubbleSettings.SetPopSpeed(1.7);
        TableCacheSettings.SetCacheRows(150_000);
        TableCacheSettings.SetEvictionRows(30_000);
        var captured = UserStateMapper.CaptureSettings();

        UiScaleSettings.SetScale(1.0);
        SnapSettings.SetEnabled(true);
        BubbleSettings.SetPopSpeed(1.0);
        TableCacheSettings.SetCacheRows(100_000);
        TableCacheSettings.SetEvictionRows(25_000);
        UserStateMapper.ApplySettings(captured);

        Assert.Equal(1.2, UiScaleSettings.Scale);
        Assert.False(SnapSettings.Enabled);
        Assert.Equal(1.7, BubbleSettings.PopSpeed);
        Assert.Equal(150_000, TableCacheSettings.CacheRows);
        Assert.Equal(30_000, TableCacheSettings.EvictionRows);

        SnapSettings.SetEnabled(true);
        UiScaleSettings.SetScale(1.0);
        BubbleSettings.SetPopSpeed(1.0);
        TableCacheSettings.SetCacheRows(100_000);
        TableCacheSettings.SetEvictionRows(25_000);
    }

    [Fact]
    public void Settings_MissingFieldsKeepCurrentValues()
    {
        var sparse = JsonSerializer.Deserialize(
            """{"schemaVersion":1,"uiScale":0.9}""", UserStateJson.Default.SettingsState)!;

        var holdBefore = BubbleSettings.HoldSeconds;
        var cacheRowsBefore = TableCacheSettings.CacheRows;
        UserStateMapper.ApplySettings(sparse);

        Assert.Equal(0.9, UiScaleSettings.Scale);
        Assert.Equal(holdBefore, BubbleSettings.HoldSeconds);
        Assert.Equal(cacheRowsBefore, TableCacheSettings.CacheRows);
        UiScaleSettings.SetScale(1.0);
    }

    [Fact]
    public void Settings_TableCacheValuesClampOnApply()
    {
        var hostile = JsonSerializer.Deserialize(
            """{"schemaVersion":1,"tableCacheRows":5,"tableEvictionRows":99999999}""",
            UserStateJson.Default.SettingsState)!;

        UserStateMapper.ApplySettings(hostile);

        Assert.Equal(TableCacheSettings.MinCacheRows, TableCacheSettings.CacheRows);
        Assert.Equal(TableCacheSettings.MaxEvictionRows, TableCacheSettings.EvictionRows);

        TableCacheSettings.SetCacheRows(100_000);
        TableCacheSettings.SetEvictionRows(25_000);
    }

    [Fact]
    public void Settings_SixEntryNavOrder_AppendsCsvExport()
    {
        NavOrderSettings.Set([4, 2, 0, 1, 3, 5]);

        Assert.Equal(
            new[] { 4, 2, 0, 1, 3, 5, 6 },
            NavOrderSettings.OrderFor(NavOrderSettings.Default.Count));

        NavOrderSettings.Set(NavOrderSettings.Default);
    }

    [Fact]
    public void Settings_GarbageNavOrder_Ignored()
    {
        NavOrderSettings.Set(NavOrderSettings.Default);

        NavOrderSettings.Set([99, -3, 42]);

        Assert.Equal(NavOrderSettings.Default, NavOrderSettings.OrderFor(NavOrderSettings.Default.Count));
    }

    // ── dashboard ────────────────────────────────────────────────────────────

    [Fact]
    public void Dashboard_RoundTrip()
    {
        var tile = new DashboardTile(TileKind.Bar, "tile.test_bar");
        tile.Sources.Add(new TileSource(TileSourceKind.Measure, "SITE_ALPHA.tank_farm.tank_01.level", "expiry_risk"));
        tile.Sources.Add(new TileSource(TileSourceKind.Figure, "total_inventory"));
        Dashboard.Instance.Load([new DashboardPlacement { Tile = tile, X = 50, Y = 75, Width = 400, Height = 300 }]);

        var json = JsonSerializer.Serialize(UserStateMapper.CaptureDashboard(), UserStateJson.Default.DashboardState);
        Dashboard.Instance.Load([]);
        UserStateMapper.ApplyDashboard(JsonSerializer.Deserialize(json, UserStateJson.Default.DashboardState)!);

        var placement = Assert.Single(Dashboard.Instance.Placements);
        Assert.Equal("tile.test_bar", placement.Tile.Name);
        Assert.Equal(TileKind.Bar, placement.Tile.Kind);
        Assert.Equal(50, placement.X);
        Assert.Equal(400, placement.Width);
        Assert.Equal(
            [
                new TileSource(TileSourceKind.Measure, "SITE_ALPHA.tank_farm.tank_01.level", "expiry_risk"),
                new TileSource(TileSourceKind.Figure, "total_inventory")
            ],
            placement.Tile.Sources);

        Dashboard.Instance.Reset(seedDemo: true);
    }

    // ── transfer functions ───────────────────────────────────────────────────

    [Fact]
    public void Functions_CompositeReferencingAnotherComposite_RoundTrips()
    {
        var library = FunctionLibrary.Instance;
        var log = library.Find("log")!;
        var add = library.Find("add")!;

        var inner = library.SaveComposite("rt_inner", new FunctionNode(log, [new VariableNode()]));
        library.SaveComposite("rt_outer",
            new FunctionNode(add, [new FunctionNode(inner, [new VariableNode()]), new ConstantNode(2.5)]));

        var json = JsonSerializer.Serialize(UserStateMapper.CaptureFunctions(), UserStateJson.Default.FunctionsState);
        library.LoadUserFunctions([]);
        UserStateMapper.ApplyFunctions(JsonSerializer.Deserialize(json, UserStateJson.Default.FunctionsState)!);

        var outer = library.FindUserFunction("rt_outer");
        Assert.NotNull(outer);
        // log(e) + 2.5 = 3.5 — proves the nested composite resolved to a working definition.
        Assert.Equal(3.5, outer!.ApplyUnary(Math.E), precision: 10);

        library.LoadUserFunctions([]);
    }

    [Fact]
    public void Functions_UnknownFunctionName_IsSkippedWithoutThrowing()
    {
        var state = new FunctionsState(1,
        [
            new UserFunctionState("rt_broken",
                new CompositionNodeState("fn", null, "no_such_function", [new CompositionNodeState("var", null, null, null)])),
            new UserFunctionState("rt_fine",
                new CompositionNodeState("fn", null, "square", [new CompositionNodeState("var", null, null, null)]))
        ]);

        UserStateMapper.ApplyFunctions(state);

        Assert.Null(FunctionLibrary.Instance.FindUserFunction("rt_broken"));
        Assert.NotNull(FunctionLibrary.Instance.FindUserFunction("rt_fine"));
        FunctionLibrary.Instance.LoadUserFunctions([]);
    }

    [Fact]
    public void Functions_AbsurdDepth_IsRefused()
    {
        var node = new CompositionNodeState("var", null, null, null);
        for (var i = 0; i < 100; i++)
            node = new CompositionNodeState("fn", null, "negate", [node]);

        UserStateMapper.ApplyFunctions(new FunctionsState(1, [new UserFunctionState("rt_deep", node)]));

        Assert.Null(FunctionLibrary.Instance.FindUserFunction("rt_deep"));
    }

    // ── network ──────────────────────────────────────────────────────────────

    [Fact]
    public void Network_RoundTrip_AndTransferCounterResumes()
    {
        var state = new NetworkState(1,
        [
            new NetworkNodeState("SITE_ALPHA.tank_farm.tank_01.level", "Measure",
                "SITE_ALPHA.tank_farm.tank_01.level", 75, 125, "Sum", "", "", false, null),
            new NetworkNodeState("transfer:t3", "Transfer", "", 450, 175, "Mean", "exp", "", false, null),
            new NetworkNodeState("figure:total_inventory", "Figure", "total_inventory", 875, 200, "Sum", "", "", false, null)
        ],
        [
            new NetworkEdgeState("SITE_ALPHA.tank_farm.tank_01.level", "transfer:t3", ""),
            new NetworkEdgeState("transfer:t3", "figure:total_inventory", "")
        ]);

        var json = JsonSerializer.Serialize(state, UserStateJson.Default.NetworkState);
        UserStateMapper.ApplyNetwork(JsonSerializer.Deserialize(json, UserStateJson.Default.NetworkState)!);

        var graph = NetworkGraph.Instance;
        Assert.Equal(3, graph.Nodes.Count);
        Assert.Equal(2, graph.Edges.Count);
        var transfer = graph.Find("transfer:t3")!;
        Assert.Equal(TransferCombiner.Mean, transfer.Combiner);
        Assert.Equal("exp", transfer.Stage);

        // A new transfer must not collide with the loaded t3.
        var added = graph.AddTransfer(0, 0);
        Assert.Equal("transfer:t4", added.Id);

        graph.Reset(seedDemo: true);
    }

    [Fact]
    public void Network_EdgeToAMissingNode_IsDropped()
    {
        UserStateMapper.ApplyNetwork(new NetworkState(1,
            [new NetworkNodeState("transfer:t1", "Transfer", "", 0, 0, "Sum", "", "", false, null)],
            [new NetworkEdgeState("transfer:t1", "figure:gone", "")]));

        Assert.Empty(NetworkGraph.Instance.Edges);
        NetworkGraph.Instance.Reset(seedDemo: true);
    }

    // ── workspace ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Workspace_RoundTrip_AgainstTheStubCatalogue()
    {
        var captured = UserStateMapper.CaptureWorkspace();
        Assert.Contains(captured.Mounts!, mount => mount.Dataset == "SITE_ALPHA");

        await UserStateMapper.ApplyWorkspaceAsync(captured, StubDatasetCatalog.Instance);

        var subtree = Workspace.Instance.Find("SITE_ALPHA");
        Assert.NotNull(subtree);
        Assert.NotNull(Workspace.Instance.FindNode("SITE_ALPHA.tank_farm.tank_01.level"));

        Workspace.Instance.Reset(seedDemo: true);
        NetworkGraph.Instance.Reset(seedDemo: true);
        Dashboard.Instance.Reset(seedDemo: true);
    }

    [Fact]
    public async Task Workspace_UnresolvableDataset_IsSkipped()
    {
        var state = new WorkspaceState(1,
            [new MountState("NO_SUCH_DATASET", "", true, ["NO_SUCH_DATASET.leaf"])],
            null);

        await UserStateMapper.ApplyWorkspaceAsync(state, StubDatasetCatalog.Instance);

        Assert.Null(Workspace.Instance.Find("NO_SUCH_DATASET"));
        Workspace.Instance.Reset(seedDemo: true);
        NetworkGraph.Instance.Reset(seedDemo: true);
        Dashboard.Instance.Reset(seedDemo: true);
    }

    // ── sync ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_LoadApplies_WithoutEchoingSaves()
    {
        var store = new InMemoryUserStateStore();
        store.Documents[UserStateKinds.Settings] = JsonSerializer.Serialize(
            UserStateMapper.CaptureSettings() with { UiScale = 1.15 }, UserStateJson.Default.SettingsState);

        UserStateSync.ResetForTests();
        UserStateSync.Store = store;
        UserStateSync.SignedInProbe = () => true;
        UserStateSync.Start();
        try
        {
            await UserStateSync.LoadAllAsync(StubDatasetCatalog.Instance);
            await UserStateSync.FlushAsync();

            Assert.Equal(1.15, UiScaleSettings.Scale);
            Assert.Empty(store.Puts);
        }
        finally
        {
            UiScaleSettings.SetScale(1.0);
            await UserStateSync.FlushAsync();
            UserStateSync.ResetForTests();
            UserStateSync.Store = new NullUserStateStore();
            UserStateSync.SignedInProbe = () => false;
        }
    }

    [Fact]
    public async Task Sync_AnEditAfterLoad_SavesExactlyThatKind()
    {
        var store = new InMemoryUserStateStore();
        UserStateSync.ResetForTests();
        UserStateSync.Store = store;
        UserStateSync.SignedInProbe = () => true;
        UserStateSync.Start();
        try
        {
            await UserStateSync.LoadAllAsync(StubDatasetCatalog.Instance);
            VarianceSettings.Toggle();
            await UserStateSync.FlushAsync();

            Assert.Contains(UserStateKinds.Settings, store.Puts);
            Assert.DoesNotContain(UserStateKinds.Dashboard, store.Puts);
            Assert.True(store.Documents.ContainsKey(UserStateKinds.Settings));
        }
        finally
        {
            VarianceSettings.Toggle();
            await UserStateSync.FlushAsync();
            UserStateSync.ResetForTests();
            UserStateSync.Store = new NullUserStateStore();
            UserStateSync.SignedInProbe = () => false;
        }
    }

    [Fact]
    public async Task Sync_AFailedSave_StaysDirtyAndRetries()
    {
        var store = new InMemoryUserStateStore { FailPuts = true };
        UserStateSync.ResetForTests();
        UserStateSync.Store = store;
        UserStateSync.SignedInProbe = () => true;
        UserStateSync.Start();
        try
        {
            await UserStateSync.LoadAllAsync(StubDatasetCatalog.Instance);
            HintSettings.Toggle();
            await UserStateSync.FlushAsync();
            Assert.Empty(store.Puts);

            store.FailPuts = false;
            await UserStateSync.FlushAsync();
            Assert.Contains(UserStateKinds.Settings, store.Puts);
        }
        finally
        {
            HintSettings.Toggle();
            await UserStateSync.FlushAsync();
            UserStateSync.ResetForTests();
            UserStateSync.Store = new NullUserStateStore();
            UserStateSync.SignedInProbe = () => false;
        }
    }

    [Fact]
    public async Task Sync_SignedOut_MarksNothingDirty()
    {
        var store = new InMemoryUserStateStore();
        UserStateSync.ResetForTests();
        UserStateSync.Store = store;
        UserStateSync.SignedInProbe = () => false;
        UserStateSync.Start();

        BuilderModeSettings.Toggle();
        await UserStateSync.FlushAsync();
        BuilderModeSettings.Toggle();

        Assert.Empty(store.Puts);
        UserStateSync.Store = new NullUserStateStore();
    }

    // ── the HTTP store ───────────────────────────────────────────────────────

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task HttpStore_Get_SendsTheBearerTokenAndReturnsTheBody()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"schemaVersion":1}""")
        });
        using var store = new HttpUserStateStore(new HttpClient(handler), () => Task.FromResult<string?>("token-1"));

        var body = await store.GetAsync("settings");

        Assert.Equal("""{"schemaVersion":1}""", body);
        Assert.Equal("Bearer token-1", handler.LastRequest!.Headers.Authorization!.ToString());
        Assert.EndsWith("/api/user-state/settings", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task HttpStore_Get_TreatsMissingAndRejectedAsNoDocument(HttpStatusCode status)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));
        using var store = new HttpUserStateStore(new HttpClient(handler));

        Assert.Null(await store.GetAsync("settings"));
    }

    [Fact]
    public async Task HttpStore_Get_ThrowsOnAServerFault()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var store = new HttpUserStateStore(new HttpClient(handler));

        await Assert.ThrowsAsync<DataFeedException>(() => store.GetAsync("settings"));
    }

    [Fact]
    public async Task HttpStore_Put_SendsJsonAndThrowsOnFailure()
    {
        var handler = new StubHandler(request =>
            request.Method == HttpMethod.Put
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
        using var store = new HttpUserStateStore(new HttpClient(handler), () => Task.FromResult<string?>("token-2"));

        await store.PutAsync("dashboard", """{"schemaVersion":1}""");
        Assert.Equal("application/json", handler.LastRequest!.Content!.Headers.ContentType!.MediaType);

        var failing = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge));
        using var failingStore = new HttpUserStateStore(new HttpClient(failing));
        await Assert.ThrowsAsync<DataFeedException>(() => failingStore.PutAsync("dashboard", "{}"));
    }
}
