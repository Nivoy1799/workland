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
#   --net-stats  spec 054: the host runs with -net-stats (FishNet's per-packet statistics; allocates, so never a perf baseline);
#               the summary adds pid_<type>_bps_avg and its share, and the top obj_<name>_bps_avg.
#   spec 054b: the summary also has the correction smoothing: owner_shift_* (a reconcile moved the own current position more than
#               5 cm: count per minute, p50/p95/max in cm before smoothing) and owner_vis_cm_* (how far the drawn body moves in the
#               next tick because of each, after smoothing), and owner_visible_corrections_per_min (ticks it moves more than 5 cm).
#               A/B: --host-arg -player-collision --host-arg soft|hard|off.
#   --kbps-budget AVG,WORST  spec 054: KB/s per client (KB = 1000 bytes), the FishNet payload a bot received: the average and the
#               worst second. Prints BANDWIDTH: PASS|FAIL and exits 4 when either is over (a process that ended early still exits 1).
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
NET_STATS=0
KBPS_BUDGET=""
while [ $# -gt 0 ]; do
  case "$1" in
    --bots) BOTS="${2:?--bots needs a number}"; shift 2 ;;
    --ramp) RAMP="${2:?--ramp needs seconds}"; shift 2 ;;
    --hold) HOLD="${2:?--hold needs seconds}"; shift 2 ;;
    --out) OUT="${2:?--out needs a directory}"; shift 2 ;;
    --res) RES="${2:?--res needs WIDTHxHEIGHT}"; shift 2 ;;
    --host-arg) HOST_EXTRA+=("${2?--host-arg needs a value}"); shift 2 ;;
    --net-stats) NET_STATS=1; shift ;;
    --kbps-budget) KBPS_BUDGET="${2:?--kbps-budget needs AVG,WORST in KB/s}"; shift 2 ;;
    -h|--help) sed -n '2,24p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
case "$BOTS$RAMP$HOLD" in *[!0-9]*) echo "--bots, --ramp and --hold take whole numbers" >&2; exit 2 ;; esac
if [ "$BOTS" -lt 1 ]; then echo "--bots must be at least 1" >&2; exit 2; fi
case "$RES" in [1-9]*x[1-9]*) ;; *) echo "--res takes WIDTHxHEIGHT, e.g. 1920x1080" >&2; exit 2 ;; esac
case "$KBPS_BUDGET" in ""|[0-9]*,[0-9]*) ;; *) echo "--kbps-budget takes AVG,WORST in KB/s, e.g. 35,40" >&2; exit 2 ;; esac
KBPS_AVG="${KBPS_BUDGET%,*}"
KBPS_WORST="${KBPS_BUDGET#*,}"
case "$KBPS_AVG$KBPS_WORST" in *[!0-9.]*) echo "--kbps-budget takes AVG,WORST in KB/s, e.g. 35,40" >&2; exit 2 ;; esac
if [ "$NET_STATS" -eq 1 ]; then HOST_EXTRA+=("-net-stats"); fi
RES_W="${RES%x*}"
RES_H="${RES#*x}"
case "$RES_W$RES_H" in *[!0-9]*) echo "--res takes WIDTHxHEIGHT, e.g. 1920x1080" >&2; exit 2 ;; esac

GAME="$ROOT/game/Builds/Windows/Worktown.exe"
if [ ! -f "$GAME" ]; then echo "no client build at $GAME (run WorktownBuild.BuildAll)" >&2; exit 2; fi
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
rm -f "${OUT:?}"/host.log "${OUT:?}"/host.jsonl "${OUT:?}"/bot*.log "${OUT:?}"/bot*.jsonl "${OUT:?}"/summary.json \
  "${OUT:?}"/host-crash.log "${OUT:?}"/bot*-crash.log "${OUT:?}"/bandwidth.txt
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
  my ($out, $from, $to, $bots, $ramp, $hold, $alive, $hostMb, $botMb, $kbpsAvg, $kbpsWorst) = @ARGV;
  sub rows { my ($f) = @_; my @r; open(my $h, "<", $f) or return @r;
    while (<$h>) { my %x = /"(\w+)":(-?[0-9.]+)/g; push @r, \%x if %x && $x{unix} >= $from && $x{unix} < $to; } @r }
  sub avg { my @v = @_; return 0 unless @v; my $s = 0; $s += $_ for @v; $s / @v }
  sub max { my $m = 0; for (@_) { $m = $_ if $_ > $m } $m }
  sub p95 { my @v = sort { $a <=> $b } @_; return 0 unless @v; my $i = int(0.95 * @v + 0.999999) - 1; $i = 0 if $i < 0; $v[$i] }
  my @h = rows("$out/host.jsonl");
  my (@perBotAvg, @perBotMax, @rtt, @wireAvg, @wireMax, @jitter, @corrPerMin);
  my ($teleports, $reconciles, $feelSeconds) = (0, 0, 0);
  my %sizeBins; my ($sizeOver, $sizeMax) = (0, 0);
  # Spec 054b: the correction smoothing per bot, before (owner_shift_cm_*) and after (owner_vis_cm_*), and the counts per minute.
  my %smooth = (shift => { bins => {}, over => 0, max => 0 }, vis => { bins => {}, over => 0, max => 0 });
  my (@shiftsPerMin, @visiblePerMin);
  my $connectedBots = 0;
  for my $n (1 .. $bots) {
    my @b = rows("$out/bot$n.jsonl");
    next unless @b;
    my @c = grep { $_->{connected} } @b;
    $connectedBots++ if @c && @c == @b;
    push @perBotAvg, avg(map { $_->{in_bytes} } @b);
    push @perBotMax, max(map { $_->{in_bytes} } @b);
    push @rtt, map { $_->{rtt_ms} } @c;
    # Spec 054: wire bytes (payload + LiteNetLib + IPv4/UDP headers) and the feel guard (B4).
    push @wireAvg, avg(map { $_->{wire_in_bytes} // 0 } @b);
    push @wireMax, max(map { $_->{wire_in_bytes} // 0 } @b);
    push @jitter, map { $_->{remote_jitter_p95} } grep { ($_->{remote_moving_frames} // 0) > 0 } @b;
    $teleports += $_->{remote_teleports} // 0 for @b;
    $reconciles += $_->{owner_reconciles} // 0 for @b;
    $feelSeconds += scalar(@b);
    my $corr = 0; $corr += $_->{owner_corrections} // 0 for @b;
    push @corrPerMin, $corr / (@b / 60);
    # Spec 054 follow-up: correction sizes, binned per second (owner_corr_cm_le_<edge>, owner_corr_cm_gt_<edge>) and the largest.
    for my $r (@b) {
      for (keys %$r) { $sizeBins{$1} += $r->{$_} if /^owner_corr_cm_le_([0-9.]+)$/; $sizeOver += $r->{$_} if /^owner_corr_cm_gt_/; }
      $sizeMax = $r->{owner_corr_cm_max} if ($r->{owner_corr_cm_max} // 0) > $sizeMax;
      for my $k (qw(shift vis)) {
        my $m = $smooth{$k};
        for (keys %$r) { $m->{bins}{$1} += $r->{$_} if /^owner_${k}_cm_le_([0-9.]+)$/; $m->{over} += $r->{$_} if /^owner_${k}_cm_gt_/; }
        $m->{max} = $r->{"owner_${k}_cm_max"} if ($r->{"owner_${k}_cm_max"} // 0) > $m->{max};
      }
    }
    my ($shifts, $visible) = (0, 0);
    $shifts += $_->{owner_shifts} // 0 for @b;
    $visible += $_->{owner_visible_corrections} // 0 for @b;
    push @shiftsPerMin, $shifts / (@b / 60);
    push @visiblePerMin, $visible / (@b / 60);
  }
  # The p50 and p95 size over all corrections of all bots, linear inside a bin (the first bin starts at the 5 cm threshold, the
  # open last bin ends at the largest correction).
  my @edges = sort { $a <=> $b } keys %sizeBins;
  my $sizeCount = $sizeOver; $sizeCount += $sizeBins{$_} for @edges;
  my $sizeAt = sub {
    my ($q) = @_; return 0 unless $sizeCount; my $want = $q * $sizeCount; my ($seen, $low) = (0, 5);
    for my $e (@edges) { my $n = $sizeBins{$e}; if ($n && $seen + $n >= $want) { return $low + ($e - $low) * ($want - $seen) / $n } $seen += $n; $low = $e; }
    return $sizeOver ? $low + ($sizeMax - $low) * ($want - $seen) / $sizeOver : $low;
  };
  # Spec 054b: the same for the smoothing bins; their first bin starts at 0 cm (a visible move can be tiny).
  my $binAt = sub {
    my ($m, $q) = @_; my @e = sort { $a <=> $b } keys %{$m->{bins}};
    my $count = $m->{over}; $count += $m->{bins}{$_} for @e; return (0, 0) unless $count;
    my ($want, $seen, $low) = ($q * $count, 0, 0);
    for my $x (@e) { my $n = $m->{bins}{$x}; if ($n && $seen + $n >= $want) { return ($count, $low + ($x - $low) * ($want - $seen) / $n) } $seen += $n; $low = $x; }
    return ($count, $m->{over} ? $low + ($m->{max} - $low) * ($want - $seen) / $m->{over} : $low);
  };
  my ($shiftCount, $shiftP50) = $binAt->($smooth{shift}, 0.5); my (undef, $shiftP95) = $binAt->($smooth{shift}, 0.95);
  my (undef, $visP50) = $binAt->($smooth{vis}, 0.5); my (undef, $visP95) = $binAt->($smooth{vis}, 0.95);
  # Spec 054: the per-packet buckets (-net-stats): the average per second per client over the hold (a missing field is 0),
  # the share of each bucket of all pid_ buckets, and the objects.
  my (%pid, %obj);
  for my $r (@h) { for (keys %$r) { $pid{$1} += $r->{$_} if /^pid_(\w+)_bps$/; $obj{$1} += $r->{$_} if /^obj_(\w+)_bps$/; } }
  my $pidSum = 0; $pidSum += $_ for values %pid;
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
    wire_bytes_per_client_avg => avg(@wireAvg), wire_bytes_per_client_max_second => max(@wireMax),
    host_wire_out_bytes_per_s => avg(map { $_->{wire_out_bytes} // 0 } @h),
    remote_jitter_p95 => p95(@jitter), remote_jitter_median => (@jitter ? (sort { $a <=> $b } @jitter)[int(@jitter / 2)] : 0),
    remote_teleports => $teleports, owner_corrections_per_min => avg(@corrPerMin),
    owner_reconciles_per_s => ($feelSeconds ? $reconciles / $feelSeconds : 0),
    owner_corr_count => $sizeCount, owner_corr_cm_p50 => $sizeAt->(0.5), owner_corr_cm_p95 => $sizeAt->(0.95),
    owner_corr_cm_max => $sizeMax,
    owner_shifts_per_min => avg(@shiftsPerMin), owner_shift_count => $shiftCount, owner_shift_cm_p50 => $shiftP50,
    owner_shift_cm_p95 => $shiftP95, owner_shift_cm_max => $smooth{shift}{max},
    owner_vis_cm_p50 => $visP50, owner_vis_cm_p95 => $visP95, owner_vis_cm_max => $smooth{vis}{max},
    owner_visible_corrections_per_min => avg(@visiblePerMin),
  );
  if (@h && %pid) {
    $s{"pid_${_}_bps_avg"} = $pid{$_} / @h for keys %pid;
    $s{"pid_${_}_share_pct"} = ($pidSum > 0 ? $pid{$_} / $pidSum * 100 : 0) for keys %pid;
    $s{"obj_${_}_bps_avg"} = $obj{$_} / @h for keys %obj;
    $s{net_socket_bps_avg} = avg(map { $_->{net_socket_bps} // 0 } @h);
  }
  # Spec 054: the bandwidth budget (KB = 1000 bytes): the payload per client, average and worst second.
  if ($kbpsAvg ne "") {
    my ($av, $wo) = ($s{out_bytes_per_client_avg} / 1000, $s{out_bytes_per_client_max_second} / 1000);
    my $pass = ($av <= $kbpsAvg && $wo <= $kbpsWorst) ? 1 : 0;
    $s{kbps_budget_avg} = $kbpsAvg + 0; $s{kbps_budget_worst} = $kbpsWorst + 0; $s{bandwidth_pass} = $pass;
    open(my $bw, ">", "$out/bandwidth.txt") or die;
    printf $bw "BANDWIDTH: %s (payload per client %.1f KB/s average, worst second %.1f; budget %s and %s; wire %.1f KB/s average)\n",
      ($pass ? "PASS" : "FAIL"), $av, $wo, $kbpsAvg, $kbpsWorst, $s{wire_bytes_per_client_avg} / 1000;
    close $bw;
  }
  open(my $j, ">", "$out/summary.json") or die;
  print $j "{\n", join(",\n", map { sprintf("  \"%s\": %s", $_, ($s{$_} =~ /^-?\d+$/ ? $s{$_} : sprintf("%.3f", $s{$_}))) } sort keys %s), "\n}\n";
  close $j;
  printf "%-34s %s\n", $_, $s{$_} for sort keys %s;
' "$OUT" "$HOLD_START" "$HOLD_END" "$BOTS" "$RAMP" "$HOLD" "$alive" "$HOST_MB" "$BOT_MB" "$KBPS_AVG" "$KBPS_WORST"
cp "$OUT/summary.json" "$ROOT/.verify/loadtest.json" 2>/dev/null || true

expected=$(( BOTS + 1 ))
echo "processes alive at the end of the hold: $alive of $expected"
BANDWIDTH_FAIL=0
if [ -n "$KBPS_BUDGET" ]; then
  if [ -f "$OUT/bandwidth.txt" ]; then cat "$OUT/bandwidth.txt"; else echo "BANDWIDTH: FAIL (no summary)"; fi
  grep -q "^BANDWIDTH: PASS" "$OUT/bandwidth.txt" 2>/dev/null || BANDWIDTH_FAIL=1
fi
if [ "$alive" -ne "$expected" ]; then echo "RESULT: FAIL (a process ended early)"; exit 1; fi
if [ "$BANDWIDTH_FAIL" -eq 1 ]; then echo "RESULT: FAIL (over the bandwidth budget, $OUT/summary.json)"; exit 4; fi
echo "RESULT: PASS ($OUT/summary.json)"
