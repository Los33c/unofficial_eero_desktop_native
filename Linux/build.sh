#!/usr/bin/env bash
# Builds eero Desktop (Unofficial) for Linux and packages it.
#
#   ./build.sh                 x64 + arm64: tar.gz, .deb and AppImage in out/
#   ./build.sh --arch x64      one architecture (x64 or arm64)
#   ./build.sh --no-appimage   skip AppImages (no download of the AppImage runtime)
#   ./build.sh --test          also run the self-tests on the x64 build (needs x86_64 host)
#
# Needs: .NET 10 SDK, dpkg-deb (for .deb), mksquashfs (squashfs-tools, for AppImages).
# AppImages download the official AppImage runtime into .build/ on first use.
# The app itself needs GTK 4 (>= 4.12), libadwaita (>= 1.5), WebKitGTK 6.0 and libsecret on the target system.
set -euo pipefail

VERSION=1.1.0
ID=io.github.los33.EeroDesktop
ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/out"
TOOLS="$ROOT/.build/tools"
ARCHES=(x64 arm64)
APPIMAGE=1
RUN_TESTS=0

while [ $# -gt 0 ]; do
  case "$1" in
    --arch) ARCHES=("$2"); shift 2 ;;
    --no-appimage) APPIMAGE=0; shift ;;
    --test) RUN_TESTS=1; shift ;;
    -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

command -v dotnet >/dev/null || { echo "The .NET 10 SDK is required (https://dot.net or your distribution's dotnet-sdk-10.0 package)." >&2; exit 1; }
rm -rf "$OUT"; mkdir -p "$OUT" "$TOOLS"

deb_arch() { [ "$1" = x64 ] && echo amd64 || echo arm64; }
appimage_arch() { [ "$1" = x64 ] && echo x86_64 || echo aarch64; }

# Installs the app's files under a prefix-like root ($1) using lib/eero-desktop + share/.
stage_tree() {
  local publish="$1" dest="$2"
  mkdir -p "$dest/lib/eero-desktop" "$dest/share/applications" "$dest/share/metainfo"
  cp -R "$publish/." "$dest/lib/eero-desktop/"
  chmod 755 "$dest/lib/eero-desktop/eero-desktop"
  cp "$ROOT/packaging/$ID.desktop" "$dest/share/applications/"
  cp "$ROOT/packaging/$ID.metainfo.xml" "$dest/share/metainfo/"
  cp -R "$ROOT/assets/icons" "$dest/share/"
}

for arch in "${ARCHES[@]}"; do
  rid="linux-$arch"
  echo "==> Publishing $rid"
  publish="$OUT/publish/$rid"
  dotnet publish "$ROOT/src/EeroDesktop.csproj" -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true \
    -p:DebugType=none -p:GenerateDocumentationFile=false -o "$publish" -nologo -v quiet
  rm -f "$publish"/*.pdb
  for doc in README.md LICENSE-THIRD-PARTY.txt; do [ -f "$ROOT/$doc" ] && cp "$ROOT/$doc" "$publish/"; done

  # tar.gz: portable folder with a per-user installer.
  name="eero-desktop-$VERSION-linux-$arch"
  echo "==> Packaging $name.tar.gz"
  stage="$OUT/stage/$name"
  stage_tree "$publish" "$stage"
  cp "$ROOT/packaging/install.sh" "$stage/"; chmod 755 "$stage/install.sh"
  ln -s lib/eero-desktop/eero-desktop "$stage/eero-desktop"
  tar -C "$OUT/stage" -czf "$OUT/$name.tar.gz" "$name"

  # .deb: system-wide install with dependencies declared.
  if command -v dpkg-deb >/dev/null; then
    darch="$(deb_arch "$arch")"
    echo "==> Packaging eero-desktop_${VERSION}_$darch.deb"
    deb="$OUT/deb/$darch"
    stage_tree "$publish" "$deb/usr"
    mkdir -p "$deb/usr/bin" "$deb/DEBIAN" "$deb/usr/share/polkit-1/actions"
    cp "$ROOT/packaging/$ID.policy" "$deb/usr/share/polkit-1/actions/"
    ln -s ../lib/eero-desktop/eero-desktop "$deb/usr/bin/eero-desktop"
    size=$(du -sk "$deb/usr" | cut -f1)
    cat > "$deb/DEBIAN/control" <<CONTROL
Package: eero-desktop
Version: $VERSION
Section: net
Priority: optional
Architecture: $darch
Maintainer: Carlos B. (Los33) <noreply@example.invalid>
Installed-Size: $size
Depends: libgtk-4-1 (>= 4.12), libadwaita-1-0 (>= 1.5), libwebkitgtk-6.0-4, libsecret-1-0, libicu78 | libicu77 | libicu76 | libicu74 | libicu72, ca-certificates
Recommends: gnome-keyring | kwalletmanager | keepassxc, wireguard-tools, network-manager, bluez, xdg-desktop-portal
Description: Unofficial desktop client for eero mesh wifi networks
 Independent GTK 4 / libadwaita app to view and manage an eero network:
 status, devices, profiles, security, activity and settings.
 Not affiliated with Amazon or eero.
CONTROL
    dpkg-deb --root-owner-group --build "$deb" "$OUT/eero-desktop_${VERSION}_$darch.deb" >/dev/null
  else
    echo "    dpkg-deb not found; skipping .deb" >&2
  fi

  # AppImage: one file, uses the system's GTK 4 / libadwaita / WebKitGTK.
  if [ "$APPIMAGE" = 1 ]; then
    aarch="$(appimage_arch "$arch")"
    # An AppImage is the AppImage type-2 runtime followed by a squashfs image of the AppDir
    # (what appimagetool produces). The runtime is downloaded once per architecture; nothing downloaded is executed here.
    runtime="$TOOLS/runtime-$aarch"
    if [ ! -s "$runtime" ]; then
      echo "==> Downloading the AppImage runtime for $aarch"
      curl -fsSL -o "$runtime.tmp" "https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-$aarch" && mv "$runtime.tmp" "$runtime"
    fi
    command -v mksquashfs >/dev/null || { echo "mksquashfs (squashfs-tools) is required for AppImages; use --no-appimage to skip." >&2; exit 1; }
    echo "==> Packaging eero-desktop-$VERSION-$aarch.AppImage"
    appdir="$OUT/appdir/$aarch"
    stage_tree "$publish" "$appdir/usr"
    cp "$ROOT/packaging/$ID.desktop" "$appdir/"
    cp "$ROOT/assets/icons/hicolor/256x256/apps/$ID.png" "$appdir/$ID.png"
    cat > "$appdir/AppRun" <<'APPRUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export XDG_DATA_DIRS="$HERE/usr/share:${XDG_DATA_DIRS:-/usr/local/share:/usr/share}"
exec "$HERE/usr/lib/eero-desktop/eero-desktop" "$@"
APPRUN
    chmod 755 "$appdir/AppRun"
    ln -s "$ID.png" "$appdir/.DirIcon"
    image="$OUT/eero-desktop-$VERSION-$aarch.AppImage"
    mksquashfs "$appdir" "$OUT/appdir/$aarch.squashfs" -root-owned -noappend -comp zstd -quiet >/dev/null
    cat "$runtime" "$OUT/appdir/$aarch.squashfs" > "$image"
    chmod 755 "$image"
  fi
done

if [ "$RUN_TESTS" = 1 ] && [ "$(uname -m)" = x86_64 ] && [ -x "$OUT/publish/linux-x64/eero-desktop" ]; then
  echo "==> Running self-tests"
  "$OUT/publish/linux-x64/eero-desktop" --self-test "$OUT/self-test.txt" && echo "    all self-tests passed ($(grep -c '^PASS' "$OUT/self-test.txt") checks)"
fi

rm -rf "$OUT/stage" "$OUT/deb" "$OUT/appdir"
( cd "$OUT" && sha256sum *.tar.gz *.deb *.AppImage 2>/dev/null > SHA256SUMS || true )
echo "==> Done. Packages in $OUT:"
ls -1 "$OUT" | grep -v '^publish$'
