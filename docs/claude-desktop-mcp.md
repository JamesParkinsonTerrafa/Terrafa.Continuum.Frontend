# Claude Desktop → Continuum data tree, no API key

A second on-ramp into the sandbox, alongside the bring-your-own-key SANDBOX screen
(`SandboxView`/`SandboxAgent`, Managed Agents over the Anthropic API). This one runs the
conversation in the Claude Desktop app instead of embedded in Continuum, and needs no Anthropic
API key at all — Claude Desktop authenticates with the operator's own Claude account.

## What it is

`src/Terrafa.Continuum.Frontend.McpServer` is a local MCP (Model Context Protocol) server. Claude
Desktop spawns it over stdio and can call its one tool, `query_datasets` — the same
`list_datasets` / `get_schema` / `get_series` surface `SandboxDataTool` gives the in-app agent,
read-only against `IDatasetCatalog`.

Auth is borrowed, not re-entered: on each call the server restores a session from the **same
macOS keychain entry** (`com.terrafa.continuum` / account `auth`) the desktop app already writes
on sign-in. If nothing is signed in yet, the tool says so plainly rather than guessing or falling
back to demo data — it never touches `StubDatasetCatalog`.

**macOS only.** The desktop head has nowhere durable to put a session on Windows either (see
`Program.cs` in both projects — `KeychainSecretStore`/`KeychainAuthStore` no-op there), so there is
nothing for this process to restore on that platform yet.

## Try it now (no packaging needed)

1. Sign into Terrafa Continuum desktop app once, so a session exists in the keychain.
2. Build the server: `dotnet build src/Terrafa.Continuum.Frontend.McpServer`
3. Add it to Claude Desktop's config (Settings → Developer → Edit Config):

   ```json
   {
     "mcpServers": {
       "terrafa-continuum": {
         "command": "dotnet",
         "args": [
           "/absolute/path/to/src/Terrafa.Continuum.Frontend.McpServer/bin/Debug/net10.0/terrafa-continuum-mcp.dll"
         ]
       }
     }
   }
   ```

4. Quit Claude Desktop completely and reopen it. The connector indicator in the message composer
   should list `terrafa-continuum` with one tool.

## One-click install (`.mcpb`)

`packaging/mcpb/package.sh` wraps a self-contained publish of the server in a `.mcpb` bundle —
Claude Desktop's one-click extension format (double-click to install, no config file editing).
Not yet wired into `.github/workflows/release.yml` — that's a deliberate follow-up, not an
oversight, since it changes what ships in a release. Build one locally:

```sh
dotnet publish src/Terrafa.Continuum.Frontend.McpServer \
  -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true \
  -o /tmp/mcp-publish

packaging/mcpb/package.sh /tmp/mcp-publish 0.0.1 dist
```

Then in Claude Desktop: **Settings → Extensions → Install Extension…** and pick the `.mcpb` file
from `dist/`.

## What it trades away vs. the in-app sandbox

No sandboxed cloud container — tool calls run locally, under the operator's own OS permissions,
not Anthropic's isolated environment. Low blast radius here since the tool surface is read-only
data access with no code execution, but it's a different trust model from the Managed Agents path
and worth knowing about. The conversation also lives in Claude Desktop's own window, not the
Continuum SANDBOX screen — that screen is the bring-your-own-key path; this doc is the other one.

## Known follow-up

`AuthSession.RenewAsync` treats *any* renewal failure — including a transient network blip, not
just genuine credential revocation — as grounds to sign out and revoke the refresh token
server-side (see the catch in `AuthSession.cs` and `CognitoAuthenticator.RunAsync`'s blanket
exception wrapping). This MCP server is a second, independent process now capable of tripping that
same pre-existing path in the background, which could sign the operator out of the *desktop app*
too if it revokes a refresh token during a flaky moment. Worth hardening `CognitoAuthenticator` to
distinguish "could not reach the service" from "credentials rejected" — out of scope for this
change since it touches shared, security-critical auth code, not something to fix as a side effect
of adding a new caller.
