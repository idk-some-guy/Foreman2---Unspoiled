#!/bin/bash
set -uo pipefail

# Structural verification for a built Foreman2.app (and optionally its dmg). No live app launch:
# bundle tree shape, plist parse + key values, icns size coverage, codesign --verify, dmg
# mount/contains/detach.
# Usage: verify-bundle.sh <app-path> [dmg-path]
# Exits 0 if every check passes, 1 otherwise.

APP_PATH="${1:?usage: verify-bundle.sh <app-path> [dmg-path]}"
DMG_PATH="${2:-}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
VERSION="$(dotnet msbuild "$REPO_ROOT/src/Foreman.Mac/Foreman.Mac.csproj" -getProperty:Version | tail -1 | tr -d '[:space:]')"

FAILURES=0
CHECK=0
fail() { CHECK=$((CHECK + 1)); echo "[$CHECK] FAIL: $1"; FAILURES=$((FAILURES + 1)); }
pass() { CHECK=$((CHECK + 1)); echo "[$CHECK] PASS: $1"; }

BUNDLE_ID="io.idksome.foreman2.ported"
ICON_FILE="Foreman2.icns"

# Same switch build-app.sh reads: main's divergence commit drops packaging/unspoiled.env, parity
# never has it.
if [ -f "$SCRIPT_DIR/unspoiled.env" ]; then
  # shellcheck disable=SC1091
  source "$SCRIPT_DIR/unspoiled.env"
fi

EXPECTED_BUNDLE_ID="$BUNDLE_ID"
EXPECTED_NAME="Foreman2"
EXPECTED_EXECUTABLE="Foreman.Mac"
EXPECTED_ICON="$ICON_FILE"

CONTENTS="$APP_PATH/Contents"
MACOS="$CONTENTS/MacOS"
RESOURCES="$CONTENTS/Resources"

[ -f "$CONTENTS/Info.plist" ] && pass "Info.plist present" || fail "Info.plist missing"
[ -f "$MACOS/$EXPECTED_EXECUTABLE" ] && pass "executable present in Contents/MacOS" || fail "executable missing from Contents/MacOS"
[ -f "$RESOURCES/$EXPECTED_ICON" ] && pass "icns present in Contents/Resources" || fail "icns missing from Contents/Resources"

for d in Graphics Presets; do
  [ -d "$MACOS/$d" ] && pass "$d/ landed flat in Contents/MacOS" || fail "$d/ missing from Contents/MacOS"
done
[ -f "$MACOS/baseCustom.json" ] && pass "baseCustom.json landed flat in Contents/MacOS" || fail "baseCustom.json missing from Contents/MacOS"

# Content must stay flat in Contents/MacOS, never split to Resources (the AppContext.BaseDirectory contract).
[ -d "$RESOURCES/Graphics" ] && fail "Graphics wrongly split into Contents/Resources" || pass "content not split into Resources"
[ -d "$RESOURCES/Presets" ] && fail "Presets wrongly split into Contents/Resources" || pass "Presets not split into Resources"

# Mods/ is the one exception: its dotted mod-version folder names break codesign inside
# Contents/MacOS, so build-app.sh relocates the real directory to Contents/Resources/Mods and
# leaves a relative symlink behind. AppContext.BaseDirectory reads still resolve transparently.
if [ -L "$MACOS/Mods" ]; then
  pass "Mods is a symlink at Contents/MacOS/Mods"
else
  fail "Mods is not a symlink at Contents/MacOS/Mods"
fi
if [ -d "$RESOURCES/Mods" ]; then
  pass "Mods/ real directory lives under Contents/Resources"
else
  fail "Mods/ real directory missing from Contents/Resources"
fi
for mod in foremanexport_2.0.0 foremansavereader_2.0.0; do
  [ -f "$MACOS/Mods/$mod/info.json" ] && pass "Mods/$mod/info.json resolves through the symlink" || fail "Mods/$mod/info.json does not resolve through the symlink"
done

# Assert Mods symlink target is relative
MODS_TARGET="$(readlink "$MACOS/Mods")"
if [[ "$MODS_TARGET" != /* ]]; then
  pass "Mods symlink target is relative: $MODS_TARGET"
else
  fail "Mods symlink target is absolute (should be relative): $MODS_TARGET"
fi

if plutil -lint -s "$CONTENTS/Info.plist" >/dev/null 2>&1; then
  pass "Info.plist is valid"
else
  fail "Info.plist failed plutil -lint"
fi

check_plist_value() {
  local key="$1" expected="$2" actual
  actual="$(plutil -extract "$key" raw -o - "$CONTENTS/Info.plist" 2>/dev/null)"
  if [ "$actual" = "$expected" ]; then
    pass "$key == $expected"
  else
    fail "$key expected '$expected' got '$actual'"
  fi
}
check_plist_value CFBundleIdentifier "$EXPECTED_BUNDLE_ID"
check_plist_value CFBundleName "$EXPECTED_NAME"
check_plist_value CFBundleExecutable "$EXPECTED_EXECUTABLE"
check_plist_value CFBundleIconFile "$EXPECTED_ICON"
check_plist_value CFBundleShortVersionString "$VERSION"

# Single EXIT trap covering every scratch resource this script creates below, even ones created later
# in the script (cleanup() reads them by name at exit time, not at trap-registration time) - a
# script exit before the dmg mount block below still detaches MOUNT_DIR if it got that far.
ICNS_WORK="$(mktemp -d)"
COPY_WORK=""
MOUNT_DIR=""
cleanup() {
  rm -rf "$ICNS_WORK"
  [ -n "$COPY_WORK" ] && rm -rf "$COPY_WORK"
  if [ -n "$MOUNT_DIR" ]; then
    hdiutil detach "$MOUNT_DIR" -force -quiet 2>/dev/null || true
    rmdir "$MOUNT_DIR" 2>/dev/null || true
  fi
}
trap cleanup EXIT
if iconutil -c iconset "$RESOURCES/$EXPECTED_ICON" -o "$ICNS_WORK/roundtrip.iconset" 2>/dev/null; then
  pass "icns round-trips through iconutil"
  for sz in 16 32 128 256 512; do
    [ -f "$ICNS_WORK/roundtrip.iconset/icon_${sz}x${sz}.png" ] && pass "icns has ${sz}x${sz}" || fail "icns missing ${sz}x${sz}"
    [ -f "$ICNS_WORK/roundtrip.iconset/icon_${sz}x${sz}@2x.png" ] && pass "icns has ${sz}x${sz}@2x" || fail "icns missing ${sz}x${sz}@2x"
  done
else
  fail "icns failed iconutil round-trip"
fi

if codesign --verify --deep --strict "$APP_PATH" 2>/dev/null; then
  pass "codesign --verify passes"
else
  fail "codesign --verify failed"
fi

# Copy-and-reverify: ensure the bundle and symlink survive relocation
COPY_WORK="$(mktemp -d)"
COPIED_APP="$COPY_WORK/$(basename "$APP_PATH")"
if ditto "$APP_PATH" "$COPIED_APP" >/dev/null 2>&1; then
  pass "copied app to temp directory"
  if codesign --verify --deep --strict "$COPIED_APP" 2>/dev/null; then
    pass "codesign --verify passes on copied app"
  else
    fail "codesign --verify failed on copied app"
  fi
  # Verify symlink still works in the copy
  COPIED_MODS="$COPIED_APP/Contents/MacOS/Mods"
  if [ -L "$COPIED_MODS" ] && [ -f "$COPIED_MODS/foremanexport_2.0.0/info.json" ]; then
    pass "symlink and mod files readable in copied app"
  else
    fail "symlink or mod files not accessible in copied app"
  fi
else
  fail "failed to copy app to temp directory"
fi

if [ -n "$DMG_PATH" ]; then
  if [ -f "$DMG_PATH" ]; then
    MOUNT_DIR="$(mktemp -d)"
    if hdiutil attach "$DMG_PATH" -mountpoint "$MOUNT_DIR" -nobrowse -quiet; then
      pass "dmg mounts"
      [ -d "$MOUNT_DIR/$EXPECTED_NAME.app" ] && pass "dmg contains $EXPECTED_NAME.app" || fail "dmg does not contain $EXPECTED_NAME.app"
      if hdiutil detach "$MOUNT_DIR" -quiet; then
        pass "dmg detaches cleanly"
      else
        fail "dmg detach failed"
      fi
    else
      fail "dmg failed to mount"
    fi
    rmdir "$MOUNT_DIR" 2>/dev/null || true
  else
    fail "dmg path given but file does not exist: $DMG_PATH"
  fi
fi

echo "----"
if [ "$FAILURES" -eq 0 ]; then
  echo "all checks passed"
  exit 0
else
  echo "$FAILURES check(s) failed"
  exit 1
fi
