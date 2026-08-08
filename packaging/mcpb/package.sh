#!/usr/bin/env bash
# Wraps a self-contained publish of the MCP server in a .mcpb bundle — the one-click install
# format Claude Desktop reads directly, no manual claude_desktop_config.json editing.
#
# macOS only, like the server's session handoff: it reads the desktop app's own keychain entry, so
# a bundle built for another OS would only ever report "not signed in".
#
# usage: packaging/mcpb/package.sh <publish-dir> <version> [out-dir]
set -euo pipefail

PUBLISH_DIR=${1:?publish dir}
VERSION=${2:?version}
OUT_DIR=${3:-dist}

BASENAME="terrafa-continuum-sandbox-${VERSION}"

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
mkdir -p "$OUT_DIR"
OUT_DIR=$(cd "$OUT_DIR" && pwd)

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

bundle="$work/bundle"
mkdir -p "$bundle/server"
cp "$PUBLISH_DIR/terrafa-continuum-mcp" "$bundle/server/"
chmod +x "$bundle/server/terrafa-continuum-mcp"

sed "s|__VERSION__|${VERSION}|g" "$script_dir/manifest.json.template" > "$bundle/manifest.json"

npx -y @anthropic-ai/mcpb pack "$bundle" "$OUT_DIR/${BASENAME}.mcpb"

echo "Wrote $OUT_DIR/${BASENAME}.mcpb"
