#!/usr/bin/env bash
# Terrafa Continuum installer for macOS and Linux.
#
#   curl -fsSL https://raw.githubusercontent.com/JamesParkinsonTerrafa/Terrafa.Continuum.Frontend/main/install.sh | bash
#
# Files fetched by curl are never given the com.apple.quarantine flag, so the
# app installed this way launches without a Gatekeeper prompt — unlike the
# same build downloaded through a browser.
#
# Environment:
#   TERRAFA_VERSION      install a specific version (default: latest release)
#   TERRAFA_INSTALL_DIR  override install location
set -euo pipefail

REPO="JamesParkinsonTerrafa/Terrafa.Continuum.Frontend"
APP_NAME="Terrafa Continuum"
EXECUTABLE="Terrafa.Continuum.Frontend"
LINUX_CMD="terrafa-continuum"

info() { printf '\033[1;36m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33mwarning:\033[0m %s\n' "$*" >&2; }
die() {
    printf '\033[1;31merror:\033[0m %s\n' "$*" >&2
    exit 1
}

command -v curl > /dev/null 2>&1 || die "curl is required"

os=$(uname -s)
arch=$(uname -m)
case "$os:$arch" in
    Darwin:arm64) RID=osx-arm64 ;;
    Darwin:x86_64) RID=osx-x64 ;;
    Linux:x86_64) RID=linux-x64 ;;
    *) die "unsupported platform: $os $arch" ;;
esac

# --- resolve version --------------------------------------------------------

if [ -n "${TERRAFA_VERSION:-}" ]; then
    TAG="v${TERRAFA_VERSION#v}"
else
    info "Resolving latest release"
    effective=$(curl -fsSLI -o /dev/null -w '%{url_effective}' \
        "https://github.com/$REPO/releases/latest") || die "could not reach GitHub"
    TAG=${effective##*/}
    [ "$TAG" != "latest" ] || die "no releases have been published yet"
fi
VERSION=${TAG#v}

ASSET="Terrafa.Continuum-${VERSION}-${RID}.zip"
BASE="https://github.com/$REPO/releases/download/$TAG"

tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

# --- download and verify ----------------------------------------------------

info "Downloading $ASSET"
curl -fsSL --retry 3 -o "$tmp/$ASSET" "$BASE/$ASSET" \
    || die "no build for $RID in release $TAG"

if curl -fsSL --retry 3 -o "$tmp/SHA256SUMS" "$BASE/SHA256SUMS" 2> /dev/null; then
    expected=$(awk -v f="$ASSET" '$2 == f || $2 == "*" f { print $1 }' "$tmp/SHA256SUMS")
    if [ -n "$expected" ]; then
        if command -v sha256sum > /dev/null 2>&1; then
            actual=$(sha256sum "$tmp/$ASSET" | awk '{print $1}')
        else
            actual=$(shasum -a 256 "$tmp/$ASSET" | awk '{print $1}')
        fi
        [ "$expected" = "$actual" ] || die "checksum mismatch for $ASSET"
        info "Checksum verified"
    else
        warn "no checksum listed for $ASSET"
    fi
else
    warn "no SHA256SUMS in release $TAG — skipping verification"
fi

extract() {
    if command -v unzip > /dev/null 2>&1; then
        unzip -qo "$1" -d "$2"
    elif command -v bsdtar > /dev/null 2>&1; then
        mkdir -p "$2" && bsdtar -xf "$1" -C "$2"
    elif command -v python3 > /dev/null 2>&1; then
        python3 -c 'import sys, zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])' "$1" "$2"
    else
        die "need unzip, bsdtar or python3 to extract the archive"
    fi
}

# --- install ----------------------------------------------------------------

install_macos() {
    local dest explicit=0
    if [ -n "${TERRAFA_INSTALL_DIR:-}" ]; then
        dest=$TERRAFA_INSTALL_DIR
        explicit=1
    else
        dest=/Applications
    fi

    # Create before testing -w: a missing directory is not writable either.
    mkdir -p "$dest" 2> /dev/null || true
    if [ ! -w "$dest" ]; then
        [ "$explicit" -eq 0 ] || die "$dest is not writable"
        warn "/Applications is not writable, using ~/Applications"
        dest="$HOME/Applications"
        mkdir -p "$dest"
    fi

    # ditto keeps bundle metadata that unzip would flatten.
    ditto -x -k "$tmp/$ASSET" "$tmp/extract"
    [ -d "$tmp/extract/$APP_NAME.app" ] || die "archive did not contain $APP_NAME.app"

    if pgrep -x "$EXECUTABLE" > /dev/null 2>&1; then
        warn "$APP_NAME is running — quit it before launching the new version"
    fi

    rm -rf "${dest:?}/$APP_NAME.app"
    ditto "$tmp/extract/$APP_NAME.app" "$dest/$APP_NAME.app"

    # Normally a no-op: curl does not quarantine. Covers the case where someone
    # saved this script from a browser and ran it against a browser-fetched zip.
    xattr -dr com.apple.quarantine "$dest/$APP_NAME.app" 2> /dev/null || true

    info "Continuum $VERSION installed — click \"$APP_NAME\" in $dest to run"
}

install_linux() {
    local bindir=${TERRAFA_INSTALL_DIR:-$HOME/.local/bin}
    local appdir="$HOME/.local/share/applications"

    extract "$tmp/$ASSET" "$tmp/extract"
    [ -f "$tmp/extract/$EXECUTABLE" ] || die "archive did not contain $EXECUTABLE"

    mkdir -p "$bindir" "$appdir"
    install -m 755 "$tmp/extract/$EXECUTABLE" "$bindir/$LINUX_CMD"

    cat > "$appdir/$LINUX_CMD.desktop" << EOF
[Desktop Entry]
Type=Application
Name=$APP_NAME
Comment=Desktop frontend for Terrafa Continuum
Exec=$bindir/$LINUX_CMD
Terminal=false
Categories=Science;Education;
EOF

    case ":$PATH:" in
        *":$bindir:"*)
            info "Continuum $VERSION installed — open \"$APP_NAME\" from your applications menu, or run: $LINUX_CMD"
            ;;
        *)
            info "Continuum $VERSION installed — open \"$APP_NAME\" from your applications menu, or run: $bindir/$LINUX_CMD"
            warn "$bindir is not on your PATH"
            ;;
    esac
}

case "$os" in
    Darwin) install_macos ;;
    Linux) install_linux ;;
esac
