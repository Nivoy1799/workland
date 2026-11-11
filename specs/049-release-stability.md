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

## Implementation notes

**Crash log (AC4).** `Scripts/Diagnostics/`: `CrashLog` installs before the first scene in every player build (not the Editor):
Unity's threaded log callback (Exception, Assert), `AppDomain.UnhandledException` (any thread, written at once: the runtime may
end the process right after), `TaskScheduler.UnobservedTaskException`, plus a host tick stall note. Entries (`CrashReport`, pure):
UTC time, version and the spec 047 build hash, kind, thread (main or not, id, name, pool/background), message, the whole stack.
The same exception is written 5 times per session, then counted; the file starts over at 2 MB (`.old` kept). File:
`-crash-log path`, else `crash.log` in `Application.persistentDataPath`. A player's own session (not scripted runs) leaves a
`session.marker`; the next start notes a session that never quit cleanly and names Unity's native `Crashes` folder. Local only:
`ICrashSink` is the seam for an opt-in upload (OPEN, not built). Tests: `ReleaseStabilityTests` (a forced exception on a background
thread lands in the file with hash and stack); the perf check's `-crash-test` player forces main-thread, task and background-thread
exceptions: all three were written, the background one as "unhandled (the runtime ends the process)".

**Host network freeze.** `TickWatchdog` (on the NetBootstrap, every build) stamps FishNet's `OnTick`; on a server with no tick for
over 2 s while at least 10 frames were drawn (`TickStallRules`), it logs `[Net] HOST TICK STALL ...` (LogError), writes a crash log
entry with FishNet's state (Tick, reader loop enabled or not, timeScale, deltaTime, connections) and closes the shop
(`NetBootstrap.CloseAfterStall`): a new `DisconnectReason.HostStalled = 9` notice to every other player, flushed with FishNet's
disconnect messages (they send without a tick), the server stopped a second later with the transports' stop events pumped by hand,
and the host's own "The shop stopped" screen. Verified by `-stall-test 5` (disables FishNet's `NetworkReaderLoop`, the tick's
driver): the watchdog fired after 2.0 s and the shop closed. **Trigger not found.** Evidence: 047c had no exception at all, the
tick just stopped after "Fryer basket 0: Lowered" while frames continued (~250 fps), clients timed out, reconnects got
ConnectionFailed (nobody polled the server socket), a native crash ~2 min later; 039b stopped after "Stove 2 burner 1: on" and
ended with the `TimeManager.get_LocalTick` NRE at IL offset 0 on a non-main thread with no caller frames (a null `this` faulting
inside the getter, typical for a finalizer or a thread callback after teardown; `~FishySteamworks()` calls `Shutdown()` on the
finalizer thread and is one candidate, unproven). FishNet untouched. The next freeze will leave the watchdog's state dump.

**Profiling tour, soak, perf check (AC2, AC3).** `scripts/perf-check.sh` (documented in its header and docs/workflow.md, "Perf
check"): the crash-log check (`-crash-test`), the watchdog check (`-host -stall-test 5`), then `tools/bots/run-loadtest.sh` (new
`--res` and `--host-arg`; bots and host now write their crash logs into the run folder) with 15 bots and a windowed 1920x1080 host
running `-perf-tour` (Dev `PerfTourRunner`: waits for the 16 players, walks the host's own player to every `Station` found in the
scene, aims at it, records 6 s of `unscaledDeltaTime` and Unity's "GC Allocated In Frame"; vsync and the cap off in memory). The
host's metrics line (LoadTestRunner) gained `gc_kb_per_frame`, `unity_objects` (the "Object Count" counter), `gc_collections`. The verdict (perl):
budget, memory bounds, regression against the best passing row of the same scenario, a dated row in docs/performance.md, the
per-station table replaced (worst p95 first), `report.md` in `.verify/perf/<run>/`. Exit 0/1/2. The soak is 15 minutes by default (coordinator, after a 2-hour run died of Git Bash fork failures);
`--soak-minutes 120` is the spec's 2 hours. Options: `--quick` (10-min soak), `--soak-minutes`, `--bots`, `--res`, `--no-record`, `--skip-crash`, `--skip-soak` (partial runs are never recorded). It refuses to
start while another Worktown window runs (port 7770) and warns when the build is older than the scripts.
Choices for review: the budget is frame avg and p95 at most 16.7 ms (p99 and GC per frame are regression-only); the bounds are a
10 MB managed-heap-floor growth and 2 % object-count growth (first vs last window after a warm-up); the 10 % rule has a noise
floor (0.25 ms, 0.5 ms for p99, 256 B, 1 MB, 0.5 %). All are constants at the top of the verdict part.

**Top offender fixed.** Frames are far inside the budget (below); the constant GC (about 2.5 KB a frame at every station) was the
offender. HudView rebuilt its wallet, clock, shift and held-item strings every frame; now only on a change (held text 10 times a
second). Next candidates (not done): the crosshair prompt (InteractionRules.PromptFor builds a string every frame in first
person; Held/AimTarget lack IEquatable to cache it), FishNet's own per-tick allocations; a profiler capture would rank them.

**Bandwidth per packet type (045 follow-up): not done.** 049's budget is frames, GC and memory. Found: FishNet's
`StatisticsManager`/`NetworkTrafficStatistics` give per-packet-id bytes, but its enable mode is a serialized field on the scene's
StatisticsManager (scene builder change) and the traffic types live under `FishNet/Runtime/Editor/NetworkProfiler` with internal
members. A spec of its own.

**048 review follow-ups.**
1. Prompts follow a rebind: `Settings/KeyPrompts` (pure: `KeyName(bindings, action)`, `MoveKeys`, `Fill` of `{Action}`/`{Move}`
   tokens). `StationRules.Prompt(kind, name, useKey, viewKey)` (Station caches it per settings version); `ControlsHelp.Text(bindings)`
   with tokens; the lobby hint names the Players key; the tutorial's first step says `{Move}` and TutorialRunner fills it. Tests:
   `Review048Tests` (a rebind of Use, ToggleView, Players, MoveForward shows in the prompt, F1, lobby and tutorial texts). Dropped
   from 048's "Not covered".
2. Key capture: `Settings/CaptureGate` (pure). Jump and crouch wait while a menu or the settings are open
   (`SettingsView.AnyOpen`); the lobby's key only toggles with no other menu open; a capture reads input only from a later frame,
   and for the gamepad only after all buttons were up once. Tests in `Review048Tests`.
- Nits: SettingsApplier uses an `Instantiate`d URP asset as `QualitySettings.renderPipeline` (the shared asset is never written;
  restored and destroyed on teardown); `RenderScale(preset, original)` keeps the asset's own scale for High; `GameSettings.SchemaVersion
  = 1` (an old file without it loads; test); `UsesSavedSettings(LaunchArgs)` and a `-host -metrics` host counts as scripted; Digit1-6
  reserved (quick chat); `HeatRingRules.GoodMark/PerfectMark` removed (the test uses RingPalette); unused `keyboard` parameters gone;
  the pad capture caches its button array.

**Hygiene.** DefaultPrefabObjects re-saves reverted after each build; UnityConnectSettings.asset untouched; LF everywhere.

**Numbers (docs/performance.md, 2026-11-11, i7-13700K / RTX 5080, 15 bots + host at 1920x1080).** Tour over 17 stations: frame
avg 1.73 ms, p95 2.43, p99 2.85 (budget 16.7: met by far); GC 2.0 KB/frame (2.5 before the HudView fix); 15-min soak: heap floor
+0.6 MB, objects +0.0 % (bounds 10 MB, 2 %): PASS; crash log and watchdog checks PASS. The 2-hour soak was started once and died
at about 80 minutes of Git Bash fork failures (the shell's process table, with the builder's grep polling loops running beside 16
game processes), not of the game; per the coordinator the default is now 15 minutes and the script forks no polling loops
(`run_player` waits once, a watcher sleeps once). AC3's 2-hour soak is therefore still open (`--soak-minutes 120`).

**Checks.** Visual check `--tours look,cook,tutorial,menu --out .verify/visual049` on fresh builds: PASS, 0 Exception lines in every log, no tick stall; `verify.sh` PASS (EditMode 1339, was 1322; sim 400).
