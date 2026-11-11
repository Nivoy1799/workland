#!/usr/bin/env bash
# Spec 045 (the spec 004 load test, Windows, Git Bash): one windowed host (the server, D33) and N headless bots in its
# restaurant. The bots are the development client build with -batchmode -nographics -connect -bot -seed n; they walk,
# pick up the nearest free item every 5-15 s, carry it 10-30 s and drop it (BotBrain). The host and every bot write one
# JSON line per second (LoadTestRunner); this script cuts out the hold phase and writes the summary.
#
#   bash tools/bots/run-loadtest.sh [--bots 16] [--ramp 120] [--hold 300] [--out .verify/loadtest] [--res 1280x720] [--host-arg A]...
#
#   --bots  N bots, started evenly over --ramp seconds, then held for --hold seconds.
#   --out   per-run folder: host.log/host.jsonl, bot<n>.log/bot<n>.jsonl, summary.json (also copied to .verify/loadtest.json);
#           spec 049: host-crash.log and bot<n>-crash.log (the crash logs of a run stay in its folder).
#   --res   the host window's size (spec 049's perf check uses 1920x1080).
#   --host-arg  one more argument for the host; repeat it (spec 049: --host-arg -perf-tour --host-arg path).
#
# Needs the development builds (Unity -batchmode -quit -projectPath game -executeMethod
# Worktown.Game.Editor.Build.WorktownBuild.BuildAll). Exit code 0 when the host and every bot ran to the end.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

BOTS=16
RAMP=120
HOLD=300
OUT=".verify/loadtest"
RES="1280x720"
HOST_EXTRA=()
while [ $# -gt 0 ]; do
  case "$1" in
    --bots) BOTS="${2:?--bots needs a number}"; shift 2 ;;
    --ramp) RAMP="${2:?--ramp needs seconds}"; shift 2 ;;
    --hold) HOLD="${2:?--hold needs seconds}"; shift 2 ;;
    --out) OUT="${2:?--out needs a directory}"; shift 2 ;;
    --res) RES="${2:?--res needs WIDTHxHEIGHT}"; shift 2 ;;
    --host-arg) HOST_EXTRA+=("${2?--host-arg needs a value}"); shift 2 ;;
    -h|--help) sed -n '2,17p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
case "$BOTS$RAMP$HOLD" in *[!0-9]*) echo "--bots, --ramp and --hold take whole numbers" >&2; exit 2 ;; esac
if [ "$BOTS" -lt 1 ]; then echo "--bots must be at least 1" >&2; exit 2; fi
case "$RES" in [1-9]*x[1-9]*) ;; *) echo "--res takes WIDTHxHEIGHT, e.g. 1920x1080" >&2; exit 2 ;; esac
RES_W="${RES%x*}"
RES_H="${RES#*x}"
case "$RES_W$RES_H" in *[!0-9]*) echo "--res takes WIDTHxHEIGHT, e.g. 1920x1080" >&2; exit 2 ;; esac

GAME="$ROOT/game/Builds/Windows/Worktown.exe"
if [ ! -f "$GAME" ]; then echo "no client build at $GAME (run WorktownBuild.BuildAll)" >&2; exit 2; fi
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
rm -f "${OUT:?}"/host.log "${OUT:?}"/host.jsonl "${OUT:?}"/bot*.log "${OUT:?}"/bot*.jsonl "${OUT:?}"/summary.json \
  "${OUT:?}"/host-crash.log "${OUT:?}"/bot*-crash.log
win() { cygpath -w "$1"; }

PIDS=()
stop_all() {
  # Bots first (hard), then the host (a normal close, hard after 20 s). Only the processes this script started.
  local pid winpid
  for pid in "${PIDS[@]:1}"; do
    winpid="$(cat "/proc/$pid/winpid" 2>/dev/null || true)"
    if [ -n "$winpid" ]; then taskkill //F //PID "$winpid" >/dev/null 2>&1 || true; fi
  done
  if [ "${#PIDS[@]}" -gt 0 ]; then
    pid="${PIDS[0]}"
    winpid="$(cat "/proc/$pid/winpid" 2>/dev/null || true)"
    if [ -n "$winpid" ]; then
      taskkill //PID "$winpid" >/dev/null 2>&1 || true
      for _ in $(seq 1 20); do kill -0 "$pid" 2>/dev/null || break; sleep 1; done
      taskkill //F //PID "$winpid" >/dev/null 2>&1 || true
    fi
  fi
  wait 2>/dev/null
}
trap 'stop_all; exit 130' INT TERM

echo "==> host"
# Spec 047: a game has 16 places with the host (D36); the load test opens one per bot plus the host (-max-players, up to 64).
"$GAME" -host -max-players $(( BOTS + 1 )) -metrics "$(win "$OUT/host.jsonl")" -screen-fullscreen 0 -screen-width "$RES_W" -screen-height "$RES_H" \
  -skip-whatsnew -force-d3d11 -crash-log "$(win "$OUT/host-crash.log")" "${HOST_EXTRA[@]}" -logFile "$(win "$OUT/host.log")" >/dev/null 2>&1 &
PIDS+=($!)
started=0
for _ in $(seq 1 90); do
  if grep -q "Server started" "$OUT/host.log" 2>/dev/null; then started=1; break; fi
  sleep 1
done
if [ "$started" -ne 1 ]; then echo "the host did not start (see $OUT/host.log)" >&2; stop_all; exit 1; fi
sleep 5

echo "==> ramp: $BOTS bots over $RAMP s"
RAMP_START=$(date +%s)
for n in $(seq 1 "$BOTS"); do
  "$GAME" -batchmode -nographics -connect 127.0.0.1 -bot -seed "$n" -metrics "$(win "$OUT/bot$n.jsonl")" -skip-whatsnew \
    -crash-log "$(win "$OUT/bot$n-crash.log")" -logFile "$(win "$OUT/bot$n.log")" >/dev/null 2>&1 &
  PIDS+=($!)
  if [ "$n" -lt "$BOTS" ]; then sleep $(( RAMP / BOTS )); fi
done
rest=$(( RAMP_START + RAMP - $(date +%s) ))
if [ "$rest" -gt 0 ]; then sleep "$rest"; fi

echo "==> hold: $HOLD s"
HOLD_START=$(date +%s)
sleep "$HOLD"
HOLD_END=$(date +%s)

# Working sets (MB) at the end of the hold, from Windows: the host and the mean of the bots.
mem_mb() {
  local winpid
  winpid="$(cat "/proc/$1/winpid" 2>/dev/null || true)"
  if [ -z "$winpid" ]; then echo 0; return; fi
  tasklist //FI "PID eq $winpid" //FO CSV //NH 2>/dev/null | awk -F'","' 'NF >= 5 { gsub(/[^0-9]/, "", $5); print int($5 / 1024); exit }'
}
HOST_MB="$(mem_mb "${PIDS[0]}")"
BOT_MB=0
for pid in "${PIDS[@]:1}"; do BOT_MB=$(( BOT_MB + $(mem_mb "$pid") )); done
BOT_MB=$(( BOT_MB / BOTS ))

alive=0
for pid in "${PIDS[@]}"; do if kill -0 "$pid" 2>/dev/null; then alive=$((alive + 1)); fi; done
stop_all

# The summary: the hold phase of the host's and the bots' lines (perl, no extra tools).
perl -e '
  use strict; use warnings;
  my ($out, $from, $to, $bots, $ramp, $hold, $alive, $hostMb, $botMb) = @ARGV;
  sub rows { my ($f) = @_; my @r; open(my $h, "<", $f) or return @r;
    while (<$h>) { my %x = /"(\w+)":(-?[0-9.]+)/g; push @r, \%x if %x && $x{unix} >= $from && $x{unix} < $to; } @r }
  sub avg { my @v = @_; return 0 unless @v; my $s = 0; $s += $_ for @v; $s / @v }
  sub max { my $m = 0; for (@_) { $m = $_ if $_ > $m } $m }
  sub p95 { my @v = sort { $a <=> $b } @_; return 0 unless @v; my $i = int(0.95 * @v + 0.999999) - 1; $i = 0 if $i < 0; $v[$i] }
  my @h = rows("$out/host.jsonl");
  my (@perBotAvg, @perBotMax, @rtt);
  my $connectedBots = 0;
  for my $n (1 .. $bots) {
    my @b = rows("$out/bot$n.jsonl");
    next unless @b;
    my @c = grep { $_->{connected} } @b;
    $connectedBots++ if @c && @c == @b;
    push @perBotAvg, avg(map { $_->{in_bytes} } @b);
    push @perBotMax, max(map { $_->{in_bytes} } @b);
    push @rtt, map { $_->{rtt_ms} } @c;
  }
  my %s = (
    bots => $bots, ramp_s => $ramp, hold_s => $hold, processes_alive_at_end => $alive + 0,
    host_samples => scalar(@h), clients_min => (@h ? (sort { $a <=> $b } map { $_->{clients} } @h)[0] : 0),
    clients_max => max(map { $_->{clients} } @h), objects_max => max(map { $_->{objects} } @h),
    tick_avg_ms => avg(map { $_->{tick_avg_ms} } @h), tick_p95_ms => p95(map { $_->{tick_p95_ms} } @h),
    tick_max_ms => max(map { $_->{tick_max_ms} } @h), ticks_per_s => avg(map { $_->{ticks} } @h),
    frame_avg_ms => avg(map { $_->{frame_avg_ms} } @h), frame_p95_ms => p95(map { $_->{frame_p95_ms} } @h),
    frame_max_ms => max(map { $_->{frame_max_ms} } @h),
    host_in_bytes_per_s => avg(map { $_->{in_bytes} } @h), host_cpu_pct_avg => avg(map { $_->{cpu_pct} } @h),
    host_cpu_pct_max => max(map { $_->{cpu_pct} } @h), host_working_set_mb => $hostMb + 0, bot_working_set_mb_avg => $botMb + 0,
    host_unity_reserved_mb_max => max(map { $_->{unity_reserved_mb} } @h), host_managed_heap_mb_max => max(map { $_->{managed_heap_mb} } @h),
    bots_connected_whole_hold => $connectedBots,
    out_bytes_per_client_avg => avg(@perBotAvg), out_bytes_per_client_max_avg => max(@perBotAvg),
    out_bytes_per_client_max_second => max(@perBotMax), rtt_ms_avg => avg(@rtt), rtt_ms_p95 => p95(@rtt),
  );
  open(my $j, ">", "$out/summary.json") or die;
  print $j "{\n", join(",\n", map { sprintf("  \"%s\": %s", $_, ($s{$_} =~ /^-?\d+$/ ? $s{$_} : sprintf("%.3f", $s{$_}))) } sort keys %s), "\n}\n";
  close $j;
  printf "%-34s %s\n", $_, $s{$_} for sort keys %s;
' "$OUT" "$HOLD_START" "$HOLD_END" "$BOTS" "$RAMP" "$HOLD" "$alive" "$HOST_MB" "$BOT_MB"
cp "$OUT/summary.json" "$ROOT/.verify/loadtest.json" 2>/dev/null || true

expected=$(( BOTS + 1 ))
echo "processes alive at the end of the hold: $alive of $expected"
if [ "$alive" -ne "$expected" ]; then echo "RESULT: FAIL (a process ended early)"; exit 1; fi
echo "RESULT: PASS ($OUT/summary.json)"
