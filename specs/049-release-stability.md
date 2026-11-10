---
id: 049
title: Release stability (performance budget, crash logs, leak checks)
status: draft
owner: architect
builder: opus
---

# 049: Release stability

## Design
- **Performance budget:** 60 fps at 1080p on a mid PC (the human's PC as the reference, with numbers recorded) with 8
  players in the kitchen. A profiling tour (dev) records frame times and GC allocations per station. Fix the top
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
