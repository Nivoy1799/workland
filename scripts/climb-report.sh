#!/usr/bin/env bash
# Spec 052: the climb simulation report (numbers only, /sim). Exit 0 all targets pass, 2 a target fails, 1 bad arguments.
# Usage: bash scripts/climb-report.sh [--players 1,4,16] [--profiles novice,average,expert] [--seeds 5] [--hours 40]
#        [--service-seconds 360] [--between-seconds 60] [--out file]
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
dotnet run --project "sim/Tools~/ClimbReport" -c Release -- "$@"
