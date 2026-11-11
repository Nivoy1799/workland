#!/usr/bin/env bash
# Spec 056b: the offline dish names (D38). Run from anywhere; works in the repo root.
#   bash scripts/bake-names.sh bake [--limit 500] [--dry-run]   ask the LLM for every reachable item not cached yet
#   bash scripts/bake-names.sh publish                          write the approved names for the game
# Needs GATEWAY_UPSTREAM_BASE_URL, GATEWAY_UPSTREAM_API_KEY and GATEWAY_MODEL (a dry run and publish need only
# GATEWAY_MODEL), from the shell or from .env (gitignored; loaded here when present). Never GATEWAY_SERVICE_TOKEN.
# Exit: bake 0 done, 1 bad setup, 3 five upstream errors in a row; publish 0 written, 1 bad setup, 2 a check failed.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
if [ -f .env ]; then
  set -a
  # shellcheck disable=SC1091
  . ./.env
  set +a
fi
dotnet run --project ai-gateway/tools/NameBake -c Release -- "$@"
