#!/usr/bin/env bash
# Spec 027: the scripted visual check. Runs each tour with two game windows (1280x720): the tour client
# (-tour) and the observer (-observe), which stands at the tour's observer spot and films the tour player
# from a fixed camera. Each writes <out>/<tour>[-observer]/NN-name.png and sheet.png (sheet.txt maps the
# numbers to names). The tour client starts first and the observer second, so another window has focus while
# the tour runs: the tour moves through its intents, not simulated keys.
#
#   bash scripts/visual-check.sh [--tours look,walk,cook] [--out .verify/visual] [--server local|wsl]
#
#   --server local (default): the observer is a windowed -host (server plus observer), because the WSL link
#                  has MTU 1280 while Tailscale runs and nothing spawns there (spec 003).
#   --server wsl:  the Linux dedicated server build runs in WSL (Ubuntu); both windows -connect to it.
#
# Needs Git Bash on Windows and the development builds (game/Builds/Windows, game/Builds/LinuxServer for wsl):
#   Unity -batchmode -quit -projectPath game -executeMethod Worktown.Game.Editor.Build.WorktownBuild.BuildAll
# Exit code: 0 when every tour wrote its sheet, both windows exited with 0 in time and no log has an Exception.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

TOURS="look,walk,cook"
OUT=".verify/visual"
SERVER="local"
while [ $# -gt 0 ]; do
  case "$1" in
    --tours) TOURS="${2:?--tours needs a list}"; shift 2 ;;
    --out) OUT="${2:?--out needs a directory}"; shift 2 ;;
    --server) SERVER="${2:?--server needs local or wsl}"; shift 2 ;;
    -h|--help) sed -n '2,17p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
case "$SERVER" in local|wsl) ;; *) echo "--server must be local or wsl" >&2; exit 2 ;; esac

GAME="$ROOT/game/Builds/Windows/Worktown.exe"
SERVER_DIR_WSL="/mnt/c/Users/jovin/git/workland/game/Builds/LinuxServer"
SERVER_LOG_WSL="/tmp/worktown-visual-server.log"
if [ ! -f "$GAME" ]; then echo "no client build at $GAME (run WorktownBuild.BuildAll)" >&2; exit 2; fi

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
OUT_WIN="$(cygpath -w "$OUT")"
WINDOW=(-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -skip-whatsnew -force-d3d11)  # D3D11: D3D12 crashes on exit after tours (2026-11-09, see human-inbox)

timeout_for() {
  case "$1" in
    look) echo 360 ;;
    walk) echo 420 ;;
    cook) echo 1260 ;; # spec 030: the griddle part; spec 031: the falafel wrap; spec 031b: the box; spec 032: the register
    *) echo 420 ;;
  esac
}

kill_windows() { taskkill //F //IM Worktown.exe >/dev/null 2>&1 || true; }

# Waits for a background game window; kills it after the deadline. Sets WAIT_CODE to its exit code (124 on
# timeout). Not called in $(...): a subshell cannot wait for this shell's children.
WAIT_CODE=0
wait_window() {
  local pid="$1" deadline="$2"
  while kill -0 "$pid" 2>/dev/null; do
    if [ "$(date +%s)" -ge "$deadline" ]; then
      local winpid
      winpid="$(cat "/proc/$pid/winpid" 2>/dev/null || true)"
      if [ -n "$winpid" ]; then taskkill //F //PID "$winpid" >/dev/null 2>&1 || true; fi
      kill "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null
      WAIT_CODE=124
      return
    fi
    sleep 1
  done
  wait "$pid"
  WAIT_CODE=$?
}

SERVER_PID=""
stop_server() {
  if [ "$SERVER" = "wsl" ]; then
    MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu -- pkill -f WorktownServer.x86_64 >/dev/null 2>&1 || true
    if [ -n "$SERVER_PID" ]; then wait "$SERVER_PID" 2>/dev/null || true; fi
    MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu -- cat "$SERVER_LOG_WSL" > "$OUT/server.log" 2>/dev/null || true
  fi
}
trap 'kill_windows; stop_server' INT TERM

kill_windows
ADDRESS="127.0.0.1"
if [ "$SERVER" = "wsl" ]; then
  MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu -- pkill -f WorktownServer.x86_64 >/dev/null 2>&1 || true
  MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu -- bash -c "cd '$SERVER_DIR_WSL' && ./WorktownServer.x86_64 -batchmode -nographics -server -logFile '$SERVER_LOG_WSL'" &
  SERVER_PID=$!
  started=0
  for _ in $(seq 1 60); do
    if MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu -- grep -q "Server started" "$SERVER_LOG_WSL" 2>/dev/null; then started=1; break; fi
    sleep 1
  done
  if [ "$started" -ne 1 ]; then echo "the WSL server did not start (see $OUT/server.log)" >&2; stop_server; exit 1; fi
  ADDRESS="$(MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu -- hostname -I | awk '{print $1}' | tr -d '\r')"
  echo "WSL server up at $ADDRESS"
fi

failed=0
report=()
IFS=',' read -r -a tour_list <<< "$TOURS"
for tour in "${tour_list[@]}"; do
  case "$tour" in look|walk|cook) ;; *) echo "unknown tour: $tour" >&2; failed=1; continue ;; esac
  rm -rf "${OUT:?}/${tour:?}" "${OUT:?}/${tour:?}-observer"
  tour_log="$OUT/$tour-tour.log"
  observer_log="$OUT/$tour-observer.log"
  deadline=$(( $(date +%s) + $(timeout_for "$tour") ))
  echo "==> tour $tour ($SERVER server)"

  "$GAME" -connect "$ADDRESS" -tour "$tour" -tour-out "$OUT_WIN" "${WINDOW[@]}" -logFile "$(cygpath -w "$tour_log")" &
  tour_pid=$!
  # The observer starts second, so its window is in front and the tour client runs without focus.
  sleep 3
  # Spec 032: the cook tour's host scripts its first orders (a wrap with a modifier, cash with change) and longer patience.
  host_extra=()
  if [ "$tour" = "cook" ]; then host_extra=(-tour-orders); fi
  if [ "$SERVER" = "local" ]; then
    "$GAME" -host -observe "$tour" -tour-out "$OUT_WIN" "${host_extra[@]}" "${WINDOW[@]}" -logFile "$(cygpath -w "$observer_log")" &
  else
    "$GAME" -connect "$ADDRESS" -observe "$tour" -tour-out "$OUT_WIN" "${WINDOW[@]}" -logFile "$(cygpath -w "$observer_log")" &
  fi
  observer_pid=$!

  wait_window "$tour_pid" "$deadline"
  tour_code=$WAIT_CODE
  # The observer quits a few seconds after the tour player left; give it at least 30 s more.
  now=$(date +%s)
  wait_window "$observer_pid" $(( now + 30 > deadline ? now + 30 : deadline ))
  observer_code=$WAIT_CODE
  kill_windows

  exceptions=0
  for log in "$tour_log" "$observer_log"; do
    if [ -f "$log" ]; then exceptions=$(( exceptions + $(grep -c "Exception" "$log" || true) )); fi
  done
  status="ok"
  if [ "$tour_code" != "0" ] || [ "$observer_code" != "0" ] || [ "$exceptions" -gt 0 ] || [ ! -f "$OUT/$tour/sheet.png" ]; then
    status="FAILED"
    failed=1
  fi
  line="$tour: $status (tour exit $tour_code, observer exit $observer_code, Exception lines $exceptions)"
  for sheet in "$OUT/$tour/sheet.png" "$OUT/$tour-observer/sheet.png"; do
    if [ -f "$sheet" ]; then line="$line"$'\n'"    $sheet"; fi
  done
  report+=("$line")
done

stop_server
echo
echo "---- visual check ----"
printf '%s\n' "${report[@]}"
echo "logs: $OUT/*.log"
if [ "$failed" -ne 0 ]; then echo "RESULT: FAIL"; exit 1; fi
echo "RESULT: PASS"
