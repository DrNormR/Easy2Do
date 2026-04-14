#!/usr/bin/env bash
set -euo pipefail

WIN_ROOT="/mnt/c/Users/normanr/Easy2Do/Easy2Do"
PUBLISH_DIR="$WIN_ROOT/Easy2Do.Desktop/bin/Release/net8.0/linux-x64/publish"
OUT_DEB="$WIN_ROOT/easy2do_linux_amd64.deb"

PKGROOT="$HOME/easy2do-deb-build"
OUT_TMP="$HOME/easy2do_linux_amd64.deb"

rm -rf "$PKGROOT" "$OUT_TMP"
mkdir -p "$PKGROOT/DEBIAN" "$PKGROOT/opt/easy2do" "$PKGROOT/usr/share/applications"

cp -a "$PUBLISH_DIR/." "$PKGROOT/opt/easy2do/"
cp -f "$WIN_ROOT/packaging/deb/DEBIAN/control" "$PKGROOT/DEBIAN/control"
cp -f "$WIN_ROOT/packaging/deb/DEBIAN/postinst" "$PKGROOT/DEBIAN/postinst"
cp -f "$WIN_ROOT/packaging/deb/usr/share/applications/easy2do.desktop" "$PKGROOT/usr/share/applications/easy2do.desktop"

if [ -f "$WIN_ROOT/Easy2Do/Assets/post-it-green.png" ]; then
  mkdir -p "$PKGROOT/usr/share/icons/hicolor/256x256/apps"
  cp -f "$WIN_ROOT/Easy2Do/Assets/post-it-green.png" "$PKGROOT/usr/share/icons/hicolor/256x256/apps/easy2do.png"
fi

chmod 755 "$PKGROOT/DEBIAN" "$PKGROOT/DEBIAN/postinst"
chmod 644 "$PKGROOT/DEBIAN/control"
chmod +x "$PKGROOT/opt/easy2do/Easy2Do.Desktop"

dpkg-deb --build "$PKGROOT" "$OUT_TMP"
cp -f "$OUT_TMP" "$OUT_DEB"

echo "Built: $OUT_DEB"
