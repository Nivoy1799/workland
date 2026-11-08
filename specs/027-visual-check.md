---
id: 027
title: Visual check tool (scripted tour, walking frames, contact sheet)
status: approved
owner: architect
builder: opus
---

# 027: Visual check tool

## Goal
Every later spec gets checked by eye without the human. Today the demo walks by simulating keyboard
keys, which Unity ignores when the window has no focus. So our screenshots never showed motion, and the
human had to find the walking problems in spec 025. This spec adds a scripted tour that moves the player
through its normal movement intents (no fake keys) and shoots a fixed set of views, frame sequences of a
walking and a sprinting person seen by another player, and one contact sheet image per run. One script
runs the whole check against the WSL dedicated server.

Approved by the orchestrator under the human's 24 h grant of 2026-11-08 (docs/cooking-roadmap.md).

## Depends on
- Specs 010 (movement intents, MoveData), 018 (views), 020/021 (`-demo-cookware`), 025 (characters).

## Scope
- `game/Assets/_Project/Scripts/Dev/` (new folder, dev builds only: `#if DEVELOPMENT_BUILD || UNITY_EDITOR`):
  `ScriptedIntentSource` (feeds move, look, sprint into the owner's PlayerController), `TourRules` (pure:
  path following, shot table), `TourRunner` (`-tour <name>`), `ContactSheet` (pure layout + Texture2D writer).
- `game/Assets/_Project/Scripts/Net/PlayerController.cs` (change: optional intent source; null = devices as today).
- `game/Assets/_Project/Scripts/Net/PickupInteractor.cs` (change: `-demo-cookware` walks through
  `ScriptedIntentSource` instead of simulated keys; behaviour otherwise unchanged).
- `game/Assets/_Project/Scripts/Net/Rules/LaunchArgs.cs` (change: `-tour <name>`, `-tour-out <dir>`, `-observe`).
- `game/Assets/_Project/Scripts/UI/DevScreenshot.cs` (change only if needed to share the writer).
- `scripts/visual-check.sh` (new), `docs/workflow.md` (one paragraph: how to run it), tests, `WhatsNew.json` not needed
  (developer tool, no player-facing change).

## Out of scope
- New RPCs or MoveData fields (RpcSurfaceTests and MoveDataSurfaceTests stay unchanged).
- Anything in release builds: all new code is compiled out of non-development builds.
- Image comparison against reference images (later).

## Design
**Intents, not keys.** `PlayerController` gets `IPlayerIntentSource` (move vector, look yaw/pitch delta,
sprint, crouch, jump). When set, it replaces device input on the owner only; prediction and the server stay
the same, so the server still validates everything (rule 6).

**Tours (`-tour <name>`, client, dev builds).** A tour is a list of steps: `WalkTo(point, sprint)`,
`Look(yaw, pitch)`, `View(first|third)`, `Wait(s)`, `Shot(name)`, `Frames(name, seconds, interval)`,
`Act(...)` (reuses the demo's pickup/place/press helpers). Tours, all in code:
- `look`: standard shots: kitchen overview third person, stoves close, counter and queue, street, first person
  at the worktop, first person holding the pan, a person standing next to a worktop (scale).
- `walk`: the player walks 6 m, then sprints 8 m, then crouch-walks 2 m, past a fixed observer.
- `cook`: the `-demo-cookware` sequence with shots between the steps.
After the last step the client writes the contact sheet and quits (exit code 0; 1 if a step timed out).

**Observer (`-observe <tour>`).** A second client stands at the tour's observer point with a fixed camera,
and takes `Frames` at the same times. This is how we see remote players (network smoothing plus animation)
the way other players see them.

**Contact sheet.** `ContactSheet.Layout(count, columns, cellSize)` (pure) plus a writer that downsamples each
frame into one PNG with frame numbers. Output: `<out>/<tour>/NN-name.png`, `<out>/<tour>/sheet.png`.

**Script.** `scripts/visual-check.sh [--tours look,walk,cook] [--out .verify/visual]`: starts the WSL server
build, the observer, then the tour client (both windowed 1280x720), waits for both to exit (timeout per tour),
stops the server, prints the sheet paths and the count of `Exception` lines in the client logs (non-zero
exit if any exception or timeout).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes.
- [ ] AC2: Tests: `TourRules` (path following reaches each point within 0.3 m, sprint flag per step, unknown
      tour name rejected), `ContactSheet.Layout` (rows, columns, sizes, edge cases), `LaunchArgs` (new args,
      `-tour` only valid with `-connect`).
- [ ] AC3: Release-build check: the Dev scripts are excluded when DEVELOPMENT_BUILD is off (compile test or
      asmdef define constraint).
- [ ] AC4: `scripts/visual-check.sh --tours look,walk` produces both sheets with the walking person visibly
      moving across frames, while another window has focus (run with a third window focused). No exceptions.
- [ ] AC5: `-demo-cookware` still finishes its sequence (log lines as before), now without window focus.

## Defaults to review
- Frame interval 0.1 s for `walk`, 24 frames per segment; sheet 6 columns, 320x180 cells.

## Test plan
- EditMode: `TourRulesTests`, `ContactSheetTests`, `LaunchArgsTests` (extended).
- Run `scripts/visual-check.sh` once and attach the sheets to the implementation notes (paths).

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
