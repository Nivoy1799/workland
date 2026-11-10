---
id: 049
title: Release stability (performance budget, crash logs, leak checks)
status: approved
owner: architect
builder: opus
---

# 049: Release stability

## Design
- **Performance budget:** 60 fps at 1080p on a mid PC (the human's PC as the reference, with numbers recorded) with 16 players (D36) in the restaurant. A profiling tour (dev) records frame times and GC allocations per station. Fix the top
  offenders.
- **Memory:** a 2-hour soak run (bots) with no growth in managed memory or object counts beyond a bound.
- **Crash logs:** a local crash and exception log with the build hash. An opt-in upload is an OPEN decision (O6-like);
  it is local-only until the human decides.
- Builds on the 045 fixes (crash on quit, MTU, spam).

## Acceptance criteria
- [ ] AC1: `verify.sh`.
- [ ] AC2: The profiling tour numbers in docs/performance.md, within budget or with a plan.
- [ ] AC3: The soak test result.
- [ ] AC4: The crash log written on a forced exception (test).

## Repeatable, not one-off (the human, 2026-11-10: "will need to run multiple times, especially in late-game dev")
- Everything above is one script: `scripts/perf-check.sh` runs the profiling tour, the soak test with bots, and the
  crash-log check, unattended.
- Each run appends a dated row to `docs/performance.md`: build hash, frame times (avg, p95, p99), GC per frame,
  memory growth, bot count, and pass or fail against the budget.
- A run fails (non-zero exit) when a number regresses more than 10 % against the best recorded run or breaks the budget,
  so a slow change is caught right away.
- It is run at least once per merged milestone (the orchestrator adds it to the pipeline for big specs), and the
  workflow doc says how.
- The scenario grows with the game: the profiling tour reads its station list from the scene, so new stations are
  measured without editing the script.
