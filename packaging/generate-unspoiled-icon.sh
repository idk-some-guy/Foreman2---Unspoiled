#!/bin/bash
set -euo pipefail

# Regenerates the Unspoiled .icns from the badged master icon (upstream's icon with a
# half-fresh/half-spoiled apple badge, top right). Same recipe as generate-icon.sh, but the
# source is already a PNG so it skips the .ico conversion step.
#
# Known simplification (spec, phase 8): the spec asks for a simplified badge at the small
# 16-32px sizes for legibility. This script downscales the same master to every size instead;
# a dedicated small-size badge variant is left for phase 9's design pass.
#
# Output lands in packaging/restructure/unspoiled-divergence/, not packaging/ itself: parity's
# snapshot is taken from the tree as it stands before the restructure, and packaging/ must stay
# untouched there. create-public-branches.sh copies the output into packaging/ on main-unspoiled
# only, as that branch's divergence commit.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SOURCE_PNG="$REPO_ROOT/src/Foreman.Mac/Assets/unspoiled-icon-512.png"
OUT_DIR="$SCRIPT_DIR/restructure/unspoiled-divergence"
OUT_ICNS="$OUT_DIR/Foreman2-unspoiled.icns"

if [ ! -f "$SOURCE_PNG" ]; then
  echo "source icon not found: $SOURCE_PNG" >&2
  exit 1
fi

mkdir -p "$OUT_DIR"

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

ICONSET_DIR="$WORK_DIR/Foreman2-unspoiled.iconset"
mkdir -p "$ICONSET_DIR"
for sz in 16 32 128 256 512; do
  sips -z "$sz" "$sz" "$SOURCE_PNG" --out "$ICONSET_DIR/icon_${sz}x${sz}.png" >/dev/null
  sips -z $((sz * 2)) $((sz * 2)) "$SOURCE_PNG" --out "$ICONSET_DIR/icon_${sz}x${sz}@2x.png" >/dev/null
done

iconutil -c icns "$ICONSET_DIR" -o "$OUT_ICNS"

echo "wrote $OUT_ICNS"
