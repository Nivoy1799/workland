#!/usr/bin/env bash
# Spec 065: the revival report (district levels from the 052b climb, /sim). Exit 0 every target passes, 2 a target fails,
# 1 bad arguments.
# Usage: bash scripts/revival-report.sh [--players 1,4,16] [--seeds 5] [--hours 40] [--out file]
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
dotnet run --project "sim/Tools~/RevivalReport" -c Release -- "$@"
