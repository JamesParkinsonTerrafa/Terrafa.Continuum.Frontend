// Copyright (c) 2026 Terrafa Limited. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Terrafa.Continuum.Frontend;
using Terrafa.Continuum.Frontend.Services;

// stdout is the MCP wire — every byte on it must be a JSON-RPC frame. Logs go to stderr, which is
// what Claude Desktop captures into mcp-server-<name>.log rather than treating as protocol noise.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// One AuthSession for the process, restoring from the same keychain entry the desktop head
// writes. Not signing in itself — see DataTools, which restores lazily per call and reports
// plainly when there is nothing to restore yet.
AuthSession.Instance.Store = new KeychainAuthStore();
builder.Services.AddSingleton(AuthSession.Instance);

// HttpDatasetCatalog's default constructor already reads through AuthSession.Instance, so this is
// the live feed with no demo fallback: a data question answered here should never turn out to be
// about the six-dataset seed instead of the operator's real tree.
builder.Services.AddSingleton<IDatasetCatalog>(new HttpDatasetCatalog());

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
