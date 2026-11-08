#!/bin/bash
# Builds "eero Desktop.app" (unofficial eero network manager) on a Mac.
#
#   ./build.sh            universal (Apple silicon + Intel) app, plus per-architecture zips, in out/
#   ./build.sh --test     also run the self-tests
#   ./build.sh --sign "Developer ID Application: Name (TEAMID)"   sign with your certificate instead of ad-hoc
#
# Needs Xcode or the Command Line Tools (xcode-select --install); macOS 13 or later to run the app.
set -euo pipefail

VERSION=1.1.0
ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/out"
APP_NAME="eero Desktop"
IDENTITY="-"
RUN_TESTS=0
while [ $# -gt 0 ]; do
  case "$1" in
    --test) RUN_TESTS=1; shift ;;
    --sign) IDENTITY="$2"; shift 2 ;;
    -h|--help) sed -n '2,9p' "$0"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done
command -v swift >/dev/null || { echo "Swift is required: install Xcode or run xcode-select --install." >&2; exit 1; }

cd "$ROOT"
rm -rf "$OUT"; mkdir -p "$OUT"
for triple in arm64-apple-macosx13.0 x86_64-apple-macosx13.0; do
  echo "==> Building $triple"
  swift build -c release --triple "$triple" --product EeroDesktop >/dev/null
done
ARM="$(swift build -c release --triple arm64-apple-macosx13.0 --show-bin-path)/EeroDesktop"
X86="$(swift build -c release --triple x86_64-apple-macosx13.0 --show-bin-path)/EeroDesktop"

# App icon: Resources/app-icon.png → AppIcon.icns
ICONSET="$OUT/AppIcon.iconset"; mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z $size $size Resources/app-icon.png --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2)); [ $double -le 1024 ] && sips -z $double $double Resources/app-icon.png --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$OUT/AppIcon.icns"; rm -rf "$ICONSET"

make_app() { # make_app <dest .app> <binary...>
  local app="$1"; shift
  rm -rf "$app"; mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  if [ $# -gt 1 ]; then lipo -create "$@" -output "$app/Contents/MacOS/EeroDesktop"; else cp "$1" "$app/Contents/MacOS/EeroDesktop"; fi
  chmod 755 "$app/Contents/MacOS/EeroDesktop"
  sed -e "s/__VERSION__/$VERSION/g" Resources/Info.plist > "$app/Contents/Info.plist"
  plutil -lint "$app/Contents/Info.plist" >/dev/null
  printf 'APPL????' > "$app/Contents/PkgInfo"
  cp "$OUT/AppIcon.icns" "$app/Contents/Resources/AppIcon.icns"
  cp Resources/app-icon.png "$app/Contents/Resources/"
  mkdir -p "$app/Contents/Resources/products"; cp Resources/products/*.png Resources/products/sources.json "$app/Contents/Resources/products/"
  [ -f "$ROOT/LICENSE-THIRD-PARTY.txt" ] && cp "$ROOT/LICENSE-THIRD-PARTY.txt" "$app/Contents/Resources/"
  # Ad-hoc signing ("-") is required for Apple silicon; pass --sign to use a Developer ID instead.
  local opts=(--force --sign "$IDENTITY" --timestamp=none)
  [ "$IDENTITY" != "-" ] && opts=(--force --sign "$IDENTITY" --options runtime --timestamp)
  codesign "${opts[@]}" "$app"
  codesign --verify --strict "$app"
}

echo "==> Assembling apps"
make_app "$OUT/universal/$APP_NAME.app" "$ARM" "$X86"
make_app "$OUT/arm64/$APP_NAME.app" "$ARM"
make_app "$OUT/x86_64/$APP_NAME.app" "$X86"
lipo -info "$OUT/universal/$APP_NAME.app/Contents/MacOS/EeroDesktop"

echo "==> Zipping"
for flavor in universal arm64 x86_64; do
  ditto -c -k --sequesterRsrc --keepParent "$OUT/$flavor/$APP_NAME.app" "$OUT/eero-Desktop-$VERSION-macOS-$flavor.zip"
done
rm -f "$OUT/AppIcon.icns"

if [ "$RUN_TESTS" = 1 ]; then
  echo "==> Running self-tests"
  "$OUT/universal/$APP_NAME.app/Contents/MacOS/EeroDesktop" --self-test "$OUT/self-test.txt" && echo "    all self-tests passed ($(grep -c '^PASS' "$OUT/self-test.txt") checks)"
fi
( cd "$OUT" && shasum -a 256 *.zip > SHA256SUMS )
echo "==> Done:"; ls -1 "$OUT"
echo "First launch of an ad-hoc signed app: right-click the app and choose Open (or System Settings > Privacy & Security > Open Anyway)."
