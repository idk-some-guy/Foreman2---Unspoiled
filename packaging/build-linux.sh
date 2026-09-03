#!/bin/bash
set -euo pipefail

# Publishes Foreman.Mac self-contained for linux-x64 and packages it as a tar.gz with a flat
# launcher layout, then runs structural checks. No .desktop/icon registration this phase - ships as
# a tar.gz users extract and run directly (deferred to a later packaging pass once a real Linux
# host can verify a desktop-entry install).
# Usage: build-linux.sh [output-dir]  (default: packaging/out, gitignored)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
OUT_DIR="${1:-$SCRIPT_DIR/out}"

APP_NAME="Foreman2"
LAUNCHER_NAME="foreman2"

VERSION="$(dotnet msbuild "$REPO_ROOT/src/Foreman.Mac/Foreman.Mac.csproj" -getProperty:Version | tail -1 | tr -d '[:space:]')"

mkdir -p "$OUT_DIR"
STAGE_DIR="$OUT_DIR/$APP_NAME-linux-x64"
rm -rf "$STAGE_DIR"
mkdir -p "$STAGE_DIR"

dotnet publish "$REPO_ROOT/src/Foreman.Mac/Foreman.Mac.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=false -o "$STAGE_DIR"

cat > "$STAGE_DIR/$LAUNCHER_NAME" <<'EOF'
#!/bin/sh
# Flat self-contained launch layout (packaging/build-linux.sh) - no .desktop/icon registration yet;
# extract this tar.gz anywhere and run this script.
DIR="$(cd "$(dirname "$0")" && pwd)"
exec "$DIR/Foreman.Mac" "$@"
EOF
chmod +x "$STAGE_DIR/$LAUNCHER_NAME"

TAR_PATH="$OUT_DIR/$APP_NAME-$VERSION-linux-x64.tar.gz"
rm -f "$TAR_PATH"
tar -czf "$TAR_PATH" -C "$OUT_DIR" "$(basename "$STAGE_DIR")"

echo "built $TAR_PATH (version $VERSION)"

"$SCRIPT_DIR/verify-linux-package.sh" "$STAGE_DIR" "$TAR_PATH"
