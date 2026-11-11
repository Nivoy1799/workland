#!/usr/bin/env bash
# Spec 049: the release-stability check, one command, unattended, made to be run again and again (at least once per merged
# milestone; docs/workflow.md). Windows, Git Bash, the development builds. Three parts:
#
#   1. Crash log: a -crash-test player forces exceptions (main thread, an unobserved task, a background thread) and the crash log
#      must hold all three with the build hash; a -host -stall-test player stops its own network tick (one bot joined first) and
#      the tick watchdog must log the stall and close the shop, and the bot must get the HostStalled notice.
#   2. Profiling tour + soak: tools/bots/run-loadtest.sh runs one windowed host (1920x1080) and 15 bots, so 16 players (D36) are in
#      the restaurant. The host's own player walks to every station of the scene (read at run time: new stations are measured
#      without editing this script) and records frame times and GC allocations per station; then the bots keep playing until the
#      soak ends, while the host writes its memory and object counts every second.
#   3. Verdict: the numbers against the budget (60 fps: frame avg and p95 at most 16.7 ms; memory: managed heap floor growth at most
#      10 MB and Unity object count growth at most 2 % over the soak) and against ONE baseline row: the passing run of the same
#      scenario with the lowest p95, not from a dirty tree or a stale build (frame avg, p95, p99 and GC per frame more than 10 %
#      worse than that row fail; heap and objects only have their bounds). A dated row is appended to docs/performance.md and the
#      latest per-station table replaced there.
#
#   bash scripts/perf-check.sh [--quick] [--soak-minutes 120] [--bots 15] [--res 1920x1080] [--out .verify/perf]
#                              [--no-record] [--skip-crash] [--skip-soak] [--doc docs/performance.md] [--self-test]
#
#   --soak-minutes   the soak's length (default 15, about 20 minutes in all; 120 is the spec's 2-hour soak, about 2 h 5 min).
#                    Rows are only compared with rows of the same scenario (bots, minutes, resolution).
#   --quick          a 10-minute soak (about 15 minutes in all) for a quick look after a change.
#   --bots           load-test bots (default 15: 16 players with the host).
#   --res            the host window (default 1920x1080, the budget's resolution).
#   --out            where each run gets a dated folder (logs, the tour's JSON lines, report.md).
#   --no-record      measure and judge, but leave docs/performance.md alone (experiments).
#   --skip-crash / --skip-soak   only one part; such a partial run is judged on what ran and never recorded.
#   --self-test      checks the baseline rules on made-up rows and prints the baseline of every scenario in --doc (no game).
#
# Needs the development builds: Unity -batchmode -quit -projectPath game -executeMethod Worktown.Game.Editor.Build.WorktownBuild.BuildAll
# (a build older than the scripts is warned about). Nothing else may use port 7770 (a visual check or a load test): it refuses to
# start then. Exit code: 0 PASS, 1 FAIL (a budget, a bound, a regression or a check), 2 could not run.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

BOTS=15
SOAK_MIN=15
RAMP=60
RES="1920x1080"
OUT=".verify/perf"
DOC="docs/performance.md"
RECORD=1
DO_CRASH=1
DO_SOAK=1
SELF_TEST=0
while [ $# -gt 0 ]; do
  case "$1" in
    --quick) SOAK_MIN=10; shift ;;
    --soak-minutes) SOAK_MIN="${2:?--soak-minutes needs minutes}"; shift 2 ;;
    --bots) BOTS="${2:?--bots needs a number}"; shift 2 ;;
    --res) RES="${2:?--res needs WIDTHxHEIGHT}"; shift 2 ;;
    --out) OUT="${2:?--out needs a directory}"; shift 2 ;;
    --doc) DOC="${2:?--doc needs a file}"; shift 2 ;;
    --no-record) RECORD=0; shift ;;
    --skip-crash) DO_CRASH=0; shift ;;
    --skip-soak) DO_SOAK=0; shift ;;
    --self-test) SELF_TEST=1; shift ;;
    -h|--help) sed -n '2,36p' "$0"; exit 0 ;;
    *) echo "unknown argument: $1 (see --help)" >&2; exit 2 ;;
  esac
done
case "$BOTS$SOAK_MIN" in *[!0-9]*|"") echo "--bots and --soak-minutes take whole numbers" >&2; exit 2 ;; esac
if [ "$BOTS" -lt 1 ] || [ "$SOAK_MIN" -lt 5 ]; then echo "at least 1 bot and a 5-minute soak" >&2; exit 2; fi
case "$RES" in [1-9]*x[1-9]*) ;; *) echo "--res takes WIDTHxHEIGHT, e.g. 1920x1080" >&2; exit 2 ;; esac

# The verdict, the row and the station table (perl). A function so that --self-test runs it before any build check.
verdict() {
perl - "$@" <<'PERL'
use strict; use warnings;
my ($run, $doc, $record, $stamp, $commit, $bots, $soakMin, $res, $crash, $stall, $loadtest, $stale) = @ARGV;

# The budget (spec 049: 60 fps at 1080p with 16 players) and the bounds; the regression rule (more than 10 % worse than the best
# run of the same scenario, beyond a small noise floor so a 0.1 ms wobble never fails a run).
my $FRAME_BUDGET_MS = 1000 / 60;
my $HEAP_BOUND_MB = 10;
my $OBJECTS_BOUND_PCT = 2;
my $REGRESSION = 0.10;
my %SLACK = (avg => 0.25, p95 => 0.25, p99 => 0.5, gc => 256);

sub fields { my ($line) = @_; my %x; $x{$1} = $2 while $line =~ /"(\w+)":"([^"]*)"/g; $x{$1} = $2 + 0 while $line =~ /"(\w+)":(-?[0-9.]+)/g; return \%x; }
sub minv { my $m; for (@_) { $m = $_ if !defined $m || $_ < $m } $m }
sub median { my @v = sort { $a <=> $b } @_; return undef unless @v; $v[int(@v / 2)] }
sub avgv { return undef unless @_; my $s = 0; $s += $_ for @_; $s / @_ }
sub fmt { my ($v, $d) = @_; defined $v ? sprintf("%.${d}f", $v) : "n/a" }
sub signed { my ($v, $d, $u) = @_; defined $v ? sprintf("%+.${d}f%s", $v, $u) : "n/a" }

# The rows of the runs table (docs/performance.md): date, commit, scenario, result and the four compared numbers (undef for n/a).
sub run_rows {
  my @rows;
  for (@_) {
    next unless /^\| (\d{4}-\d\d-\d\d [\d:]+) \|/;
    my @c = map { s/^\s+|\s+$//gr } split /\|/;
    shift @c;
    next unless @c >= 11;
    my %r = (date => $c[0], commit => $c[1], scenario => $c[3], result => $c[10]);
    my %col = (avg => 4, p95 => 5, p99 => 6, gc => 7);
    for my $k (keys %col) { my $v = $c[$col{$k}]; $r{$k} = $v =~ /^-?[0-9]+(\.[0-9]+)?$/ ? $v + 0 : undef }
    push @rows, \%r;
  }
  return @rows;
}

# The baseline (049 review): ONE row, the passing run of the same scenario with the lowest p95, and every number is compared with
# that row (a per-metric minimum across rows mixed runs and only ever shrank). Never a baseline: a failed or partial run, a run
# from a dirty tree ("+dirty") or of a stale build ("(stale build)"), a row without a p95.
sub baseline {
  my ($scenario, @rows) = @_;
  my $best;
  for my $r (@rows) {
    next unless $r->{scenario} eq $scenario && $r->{result} =~ /^PASS/ && $r->{result} !~ /partial/;
    next if $r->{commit} =~ /\+dirty|stale build/;
    next unless defined $r->{p95};
    $best = $r if !defined $best || $r->{p95} < $best->{p95};
  }
  return $best;
}

# The regressions against the baseline row: frame avg, p95, p99 and GC per frame, more than 10 % worse (beyond the noise floor).
# Heap and object growth are judged only against their bounds, never as a regression.
sub regressions {
  my ($now, $best) = @_;
  my @out;
  return @out unless $best;
  for my $k (qw(avg p95 p99 gc)) {
    next unless defined $now->{$k} && defined $best->{$k};
    my $base = $best->{$k} > 0 ? $best->{$k} : 0;
    my $limit = $base + ($base * $REGRESSION > $SLACK{$k} ? $base * $REGRESSION : $SLACK{$k});
    push @out, sprintf("%s regressed: %.2f against %.2f in the baseline run of %s (%s; limit %.2f)", $k, $now->{$k}, $best->{$k},
      $best->{date}, $best->{commit}, $limit) if $now->{$k} > $limit;
  }
  return @out;
}

# --self-test: the baseline rules on made-up rows, then the baseline each scenario of the real table would get.
if ($run eq "--self-test") {
  my $fails = 0;
  my $ok = sub { my ($cond, $what) = @_; print(($cond ? "ok   " : "FAIL ") . "$what\n"); $fails++ unless $cond; };
  my $s = "15 bots, 15 min, 1920x1080";
  my @t = (
    "| 2026-01-01 10:00 | aaaaaaa | h | $s | 1.90 | 2.00 | 3.00 | 2000 | +0.5 | +0.0 % | PASS (crash PASS, watchdog PASS) |\n",
    "| 2026-01-02 10:00 | bbbbbbb | h | $s | 1.50 | 2.50 | 2.50 | 1500 | -0.2 | +0.0 % | PASS (crash PASS, watchdog PASS) |\n",
    "| 2026-01-03 10:00 | ccccccc+dirty | h | $s | 1.00 | 1.00 | 1.00 | 900 | +0.1 | +0.0 % | PASS (crash PASS, watchdog PASS) |\n",
    "| 2026-01-04 10:00 | ddddddd (stale build) | h | $s | 1.00 | 1.10 | 1.00 | 900 | +0.1 | +0.0 % | PASS (crash PASS, watchdog PASS) |\n",
    "| 2026-01-05 10:00 | eeeeeee | h | $s | 1.00 | 1.20 | 1.00 | 900 | +0.1 | +0.0 % | FAIL (crash FAIL, watchdog PASS) |\n",
    "| 2026-01-06 10:00 | fffffff | h | $s | 1.00 | 1.30 | 1.00 | 900 | +0.1 | +0.0 % | PASS (partial run) (crash skipped, watchdog skipped) |\n",
    "| 2026-01-07 10:00 | 1111111 | h | 15 bots, 120 min, 1920x1080 | 1.00 | 1.40 | 1.00 | 900 | +0.1 | +0.0 % | PASS (crash PASS, watchdog PASS) |\n",
    "| 2026-01-08 10:00 | 2222222 | h | $s | 1.00 | n/a | 1.00 | 900 | +0.1 | +0.0 % | PASS (crash PASS, watchdog PASS) |\n",
  );
  my $b = baseline($s, run_rows(@t));
  $ok->($b && $b->{commit} eq "aaaaaaa", "one row: the lowest p95 of the clean passing runs of the scenario (" . ($b ? $b->{commit} : "none") . ")");
  $ok->($b && $b->{avg} == 1.90 && $b->{gc} == 2000, "every number comes from that row, not the per-metric minimum (avg 1.50, gc 1500)");
  my @r = regressions({ avg => 2.05, p95 => 2.10, p99 => 3.20, gc => 2150, heap => 9, objects => 1.9 }, $b);
  $ok->(!@r, "within 10 % of the baseline row passes, heap and objects are not compared (" . join("; ", @r) . ")");
  @r = regressions({ avg => 2.20, p95 => 2.00, p99 => 3.00, gc => 2000 }, $b);
  $ok->(@r == 1 && $r[0] =~ /^avg regressed/ && $r[0] =~ /2026-01-01 10:00 \(aaaaaaa/, "more than 10 % worse fails and names the baseline run");
  @r = regressions({ avg => 1.00, p95 => 0.30, p99 => 0.30, gc => 100 }, { avg => 0.10, p95 => 0.10, p99 => 0.10, gc => 10, date => "d", commit => "c" });
  $ok->(@r == 1 && $r[0] =~ /^avg/, "the noise floor: 0.30 against 0.10 passes (p95, p99), 1.00 against 0.10 fails (" . join("; ", @r) . ")");
  $ok->(!defined baseline("3 bots, 5 min, 800x600", run_rows(@t)), "no baseline for a new scenario");
  $ok->(!regressions({ avg => 99 }, undef), "no baseline, no regression");
  my @only = (grep { !/dirty|stale|eeeeeee|fffffff|aaaaaaa|bbbbbbb/ } @t);
  my $na = baseline($s, run_rows(@only));
  $ok->(!defined $na, "a row without a p95 is no baseline");

  print "\nThe real table ($doc):\n";
  my @doclines;
  if (-f $doc) { open(my $d, "<", $doc) or die "cannot read $doc"; @doclines = <$d>; close $d; }
  my @rows = run_rows(@doclines);
  my %scen; $scen{$_->{scenario}} = 1 for @rows;
  for my $sc (sort keys %scen) {
    my $bb = baseline($sc, @rows);
    printf("  %s: %d rows, baseline %s\n", $sc, scalar(grep { $_->{scenario} eq $sc } @rows),
      $bb ? "$bb->{date} ($bb->{commit}): avg $bb->{avg}, p95 $bb->{p95}, p99 $bb->{p99}, gc " . ($bb->{gc} // "n/a") : "none");
  }
  print $fails ? "SELF-TEST: FAIL ($fails)\n" : "SELF-TEST: PASS\n";
  exit($fails ? 1 : 0);
}

# The profiling tour.
my (@stations, $all);
if (open(my $h, "<", "$run/perf-tour.jsonl")) {
  while (<$h>) { my $x = fields($_); next unless defined $x->{station}; if ($x->{station} eq "ALL") { $all = $x } else { push @stations, $x } }
}
# The build hash, from the host's log.
my $build = "unknown";
if (open(my $h, "<", "$run/soak/host.log")) { while (<$h>) { if (/\[Crash\] log .* hash ([0-9a-f]{16})/) { $build = substr($1, 0, 8); last } } }

# The soak: the host's samples with every player connected; a warm-up first, then the first and the last window.
my ($heapGrowth, $objGrowth, $soakGc, $samples, $memNote) = (undef, undef, undef, 0, "");
if (open(my $h, "<", "$run/soak/host.jsonl")) {
  my @rows; while (<$h>) { my $x = fields($_); push @rows, $x if defined $x->{clients} }
  my $full = 0; for (@rows) { $full = $_->{clients} if $_->{clients} > $full }
  my @hold = grep { $_->{clients} >= $full && $full > 0 } @rows;
  $samples = scalar @hold;
  my $warm = int(0.2 * @hold); $warm = 120 if $warm < 120;
  my $w = int(0.1 * @hold); $w = 60 if $w < 60;
  if (@hold - $warm >= 2 * $w) {
    my @first = @hold[$warm .. $warm + $w - 1];
    my @last = @hold[$#hold - $w + 1 .. $#hold];
    $heapGrowth = minv(map { $_->{managed_heap_mb} } @last) - minv(map { $_->{managed_heap_mb} } @first);
    my $key = (grep { ($_->{unity_objects} // 0) > 0 } @first) ? "unity_objects" : "gameobjects";
    my $f = median(map { $_->{$key} // 0 } @first);
    my $l = median(map { $_->{$key} // 0 } @last);
    $objGrowth = ($f && $f > 0) ? ($l - $f) / $f * 100 : undef;
    $soakGc = avgv(map { ($_->{gc_kb_per_frame} // 0) * 1024 } @hold[$warm .. $#hold]);
    $memNote = "$samples samples with all players; heap floor and $key median, first vs last " . $w . " s after a " . $warm . " s warm-up";
  } else {
    $memNote = "soak too short for the memory check ($samples samples with all players)";
  }
}

my $scenario = "$bots bots, $soakMin min, $res";
my %now = (avg => $all ? $all->{avg_ms} : undef, p95 => $all ? $all->{p95_ms} : undef, p99 => $all ? $all->{p99_ms} : undef,
           gc => $all ? $all->{gc_bytes_per_frame} : undef, heap => $heapGrowth, objects => $objGrowth);

# The baseline: one recorded run of the same scenario (baseline()).
my @lines;
if (-f $doc) { open(my $d, "<", $doc) or die "cannot read $doc"; @lines = <$d>; close $d; }
my $best = baseline($scenario, run_rows(@lines));

my @why;
push @why, "the crash log check failed" if $crash eq "FAIL";
push @why, "the tick watchdog check failed" if $stall eq "FAIL";
push @why, "the load test failed (a process ended early, see soak/)" if $loadtest eq "FAIL";
# A partial run (--skip-crash, --skip-soak) is judged on what ran, and never recorded.
my $partial = ($crash eq "skipped" || $stall eq "skipped" || $loadtest eq "skipped") ? 1 : 0;
$record = 0 if $partial;
push @why, "the profiling tour did not finish (no ALL line in perf-tour.jsonl)" if $loadtest ne "skipped" && !$all;
if ($all) {
  push @why, sprintf("frame avg %.2f ms is over the %.1f ms budget", $now{avg}, $FRAME_BUDGET_MS) if $now{avg} > $FRAME_BUDGET_MS;
  push @why, sprintf("frame p95 %.2f ms is over the %.1f ms budget", $now{p95}, $FRAME_BUDGET_MS) if $now{p95} > $FRAME_BUDGET_MS;
}
push @why, sprintf("managed heap grew %.1f MB (bound %d)", $heapGrowth, $HEAP_BOUND_MB) if defined $heapGrowth && $heapGrowth > $HEAP_BOUND_MB;
push @why, sprintf("Unity objects grew %.1f %% (bound %d)", $objGrowth, $OBJECTS_BOUND_PCT) if defined $objGrowth && $objGrowth > $OBJECTS_BOUND_PCT;
push @why, "no memory numbers ($memNote)" if $loadtest ne "skipped" && !defined $heapGrowth;
push @why, regressions(\%now, $best);
my $baseNote = $best ? "Baseline: the run of $best->{date} ($best->{commit}, p95 $best->{p95} ms)." : "Baseline: none yet for this scenario.";
my $result = (@why ? "FAIL" : "PASS") . ($partial ? " (partial run)" : "");

my $checks = "crash $crash, watchdog $stall";
my $row = sprintf("| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |\n", $stamp, $commit . ($stale ? " (stale build)" : ""), $build,
  $scenario, fmt($now{avg}, 2), fmt($now{p95}, 2), fmt($now{p99}, 2), fmt($now{gc}, 0), signed($heapGrowth, 1, ""),
  signed($objGrowth, 1, " %"), "$result ($checks)");

my @sorted = sort { $b->{p95_ms} <=> $a->{p95_ms} } @stations;
my $table = "Latest run: $stamp, commit $commit, $scenario. Sorted by p95, the worst first.\n\n"
  . "| Station | Kind | Frames | Avg ms | p95 ms | p99 ms | Max ms | GC B/frame |\n|---|---|---|---|---|---|---|---|\n";
$table .= sprintf("| %s | %s | %d | %.2f | %.2f | %.2f | %.2f | %.0f |\n", $_->{station}, $_->{kind}, $_->{frames}, $_->{avg_ms}, $_->{p95_ms},
  $_->{p99_ms}, $_->{max_ms}, $_->{gc_bytes_per_frame}) for @sorted;
$table .= "\nSoak: $memNote; GC " . fmt($soakGc, 0) . " B/frame over the soak.\n";

my $report = "# Perf check $stamp\n\n$row\n" . (!@why ? "$result\n" : "FAIL:\n" . join("", map { "- $_\n" } @why)) . "\n$baseNote\n\n$table";
open(my $r, ">", "$run/report.md") or die "cannot write $run/report.md"; print $r $report; close $r;

if ($record) {
  if (!@lines) {
    @lines = ("# Performance (spec 049)\n\n", "Written by `scripts/perf-check.sh`.\n\n", "## Runs\n\n", "<!-- perf-runs:start -->\n",
      "| Date | Commit | Build | Scenario | Frame avg ms | p95 ms | p99 ms | GC B/frame | Heap growth MB | Objects growth | Result |\n",
      "|---|---|---|---|---|---|---|---|---|---|---|\n", "<!-- perf-runs:end -->\n\n", "## Stations\n\n", "<!-- perf-stations:start -->\n",
      "<!-- perf-stations:end -->\n");
  }
  my $text = join("", @lines);
  $text =~ s/(<!-- perf-runs:end -->)/$row$1/ or $text .= "\n<!-- perf-runs:start -->\n$row<!-- perf-runs:end -->\n";
  if (@stations) {
    $text =~ s/<!-- perf-stations:start -->.*?<!-- perf-stations:end -->/<!-- perf-stations:start -->\n$table<!-- perf-stations:end -->/s
      or $text .= "\n<!-- perf-stations:start -->\n$table<!-- perf-stations:end -->\n";
  }
  open(my $o, ">", $doc) or die "cannot write $doc"; print $o $text; close $o;
}

print "\n---- perf check ----\n$row";
print "$_\n" for map { "  - $_" } @why;
print "$baseNote\n";
print "report: $run/report.md\n", ($record ? "row added to $doc\n" : $partial ? "not recorded (a partial run)\n" : "not recorded (--no-record)\n");
print "RESULT: $result\n";
exit(@why ? 1 : 0);
PERL
}

if [ "$SELF_TEST" -eq 1 ]; then verdict --self-test "$DOC"; exit $?; fi


GAME="$ROOT/game/Builds/Windows/Worktown.exe"
# The newest game assembly of the build (an incremental build rewrites only the assemblies that changed).
GAME_DLL="$(ls -t "$ROOT"/game/Builds/Windows/Worktown_Data/Managed/Worktown.Game*.dll 2>/dev/null | head -1)"
if [ ! -f "$GAME" ]; then echo "no development build at $GAME (run WorktownBuild.BuildAll)" >&2; exit 2; fi
for tool in perl git cygpath tasklist netstat; do
  if ! command -v "$tool" >/dev/null 2>&1; then echo "needs $tool (Git Bash on Windows)" >&2; exit 2; fi
done
if tasklist //FI "IMAGENAME eq Worktown.exe" //NH 2>/dev/null | grep -qi "Worktown.exe"; then
  echo "a Worktown window is already running (a visual check or a load test?): they share port 7770. Wait for it or close it." >&2
  exit 2
fi
# Port 7770 in use by anything else (an Editor in play mode, a renamed build): TCP listening, or a UDP socket (Tugboat is UDP).
if netstat -ano 2>/dev/null | grep -Eq ':7770 .*LISTENING|^ *UDP +[^ ]*:7770 '; then
  echo "port 7770 is in use (netstat -ano | grep :7770): a game, an Editor in play mode or a load test. Wait for it or close it." >&2
  exit 2
fi
STALE=0
if [ -n "$GAME_DLL" ] && [ -f "$GAME_DLL" ] && [ -n "$(find game/Assets/_Project/Scripts -name '*.cs' -newer "$GAME_DLL" -print -quit 2>/dev/null)" ]; then
  STALE=1
  echo "WARNING: game scripts changed after the build ($GAME_DLL): the numbers are for the old build. Rebuild for a fair row."
fi

win() { cygpath -w "$1"; }
STAMP="$(date +%Y-%m-%d\ %H:%M)"
RUN_NAME="$(date +%Y%m%d-%H%M%S)"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
RUN="$OUT/$RUN_NAME"
mkdir -p "$RUN"
COMMIT="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)"
if [ -n "$(git status --porcelain -- game/Assets sim tools scripts 2>/dev/null)" ]; then COMMIT="$COMMIT+dirty"; fi
echo "perf check $STAMP, commit $COMMIT, $BOTS bots, soak $SOAK_MIN min, $RES -> $RUN"

# Ends a player at once: Windows' taskkill by its Windows pid, and kill -9 by the shell's pid (the fallback when
# /proc/<pid>/winpid could not be read), so a `wait` on it never blocks forever.
kill_player() {
  local pid="$1" winpid="${2:-}"
  if [ -n "$winpid" ]; then taskkill //F //PID "$winpid" >/dev/null 2>&1; fi
  kill -9 "$pid" 2>/dev/null
}

# Starts the game with the given arguments in the background ($1: the time limit in seconds, then it is killed). Sets PLAYER_PID,
# PLAYER_WINPID and PLAYER_WATCHER for finish_player.
# No polling loop: a 2-hour soak once used up Git Bash's process table (fork failures), so nothing here forks in a loop. The
# Windows pid is read at most 5 times, once a second; a watcher sleeps once for the limit and kills the player if it is still there.
start_player() {
  local limit="$1"; shift
  "$GAME" "$@" >/dev/null 2>&1 &
  local pid=$!
  local winpid=""
  for _ in 1 2 3 4 5; do winpid="$(cat "/proc/$pid/winpid" 2>/dev/null || true)"; [ -n "$winpid" ] && break; sleep 1; done
  ( sleep "$limit"; if kill -0 "$pid" 2>/dev/null; then touch "$RUN/.timeout-$pid"; kill_player "$pid" "$winpid"; fi ) &
  PLAYER_WATCHER=$!
  PLAYER_PID=$pid
  PLAYER_WINPID=$winpid
}

# Waits for a started player (its pid and watcher). Sets RUN_CODE (124 when the watcher killed it).
finish_player() {
  local pid="$1" watcher="$2"
  wait "$pid"
  RUN_CODE=$?
  kill "$watcher" 2>/dev/null
  wait "$watcher" 2>/dev/null
  if [ -f "$RUN/.timeout-$pid" ]; then RUN_CODE=124; rm -f "${RUN:?}/.timeout-$pid"; fi
}

# Starts the game with the given arguments and waits up to $1 seconds (then kills it). Sets RUN_CODE (124 on a timeout).
RUN_CODE=0
run_player() {
  start_player "$@"
  finish_player "$PLAYER_PID" "$PLAYER_WATCHER"
}

# ---- 1. the crash log and the tick watchdog ----
CRASH="skipped"
STALL="skipped"
if [ "$DO_CRASH" -eq 1 ]; then
  echo "==> crash log: forced exceptions"
  run_player 90 -batchmode -nographics -crash-test -crash-log "$(win "$RUN/crash-test.log")" -logFile "$(win "$RUN/crash-test-player.log")"
  missing=()
  for want in "crash test 049: main thread" "crash test 049: task" "crash test 049: background thread"; do
    grep -qF "$want" "$RUN/crash-test.log" 2>/dev/null || missing+=("$want")
  done
  grep -Eq '^build: .* hash [0-9a-f]{16}$' "$RUN/crash-test.log" 2>/dev/null || missing+=("the build hash")
  if [ "${#missing[@]}" -eq 0 ]; then CRASH="PASS"; else CRASH="FAIL"; fi
  echo "    crash log: $CRASH (player exit $RUN_CODE)$([ "${#missing[@]}" -gt 0 ] && printf ', missing: %s;' "${missing[@]}")"

  echo "==> tick watchdog: a forced stall on a host, one bot in the shop"
  # The host stops its tick 20 s after its server started (time for the bot to join); the bot must get the HostStalled notice
  # (flushed without a tick). The host quits by itself; the bot is ended after it (it would try to rejoin).
  start_player 120 -batchmode -nographics -host -stall-test 20 -crash-log "$(win "$RUN/stall.log")" -logFile "$(win "$RUN/stall-player.log")"
  host_pid=$PLAYER_PID; host_watcher=$PLAYER_WATCHER
  sleep 2
  start_player 150 -batchmode -nographics -connect 127.0.0.1 -bot -seed 1 -skip-whatsnew \
    -crash-log "$(win "$RUN/stall-bot-crash.log")" -logFile "$(win "$RUN/stall-bot.log")"
  bot_pid=$PLAYER_PID; bot_winpid=$PLAYER_WINPID; bot_watcher=$PLAYER_WATCHER
  finish_player "$host_pid" "$host_watcher"
  host_code=$RUN_CODE
  sleep 2
  kill_player "$bot_pid" "$bot_winpid"
  finish_player "$bot_pid" "$bot_watcher"
  stall_missing=()
  [ "$host_code" -eq 0 ] || stall_missing+=("host exit 0")
  grep -qF "kind: host tick stall" "$RUN/stall.log" 2>/dev/null || stall_missing+=("the crash log entry")
  grep -qF "[StallTest] the watchdog closed the shop" "$RUN/stall-player.log" 2>/dev/null || stall_missing+=("the host's close")
  grep -qF "[Net] the host says: HostStalled" "$RUN/stall-bot.log" 2>/dev/null || stall_missing+=("the bot's HostStalled notice")
  if [ "${#stall_missing[@]}" -eq 0 ]; then STALL="PASS"; else STALL="FAIL"; fi
  echo "    tick watchdog: $STALL (host exit $host_code)$([ "${#stall_missing[@]}" -gt 0 ] && printf ', missing: %s;' "${stall_missing[@]}")"
fi

# ---- 2. the profiling tour and the soak ----
LOADTEST="skipped"
if [ "$DO_SOAK" -eq 1 ]; then
  echo "==> profiling tour and soak: $BOTS bots, $SOAK_MIN min (ends about $(date -d "+$(( SOAK_MIN + 3 )) min" +%H:%M 2>/dev/null || echo later))"
  bash tools/bots/run-loadtest.sh --bots "$BOTS" --ramp "$RAMP" --hold $(( SOAK_MIN * 60 )) --out "$RUN/soak" --res "$RES" \
    --host-arg -perf-tour --host-arg "$(win "$RUN/perf-tour.jsonl")" --host-arg -view --host-arg first 2>&1 | tee "$RUN/loadtest.out"
  if [ "${PIPESTATUS[0]}" -eq 0 ]; then LOADTEST="PASS"; else LOADTEST="FAIL"; fi
fi

# ---- 3. the verdict, the row and the station table ----
verdict "$RUN" "$DOC" "$RECORD" "$STAMP" "$COMMIT" "$BOTS" "$SOAK_MIN" "$RES" "$CRASH" "$STALL" "$LOADTEST" "$STALE"
