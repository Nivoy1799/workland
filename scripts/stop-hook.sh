#!/usr/bin/env bash
# Claude Code Stop hook: before Claude finishes a turn, run verify.sh.
# If it fails, exit 2 so Claude is told to keep working, with the summary as feedback.
# Guard against loops: if Claude is already continuing because of this hook, let it stop.

input="$(cat)"
if echo "$input" | grep -q '"stop_hook_active"[[:space:]]*:[[:space:]]*true'; then
  exit 0
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Only verify if files changed since the last commit, so pure Q&A turns stay cheap.
if git -C "$ROOT" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  if [ -z "$(git -C "$ROOT" status --porcelain)" ]; then
    exit 0
  fi
fi

output="$(bash "$ROOT/scripts/verify.sh" 2>&1)"
status=$?
if [ $status -ne 0 ]; then
  echo "verify.sh failed. Fix before finishing, or write a Blockers note in the spec." >&2
  echo "$output" | sed -n '/---- verify summary ----/,$p' >&2
  exit 2
fi
exit 0
