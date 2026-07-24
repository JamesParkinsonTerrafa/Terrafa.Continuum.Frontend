#!/usr/bin/env bash
# Emits a portable zip and a .deb from a self-contained publish output.
#
# usage: packaging/linux/package.sh <publish-dir> <version> <rid> [out-dir]
set -euo pipefail

PUBLISH_DIR=${1:?publish dir}
VERSION=${2:?version}
RID=${3:?rid}
OUT_DIR=${4:-dist}

APP_NAME="Terrafa Continuum"
EXECUTABLE="Terrafa.Continuum.Frontend"
PKG="terrafa-continuum"
BASENAME="Terrafa.Continuum-${VERSION}-${RID}"

case "$RID" in
    linux-x64) ARCH=amd64 ;;
    linux-arm64) ARCH=arm64 ;;
    *) echo "unsupported rid: $RID" >&2; exit 1 ;;
esac

mkdir -p "$OUT_DIR"
OUT_DIR=$(cd "$OUT_DIR" && pwd)
PUBLISH_DIR=$(cd "$PUBLISH_DIR" && pwd)

(cd "$PUBLISH_DIR" && zip -qr "$OUT_DIR/${BASENAME}.zip" .)

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

root="$work/$PKG"
mkdir -p "$root/DEBIAN" \
         "$root/usr/lib/$PKG" \
         "$root/usr/bin" \
         "$root/usr/share/applications"

cp -R "$PUBLISH_DIR/." "$root/usr/lib/$PKG/"
chmod +x "$root/usr/lib/$PKG/$EXECUTABLE"
ln -s "/usr/lib/$PKG/$EXECUTABLE" "$root/usr/bin/$PKG"

installed_size=$(du -sk "$root/usr" | cut -f1)

cat > "$root/DEBIAN/control" <<EOF
Package: $PKG
Version: $VERSION
Section: science
Priority: optional
Architecture: $ARCH
Installed-Size: $installed_size
Depends: libc6, libx11-6, libice6, libsm6, libfontconfig1, libgcc-s1, libstdc++6
Maintainer: Terrafa <noreply@terrafa.dev>
Description: $APP_NAME
 Desktop frontend for Terrafa Continuum.
EOF

cat > "$root/usr/share/applications/$PKG.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=$APP_NAME
Comment=Desktop frontend for Terrafa Continuum
Exec=/usr/bin/$PKG
Terminal=false
Categories=Science;Education;
EOF

dpkg-deb --root-owner-group --build "$root" "$OUT_DIR/${PKG}_${VERSION}_${ARCH}.deb" > /dev/null

echo "wrote $OUT_DIR/${BASENAME}.zip"
echo "wrote $OUT_DIR/${PKG}_${VERSION}_${ARCH}.deb"
