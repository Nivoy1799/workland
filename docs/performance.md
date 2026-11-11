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
| Regression | frame avg, p95, p99 or GC per frame more than 10 % worse than ONE baseline row fails: the passing run of the same scenario with the lowest p95, not from a dirty tree or a stale build (heap and objects only have their bounds) | with a noise floor: 0.25 ms (avg, p95), 0.5 ms (p99), 256 B/frame; `perf-check.sh --self-test` checks the rule |

The 16.7 ms and the memory bounds are the builder's reading of the spec ("60 fps", "no growth beyond a bound"); the reviewer or
the human may set other numbers in `scripts/perf-check.sh` (the constants at the top of its verdict part).

**How it is measured.** The host is the development build, windowed 1920x1080, Direct3D 11, vsync and the frame cap off for the
run (in memory only), first-person view; 15 headless bots (`-bot`, spec 045) walk, pick up and carry in the same restaurant. The
host's own player walks to every Station of the scene (sorted by index; found at run time) and records 6 s there. Frame time is
`Time.unscaledDeltaTime`; GC per frame is Unity's "GC Allocated In Frame" counter (development builds only).

**Reference PC (the human's):** Intel Core i7-13700K (16 cores, 24 threads), 64 GB RAM, RTX 5080, Windows 11 Pro. The bots run
on the same PC, so the host's frame times are pessimistic (16 game processes share the CPU).

## Runs

<!-- perf-runs:start -->
| Date | Commit | Build | Scenario | Frame avg ms | p95 ms | p99 ms | GC B/frame | Heap growth MB | Objects growth | Result |
|---|---|---|---|---|---|---|---|---|---|---|
| 2026-11-11 07:21 | f795e0b+dirty | 7a49dcff | 15 bots, 15 min, 1920x1080 | 1.73 | 2.43 | 2.85 | 2025 | +0.6 | +0.0 % | PASS (crash PASS, watchdog PASS) |
| 2026-11-11 07:59 | 44ea185 | 7a49dcff | 15 bots, 120 min, 1920x1080 | 1.68 | 2.22 | 2.65 | 1965 | -0.1 | +0.0 % | PASS: 2-hour soak (crash part skipped; run separately), 16 of 16 processes alive, 47.9 KB/s per client (worst second 51.9), tick p95 0.78 ms; added by hand as spec 049 AC3 evidence (.verify/perf/20261111-075937) |
| 2026-11-11 10:07 | 7a78a00+dirty | 7a49dcff | 15 bots, 10 min, 1920x1080 | 1.55 | 1.98 | 2.36 | 1957 | +1.1 | +0.0 % | PASS (crash PASS, watchdog PASS) |
<!-- perf-runs:end -->

## Stations

<!-- perf-stations:start -->
Latest run: 2026-11-11 10:07, commit 7a78a00+dirty, 15 bots, 10 min, 1920x1080. Sorted by p95, the worst first.

| Station | Kind | Frames | Avg ms | p95 ms | p99 ms | Max ms | GC B/frame |
|---|---|---|---|---|---|---|---|
| Spit | Spit | 3585 | 1.67 | 2.16 | 2.56 | 4.08 | 2417 |
| Fryer | Fryer | 3743 | 1.60 | 2.08 | 2.42 | 3.97 | 1862 |
| Drinks | DrinkDispenser | 3619 | 1.66 | 2.08 | 2.44 | 3.56 | 1889 |
| Counter | Counter | 3738 | 1.60 | 2.03 | 2.38 | 3.87 | 2041 |
| Plates | PlateShelf | 3796 | 1.58 | 2.02 | 2.33 | 3.37 | 2197 |
| Fridge | Fridge | 3803 | 1.58 | 2.02 | 2.36 | 3.75 | 2106 |
| Griddle | Griddle | 3755 | 1.60 | 2.02 | 2.31 | 4.64 | 1926 |
| Dish return | DishReturn | 3862 | 1.55 | 2.00 | 2.40 | 3.89 | 1921 |
| Stove 1 | Stove | 3904 | 1.54 | 1.99 | 2.36 | 2.97 | 1864 |
| Dry shelf | Shelf | 3890 | 1.54 | 1.99 | 2.36 | 2.85 | 2005 |
| Sink | Sink | 3866 | 1.55 | 1.95 | 2.39 | 4.43 | 1894 |
| Orders | Terminal | 4024 | 1.49 | 1.92 | 2.31 | 2.88 | 1805 |
| Trash | TrashCan | 4024 | 1.49 | 1.90 | 2.23 | 3.95 | 1865 |
| Stove 2 | Stove | 4007 | 1.50 | 1.90 | 2.28 | 4.38 | 1932 |
| Bin | Bin | 4011 | 1.50 | 1.89 | 2.22 | 3.42 | 1711 |
| Register | Register | 4112 | 1.46 | 1.86 | 2.24 | 3.21 | 1850 |
| Assembly | Assembly | 4187 | 1.43 | 1.78 | 2.15 | 2.94 | 2031 |

Soak: 610 samples with all players; heap floor and unity_objects median, first vs last 61 s after a 122 s warm-up; GC 2259 B/frame over the soak.
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
