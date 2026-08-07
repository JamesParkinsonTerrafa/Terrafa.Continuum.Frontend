// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.Net;
using System.Text.Json.Nodes;
using Avalonia.Threading;

namespace Terrafa.Continuum.Frontend.Services;

public enum SandboxPhase
{
    /// <summary>No Anthropic key. The screen shows the key entry and nothing else works.</summary>
    NoKey,

    /// <summary>The key is being verified and the agent provisioned under it.</summary>
    Connecting,

    /// <summary>Provisioned and idle. Messages can be sent.</summary>
    Ready,

    /// <summary>A turn is in flight — the agent is working in its container.</summary>
    Running,

    /// <summary>The key or the provisioning failed. See <see cref="Note"/>.</summary>
    Failed
}

public enum SandboxEntryKind
{
    User,
    Agent,
    Activity,
    Error
}

public sealed record SandboxEntry(SandboxEntryKind Kind, string Text);

/// <summary>
/// The sandbox screen's state machine, and the client side of its Managed Agents session. One
/// instance for the app, like <see cref="Session"/>: the screen is rebuilt on every theme change
/// and navigation, and a conversation that died with the control would be unusable.
///
/// <para>
/// The shape of a turn: the user's message goes up, the agent works in a container on Anthropic's
/// infrastructure, and every time it wants data it calls the <see cref="SandboxDataTool"/> custom
/// tool — which arrives here as an event, is answered from <see cref="Session.Instance"/>'s own
/// catalogue, and goes back up. Data access therefore rides the operator's existing sign-in; the
/// container never holds a Terrafa credential, and the Anthropic key never touches the data feed.
/// </para>
/// </summary>
public sealed class SandboxAgent
{
    public static SandboxAgent Instance { get; } = new();

    private const string EnvironmentName = "terrafa-continuum-sandbox";
    private const string AgentName = "Terrafa Continuum Sandbox";

    private const string SystemPrompt =
        "You are the sandbox agent inside Terrafa Continuum, an operations console whose screens " +
        "read a tree of datasets: objects containing measure leaves, each leaf a recent series of " +
        "readings with units and, often, a per-reading sigma. The operator uses you to build " +
        "functionality the platform does not have — one-off analyses, joins, models, generated " +
        "files.\n\n" +
        "Data access goes through the query_datasets tool and nowhere else: list datasets, fetch " +
        "a schema, then fetch series for the specific leaf paths you need. Readings arrive oldest " +
        "first, ordered by the dataset's axis column. Request only the leaves and rows a task " +
        "needs.\n\n" +
        "You have a sandboxed Linux container: use bash and Python freely for real computation " +
        "rather than estimating in prose. Write any file the operator should keep to " +
        "/mnt/session/outputs/. Keep replies concise and lead with the result; the operator is " +
        "technical.";

    private readonly Lock gate = new();
    private readonly List<SandboxEntry> transcript = [];
    private readonly HashSet<string> seenEvents = [];

    private AnthropicClient? client;
    private string? agentId;
    private string? environmentId;
    private string? sessionId;
    private CancellationTokenSource? streamCancellation;
    private bool started;

    public ISandboxKeyStore KeyStore { get; set; } = new NullSandboxKeyStore();

    public SandboxPhase Phase { get; private set; } = SandboxPhase.NoKey;

    /// <summary>One sentence on what the phase means right now — shown in the side panel.</summary>
    public string Note { get; private set; } = "";

    public string? SessionId => sessionId;

    public bool CanSend => Phase is SandboxPhase.Ready;

    /// <summary>Raised on the UI thread after any state or transcript change.</summary>
    public event Action? Changed;

    public IReadOnlyList<SandboxEntry> Transcript
    {
        get
        {
            lock (gate) return [.. transcript];
        }
    }

    /// <summary>
    /// Picks up a stored key, once, the first time the screen appears. Fire-and-forget from the
    /// view: connecting can take seconds and the screen shows the phase as it moves.
    /// </summary>
    public void Start()
    {
        if (started) return;
        started = true;
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        string? stored;
        try
        {
            stored = await KeyStore.LoadAsync();
        }
        catch
        {
            stored = null;
        }
        if (stored is not null) await ConnectAsync(stored, persist: false);
    }

    /// <summary>Takes a key, proves it against the API by provisioning, and keeps it on success.</summary>
    public async Task ConnectAsync(string apiKey, bool persist = true)
    {
        apiKey = apiKey.Trim();
        if (apiKey.Length == 0) return;

        Enter(SandboxPhase.Connecting, "verifying key and provisioning the agent");
        var candidate = new AnthropicClient(apiKey);
        try
        {
            var environment = await candidate.EnsureEnvironmentAsync(EnvironmentName, CancellationToken.None);
            var agent = await candidate.EnsureAgentAsync(AgentName, AgentConfiguration(), CancellationToken.None);

            client = candidate;
            environmentId = environment;
            agentId = agent;
            if (persist) await KeyStore.SaveAsync(apiKey);
            Enter(SandboxPhase.Ready, "connected — the agent runs in a sandboxed container on anthropic's infrastructure");
        }
        catch (AnthropicApiException ex) when (ex.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await KeyStore.ClearAsync();
            Enter(SandboxPhase.NoKey, "key rejected — check it and paste it again");
        }
        catch (Exception ex)
        {
            Enter(SandboxPhase.Failed, Describe(ex));
        }
    }

    /// <summary>Forgets the key and everything reached through it.</summary>
    public async Task DisconnectAsync()
    {
        CancelStream();
        client = null;
        agentId = null;
        environmentId = null;
        sessionId = null;
        lock (gate)
        {
            transcript.Clear();
            seenEvents.Clear();
        }
        await KeyStore.ClearAsync();
        Enter(SandboxPhase.NoKey, "");
    }

    /// <summary>Drops the conversation and starts the next message in a fresh container.</summary>
    public void NewSession()
    {
        if (Phase is SandboxPhase.NoKey or SandboxPhase.Connecting) return;
        CancelStream();
        sessionId = null;
        lock (gate)
        {
            transcript.Clear();
            seenEvents.Clear();
        }
        Enter(SandboxPhase.Ready, "new session — the next message starts it");
    }

    public async Task SendAsync(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || client is null || !CanSend) return;

        Append(SandboxEntryKind.User, text);
        Enter(SandboxPhase.Running, "agent working");
        try
        {
            if (sessionId is null)
            {
                sessionId = await client.CreateSessionAsync(
                    agentId!, environmentId!, "Terrafa Continuum sandbox", CancellationToken.None);
                StartStream(sessionId);
            }

            await client.SendEventsAsync(sessionId, [UserMessage(text)], CancellationToken.None);
        }
        catch (Exception ex)
        {
            Append(SandboxEntryKind.Error, Describe(ex));
            Enter(SandboxPhase.Ready, "send failed — the session is still here");
        }
    }

    // ── the event stream ─────────────────────────────────────────────────────────────

    /// <summary>
    /// One long-lived reader per session. Opened before the first message is sent — the stream
    /// only carries events that happen after it opens — and every (re)connect overlaps a history
    /// read with the live tail, deduplicating on event id, because SSE has no replay: whatever
    /// happened while the connection was down exists only in the history.
    /// </summary>
    private void StartStream(string session)
    {
        CancelStream();
        var cancellation = new CancellationTokenSource();
        streamCancellation = cancellation;
        _ = PumpAsync(session, cancellation.Token);
    }

    private async Task PumpAsync(string session, CancellationToken cancellationToken)
    {
        var firstConnect = true;
        while (!cancellationToken.IsCancellationRequested && sessionId == session)
        {
            try
            {
                var streaming = client!.StreamEventsAsync(session, cancellationToken);

                if (!firstConnect)
                {
                    foreach (var missed in await client.ListEventsAsync(session, cancellationToken))
                        Handle(missed);
                }
                firstConnect = false;

                await foreach (var received in streaming) Handle(received);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // A dropped stream is weather, not failure — note it only through the retry pause.
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void CancelStream()
    {
        streamCancellation?.Cancel();
        streamCancellation = null;
    }

    private void Handle(JsonNode received)
    {
        var type = received["type"]?.GetValue<string>() ?? "";
        var id = received["id"]?.GetValue<string>() ?? "";
        if (id.Length > 0)
        {
            lock (gate)
            {
                if (!seenEvents.Add(id)) return;
            }
        }

        if (type.StartsWith("user.", StringComparison.Ordinal)) return; // our own events, echoed
        switch (type)
        {
            case "agent.message":
                var text = ReadText(received["content"]);
                if (text.Length > 0) Append(SandboxEntryKind.Agent, text);
                break;

            case "agent.tool_use":
            case "agent.mcp_tool_use":
                Append(SandboxEntryKind.Activity, DescribeToolUse(received));
                break;

            case "agent.custom_tool_use":
                _ = AnswerToolAsync(received);
                break;

            case "agent.thread_context_compacted":
                Append(SandboxEntryKind.Activity, "context compacted");
                break;

            case "session.error":
                Append(SandboxEntryKind.Error,
                    received["error"]?["message"]?.GetValue<string>()
                    ?? received["message"]?.GetValue<string>() ?? "session error");
                break;
        }

        // Status events arrive with and without their namespace depending on the surface — match
        // on the suffix so both spellings drive the same transitions.
        if (type.EndsWith("status_running", StringComparison.Ordinal))
        {
            Enter(SandboxPhase.Running, "agent working");
        }
        else if (type.EndsWith("status_idle", StringComparison.Ordinal))
        {
            // Idle while waiting on a tool answer is not idle for the operator — the turn goes on.
            var reason = received["stop_reason"]?["type"]?.GetValue<string>();
            if (reason != "requires_action") Enter(SandboxPhase.Ready, "");
        }
        else if (type.EndsWith("status_terminated", StringComparison.Ordinal))
        {
            sessionId = null;
            CancelStream();
            Append(SandboxEntryKind.Activity, "session ended — the next message starts a fresh one");
            Enter(SandboxPhase.Ready, "");
        }
    }

    private async Task AnswerToolAsync(JsonNode toolUse)
    {
        var useId = toolUse["id"]?.GetValue<string>() ?? "";
        var name = toolUse["name"]?.GetValue<string>() ?? "";
        var input = toolUse["input"];
        var session = sessionId;
        if (session is null || client is null) return;

        string answer;
        var failed = false;
        if (name == SandboxDataTool.Name)
        {
            Append(SandboxEntryKind.Activity,
                $"data query · {input?["command"]?.GetValue<string>() ?? "?"}" +
                (input?["dataset"]?.GetValue<string>() is { } dataset ? $" · {dataset}" : ""));
            try
            {
                // The session's catalogue, read at call time: a sign-in mid-conversation swaps the
                // source under the agent exactly as it swaps it under the screens.
                answer = await SandboxDataTool.ExecuteAsync(
                    Session.Instance.Catalog, input, CancellationToken.None);
            }
            catch (Exception ex)
            {
                answer = Describe(ex);
                failed = true;
            }
        }
        else
        {
            answer = $"unknown tool '{name}'";
            failed = true;
        }

        try
        {
            await client.SendEventsAsync(session, [new JsonObject
            {
                ["type"] = "user.custom_tool_result",
                ["custom_tool_use_id"] = useId,
                ["is_error"] = failed,
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = answer })
            }], CancellationToken.None);
        }
        catch (Exception ex)
        {
            Append(SandboxEntryKind.Error, $"could not answer the agent's data query — {Describe(ex)}");
        }
    }

    // ── small pieces ─────────────────────────────────────────────────────────────────

    private static JsonObject UserMessage(string text) => new()
    {
        ["type"] = "user.message",
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text })
    };

    private static string ReadText(JsonNode? content)
    {
        if (content is not JsonArray blocks) return "";
        var pieces = blocks
            .Where(block => block?["type"]?.GetValue<string>() == "text")
            .Select(block => block?["text"]?.GetValue<string>() ?? "");
        return string.Join("\n", pieces).Trim();
    }

    private static string DescribeToolUse(JsonNode toolUse)
    {
        var name = toolUse["name"]?.GetValue<string>() ?? "tool";
        var detail = toolUse["input"]?["command"]?.GetValue<string>()
            ?? toolUse["input"]?["path"]?.GetValue<string>()
            ?? toolUse["input"]?["query"]?.GetValue<string>()
            ?? "";
        if (detail.Length > 100) detail = detail[..100] + "…";
        return detail.Length > 0 ? $"{name} · {detail}" : name;
    }

    private static string Describe(Exception ex) => ex switch
    {
        AnthropicApiException api => $"anthropic api: {api.Message}",
        // Anthropic's CORS allowlist covers /v1/messages but not the Managed Agents endpoints,
        // so a page cannot reach them directly — the browser reports that as a network failure.
        // Not worked around: the honest paths are the desktop app or a Terrafa-side proxy.
        HttpRequestException when OperatingSystem.IsBrowser() =>
            "anthropic's api does not accept managed-agents calls from a web page yet — " +
            "use the terrafa continuum desktop app for the sandbox",
        HttpRequestException => "could not reach api.anthropic.com — check the connection",
        _ => ex.Message
    };

    private void Append(SandboxEntryKind kind, string text)
    {
        lock (gate) transcript.Add(new SandboxEntry(kind, text));
        Announce();
    }

    private void Enter(SandboxPhase phase, string note)
    {
        Phase = phase;
        Note = note;
        Announce();
    }

    private void Announce()
    {
        var handlers = Changed;
        if (handlers is null) return;
        if (Dispatcher.UIThread.CheckAccess()) handlers();
        else Dispatcher.UIThread.Post(() => handlers());
    }

    private static JsonObject AgentConfiguration() => new()
    {
        ["model"] = "claude-opus-5",
        ["system"] = SystemPrompt,
        ["tools"] = new JsonArray(
            new JsonObject
            {
                ["type"] = "agent_toolset_20260401",
                ["default_config"] = new JsonObject { ["enabled"] = true }
            },
            SandboxDataTool.Definition())
    };
}
