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
- [x] AC3: The soak test result. (2-hour soak 2026-11-11, `--soak-minutes 120 --skip-crash`: PASS, heap -0.1 MB, objects +0.0 %, 16 of 16 alive; row in docs/performance.md, run folder `.verify/perf/20261111-075937`.)
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
(`run_player` waits once, a watcher sleeps once). AC3's 2-hour soak then ran on 2026-11-11 (see AC3).

**049 review fixes (part 1, C#).** `CloseSoon` runs each `StopConnection` through `Net/Rules/GuardedCall` (pure; a throw is
logged, the next stop, `Flow.Fail` and the pump still run). A dedicated server (`-server`, or a server build's default) quits with
exit 3 after the stall close (`TickStallRules.QuitsAfterStall`; not under `-stall-test`, whose runner owns the exit). Crash log:
a session cap of 256 keys and 1000 entries (`CrashReport.WithinCap`), then one `CapText` line, the rest only counted in the closing
line; the recorder scrubs message and stack (`CrashReport.Scrub`): the temp folder becomes `%TEMP%` (first, it sits inside the profile),
the user profile `%USERPROFILE%`, either slash, any case, whole folder names only; this also covers the previous-session note's
Crashes path. `UsesSavedSettings` also treats `-stall-test`, `-crash-test`, `-perf-tour` as scripted. The friends hint names the
bound Players key (`MenuRules.NoFriendsHosting`). An unknown reason shows "Disconnected (reason n)" (`ConnectionMessages.Unknown`);
ProtocolSurface's doc says enum values are not hashed. Tests: `Review049Tests` (11). verify.sh PASS (EditMode 1350, sim 400).

**049 review fixes (part 2, perf-check.sh).** The regression baseline is ONE row (`baseline()`: the passing, non-partial run of the
same scenario with the lowest p95, never `+dirty` or `(stale build)`); avg, p95, p99 and GC are compared with that row, heap and
objects only with their bounds; `--self-test` checks the rule on made-up rows and prints each scenario's baseline from the doc. The
watcher falls back to `kill -9` when the Windows pid is unknown (`kill_player`). The start refuses when port 7770 is in use
(netstat: TCP LISTENING or a UDP socket; Tugboat is UDP). The stall test now runs `-stall-test 20` with one bot, which must log the
HostStalled notice (no polling loops: start, one wait, then the bot is killed). The 2-hour soak row was added by hand (AC3).
Quick run 2026-11-11 10:07 on fresh builds: PASS (crash, watchdog with the bot's notice, 16 of 16 alive); visual check
`.verify/visual049b` PASS.

**Checks.** Visual check `--tours look,cook,tutorial,menu --out .verify/visual049` on fresh builds: PASS, 0 Exception lines in every log, no tick stall; `verify.sh` PASS (EditMode 1339, was 1322; sim 400).

**Host freeze investigation (049b).** Finding: in both cases the host **process died**. It did not stall while still
alive, so the tick watchdog would not have fired.
- Evidence, 047c. Unity's native crash report `Crash_2026-11-11_024338590` (02:43:38 UTC, 03:43:38 local) belongs to the
  cook observer/host. The client's "Server has timed out" came at 03:44:38, exactly FishNet's 60 s client timeout
  (`ClientManager._remoteServerTimeoutDuration`) later. The client's fry ring rose until about 20 s after the lower and
  then froze. The host log shows about 20 s of observer frames after "Fryer basket 0: Lowered" (nothing was due from the
  cook in that time), then `Crash!!!`. Read with a small minidump parser: the fault is 0xC0000005 with flag 8 (DEP, an
  *execute* of non-executable memory at 0x1be267984b5, not inside any module), on the **main thread**. The return
  address on top of the stack is in JIT code (0x1bddc7984c0, the call site with 0x4A000000 more in its high bits), below
  it mono runtime-invoke and UnityPlayer. So managed code called through a corrupted or stale code pointer. All other
  threads were idle: finalizer, thread pool, Steam, sockets.
- Evidence, 039b. The client's pan stopped at "Cooking 22" about 5 s after "burner on". The client timed out at 21:44:33,
  60 s after the host's log ends. The host log ends with the `get_LocalTick` NRE. Its trace has one frame (IL 0x0, no
  caller), it arrived through Unity's UnhandledException catcher, and then come Steam's
  "Trying to close low level socket support" lines. Those are printed when the process exits (DLL detach). There is no
  `Crash!!!`. A managed thread's unhandled exception does *not* end the player (049's crash test logs "done" afterwards).
  A one-frame NRE (Mono's name for an access violation in JIT code) followed by process exit fits a thread entering JIT
  code through a bad pointer, then a Mono abort. That is the same signature as 047c.
- Hypotheses checked against the sources (read only):
  1. `~FishySteamworks` → `Shutdown()`. Unlikely. A live transport cannot be collected, because Multipass and native
     hold it. At quit, `OnDestroy` → `Shutdown` runs first and sets `_shutdownCalled`, so the finalizer returns at once.
     A duplicate that was never started returns too (`_shutdownCalled` starts true). The logs show no duplicate
     NetworkManager. `~Tugboat` and the Tugboat socket finalizers are no-ops after `OnDestroy`.
  2. FishNet finalizers on the finalizer thread: `SyncList` (our `Food.Parts` and `Vessel` lists die all through the cook
     tour), `UniversalTickSmoother`. The SyncList ones return their collections to `CollectionCaches`, which locks. The
     smoothers are pooled, so they are not collected. Managed only, so they cannot cause a DEP fault.
  3. Thread-unsafe LiteNetLib/Tugboat use. Native sockets are off (`UseNativeSockets = false`), the stop path is not
     threaded, and events are polled on the main thread.
  4. Native callbacks into managed code. Steamworks.NET uses manual dispatch, and we register no warning or debug hooks.
     FishNet's threaded smoothers are off (no `FISHNET_THREADED_TICKSMOOTHERS`). Our own threads: only TourCapture's
     PNG tasks (managed arrays) and CrashLog.
  Left open: a native-side corruption, from Unity or Mono, the Steam overlay (`gameoverlayrenderer64.dll` is loaded in the
  host), or the GPU driver. Without UnityPlayer and mono symbols the dump cannot name it. Both deaths happened while heat
  was cooking food: the fryer in 047c, pan and pot in 039b.
- Reproduction: 4 cook tours on the current builds (`.verify/freeze-1..4`), one at a time. All PASS: 0 Exception lines,
  no tick stall, no crash log entry, no native crash. **Not reproduced** in 4 runs.
- Changed (no C#): `scripts/visual-check.sh` now passes `-crash-log <out>/<tour>-tour.crash.log` and `-observer.crash.log`
  to each window. Before, both shared the default file in the data folder. It also copies any Unity native crash report
  written during a tour (`%TEMP%/DefaultCompany/Worktown/Crashes`, newer than a mark file) into
  `<out>/<tour>-native-crash/`. A native crash fails the tour, and the report line counts crash log entries and native
  crashes. The next death will leave its dump and per-window log in the run folder.
- Checks: `verify.sh` PASS (EditMode 1350, sim 400, gateway 175). Visual check `--tours look,cook,tutorial,menu --out .verify/visual049c`: PASS, with 0 crash log entries and 0 native crashes in every tour.
- Follow-ups (not done): a client-side "host gone" sooner than FishNet's 60 s Development timeout. A run with Steam's
  overlay off for the observer, to rule it in or out. If the native crash repeats, take a full-memory dump with
  symbols (Unity's symbol server) to name the faulting frame.
