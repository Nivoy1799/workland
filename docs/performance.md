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
| Regression | more than 10 % worse than the best passing run of the same scenario fails | with a noise floor: 0.25 ms (avg, p95), 0.5 ms (p99), 256 B/frame, 1 MB, 0.5 % |

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
<!-- perf-runs:end -->

## Stations

<!-- perf-stations:start -->
Latest run: 2026-11-11 07:21, commit f795e0b+dirty, 15 bots, 15 min, 1920x1080. Sorted by p95, the worst first.

| Station | Kind | Frames | Avg ms | p95 ms | p99 ms | Max ms | GC B/frame |
|---|---|---|---|---|---|---|---|
| Drinks | DrinkDispenser | 2613 | 2.30 | 3.05 | 3.64 | 5.82 | 2282 |
| Spit | Spit | 2826 | 2.12 | 2.86 | 3.53 | 37.05 | 2462 |
| Bin | Bin | 3494 | 1.72 | 2.42 | 2.93 | 4.81 | 1957 |
| Orders | Terminal | 3353 | 1.79 | 2.40 | 2.69 | 3.79 | 1967 |
| Fryer | Fryer | 3532 | 1.70 | 2.37 | 2.75 | 3.88 | 1916 |
| Trash | TrashCan | 3525 | 1.70 | 2.37 | 2.80 | 5.32 | 1963 |
| Sink | Sink | 3520 | 1.71 | 2.28 | 2.62 | 4.40 | 1952 |
| Counter | Counter | 3398 | 1.77 | 2.28 | 2.84 | 4.92 | 2118 |
| Plates | PlateShelf | 3438 | 1.75 | 2.28 | 2.75 | 4.22 | 2013 |
| Dish return | DishReturn | 3604 | 1.67 | 2.27 | 2.69 | 4.13 | 1949 |
| Griddle | Griddle | 3531 | 1.70 | 2.22 | 2.53 | 3.82 | 1953 |
| Stove 2 | Stove | 3636 | 1.65 | 2.17 | 2.63 | 3.88 | 1898 |
| Stove 1 | Stove | 3591 | 1.67 | 2.17 | 2.56 | 3.77 | 1911 |
| Register | Register | 3751 | 1.60 | 2.15 | 2.79 | 3.35 | 2152 |
| Dry shelf | Shelf | 3679 | 1.63 | 2.15 | 2.53 | 3.22 | 2035 |
| Fridge | Fridge | 3690 | 1.63 | 2.12 | 2.50 | 3.74 | 2087 |
| Assembly | Assembly | 3884 | 1.54 | 2.01 | 2.31 | 3.76 | 1959 |

Soak: 909 samples with all players; heap floor and unity_objects median, first vs last 90 s after a 181 s warm-up; GC 2445 B/frame over the soak.
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
