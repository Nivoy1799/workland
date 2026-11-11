#!/usr/bin/env bash
# Spec 056a: the items the shipped menu can produce, for the offline name bake (/sim, no network).
# Usage: bash scripts/item-list.sh [--out ai-gateway/offline/reachable.v1.json]
#        Prints the count and the set hash; without --out the file text goes to stdout.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
dotnet run --project "sim/Tools~/ItemList" -c Release -- "$@"
