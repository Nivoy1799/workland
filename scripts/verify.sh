#!/usr/bin/env bash
# The single "is it green?" command for humans, agents and CI.
# Runs every check that applies to what exists in the repo right now.
# Parts that do not exist yet are skipped, so this works from day one.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

failed=0
ran=0
summary=()

run() {
  local name="$1"; shift
  ran=$((ran + 1))
  echo "==> $name"
  if "$@"; then
    summary+=("PASS  $name")
  else
    summary+=("FAIL  $name")
    failed=$((failed + 1))
  fi
}

skip() { summary+=("SKIP  $1 ($2)"); }

# 1. Pure C# simulation: build and unit tests
if [ -f sim/Sim.sln ] || ls sim/*.sln >/dev/null 2>&1; then
  if command -v dotnet >/dev/null 2>&1; then
    run "sim: dotnet test" dotnet test sim --nologo --verbosity quiet
  else
    skip "sim: dotnet test" "dotnet not installed"
  fi
else
  skip "sim: dotnet test" "no solution in /sim yet"
fi

# 2. Unity edit-mode tests (batch mode). Needs UNITY_PATH pointing to the Unity editor binary.
if [ -d game/Assets ]; then
  if [ -n "${UNITY_PATH:-}" ] && [ -x "${UNITY_PATH}" ]; then
    run "game: Unity EditMode tests" "$UNITY_PATH" -batchmode -nographics -projectPath game \
      -runTests -testPlatform EditMode -testResults "$ROOT/.verify/unity-editmode.xml" -logFile -
  else
    skip "game: Unity EditMode tests" "set UNITY_PATH to enable"
  fi
else
  skip "game: Unity EditMode tests" "no Unity project yet"
fi

# 3. AI gateway tests
if [ -f ai-gateway/package.json ]; then
  run "ai-gateway: tests" bash -c "cd ai-gateway && npm test --silent"
elif [ -f ai-gateway/pyproject.toml ]; then
  run "ai-gateway: tests" bash -c "cd ai-gateway && python -m pytest -q"
else
  skip "ai-gateway: tests" "not created yet"
fi

# 4. Secrets check: no .env or obvious keys tracked by git
if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  run "secrets: no tracked .env" bash -c '! git ls-files | grep -E "(^|/)\.env$"'
  run "secrets: no API keys in tracked files" bash -c \
    '! git grep -nE "(sk-[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16})" -- ":!*.md" >/dev/null 2>&1'
else
  skip "secrets checks" "not a git repo yet"
fi

echo
echo "---- verify summary ----"
printf '%s\n' "${summary[@]}"
echo "------------------------"
if [ "$failed" -gt 0 ]; then
  echo "RESULT: FAIL ($failed failed, $ran ran)"
  exit 1
fi
echo "RESULT: PASS ($ran ran)"
exit 0
