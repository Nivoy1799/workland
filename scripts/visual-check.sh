#!/usr/bin/env bash
# Spec 027: the scripted visual check. Runs each tour with two game windows (1280x720): the tour client
# (-tour) and the observer (-observe), which stands at the tour's observer spot and films the tour player
# from a fixed camera. Each writes <out>/<tour>[-observer]/NN-name.png and sheet.png (sheet.txt maps the
# numbers to names). The tour client starts first and the observer second, so another window has focus while
# the tour runs: the tour moves through its intents, not simulated keys.
#
#   bash scripts/visual-check.sh [--tours look,walk,cook,tutorial,menu] [--out .verify/visual] [--server local|wsl] [--gfx d3d11|d3d12|default]
#
#   menu (spec 047): the tour window starts on the title screen (-tour menu, no -connect) and joins the observer window (-host -observe
#                  menu) through Join by address; the host kicks it; it then opens its own shop and closes it. Local server only.
#
#   --server local (default): the observer is a windowed -host (server plus observer), because the WSL link
#                  has MTU 1280 while Tailscale runs and nothing spawns there (spec 003).
#   --server wsl:  the Linux dedicated server build runs in WSL (Ubuntu); both windows -connect to it.
#   --gfx d3d11 (default): both windows forced to Direct3D 11 (spec 045: D3D12 crashed on quit after tours, see
#                  docs/human-inbox.md); d3d12 forces Direct3D 12 (to re-check that crash); default uses the build's own setting.
#
# Needs Git Bash on Windows and the development builds (game/Builds/Windows, game/Builds/LinuxServer for wsl):
#   Unity -batchmode -quit -projectPath game -executeMethod Worktown.Game.Editor.Build.WorktownBuild.BuildAll
# Exit code: 0 when every tour wrote its sheet, both windows exited with 0 in time, no log has an Exception and Unity wrote
# no native crash report (spec 049b: copied to <out>/<tour>-native-crash/; each window's crash log is <out>/<tour>-tour.crash.log
# and <out>/<tour>-observer.crash.log).
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

TOURS="look,walk,cook"
OUT=".verify/visual"
SERVER="local"
GFX="d3d11"
while [ $# -gt 0 ]; do
  case "$1" in
    --tours) TOURS="${2:?--tours needs a list}"; shift 2 ;;
    --out) OUT="${2:?--out needs a directory}"; shift 2 ;;
    --server) SERVER="${2:?--server needs local or wsl}"; shift 2 ;;
    --gfx) GFX="${2:?--gfx needs d3d11, d3d12 or default}"; shift 2 ;;
    -h|--help) sed -n '2,23p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
case "$SERVER" in local|wsl) ;; *) echo "--server must be local or wsl" >&2; exit 2 ;; esac
case "$GFX" in d3d11|d3d12|default) ;; *) echo "--gfx must be d3d11, d3d12 or default" >&2; exit 2 ;; esac

GAME="$ROOT/game/Builds/Windows/Worktown.exe"
SERVER_DIR_WSL="/mnt/c/Users/jovin/git/workland/game/Builds/LinuxServer"
SERVER_LOG_WSL="/tmp/worktown-visual-server.log"
if [ ! -f "$GAME" ]; then echo "no client build at $GAME (run WorktownBuild.BuildAll)" >&2; exit 2; fi

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
OUT_WIN="$(cygpath -w "$OUT")"
WINDOW=(-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -skip-whatsnew)
if [ "$GFX" != "default" ]; then WINDOW+=("-force-$GFX"); fi  # D3D11 by default: D3D12 crashed on exit after tours (spec 045)
# Spec 049b: Unity's own native crash reports (crash.dmp and the log) land here, outside the run folder; a crash during a
# tour is copied into <out>/<tour>-native-crash/ so the evidence stays with the run.
CRASH_DIR=""
if [ -n "${TEMP:-}" ]; then CRASH_DIR="$(cygpath -u "$TEMP")/DefaultCompany/Worktown/Crashes"; fi

timeout_for() {
  case "$1" in
    look) echo 600 ;; # spec 039: + place anywhere
    walk) echo 420 ;;
    cook) echo 1860 ;; # spec 030: the griddle part; spec 031: the falafel wrap; spec 031b: the box; spec 032: the register; spec 033: the lemonade; spec 034: the stock run; spec 035: the cleaning loop; spec 036: the spit
    tutorial) echo 1860 ;; # spec 038: the tutorial's steps through the cook tour's parts (fries, wrap, lemonade, cleaning, order, spit)
    menu) echo 360 ;; # spec 047: title, join by address, lobby, kick, own shop
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
  case "$tour" in look|walk|cook|tutorial|menu) ;; *) echo "unknown tour: $tour" >&2; failed=1; continue ;; esac
  if [ "$tour" = "menu" ] && [ "$SERVER" != "local" ]; then echo "the menu tour needs --server local (its host is the observer window)" >&2; failed=1; continue; fi
  rm -rf "${OUT:?}/${tour:?}" "${OUT:?}/${tour:?}-observer" "${OUT:?}/${tour:?}-native-crash"
  rm -f "${OUT:?}/${tour:?}-tour.crash.log" "${OUT:?}/${tour:?}-observer.crash.log"
  tour_log="$OUT/$tour-tour.log"
  observer_log="$OUT/$tour-observer.log"
  # Spec 049b: each window writes its own crash log (049's CrashLog: exceptions from every thread, the tick watchdog's dump)
  # into the run folder instead of the shared default in the player's data folder.
  tour_crash="$OUT/$tour-tour.crash.log"
  observer_crash="$OUT/$tour-observer.crash.log"
  crash_mark="$OUT/.native-crash-mark"
  touch "$crash_mark"
  deadline=$(( $(date +%s) + $(timeout_for "$tour") ))
  echo "==> tour $tour ($SERVER server)"

  # Spec 038: the tutorial tour's client runs the tutorial (-tutorial overrides -skip-whatsnew).
  tour_extra=()
  if [ "$tour" = "tutorial" ]; then tour_extra=(-tutorial); fi
  # Spec 047: the menu tour starts on the title screen and joins through it (no -connect).
  tour_connect=(-connect "$ADDRESS")
  if [ "$tour" = "menu" ]; then tour_connect=(); fi
  "$GAME" "${tour_connect[@]}" -tour "$tour" -tour-out "$OUT_WIN" "${tour_extra[@]}" "${WINDOW[@]}" -crash-log "$(cygpath -w "$tour_crash")" -logFile "$(cygpath -w "$tour_log")" &
  tour_pid=$!
  # The observer starts second, so its window is in front and the tour client runs without focus.
  sleep 3
  # Spec 032: the cook tour's host scripts its first orders (a wrap with a modifier, cash with change) and longer patience.
  host_extra=()
  if [ "$tour" = "cook" ]; then host_extra=(-tour-orders); fi
  # Spec 038: the tutorial host has the tour's stock and trash set-up and the tutorial's calm guests (its scripted orders come first).
  if [ "$tour" = "tutorial" ]; then host_extra=(-tour-orders -tutorial); fi
  if [ "$SERVER" = "local" ]; then
    "$GAME" -host -observe "$tour" -tour-out "$OUT_WIN" "${host_extra[@]}" "${WINDOW[@]}" -crash-log "$(cygpath -w "$observer_crash")" -logFile "$(cygpath -w "$observer_log")" &
  else
    "$GAME" -connect "$ADDRESS" -observe "$tour" -tour-out "$OUT_WIN" "${WINDOW[@]}" -crash-log "$(cygpath -w "$observer_crash")" -logFile "$(cygpath -w "$observer_log")" &
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
  # Spec 049b: crash log entries (any thread, the tick watchdog) and Unity native crash reports written during this tour.
  crash_entries=0
  for log in "$tour_crash" "$observer_crash"; do
    if [ -f "$log" ]; then crash_entries=$(( crash_entries + $(grep -c "=== Worktown crash log entry ===" "$log" || true) )); fi
  done
  native=0
  if [ -n "$CRASH_DIR" ] && [ -d "$CRASH_DIR" ]; then
    while IFS= read -r dir; do
      [ -n "$dir" ] || continue
      mkdir -p "$OUT/$tour-native-crash"
      cp -r "$dir" "$OUT/$tour-native-crash/" 2>/dev/null || true
      native=$(( native + 1 ))
    done < <(find "$CRASH_DIR" -mindepth 1 -maxdepth 1 -type d -newer "$crash_mark" 2>/dev/null)
  fi
  rm -f "${crash_mark:?}"
  status="ok"
  if [ "$tour_code" != "0" ] || [ "$observer_code" != "0" ] || [ "$exceptions" -gt 0 ] || [ "$native" -gt 0 ] || [ ! -f "$OUT/$tour/sheet.png" ]; then
    status="FAILED"
    failed=1
  fi
  line="$tour: $status (tour exit $tour_code, observer exit $observer_code, Exception lines $exceptions, crash log entries $crash_entries, native crashes $native)"
  if [ "$native" -gt 0 ]; then line="$line"$'\n'"    native crash report(s): $OUT/$tour-native-crash/"; fi
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
