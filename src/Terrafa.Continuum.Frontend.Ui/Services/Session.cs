// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Avalonia.Threading;
using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Services;

public enum SessionPhase
{
    /// <summary>Nobody is signed in. The screens read demo data.</summary>
    SignedOut,

    /// <summary>
    /// The app is starting and the stored token has not answered yet, so it is not known whether
    /// there is an identity to be. Only <see cref="Session.Start"/> enters it, which is what keeps
    /// a snapshot run — which never starts a session — out of it.
    /// </summary>
    Starting,

    /// <summary>An identity has arrived and its state is being established.</summary>
    Loading,

    /// <summary>Documents applied and values read. This is the only phase edits are saved from.</summary>
    Ready,

    /// <summary>The identity is good but its state could not be established. See <see cref="Session.FailureNote"/>.</summary>
    Failed
}

/// <summary>
/// What it means to be signed in as somebody, as one state machine with one owner.
///
/// <code>
///   Starting ──no stored token──▶ SignedOut
///       │                             │
///       └──restored──┐   ┌────identity arrives
///                    ▼   ▼
///                   Loading ──documents, then values──▶ Ready
///                       │                                 │
///                       ├──▶ Failed                        │
///                       └───────identity goes──────────────┴──▶ SignedOut
/// </code>
///
/// <para>
/// Every transition runs <see cref="TransitionAsync"/>, and that method is the whole sequence:
/// reset the singletons to their seed, apply the saved documents, read the values those documents
/// select. It exists because that sequence used to be split across three files that each subscribed
/// to <see cref="AuthSession.Changed"/> independently — <c>MainView</c> did the reset,
/// <c>UserStateSync</c> did the load, <c>SessionDatasetCatalog</c> swapped the catalogue — so which
/// ran first depended on which screens the operator had happened to visit, and a restore landing
/// mid-load had its work reset out from under it. There is nothing to order now: it is one method.
/// </para>
///
/// <para>
/// It is idempotent. Running it twice from the same identity produces the same result, because the
/// reset is <i>inside</i> the transition rather than racing beside it. That is also what makes
/// startup order stop mattering: whether the stored token is restored before or after the app
/// starts, the app converges on the same state — early, and <see cref="Start"/> reads the identity
/// it finds; late, and <see cref="AuthSession.Changed"/> brings it through the same door. Start
/// waits for the restore all the same, so the shell has one uninterrupted state to show a loading
/// screen over rather than settling into signed-out and correcting itself a moment later.
/// </para>
///
/// <para>
/// A newer transition cancels the one in flight. The superseded one unwinds without touching shared
/// state, so a fast sign-out/sign-in pair cannot land the first account's documents over the
/// second's.
/// </para>
/// </summary>
public sealed class Session(AuthSession auth)
{
    public static Session Instance { get; } = new(AuthSession.Instance);

    private readonly Lock gate = new();
    private CancellationTokenSource? inFlight;
    private bool started;

    /// <summary>
    /// The catalogue every read in this session goes through — demo data or the live service,
    /// decided by <see cref="SessionDatasetCatalog"/>. Held here because "the catalogue this app
    /// reads through" is a fact about the session, and having the screens, the restore and the
    /// probe each reach for their own was how they came to disagree about which one was in force.
    /// Settable so a test can drive a session against a stub.
    /// </summary>
    public IDatasetCatalog Catalog { get; set; } = new SessionDatasetCatalog();

    public SessionPhase Phase { get; private set; } = SessionPhase.SignedOut;

    /// <summary>Who this session is for, or null when signed out.</summary>
    public string? Identity { get; private set; }

    /// <summary>Why <see cref="SessionPhase.Failed"/>, in a sentence. Empty otherwise.</summary>
    public string FailureNote { get; private set; } = "";

    /// <summary>
    /// Datasets the session refers to and could not read. A restore reads every dataset the saved
    /// mounts, tiles and network nodes point at; one of them failing does not stop the rest, and it
    /// is reported here rather than discarded — the difference between an empty dataset and an
    /// unreachable one is exactly what an operator needs and used to have no way to see.
    /// </summary>
    public IReadOnlyList<ReadFailure> ReadFailures { get; private set; } = [];

    /// <summary>Raised on every phase change, on the UI thread.</summary>
    public event Action? Changed;

    public bool IsReady => Phase == SessionPhase.Ready;

    /// <summary>
    /// Begins following the signed-in identity, and settles the current one. Called once, where the
    /// app is assembled. A snapshot run deliberately never calls it, which is what keeps the
    /// captured screens on seeded state.
    /// </summary>
    public void Start() => _ = StartAsync();

    /// <summary>
    /// <see cref="Start"/>, as the task it really is. Public so a test can await the start rather
    /// than poll for it.
    /// </summary>
    public async Task StartAsync()
    {
        if (started) return;
        started = true;

        // Entered here rather than left to the transition below, and entered synchronously: the
        // shell reads this phase the moment it is built, and "starting" is the honest answer until
        // the stored token has said whether there is an account to load.
        Enter(SessionPhase.Starting, null);
        auth.Changed += OnIdentityChanged;

        // Restoring the stored token is part of starting, not something that races beside it. Each
        // head used to kick the restore off next to this call, so the app settled into signed-out,
        // painted the demo seed, and then replaced it with the operator's own workspace a second
        // later when the token landed. Waiting here is what lets one loading screen cover the lot.
        try
        {
            await auth.TryRestoreAsync();
        }
        catch
        {
            // A restore that cannot be made is a signed-out app, and the transition below says so.
        }

        // A restored identity has already raised Changed, and the transition that event started
        // owns the state now — running another here would reset what it is part way through loading.
        if (auth.Identity is null) await TransitionAsync();
    }

    private void OnIdentityChanged() => _ = TransitionAsync();

    /// <summary>
    /// Becomes whoever <see cref="AuthSession"/> currently says is signed in. Public so a test can
    /// drive the machine directly rather than through a Cognito pool.
    /// </summary>
    public async Task TransitionAsync()
    {
        var cancellation = new CancellationTokenSource();

        // Cancelling the previous transition and retiring a finished one both happen under this
        // lock, so the two can never interleave into a Cancel on an already-disposed source.
        lock (gate)
        {
            inFlight?.Cancel();
            inFlight = cancellation;
        }

        var token = cancellation.Token;
        var identity = auth.Identity;

        try
        {
            // Nothing may be saved until this session's own documents are in place. The reset below
            // raises change events describing seed state, and writing those back is how one
            // account's work used to be overwritten with the demo seed.
            UserStateSync.SuspendSaving();

            // Held across the whole transition, not just the reset. The network prunes measure
            // nodes that no value answers for, and a load fills the store one dataset at a time —
            // pruning against that half-filled store deleted the cards belonging to whichever
            // dataset had not been read yet.
            using var batch = NetworkGraph.Instance.Suspend();

            Workspace.Instance.Reset(seedDemo: true);
            NetworkGraph.Instance.Reset(seedDemo: true);
            Dashboard.Instance.Reset(seedDemo: true);

            if (identity is null)
            {
                // Announced once. There is no loading to report when there is nothing to load, and
                // every announcement costs the screens a rebuild.
                Enter(SessionPhase.SignedOut, null);
                return;
            }

            Enter(SessionPhase.Loading, identity);

            // Order matters and is a real dependency: the network's stages name saved functions and
            // its measures name mounted leaves, and the dashboard resolves against both. Values
            // come last, because the selections have to exist before there is anything to read.
            await UserStateSync.LoadAllAsync(Catalog, token);
            token.ThrowIfCancellationRequested();

            var failures = await ReadingLoader.LoadAsync(Catalog, token);
            token.ThrowIfCancellationRequested();

            ReadFailures = failures;
            Enter(SessionPhase.Ready, identity);
        }
        catch (OperationCanceledException)
        {
            // Superseded. The newer transition owns the state now, and it has already reset it —
            // announcing anything here would describe a session that no longer exists.
        }
        catch (Exception ex)
        {
            FailureNote = ReadingLoader.Describe(ex);
            Enter(SessionPhase.Failed, identity);
        }
        finally
        {
            lock (gate)
            {
                // Clearing the slot is what guarantees nothing will cancel this source after it is
                // disposed: a later transition can only cancel whatever the slot still holds.
                if (ReferenceEquals(inFlight, cancellation)) inFlight = null;
                cancellation.Dispose();
            }
        }
    }

    private void Enter(SessionPhase phase, string? identity)
    {
        Phase = phase;
        Identity = identity;
        if (phase != SessionPhase.Failed) FailureNote = "";
        if (phase != SessionPhase.Ready) ReadFailures = [];

        var handlers = Changed;
        if (handlers is null) return;
        if (Dispatcher.UIThread.CheckAccess()) handlers();
        else Dispatcher.UIThread.Post(() => handlers());
    }
}
