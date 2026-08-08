// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Terrafa.Continuum.Frontend.Services;

namespace Terrafa.Continuum.Frontend;

/// <summary>
/// The one tool this server exposes, wired to the same read path <see cref="SandboxDataTool"/>
/// gives the bring-your-own-key sandbox — this is the key-free sibling, called by Claude Desktop
/// over MCP instead of by a Managed Agents session. The command/dataset/paths/max_rows shape is
/// unchanged: only the transport and the auth story differ.
/// </summary>
[McpServerToolType]
public static class DataTools
{
    [McpServerTool(Name = "query_datasets"), Description(
        "Read the Terrafa Continuum data tree. Commands: list_datasets (topics and dataset names), " +
        "get_schema (one dataset's tree of objects and measure leaves, with full leaf paths), " +
        "get_series (recent readings for leaves of one dataset, ordered by the dataset's axis " +
        "column, oldest first). Always list datasets or fetch a schema before asking for series, " +
        "and pass the leaf paths you actually need — the projection is what keeps the answer small.")]
    public static async Task<string> QueryDatasetsAsync(
        AuthSession session,
        IDatasetCatalog catalog,
        [Description("list_datasets | get_schema | get_series")] string command,
        [Description("Dataset name, required for get_schema and get_series.")] string? dataset = null,
        [Description(
            "get_series only: full leaf paths to read, from get_schema. Omit to read every leaf, capped.")]
        string[]? paths = null,
        [Description("get_series only: most recent rows to return per column.")] int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        // Restore lazily rather than only at process start: Claude Desktop can spawn this server
        // before the operator has signed into Continuum, or keep it running across a sign-in that
        // happens afterwards. This is cheap when already signed in — it reads the keychain and
        // returns without touching the network. See AuthSession.TryRestoreAsync.
        if (!session.IsSignedIn) await session.TryRestoreAsync();
        if (!session.IsSignedIn)
        {
            // The SDK sanitizes any exception that is not an McpException down to a generic
            // "an error occurred" — which would leave Claude reporting a dead end instead of
            // telling the operator what to do about it.
            throw new McpException(
                "not signed in to terrafa continuum — open the desktop app and sign in, then try again.");
        }

        var input = new JsonObject { ["command"] = command };
        if (dataset is not null) input["dataset"] = dataset;
        if (paths is not null) input["paths"] = new JsonArray([.. paths.Select(path => (JsonNode)path)]);
        if (maxRows is not null) input["max_rows"] = maxRows.Value;

        try
        {
            return await SandboxDataTool.ExecuteAsync(catalog, input, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            // Same reasoning as above: SandboxDataTool's messages are written for the model to
            // read and correct itself from (bad command, missing dataset) — worth passing through
            // rather than losing to the SDK's default sanitization.
            throw new McpException(ex.Message);
        }
    }
}
