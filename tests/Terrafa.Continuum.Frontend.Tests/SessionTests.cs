// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Text.Json;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend.Tests;

/// <summary>
/// The session as a state machine. These are the properties the old arrangement could not offer,
/// because reset, load and read lived in three classes that each subscribed to the same event and
/// ran in whatever order the operator's screen history had produced.
/// </summary>
[Collection("workspace")]
public class SessionTests : IDisposable
{
    private const string Alpha = "db.alpha";
    private const string Beta = "db.beta";

    public void Dispose()
    {
        UserStateSync.ResetForTests();
        UserStateSync.Store = new NullUserStateStore();
        UserStateSync.SignedInProbe = () => false;
        Workspace.Instance.Reset(seedDemo: true);
        NetworkGraph.Instance.Reset(seedDemo: true);
        Dashboard.Instance.Reset(seedDemo: true);
        ReadingStore.Instance.Clear();
    }

    // ── the machine ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SignedOut_SettlesOnTheSeededState()
    {
        var (session, _) = NewSession();

        await session.TransitionAsync();

        Assert.Equal(SessionPhase.SignedOut, session.Phase);
        Assert.Null(session.Identity);
        Assert.True(Workspace.Instance.IsMounted("SITE_ALPHA"));
    }

    [Fact]
    public async Task SignedIn_AppliesTheDocumentsAndReadsTheValues()
    {
        var (session, auth) = NewSession();
        Save(Mounts((Alpha, "timestamp", $"{Alpha}.level")));
        await SignInAsync(auth);

        await session.TransitionAsync();

        Assert.Equal(SessionPhase.Ready, session.Phase);
        Assert.Equal("someone@terrafa.com", session.Identity);
        Assert.True(Workspace.Instance.IsMounted(Alpha));
        Assert.Equal([1, 2], Workspace.ReadingAt($"{Alpha}.level")?.History);
    }

    /// <summary>
    /// The property the whole change rests on. The reset is <i>inside</i> the transition, so a
    /// second run from the same identity cannot see a half-reset world or apply documents twice —
    /// it simply lands on the same state. This is what makes a transition safe to re-run without
    /// knowing what ran before it.
    /// </summary>
    [Fact]
    public async Task TheSameTransitionTwice_LandsOnTheSameState()
    {
        var (session, auth) = NewSession();
        Save(Mounts((Alpha, "timestamp", $"{Alpha}.level")));
        await SignInAsync(auth);

        await session.TransitionAsync();
        var first = Describe();

        await session.TransitionAsync();

        Assert.Equal(first, Describe());
        Assert.Equal(SessionPhase.Ready, session.Phase);
    }

    /// <summary>
    /// Startup order stops mattering. The stored token is restored concurrently with the app
    /// starting, so it can land before <see cref="Session.Start"/> reads the identity or after,
    /// through <see cref="AuthSession.Changed"/>. Both roads have to arrive at the same place —
    /// they did not before, and which one won decided whether the operator's work survived.
    /// </summary>
    [Fact]
    public async Task RestoreBeforeTheAppStarts_AndAfterIt_Converge()
    {
        Save(Mounts((Alpha, "timestamp", $"{Alpha}.level")));

        // Restore lands first: the transition finds an identity already in place.
        var (early, earlyAuth) = NewSession();
        await SignInAsync(earlyAuth);
        await early.TransitionAsync();
        var landedEarly = Describe();

        Workspace.Instance.Reset(seedDemo: true);
        NetworkGraph.Instance.Reset(seedDemo: true);
        Dashboard.Instance.Reset(seedDemo: true);
        ReadingStore.Instance.Clear();
        UserStateSync.ResetForTests();

        // Restore lands second: the app settles signed out, then the identity arrives.
        var (late, lateAuth) = NewSession();
        await late.TransitionAsync();
        Assert.Equal(SessionPhase.SignedOut, late.Phase);
        await SignInAsync(lateAuth);
        await late.TransitionAsync();

        Assert.Equal(landedEarly, Describe());
        Assert.Equal(SessionPhase.Ready, late.Phase);
    }

    /// <summary>
    /// A newer transition cancels the one in flight, and the superseded one unwinds without
    /// touching anything. Signing out while an account's documents are still loading must not end
    /// with those documents applied over the signed-out state.
    /// </summary>
    [Fact]
    public async Task ASupersededTransition_DoesNotLandItsDocuments()
    {
        var gate = new TaskCompletionSource();
        var (session, auth) = NewSession(new SlowStore(gate.Task, Mounts((Alpha, "timestamp", $"{Alpha}.level"))));
        await SignInAsync(auth);

        var superseded = session.TransitionAsync();

        // Signed out before the store answers. The second transition supersedes the first.
        auth.SignOut();
        var winner = session.TransitionAsync();
        gate.SetResult();
        await Task.WhenAll(superseded, winner);

        Assert.Equal(SessionPhase.SignedOut, session.Phase);
        Assert.False(Workspace.Instance.IsMounted(Alpha));
        Assert.True(Workspace.Instance.IsMounted("SITE_ALPHA"));
    }

    /// <summary>
    /// The network is pruned of measure cards no value answers for — and a load fills the store one
    /// dataset at a time. Each read records its dataset's axis, and each of those is a workspace
    /// change, so pruning on one of them deleted the cards belonging to every dataset not yet read:
    /// a restored network spanning two datasets silently lost the second. Nothing prunes until the
    /// whole transition has finished now, which is the only point at which "no value at this path"
    /// means anything.
    /// </summary>
    [Fact]
    public async Task ANetworkNodeOnADatasetReadLast_SurvivesTheLoad()
    {
        var (session, auth) = NewSession();

        // Alpha is mounted with no saved axis, so the read resolves one and the workspace changes
        // part way through the load. Beta is referenced by the network only — the dashboard-from-
        // another-machine case — so nothing but its own read can answer for it.
        Save(
            Mounts((Alpha, "", $"{Alpha}.level")),
            Network($"{Beta}.temp"));
        await SignInAsync(auth);

        await session.TransitionAsync();

        Assert.Equal(SessionPhase.Ready, session.Phase);
        Assert.Contains(NetworkGraph.Instance.Nodes, node => node.Key == $"{Beta}.temp");
        Assert.Equal([3, 4], Workspace.ReadingAt($"{Beta}.temp")?.History);
    }

    /// <summary>
    /// A dataset the session refers to and cannot read is reported rather than discarded. An empty
    /// tile used to be the only evidence, and it reads the same whether the dataset is empty or the
    /// service is down.
    /// </summary>
    [Fact]
    public async Task ADatasetThatCannotBeRead_IsReportedRatherThanSwallowed()
    {
        var (session, auth) = NewSession();
        Save(Mounts((Alpha, "timestamp", $"{Alpha}.level")), Network("db.gone.temp"));
        await SignInAsync(auth);

        await session.TransitionAsync();

        Assert.Equal(SessionPhase.Ready, session.Phase);
        // Alpha still read, so one failure does not stop the rest.
        Assert.Equal([1, 2], Workspace.ReadingAt($"{Alpha}.level")?.History);
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    /// <summary>Everything a transition is supposed to have established, as one comparable string.</summary>
    private static string Describe() =>
        string.Join("|",
            string.Join(",", Workspace.Instance.Subtrees
                .Select(subtree => $"{subtree.Dataset}:{subtree.XAxis}:{subtree.LeafCount}")
                .Order(StringComparer.Ordinal)),
            string.Join(",", NetworkGraph.Instance.Nodes.Select(node => node.Id).Order(StringComparer.Ordinal)),
            string.Join(",", Dashboard.Instance.Tiles.Select(tile => tile.Name).Order(StringComparer.Ordinal)),
            Workspace.ReadingAt($"{Alpha}.level")?.History.Count.ToString() ?? "-");

    private static (Session Session, AuthSession Auth) NewSession(IUserStateStore? store = null)
    {
        UserStateSync.ResetForTests();
        UserStateSync.Store = store ?? Documents;
        UserStateSync.SignedInProbe = () => false;

        var auth = new AuthSession(new StubAuthenticator());
        return (new Session(auth) { Catalog = new TwoTableCatalog() }, auth);
    }

    private static async Task SignInAsync(AuthSession auth) =>
        await auth.SignInAsync("someone@terrafa.com", "pw");

    private static readonly InMemoryStore Documents = new();

    private static void Save(WorkspaceState workspace, NetworkState? network = null)
    {
        Documents.Documents.Clear();
        Documents.Documents[UserStateKinds.Workspace] =
            JsonSerializer.Serialize(workspace, UserStateJson.Default.WorkspaceState);
        if (network is not null)
        {
            Documents.Documents[UserStateKinds.Network] =
                JsonSerializer.Serialize(network, UserStateJson.Default.NetworkState);
        }
    }

    private static WorkspaceState Mounts(params (string Dataset, string Axis, string Leaf)[] mounts) =>
        new(1, [.. mounts.Select(mount => new MountState(mount.Dataset, mount.Axis, true, [mount.Leaf]))], null);

    private static NetworkState Network(string leafPath) =>
        new(1, [new NetworkNodeState(leafPath, "Measure", leafPath, 0, 0, "Sum", "", "", false, null)], null);

    private sealed class InMemoryStore : IUserStateStore
    {
        public Dictionary<string, string> Documents { get; } = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(Documents.GetValueOrDefault(kind));

        public Task PutAsync(string kind, string json, CancellationToken cancellationToken = default)
        {
            Documents[kind] = json;
            return Task.CompletedTask;
        }
    }

    /// <summary>Holds every read until the gate opens, so a transition can be caught mid-load.</summary>
    private sealed class SlowStore(Task gate, WorkspaceState workspace) : IUserStateStore
    {
        public async Task<string?> GetAsync(string kind, CancellationToken cancellationToken = default)
        {
            await gate;
            cancellationToken.ThrowIfCancellationRequested();
            return kind == UserStateKinds.Workspace
                ? JsonSerializer.Serialize(workspace, UserStateJson.Default.WorkspaceState)
                : null;
        }

        public Task PutAsync(string kind, string json, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubAuthenticator : IAuthenticator
    {
        public Task<AuthTokens> SignInAsync(string username, string password) =>
            Task.FromResult(new AuthTokens("access", "id", "refresh", 3600));

        public Task<AuthTokens> RefreshAsync(string refreshToken) =>
            Task.FromResult(new AuthTokens("access", "id", refreshToken, 3600));

        public Task RevokeAsync(string refreshToken) => Task.CompletedTask;
    }

    /// <summary>Two datasets carrying real cells, and nothing else. Anything unknown throws.</summary>
    private sealed class TwoTableCatalog : IDatasetCatalog
    {
        public bool IsLive => true;

        public IReadOnlyList<string> Warnings => [];

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAvailableDatasetsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
                new Dictionary<string, IReadOnlyList<string>> { ["db"] = [Alpha, Beta] });

        /// <summary>
        /// Structure only, and — as the real catalogue does — carrying no axis: a schema read on
        /// its own has not ordered anything, so it has nothing to report. Getting this wrong in a
        /// fixture hides the very ordering these tests exist for.
        /// </summary>
        public Task<DatasetSchema> GetSchemaAsync(string dataset, CancellationToken cancellationToken = default) =>
            dataset switch
            {
                Alpha => Task.FromResult(Schema(Alpha, "level", [1, 2]) with { XAxis = "" }),
                Beta => Task.FromResult(Schema(Beta, "temp", [3, 4]) with { XAxis = "" }),
                _ => throw new DataFeedException($"'{dataset}' is not in the catalogue.")
            };

        public Task<DatasetSchema> GetSeriesAsync(DatasetQuery query, CancellationToken cancellationToken = default) =>
            query.Dataset switch
            {
                Alpha => Task.FromResult(Schema(Alpha, "level", [1, 2])),
                Beta => Task.FromResult(Schema(Beta, "temp", [3, 4])),
                _ => throw new DataFeedException($"'{query.Dataset}' is not in the catalogue.")
            };

        private static DatasetSchema Schema(string dataset, string leaf, double[] history)
        {
            var root = new DataTreeNode
            {
                Name = dataset,
                Path = dataset,
                Kind = DataNodeKind.Object,
                Tag = "SUBTREE ROOT"
            };
            root.Children.Add(new DataTreeNode
            {
                Name = leaf,
                Path = $"{dataset}.{leaf}",
                Kind = DataNodeKind.Measure,
                Reading = new Measure
                {
                    Display = history[^1].ToString(),
                    Value = history[^1],
                    History = history,
                    Cells = [.. history.Select(value => (string?)value.ToString())]
                }
            });
            return new DatasetSchema(dataset, "test", "table", "—", "—", "—", root) { XAxis = "timestamp" };
        }
    }
}
