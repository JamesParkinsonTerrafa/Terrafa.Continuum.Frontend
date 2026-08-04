// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Text.Json;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// The durable half of a session: applies a signed-in user's saved documents on the way in, then
/// listens to the singletons' change events and writes dirty kinds back through <see cref="Store"/>,
/// debounced so a drag saves once rather than sixty times a second.
///
/// <para>
/// It does not decide <i>when</i> to load. <see cref="Session"/> owns that, and calls
/// <see cref="LoadAllAsync"/> as one step of a transition it also resets and reads inside. This
/// class used to subscribe to <see cref="AuthSession.Changed"/> itself and start loading the moment
/// a token arrived, which put it in a race with the screen that reset the same singletons on the
/// same event.
/// </para>
///
/// <para>
/// Never started on a snapshot run, for the same reason auth restore is skipped there: the
/// snapshot must draw the seeded state, deterministically. Signed out, nothing loads and nothing
/// saves — the session-only behaviour the app has always had.
/// </para>
/// </summary>
public static class UserStateSync
{
    private static readonly System.Threading.Lock Gate = new();
    private static readonly HashSet<string> Dirty = new(StringComparer.Ordinal);

    private static bool started;
    private static bool flushScheduled;

    /// <summary>True while a loaded document is being applied — the change events that raises
    /// are echoes of the store, not edits, and must not be written straight back.</summary>
    private static bool applying;

    /// <summary>
    /// Whether an edit is worth saving. False from the moment a session begins until its documents
    /// are in place: everything raised in that window describes seed state or the load itself, and
    /// writing any of it back is how an account's work came to be overwritten with the demo seed.
    /// </summary>
    private static bool saving;

    public static IUserStateStore Store { get; set; } = new NullUserStateStore();

    /// <summary>How long after the last edit the write goes out.</summary>
    public static TimeSpan DebounceDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Whether saves may happen at all. Replaceable so tests can run signed-in without
    /// a Cognito pool; the app never touches it.</summary>
    public static Func<bool> SignedInProbe { get; set; } = () => AuthSession.Instance.IsSignedIn;

    /// <summary>
    /// Stops saving and discards pending edits. Called as a session begins; a successful
    /// <see cref="LoadAllAsync"/> is the only thing that turns saving back on.
    /// </summary>
    public static void SuspendSaving()
    {
        lock (Gate)
        {
            Dirty.Clear();
            saving = false;
        }
    }

    /// <summary>Wires the save side. The load side is <see cref="Session"/>'s to call.</summary>
    public static void Start()
    {
        if (started) return;
        started = true;

        SnapSettings.Changed += MarkSettingsDirty;
        UiScaleSettings.Changed += MarkSettingsDirty;
        TypographySettings.Changed += MarkSettingsDirty;
        ThemeManager.Changed += MarkSettingsDirty;
        HintSettings.Changed += MarkSettingsDirty;
        BuilderModeSettings.Changed += MarkSettingsDirty;
        TabLayoutSettings.Changed += MarkSettingsDirty;
        VarianceSettings.Changed += MarkSettingsDirty;
        NavOrderSettings.Changed += MarkSettingsDirty;
        AppearanceSettings.Changed += MarkSettingsDirty;
        ButtonSettings.Changed += MarkSettingsDirty;
        BubbleSettings.Changed += MarkSettingsDirty;
        GrainSettings.IntensityChanged += MarkSettingsDirty;
        GrainSettings.FieldChanged += MarkSettingsDirty;
        TableCacheSettings.Changed += MarkSettingsDirty;

        Dashboard.Instance.Changed += MarkDashboardDirty;
        Dashboard.Instance.Edited += MarkDashboardDirty;
        FunctionLibrary.Instance.Changed += MarkFunctionsDirty;
        Workspace.Instance.Changed += MarkWorkspaceDirty;
        NetworkGraph.Instance.Changed += MarkNetworkDirty;
        NetworkGraph.Instance.Edited += MarkNetworkDirty;
    }

    /// <summary>
    /// Reads every kind and applies what exists, in the one order that works: the network's stages
    /// name saved functions and its measures name mounted leaves, so both have to be in place
    /// before it loads. The dashboard resolves late, by string, and would survive any order.
    ///
    /// <para>
    /// A kind that is missing, unreadable or fails to apply leaves the seeded state for that kind
    /// and takes nothing else down with it. This restores structure only — the values behind it are
    /// read afterwards, by <see cref="ReadingLoader"/>, against the selections this rebuilt.
    /// </para>
    /// </summary>
    public static async Task LoadAllAsync(
        IDatasetCatalog catalog, CancellationToken cancellationToken = default)
    {
        applying = true;
        try
        {
            var settings = ReadAsync(UserStateKinds.Settings, UserStateJson.Default.SettingsState, cancellationToken);
            var functions = ReadAsync(UserStateKinds.Functions, UserStateJson.Default.FunctionsState, cancellationToken);
            var workspace = ReadAsync(UserStateKinds.Workspace, UserStateJson.Default.WorkspaceState, cancellationToken);
            var network = ReadAsync(UserStateKinds.Network, UserStateJson.Default.NetworkState, cancellationToken);
            var dashboard = ReadAsync(UserStateKinds.Dashboard, UserStateJson.Default.DashboardState, cancellationToken);
            await Task.WhenAll(settings, functions, workspace, network, dashboard);

            cancellationToken.ThrowIfCancellationRequested();

            if (await settings is { } settingsState) Apply(() => UserStateMapper.ApplySettings(settingsState));
            if (await functions is { } functionsState) Apply(() => UserStateMapper.ApplyFunctions(functionsState));

            if (await workspace is { } workspaceState)
            {
                try
                {
                    await UserStateMapper.ApplyWorkspaceAsync(workspaceState, catalog, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Whatever mounted before the failure stands; the rest keeps loading.
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (await network is { } networkState) Apply(() => UserStateMapper.ApplyNetwork(networkState));
            if (await dashboard is { } dashboardState) Apply(() => UserStateMapper.ApplyDashboard(dashboardState));

            // The documents are in place, so an edit from here on is the operator's own and worth
            // keeping. Reached only on success: a load that was cancelled or that threw leaves
            // saving off, because what is on screen then is seed state and saving it would publish
            // the seed over the account's real work.
            lock (Gate) saving = true;
        }
        finally
        {
            applying = false;
        }
    }

    /// <summary>Writes every dirty kind now. A failed write keeps its kind dirty for the retry.</summary>
    public static async Task FlushAsync()
    {
        string[] kinds;
        lock (Gate)
        {
            kinds = [.. Dirty];
            Dirty.Clear();
        }

        foreach (var kind in kinds)
        {
            try
            {
                await Store.PutAsync(kind, Serialize(kind));
            }
            catch (Exception)
            {
                lock (Gate) Dirty.Add(kind);
            }
        }

        bool retry;
        lock (Gate) retry = Dirty.Count > 0;
        if (retry) ScheduleFlush(DebounceDelay * 5);
    }

    private static string Serialize(string kind) => kind switch
    {
        UserStateKinds.Settings => JsonSerializer.Serialize(
            UserStateMapper.CaptureSettings(), UserStateJson.Default.SettingsState),
        UserStateKinds.Dashboard => JsonSerializer.Serialize(
            UserStateMapper.CaptureDashboard(), UserStateJson.Default.DashboardState),
        UserStateKinds.Functions => JsonSerializer.Serialize(
            UserStateMapper.CaptureFunctions(), UserStateJson.Default.FunctionsState),
        UserStateKinds.Workspace => JsonSerializer.Serialize(
            UserStateMapper.CaptureWorkspace(), UserStateJson.Default.WorkspaceState),
        _ => JsonSerializer.Serialize(
            UserStateMapper.CaptureNetwork(), UserStateJson.Default.NetworkState)
    };

    private static async Task<T?> ReadAsync<T>(
        string kind,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken) where T : class
    {
        try
        {
            if (await Store.GetAsync(kind, cancellationToken) is not { Length: > 0 } json) return null;
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Unreachable store or an unreadable document — the seed state stands. A store that
            // simply has nothing for this kind returns null and never reaches here.
            return null;
        }
    }

    private static void Apply(Action apply)
    {
        try
        {
            apply();
        }
        catch (Exception)
        {
            // One kind failing to apply must not take the others down with it.
        }
    }

    private static void MarkSettingsDirty() => MarkDirty(UserStateKinds.Settings);
    private static void MarkDashboardDirty() => MarkDirty(UserStateKinds.Dashboard);
    private static void MarkFunctionsDirty() => MarkDirty(UserStateKinds.Functions);
    private static void MarkWorkspaceDirty() => MarkDirty(UserStateKinds.Workspace);
    private static void MarkNetworkDirty() => MarkDirty(UserStateKinds.Network);

    private static void MarkDirty(string kind)
    {
        if (applying) return;
        if (!SignedInProbe()) return;
        lock (Gate)
        {
            if (!saving) return;
            Dirty.Add(kind);
        }
        ScheduleFlush(DebounceDelay);
    }

    private static void ScheduleFlush(TimeSpan delay)
    {
        lock (Gate)
        {
            if (flushScheduled) return;
            flushScheduled = true;
        }
        _ = FlushAfterAsync(delay);
    }

    private static async Task FlushAfterAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
        }
        finally
        {
            lock (Gate) flushScheduled = false;
        }
        await FlushAsync();
    }

    /// <summary>Back to the never-started state, for tests that each wire their own store.</summary>
    public static void ResetForTests()
    {
        lock (Gate)
        {
            Dirty.Clear();
            saving = false;
            flushScheduled = false;
        }
        applying = false;
    }
}
