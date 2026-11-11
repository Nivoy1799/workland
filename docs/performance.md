# Performance (spec 049)

Every run of `bash scripts/perf-check.sh` appends one row to the table below and replaces the station table. How and when to
run it: docs/workflow.md, "Perf check".

## Budget and bounds

| What | Budget or bound | Why |
|---|---|---|
| Frame time, average over the profiling tour | at most 16.7 ms | 60 fps at 1080p with 16 players (D36) in the restaurant (spec 049) |
| Frame time, p95 over the profiling tour | at most 16.7 ms | 95 % of frames make 60 fps |
| Frame time p99, GC bytes per frame | no budget; only the regression rule | recorded so a slow or allocating change is caught |
| Managed heap growth over the soak | at most 10 MB | the heap's floor (minimum of a window: the GC's saw-tooth removed), first vs last window after the warm-up |
| Unity object count growth over the soak | at most 2 % | median of a window ("Object Count" profiler counter), first vs last window |
| Bandwidth per client, average over the soak | at most 35.0 KB/s (KB = 1000 bytes) | spec 054: the FishNet payload each bot received, 16 players; about 30 % headroom under the 50 KB/s target for cooking traffic, the Steam relay and more NPCs (16 x 35 KB/s = 4.5 Mbit/s host upload) |
| Bandwidth per client, worst second | at most 40.0 KB/s | spec 054: the highest second of any bot; `run-loadtest.sh --kbps-budget 35,40` exits 4 when either is over |
| Wire bytes per client | recorded only (no budget yet) | spec 054: payload + LiteNetLib headers + IPv4/UDP (28 B per datagram), shown as "wire" in the KB/s column |
| Regression | frame avg, p95, p99 or GC per frame more than 10 % worse than ONE baseline row fails: the passing run of the same scenario with the lowest p95, not from a dirty tree or a stale build (heap and objects only have their bounds) | with a noise floor: 0.25 ms (avg, p95), 0.5 ms (p99), 256 B/frame; `perf-check.sh --self-test` checks the rule |

The 16.7 ms and the memory bounds are the builder's reading of the spec ("60 fps", "no growth beyond a bound"); the reviewer or
the human may set other numbers in `scripts/perf-check.sh` (the constants at the top of its verdict part; spec 054's bandwidth
budget is `KBPS_AVG_BUDGET` and `KBPS_WORST_BUDGET` just above it). The column "KB/s/client (worst s)" is the payload average
(worst second) and the wire average; rows before spec 054 read "-". A `--net-stats` run is marked "(partial run: net-stats)" and
is never a baseline (FishNet's statistics allocate).

**How it is measured.** The host is the development build, windowed 1920x1080, Direct3D 11, vsync and the frame cap off for the
run (in memory only), first-person view; 15 headless bots (`-bot`, spec 045) walk, pick up and carry in the same restaurant. The
host's own player walks to every Station of the scene (sorted by index; found at run time) and records 6 s there. Frame time is
`Time.unscaledDeltaTime`; GC per frame is Unity's "GC Allocated In Frame" counter (development builds only).

**Reference PC (the human's):** Intel Core i7-13700K (16 cores, 24 threads), 64 GB RAM, RTX 5080, Windows 11 Pro. The bots run
on the same PC, so the host's frame times are pessimistic (16 game processes share the CPU).

## Runs

<!-- perf-runs:start -->
| Date | Commit | Build | Scenario | Frame avg ms | p95 ms | p99 ms | GC B/frame | Heap growth MB | Objects growth | KB/s/client (worst s) | Result |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 2026-11-11 07:21 | f795e0b+dirty | 7a49dcff | 15 bots, 15 min, 1920x1080 | 1.73 | 2.43 | 2.85 | 2025 | +0.6 | +0.0 % | - | PASS (crash PASS, watchdog PASS) |
| 2026-11-11 07:59 | 44ea185 | 7a49dcff | 15 bots, 120 min, 1920x1080 | 1.68 | 2.22 | 2.65 | 1965 | -0.1 | +0.0 % | 47.9 (51.9) | PASS: 2-hour soak (crash part skipped; run separately), 16 of 16 processes alive, 47.9 KB/s per client (worst second 51.9), tick p95 0.78 ms; added by hand as spec 049 AC3 evidence (.verify/perf/20261111-075937) |
| 2026-11-11 10:07 | 7a78a00+dirty | 7a49dcff | 15 bots, 10 min, 1920x1080 | 1.55 | 1.98 | 2.36 | 1957 | +1.1 | +0.0 % | - | PASS (crash PASS, watchdog PASS) |
| 2026-11-11 15:22 | 796da9f+dirty | 7a49dcff | 15 bots, 15 min, 1920x1080 | 1.57 | 1.99 | 2.32 | 1892 | +0.2 | +0.0 % | 3.1 (4.8), wire 4.2 | PASS (crash PASS, watchdog PASS) |
<!-- perf-runs:end -->

## Stations

<!-- perf-stations:start -->
Latest run: 2026-11-11 15:22, commit 796da9f+dirty, 15 bots, 15 min, 1920x1080. Sorted by p95, the worst first.

| Station | Kind | Frames | Avg ms | p95 ms | p99 ms | Max ms | GC B/frame |
|---|---|---|---|---|---|---|---|
| Stove 1 | Stove | 3657 | 1.64 | 2.13 | 2.49 | 3.11 | 1808 |
| Spit | Spit | 3576 | 1.68 | 2.13 | 2.38 | 3.20 | 1650 |
| Drinks | DrinkDispenser | 3540 | 1.70 | 2.12 | 2.44 | 4.97 | 1893 |
| Stove 2 | Stove | 3627 | 1.66 | 2.10 | 2.40 | 3.80 | 1809 |
| Dry shelf | Shelf | 3838 | 1.56 | 2.03 | 2.38 | 4.55 | 1957 |
| Counter | Counter | 3796 | 1.58 | 2.01 | 2.29 | 5.55 | 1920 |
| Griddle | Griddle | 3780 | 1.59 | 1.99 | 2.37 | 3.19 | 1842 |
| Fridge | Fridge | 3911 | 1.53 | 1.99 | 2.34 | 3.05 | 2089 |
| Fryer | Fryer | 3821 | 1.57 | 1.98 | 2.29 | 3.82 | 1778 |
| Plates | PlateShelf | 3825 | 1.57 | 1.97 | 2.27 | 2.90 | 2101 |
| Sink | Sink | 3831 | 1.57 | 1.96 | 2.25 | 3.02 | 1847 |
| Trash | TrashCan | 3792 | 1.58 | 1.96 | 2.23 | 5.12 | 1862 |
| Dish return | DishReturn | 3846 | 1.56 | 1.95 | 2.25 | 3.36 | 1871 |
| Bin | Bin | 3899 | 1.54 | 1.94 | 2.18 | 3.07 | 1851 |
| Orders | Terminal | 3941 | 1.52 | 1.89 | 2.17 | 2.89 | 1845 |
| Assembly | Assembly | 4029 | 1.49 | 1.88 | 2.22 | 3.61 | 2080 |
| Register | Register | 4042 | 1.48 | 1.87 | 2.19 | 3.79 | 1917 |

Soak: 911 samples with all players; heap floor and unity_objects median, first vs last 91 s after a 182 s warm-up; GC 1874 B/frame over the soak.
<!-- perf-stations:end -->

## Findings and plan

**2026-11-11 (spec 049, first runs).** Far inside the budget: 1.73 ms average (about 580 fps), p95 2.43 ms at 1080p with 16
players, the worst station (Drinks) p95 3.05 ms; one 37 ms frame at the spit (max, not p95). The first row's "stale build"
mark was a false alarm of the build check (an incremental build kept the unchanged Worktown.Game.dll; the check now looks at
the newest game assembly). GC was the offender: about 2.5 KB allocated every frame at every station, so a constant source, not
a station. HudView rebuilt its strings every frame; with that fixed it is about 2.0 KB (-20 %). Plan for the rest: a profiler
capture of one tour (GC.Alloc by caller) to rank them; known candidates are the first-person crosshair prompt
(InteractionRules.PromptFor every frame) and FishNet's per-tick work. Memory: +0.6 MB heap floor and +0.0 % objects over the
15-minute soak. A 120-minute soak (`--soak-minutes 120`) was started once and died at about 80 minutes of Git Bash fork
failures (the process table, see docs/workflow.md); until it runs clean the 2-hour bound is only checked over 15 minutes.
Per-run data: `.verify/perf/<run>/` (gitignored).

**2026-11-11 (spec 054, network headroom).** The FishNet payload per client fell from about 44-48 KB/s to 3.1 KB/s (worst second
4.8, wire 4.2) with 16 players: other players' inputs and states are no longer forwarded to everyone; they are sent as a 15 Hz
NetworkTransform instead (the per-packet table is in docs/loadtest-results.md). Frame times and GC are unchanged (GC 1892 B/frame
against 2025 in the earlier 15-minute row; that row is from a dirty tree, so there is still no baseline for the scenario).
