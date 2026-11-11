#!/usr/bin/env bash
# Spec 064: the town report (Haven Nine in numbers, /sim). Exit 0 every check passes, 2 a check fails, 1 bad arguments.
# Usage: bash scripts/town-report.sh [--out file]
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
dotnet run --project "sim/Tools~/TownReport" -c Release -- "$@"
