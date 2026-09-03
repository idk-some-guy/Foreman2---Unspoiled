#!/bin/bash
set -euo pipefail

# Regenerates packaging/Foreman2.icns from upstream's original Foreman2.ico.
# Recipe: docs/perf-packaging-reference.md §3c.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ICO_PATH="$REPO_ROOT/upstream/Foreman/Foreman2.ico"

if [ ! -f "$ICO_PATH" ]; then
  echo "source icon not found: $ICO_PATH" >&2
  exit 1
fi

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

# -z resize is a no-op passthrough on raw .ico input unless the output format is forced first.
sips -s format png "$ICO_PATH" --out "$WORK_DIR/base.png" >/dev/null

ICONSET_DIR="$WORK_DIR/Foreman2.iconset"
mkdir -p "$ICONSET_DIR"
for sz in 16 32 128 256 512; do
  sips -z "$sz" "$sz" "$WORK_DIR/base.png" --out "$ICONSET_DIR/icon_${sz}x${sz}.png" >/dev/null
  sips -z $((sz * 2)) $((sz * 2)) "$WORK_DIR/base.png" --out "$ICONSET_DIR/icon_${sz}x${sz}@2x.png" >/dev/null
done

iconutil -c icns "$ICONSET_DIR" -o "$SCRIPT_DIR/Foreman2.icns"

echo "wrote $SCRIPT_DIR/Foreman2.icns"
