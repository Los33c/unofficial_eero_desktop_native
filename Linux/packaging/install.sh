#!/bin/sh
# Installs eero Desktop from this folder for the current user (no root needed):
#   ~/.local/lib/eero-desktop, a launcher in ~/.local/bin, menu entry and icons.
# Run ./install.sh --uninstall to remove it again.
set -eu
HERE="$(cd "$(dirname "$0")" && pwd)"
PREFIX="${PREFIX:-$HOME/.local}"
ID=io.github.los33.EeroDesktop
if [ "${1:-}" = "--uninstall" ]; then
  rm -rf "$PREFIX/lib/eero-desktop" "$PREFIX/bin/eero-desktop" "$PREFIX/share/applications/$ID.desktop" "$PREFIX/share/metainfo/$ID.metainfo.xml"
  find "$PREFIX/share/icons/hicolor" -name "$ID.png" -delete 2>/dev/null || true
  echo "eero Desktop removed. Your settings remain in ~/.local/share/eero-desktop."
  exit 0
fi
mkdir -p "$PREFIX/lib/eero-desktop" "$PREFIX/bin" "$PREFIX/share/applications" "$PREFIX/share/metainfo"
cp -R "$HERE/lib/eero-desktop/." "$PREFIX/lib/eero-desktop/"
ln -sf "$PREFIX/lib/eero-desktop/eero-desktop" "$PREFIX/bin/eero-desktop"
sed "s|^Exec=.*|Exec=$PREFIX/bin/eero-desktop|" "$HERE/share/applications/$ID.desktop" > "$PREFIX/share/applications/$ID.desktop"
cp "$HERE/share/metainfo/$ID.metainfo.xml" "$PREFIX/share/metainfo/"
cp -R "$HERE/share/icons" "$PREFIX/share/"
command -v update-desktop-database >/dev/null && update-desktop-database "$PREFIX/share/applications" 2>/dev/null || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t "$PREFIX/share/icons/hicolor" 2>/dev/null || true
echo "eero Desktop installed. Open it from your app menu or run: $PREFIX/bin/eero-desktop"
