#!/bin/bash
set -euo pipefail

# End-to-end packaging pipeline: publish + bundle, dmg, structural verification.
# Usage: build-all.sh [output-dir]  (default: packaging/out, gitignored)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="${1:-$SCRIPT_DIR/out}"

"$SCRIPT_DIR/build-app.sh" "$OUT_DIR"
"$SCRIPT_DIR/build-dmg.sh" "$OUT_DIR/Foreman2.app" "$OUT_DIR/Foreman2.dmg"
"$SCRIPT_DIR/verify-bundle.sh" "$OUT_DIR/Foreman2.app" "$OUT_DIR/Foreman2.dmg"
