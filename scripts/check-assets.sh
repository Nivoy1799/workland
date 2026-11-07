#!/usr/bin/env bash
# Spec 015: licence manifest and size check for game/Assets/ThirdParty.
# Usage: scripts/check-assets.sh [--self-test] [assets-dir] [manifest]
# Fails when (1) a pack folder has no manifest row, (2) a manifest row has no folder,
# (3) a licence is not on the allow-list, (4) the folder exceeds the size cap.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ALLOWED_LICENCES="CC0-1.0"
MAX_KB="${CHECK_ASSETS_MAX_KB:-102400}"   # 100 MB

check() {
  local dir="$1" manifest="$2" max_kb="$3" failed=0
  if [ ! -f "$manifest" ]; then
    echo "check-assets: FAIL manifest missing: $manifest"
    return 1
  fi

  # Manifest rows: | Folder | Pack | Creator | Licence | Source | Imported |
  local rows
  rows="$(grep -E '^\|' "$manifest" | grep -vE '^\|([[:space:]]*:?-+:?[[:space:]]*\|)+[[:space:]]*$' | grep -viE '^\|[[:space:]]*Folder[[:space:]]*\|' | tr -d '\r')"

  # Rule 1: every <Creator>/<Pack> folder has a row.
  local creator pack rel
  for creator in "$dir"/*/; do
    [ -d "$creator" ] || continue
    for pack in "$creator"*/; do
      [ -d "$pack" ] || continue
      rel="$(basename "$creator")/$(basename "$pack")"
      if ! printf '%s\n' "$rows" | awk -F'|' -v f="$rel" '{ g=$2; gsub(/^[ \t]+|[ \t]+$/, "", g); if (g == f) found=1 } END { exit !found }'; then
        echo "check-assets: FAIL rule 1: folder $rel has no manifest row"
        failed=1
      fi
    done
  done

  # Loose files directly under ThirdParty/ or ThirdParty/<Creator>/ are not allowed
  # (only ASSETS.md and .meta files).
  local f
  for f in "$dir"/* "$dir"/*/*; do
    [ -f "$f" ] || continue
    case "$(basename "$f")" in ASSETS.md|*.meta) continue ;; esac
    echo "check-assets: FAIL loose file outside a <Creator>/<Pack> folder: ${f#"$dir"/}"
    failed=1
  done

  # Rules 2 and 3: every row points at a folder and has an allowed licence.
  local line folder licence ok a
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    folder="$(printf '%s' "$line" | awk -F'|' '{ g=$2; gsub(/^[ \t]+|[ \t]+$/, "", g); print g }')"
    licence="$(printf '%s' "$line" | awk -F'|' '{ g=$5; gsub(/^[ \t]+|[ \t]+$/, "", g); print g }')"
    case "$folder" in
      ""|*/*/*|/*|*/) echo "check-assets: FAIL manifest folder '$folder' must be exactly <Creator>/<Pack>"; failed=1; continue ;;
      */*) ;;
      *) echo "check-assets: FAIL manifest folder '$folder' must be exactly <Creator>/<Pack>"; failed=1; continue ;;
    esac
    if [ ! -d "$dir/$folder" ]; then
      echo "check-assets: FAIL rule 2: manifest row $folder has no folder"
      failed=1
    fi
    ok=0
    for a in $ALLOWED_LICENCES; do [ "$licence" = "$a" ] && ok=1; done
    if [ "$ok" -eq 0 ]; then
      echo "check-assets: FAIL rule 3: $folder has licence '$licence' (allowed: $ALLOWED_LICENCES)"
      failed=1
    fi
  done <<EOF
$rows
EOF

  # Rule 4: size cap.
  local kb
  kb="$(du -sk "$dir" | awk '{ print $1 }')"
  if [ "$kb" -gt "$max_kb" ]; then
    echo "check-assets: FAIL rule 4: $dir is ${kb} KB, cap is ${max_kb} KB"
    failed=1
  fi

  [ "$failed" -eq 0 ] && echo "check-assets: OK ($dir, ${kb} KB)"
  return "$failed"
}

self_test() {
  local base="$ROOT/scripts/tests/check-assets" bad=0
  expect() { # name expected-exit dir manifest [max_kb] [expected-text]
    local out rc
    out="$(check "$base/$3" "$base/$4" "${5:-102400}" 2>&1)"; rc=$?
    if { [ "$2" = pass ] && [ "$rc" -ne 0 ]; } || { [ "$2" = fail ] && [ "$rc" -eq 0 ]; } \
       || { [ -n "${6:-}" ] && ! printf '%s' "$out" | grep -q "$6"; }; then
      echo "self-test FAIL: $1"; printf '%s\n' "$out"; bad=1
    else
      echo "self-test ok: $1"
    fi
  }
  expect "valid tree passes"            pass good/assets good/ASSETS.md 102400
  expect "rule 1 folder without row"    fail rule1/assets rule1/ASSETS.md 102400 "rule 1: folder Kenney/Orphan"
  expect "rule 2 row without folder"    fail rule2/assets rule2/ASSETS.md 102400 "rule 2: manifest row Kenney/Ghost"
  expect "rule 3 licence not allowed"   fail rule3/assets rule3/ASSETS.md 102400 "rule 3: Kenney/Paid"
  expect "blank folder cell"            fail blank/assets blank/ASSETS.md 102400 "must be exactly"
  expect "three-segment folder"         fail deep/assets deep/ASSETS.md 102400 "must be exactly"
  expect "loose file under creator"     fail loose/assets loose/ASSETS.md 102400 "loose file"
  expect "rule 4 over size cap"         fail good/assets good/ASSETS.md 0 "rule 4"
  return "$bad"
}

if [ "${1:-}" = "--self-test" ]; then
  self_test
  exit $?
fi

if [ $# -ge 2 ]; then
  check "$1" "$2" "$MAX_KB"
else
  check "$ROOT/game/Assets/ThirdParty" "$ROOT/game/Assets/ThirdParty/ASSETS.md" "$MAX_KB"
fi
exit $?
