#!/bin/bash
set -euo pipefail

# Packages a built .app into a distributable, compressed dmg with an /Applications symlink.
# Usage: build-dmg.sh <path-to-Foreman2.app> <output-dmg-path>

APP_PATH="${1:?usage: build-dmg.sh <app-path> <dmg-path>}"
DMG_PATH="${2:?usage: build-dmg.sh <app-path> <dmg-path>}"
VOLUME_NAME="Foreman2"

if [ ! -d "$APP_PATH" ]; then
  echo "app bundle not found: $APP_PATH" >&2
  exit 1
fi

STAGING_DIR="$(mktemp -d)"
trap 'rm -rf "$STAGING_DIR"' EXIT

cp -R "$APP_PATH" "$STAGING_DIR/"
ln -s /Applications "$STAGING_DIR/Applications"

rm -f "$DMG_PATH"
hdiutil create -volname "$VOLUME_NAME" -srcfolder "$STAGING_DIR" -ov -format UDZO "$DMG_PATH"

echo "wrote $DMG_PATH"
