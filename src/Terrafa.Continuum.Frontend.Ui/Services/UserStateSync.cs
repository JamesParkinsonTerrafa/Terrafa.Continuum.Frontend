// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Text.Json;
using Terrafa.Continuum.Frontend.Models;
using Terrafa.Continuum.Frontend.Themes;

namespace Terrafa.Continuum.Frontend.Services;

/// <summary>
/// Keeps a signed-in user's state durable: loads every saved document after sign-in and applies
/// it to the singletons, then listens to their change events and writes dirty kinds back through
/// <see cref="Store"/>, debounced so a drag saves once rather than sixty times a second.
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

    /// <summary>True once this sign-in's documents have been applied. Nothing is marked dirty
    /// before that: the events fired while screens reset around sign-in describe seed state,
    /// and saving them would overwrite the documents about to be loaded.</summary>
    private static bool loaded;

    public static IUserStateStore Store { get; set; } = new NullUserStateStore();

    /// <summary>The catalogue workspace restore re-mounts from. Set where the app builds its
    /// session catalogue; null skips workspace restore rather than failing the rest.</summary>
    public static IDatasetCatalog? Catalog { get; set; }

    /// <summary>How long after the last edit the write goes out.</summary>
    public static TimeSpan DebounceDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Whether saves may happen at all. Replaceable so tests can run signed-in without
    /// a Cognito pool; the app never touches it.</summary>
    public static Func<bool> SignedInProbe { get; set; } = () => AuthSession.Instance.IsSignedIn;

    public static void Start()
    {
        if (started) return;
        started = true;

        AuthSession.Instance.Changed += OnSessionChanged;

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

        // The session may already have been restored from the stored refresh token before this
        // ran — Program starts the restore before Avalonia builds the first frame.
        if (AuthSession.Instance.IsSignedIn) _ = LoadAllAsync();
    }

    private static void OnSessionChanged()
    {
        if (AuthSession.Instance.IsSignedIn)
        {
            _ = LoadAllAsync();
            return;
        }
        lock (Gate)
        {
            Dirty.Clear();
            loaded = false;
        }
    }

    /// <summary>
    /// Reads every kind and applies what exists. A kind that is missing, unreadable or fails to
    /// apply leaves the seeded state for that kind and takes nothing else down with it.
    /// </summary>
    public static async Task LoadAllAsync()
    {
        lock (Gate)
        {
            if (loaded) return;
            Dirty.Clear();
        }
        applying = true;
        try
        {
            var settings = ReadAsync(UserStateKinds.Settings, UserStateJson.Default.SettingsState);
            var functions = ReadAsync(UserStateKinds.Functions, UserStateJson.Default.FunctionsState);
            var workspace = ReadAsync(UserStateKinds.Workspace, UserStateJson.Default.WorkspaceState);
            var network = ReadAsync(UserStateKinds.Network, UserStateJson.Default.NetworkState);
            var dashboard = ReadAsync(UserStateKinds.Dashboard, UserStateJson.Default.DashboardState);
            await Task.WhenAll(settings, functions, workspace, network, dashboard);

            if (await settings is { } settingsState) Apply(() => UserStateMapper.ApplySettings(settingsState));

            // Order matters from here: the network's stages name saved functions, its measures
            // name mounted leaves, and the dashboard resolves against both — but late, by string,
            // so the dashboard would survive any order. The network would not.
            if (await functions is { } functionsState) Apply(() => UserStateMapper.ApplyFunctions(functionsState));
            if (await workspace is { } workspaceState && Catalog is { } catalog)
            {
                try
                {
                    await UserStateMapper.ApplyWorkspaceAsync(workspaceState, catalog);
                }
                catch (Exception)
                {
                    // Whatever mounted before the failure stands; the rest keeps loading.
                }
            }
            if (await network is { } networkState) Apply(() => UserStateMapper.ApplyNetwork(networkState));
            if (await dashboard is { } dashboardState) Apply(() => UserStateMapper.ApplyDashboard(dashboardState));

            // Structure is restored, so the selections are known and the values can be read. This
            // is the only place values are read on the way in — there is no clock behind it and no
            // second pass. A screen showing a stale number is not a case that exists any more.
            if (Catalog is { } readCatalog)
            {
                try
                {
                    await ReadingLoader.LoadAsync(readCatalog);
                }
                catch (Exception)
                {
                    // Per-dataset failures are handled inside. This guards the loader itself.
                }
            }

            lock (Gate) loaded = true;
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
        string kind, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) where T : class
    {
        try
        {
            if (await Store.GetAsync(kind) is not { Length: > 0 } json) return null;
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (Exception)
        {
            // Unreachable store or an unreadable document — the seed state stands.
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
            if (!loaded) return;
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
            loaded = false;
            flushScheduled = false;
        }
        applying = false;
    }
}
