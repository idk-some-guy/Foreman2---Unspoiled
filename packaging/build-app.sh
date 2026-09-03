#!/bin/bash
set -euo pipefail

# Publishes Foreman.Mac and assembles it into an ad-hoc-signed Foreman2.app.
# Usage: build-app.sh <output-dir>
# Produces <output-dir>/Foreman2.app.
#
# Content (Graphics/Mods/Presets/baseCustom.json) stays flat in Contents/MacOS, next to the
# executable, never split into Contents/Resources: every bundled-content read resolves via
# AppContext.BaseDirectory (docs/perf-packaging-reference.md §3d).

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
OUT_DIR="${1:?usage: build-app.sh <output-dir>}"

APP_NAME="Foreman2"
BUNDLE_ID="io.idksome.foreman2.ported"
EXECUTABLE_NAME="Foreman.Mac"
ICON_FILE="Foreman2.icns"

# main's divergence commit drops packaging/unspoiled.env to switch BUNDLE_ID/ICON_FILE to the
# unspoilt identity; parity never has that file, so this is a no-op there.
if [ -f "$SCRIPT_DIR/unspoiled.env" ]; then
  # shellcheck disable=SC1091
  source "$SCRIPT_DIR/unspoiled.env"
fi

mkdir -p "$OUT_DIR"
APP_DIR="$OUT_DIR/$APP_NAME.app"
rm -rf "$APP_DIR"

CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
mkdir -p "$MACOS_DIR" "$RESOURCES_DIR"

VERSION="$(dotnet msbuild "$REPO_ROOT/src/Foreman.Mac/Foreman.Mac.csproj" -getProperty:Version | tail -1 | tr -d '[:space:]')"

dotnet publish "$REPO_ROOT/src/Foreman.Mac/Foreman.Mac.csproj" \
  -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=false -o "$MACOS_DIR"

sed \
  -e "s/__BUNDLE_NAME__/$APP_NAME/g" \
  -e "s/__BUNDLE_ID__/$BUNDLE_ID/g" \
  -e "s/__VERSION__/$VERSION/g" \
  -e "s/__EXECUTABLE__/$EXECUTABLE_NAME/g" \
  -e "s/__ICON_FILE__/$ICON_FILE/g" \
  "$SCRIPT_DIR/Info.plist.template" > "$CONTENTS_DIR/Info.plist"

cp "$SCRIPT_DIR/$ICON_FILE" "$RESOURCES_DIR/$ICON_FILE"

# codesign on this toolchain refuses any directory with a dot in its name once it sits inside
# Contents/MacOS ("bundle format unrecognized"; it tries to validate the directory as nested
# code). Mods/foremanexport_2.0.0 and Mods/foremansavereader_2.0.0 need those exact dotted names
# for Factorio's own mod-version parsing, so Mods/ moves to Contents/Resources/ with a relative
# symlink left behind at its original path. AppContext.BaseDirectory reads still resolve
# Contents/MacOS/Mods/... transparently through the symlink, so no runtime code change is needed.
mv "$MACOS_DIR/Mods" "$RESOURCES_DIR/Mods"
ln -s ../Resources/Mods "$MACOS_DIR/Mods"

codesign --force --deep -s - "$APP_DIR"

echo "built $APP_DIR (version $VERSION)"
