#!/usr/bin/env bash
# Spec 066: the town clock and weather report (numbers only, /sim).
# Usage: bash scripts/clock-report.sh [--seed 1] [--days 28]
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
dotnet run --project "sim/Tools~/ClockReport" -c Release -- "$@"
