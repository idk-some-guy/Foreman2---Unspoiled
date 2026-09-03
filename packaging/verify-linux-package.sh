#!/bin/bash
set -uo pipefail

# Structural verification for a built linux-x64 publish + tar.gz (packaging/build-linux.sh). No live
# app launch, no Linux host required: binary format via `file`, launcher layout, content files
# present, tar round-trip.
# Usage: verify-linux-package.sh <stage-dir> <tar-path>
# Exits 0 if every check passes, 1 otherwise.

STAGE_DIR="${1:?usage: verify-linux-package.sh <stage-dir> <tar-path>}"
TAR_PATH="${2:?usage: verify-linux-package.sh <stage-dir> <tar-path>}"

FAILURES=0
CHECK=0
fail() { CHECK=$((CHECK + 1)); echo "[$CHECK] FAIL: $1"; FAILURES=$((FAILURES + 1)); }
pass() { CHECK=$((CHECK + 1)); echo "[$CHECK] PASS: $1"; }

EXPECTED_EXECUTABLE="Foreman.Mac"
EXPECTED_LAUNCHER="foreman2"

[ -f "$STAGE_DIR/$EXPECTED_EXECUTABLE" ] && pass "executable present in stage dir" || fail "executable missing from stage dir"

if file "$STAGE_DIR/$EXPECTED_EXECUTABLE" | grep -q "ELF 64-bit LSB.*x86-64"; then
  pass "executable is ELF 64-bit x86-64"
else
  fail "executable is not ELF 64-bit x86-64 ($(file "$STAGE_DIR/$EXPECTED_EXECUTABLE" 2>/dev/null))"
fi

[ -f "$STAGE_DIR/$EXPECTED_LAUNCHER" ] && pass "$EXPECTED_LAUNCHER launcher present" || fail "$EXPECTED_LAUNCHER launcher missing"
[ -x "$STAGE_DIR/$EXPECTED_LAUNCHER" ] && pass "$EXPECTED_LAUNCHER launcher is executable" || fail "$EXPECTED_LAUNCHER launcher is not executable"

for d in Graphics Presets Mods; do
  [ -d "$STAGE_DIR/$d" ] && pass "$d/ present in stage dir" || fail "$d/ missing from stage dir"
done
[ -f "$STAGE_DIR/baseCustom.json" ] && pass "baseCustom.json present in stage dir" || fail "baseCustom.json missing from stage dir"
for mod in foremanexport_2.0.0 foremansavereader_2.0.0; do
  [ -f "$STAGE_DIR/Mods/$mod/info.json" ] && pass "Mods/$mod/info.json present" || fail "Mods/$mod/info.json missing"
done

[ -f "$TAR_PATH" ] && pass "tar.gz present" || fail "tar.gz missing at $TAR_PATH"

EXTRACT_DIR="$(mktemp -d)"
cleanup() { rm -rf "$EXTRACT_DIR"; }
trap cleanup EXIT

if tar -xzf "$TAR_PATH" -C "$EXTRACT_DIR" 2>/dev/null; then
  pass "tar.gz extracts cleanly"
  STAGE_BASENAME="$(basename "$STAGE_DIR")"
  EXTRACTED="$EXTRACT_DIR/$STAGE_BASENAME"
  [ -f "$EXTRACTED/$EXPECTED_EXECUTABLE" ] && pass "extracted tar contains the executable" || fail "extracted tar missing the executable"
  [ -x "$EXTRACTED/$EXPECTED_LAUNCHER" ] && pass "extracted launcher keeps its executable bit" || fail "extracted launcher lost its executable bit"
  if file "$EXTRACTED/$EXPECTED_EXECUTABLE" 2>/dev/null | grep -q "ELF 64-bit LSB.*x86-64"; then
    pass "extracted executable is still ELF 64-bit x86-64"
  else
    fail "extracted executable lost its ELF x86-64 format"
  fi
  ORIGINAL_COUNT="$(find "$STAGE_DIR" -type f | wc -l | tr -d '[:space:]')"
  EXTRACTED_COUNT="$(find "$EXTRACTED" -type f | wc -l | tr -d '[:space:]')"
  if [ "$ORIGINAL_COUNT" = "$EXTRACTED_COUNT" ]; then
    pass "tar round-trip preserves file count ($ORIGINAL_COUNT files)"
  else
    fail "tar round-trip file count mismatch: staged $ORIGINAL_COUNT, extracted $EXTRACTED_COUNT"
  fi
else
  fail "tar.gz failed to extract"
fi

echo "----"
if [ "$FAILURES" -eq 0 ]; then
  echo "all checks passed"
  exit 0
else
  echo "$FAILURES check(s) failed"
  exit 1
fi
