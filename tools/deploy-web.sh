#!/usr/bin/env bash
# Publishes the browser head and puts it behind the CloudFront distribution in infra/.
#
# Usage: tools/deploy-web.sh [--skip-publish]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/Terrafa.Continuum.Frontend.Browser/Terrafa.Continuum.Frontend.Browser.csproj"
PUB="${PUBLISH_DIR:-$ROOT/publish/web}"

skip_publish=false
[ "${1:-}" = "--skip-publish" ] && skip_publish=true

for tool in aws dotnet; do
  command -v "$tool" > /dev/null || { echo "$tool is not on PATH" >&2; exit 1; }
done

# Terraform owns both names, and reading them from state rather than the environment is
# what stops a local deploy landing in whichever account the shell happened to be pointed
# at. CI has no state to read -- it is authenticated for the bucket and nothing else -- so
# it passes both in and terraform is never invoked.
if [ -n "${BUCKET:-}" ] && [ -n "${DISTRIBUTION:-}" ]; then
  URL="${URL:-}"
else
  command -v terraform > /dev/null \
    || { echo "terraform is not on PATH; set BUCKET and DISTRIBUTION to deploy without it" >&2; exit 1; }
  BUCKET="$(terraform -chdir="$ROOT/infra" output -raw bucket)"
  DISTRIBUTION="$(terraform -chdir="$ROOT/infra" output -raw distribution_id)"
  URL="$(terraform -chdir="$ROOT/infra" output -raw url)"
fi
echo "==> ${BUCKET} / ${DISTRIBUTION}"

if [ "$skip_publish" = false ]; then
  echo "==> Publishing"
  rm -rf "$PUB"
  dotnet publish "$PROJECT" -c Release -o "$PUB"
fi

[ -f "$PUB/wwwroot/index.html" ] || { echo "no publish output at $PUB/wwwroot" >&2; exit 1; }

content_type() {
  case "$1" in
    wasm) echo "application/wasm" ;;
    js)   echo "text/javascript" ;;
    html) echo "text/html" ;;
    json) echo "application/json" ;;
    css)  echo "text/css" ;;
    map)  echo "application/json" ;;
    *)    echo "application/octet-stream" ;;
  esac
}

# Publish fingerprints most assets as name.<10 chars>.ext. Those can be cached forever;
# the handful published under fixed names (dotnet.js, avalonia.js, storage.js, sw.js)
# cannot, because dotnet.js is the entry point and a stale copy points at framework files
# this deploy has just pruned. Glob is expressive enough to tell the two apart, and
# trailing filters beat leading ones in the AWS CLI, so "plain" is the inverse of "hashed".
HASH='??????????'

# One sync pass per (extension, encoding, class), because `aws s3 sync` applies a single
# --content-type and --cache-control to everything it uploads.
#
# The content types are not fussiness. Left to guess, the CLI resolves .wasm from the
# host's mime database: on macOS that yields application/wasm, on an ubuntu runner
# binary/octet-stream, and the browser then refuses the module with a MIME type error that
# reproduces nowhere locally. The .br and .gz siblings take the content type of the file
# they encode with the encoding declared separately -- that pairing is what lets
# encoding.js hand out a 6 MB runtime instead of a 30 MB one.
upload() {
  local src=$1 dst=$2 cache=$3 class=$4
  shift 4
  local ext enc ctype suffix pattern
  for ext in wasm js dat html json css map; do
    ctype="$(content_type "$ext")"
    for enc in identity br gz; do
      case "$enc" in
        identity) suffix="" ;;
        br) suffix=".br" ;;
        gz) suffix=".gz" ;;
      esac
      pattern="*.$ext$suffix"

      # Every pass costs a bucket listing, so skip the ones with nothing to match.
      # Substitution rather than a pipe into grep: find quits on the first hit, and under
      # pipefail the resulting SIGPIPE would read as "no match".
      [ -n "$(find "$src" -maxdepth 1 -name "$pattern" -print -quit)" ] || continue

      local args=(--exclude "*")
      case "$class" in
        hashed) args+=(--include "*.$HASH.$ext$suffix") ;;
        plain)  args+=(--include "$pattern" --exclude "*.$HASH.$ext$suffix") ;;
        all)    args+=(--include "$pattern") ;;
      esac
      args+=(--content-type "$ctype" --cache-control "$cache" --only-show-errors "$@")
      case "$enc" in
        br) args+=(--content-encoding "br") ;;
        gz) args+=(--content-encoding "gzip") ;;
      esac

      aws s3 sync "$src" "$dst" "${args[@]}"
    done
  done
}

echo "==> Uploading _framework"
upload "$PUB/wwwroot/_framework" "s3://$BUCKET/_framework" "public, max-age=31536000, immutable" hashed
upload "$PUB/wwwroot/_framework" "s3://$BUCKET/_framework" "no-cache" plain

# Trailing --exclude wins over the leading --include, so this pass skips the tree above.
echo "==> Uploading entry points"
upload "$PUB/wwwroot" "s3://$BUCKET" "no-cache" all --exclude "_framework/*"

# Fingerprinted names mean a stale object is never served, but without a prune the bucket
# gains a full runtime on every deploy.
echo "==> Pruning"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

aws s3api list-objects-v2 --bucket "$BUCKET" --output text --query 'Contents[].Key' \
  | tr '\t' '\n' | sed '/^None$/d;/^$/d' | sort > "$tmp/remote"
(cd "$PUB/wwwroot" && find . -type f | sed 's|^\./||' | sort) > "$tmp/local"
comm -23 "$tmp/remote" "$tmp/local" > "$tmp/stale"

if [ -s "$tmp/stale" ]; then
  echo "    removing $(wc -l < "$tmp/stale" | tr -d ' ') stale objects"
  xargs -I{} -P 8 aws s3 rm "s3://$BUCKET/{}" < "$tmp/stale" > /dev/null
fi

# Everything that can go stale is already uploaded no-cache and revalidates at the edge, so
# this is belt and braces: it only bites if an earlier deploy put a long max-age on one of
# these names. Derived from the publish output rather than hardcoded, so it keeps working
# on whatever the SDK stops fingerprinting next. The trailing * covers the .br and .gz
# siblings, which are the keys encoding.js actually sends viewers to.
echo "==> Invalidating"
paths=('/' '/index.html*' '/main.js*')
while IFS= read -r f; do
  paths+=("/_framework/$f*")
done < <(cd "$PUB/wwwroot/_framework" \
  && ls | grep -vE '\.(br|gz|map)$' | grep -vE "\.[a-z0-9]{10}\.[a-zA-Z0-9]+$" || true)

aws cloudfront create-invalidation \
  --distribution-id "$DISTRIBUTION" \
  --paths "${paths[@]}" \
  --query 'Invalidation.Id' --output text

echo "==> deployed${URL:+ to $URL}"
