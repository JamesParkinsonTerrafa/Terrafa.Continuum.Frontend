#!/usr/bin/env bash
# Wraps a self-contained publish output in a .app bundle, then emits a zip and a
# drag-to-Applications .dmg. Nothing here is signed or notarised — first launch
# needs right-click > Open.
#
# usage: packaging/macos/package.sh <publish-dir> <version> <rid> [out-dir]
set -euo pipefail

PUBLISH_DIR=${1:?publish dir}
VERSION=${2:?version}
RID=${3:?rid}
OUT_DIR=${4:-dist}

APP_NAME="Terrafa Continuum"
EXECUTABLE="Terrafa.Continuum.Frontend"
BUNDLE_ID="com.terrafa.continuum"
BASENAME="Terrafa.Continuum-${VERSION}-${RID}"

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
mkdir -p "$OUT_DIR"
OUT_DIR=$(cd "$OUT_DIR" && pwd)

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

app="$work/${APP_NAME}.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$PUBLISH_DIR/." "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/$EXECUTABLE"

sed -e "s|__APP_NAME__|${APP_NAME}|g" \
    -e "s|__BUNDLE_ID__|${BUNDLE_ID}|g" \
    -e "s|__EXECUTABLE__|${EXECUTABLE}|g" \
    -e "s|__VERSION__|${VERSION}|g" \
    "$script_dir/Info.plist.template" > "$app/Contents/Info.plist"

# ditto (not zip) so the bundle survives the round trip intact.
ditto -c -k --sequesterRsrc --keepParent "$app" "$OUT_DIR/${BASENAME}.zip"

dmg_root="$work/dmg"
mkdir -p "$dmg_root"
cp -R "$app" "$dmg_root/"
ln -s /Applications "$dmg_root/Applications"
hdiutil create \
    -volname "$APP_NAME" \
    -srcfolder "$dmg_root" \
    -ov -quiet -format UDZO \
    "$OUT_DIR/${BASENAME}.dmg"

echo "wrote $OUT_DIR/${BASENAME}.zip"
echo "wrote $OUT_DIR/${BASENAME}.dmg"
