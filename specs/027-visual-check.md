---
id: 027
title: Visual check tool (scripted tour, walking frames, contact sheet)
status: done
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

**Script.** `scripts/visual-check.sh [--tours look,walk,cook] [--out .verify/visual] [--server local|wsl]`: default `local` (a Windows `-host` window as server, because the WSL link has MTU 1280 while Tailscale runs, see spec 003); `wsl` starts the WSL server
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
- **Dev-only (AC3):** `Scripts/Dev` is its own assembly `Worktown.Game.Dev` with the define constraint
  `UNITY_EDITOR || DEVELOPMENT_BUILD` and `autoReferenced: false`; nothing in Worktown.Game references it. The hooks in
  Worktown.Game sit inside `#if DEVELOPMENT_BUILD || UNITY_EDITOR`: `PlayerController.IIntentSource` (nested
  interface) + `IntentSource`, `PlayerCameraRig.IntentSource`, `PickupInteractor.DevClick/DevSend*`. `DevBuildTests`
  checks the constraint with Unity's own (internal) evaluator against release/dev/editor define sets, that no release
  asmdef references the Dev assembly, and that every hook line in Scripts/ is inside the guard. Both current builds
  are development builds, so `Worktown.Game.Dev.dll` ships in them.
- **Intents (netcode-adjacent, please review):** when an intent source is set, `BuildMoveData` builds the same
  `MoveData(move, Camera.main yaw, sprint, crouch, ConsumeJump(), firstPerson)` as from the devices; replicate,
  reconcile, sanitising and the server are untouched. No MoveData field or RPC changed (surface tests unchanged).
- **Scope addition (directed by the orchestrator):** `PlayerCameraRig.cs` also changed, so look and view come from the
  intent source (degrees, no sensitivity; a scripted view change is never saved to PlayerPrefs). Release behaviour is
  the same; the wheel value is now read before the view branch instead of inside it.
- **`-demo-cookware`** moved from PickupInteractor to `Dev/CookSequence.cs` (same steps, same `[Demo]` log lines),
  started by `TourRunner` (RuntimeInitializeOnLoadMethod, only with `-tour`, `-observe` or `-demo-cookware`, never in
  batch mode). It walks via `TourActor` (steering + aiming through the intents). Arrival is now 0.12 m (was 0.25 m).
- **No focus:** scripted runs set `Application.runInBackground = true` (the player setting is off, which pauses an
  unfocused window). Shots use `ScreenCapture.CaptureScreenshotAsTexture` at end of frame; PNG encoding and
  downsampling run on worker threads (`ImageConversion.EncodeArrayToPNG`), so there is no Texture2D sheet writer.
- **Script:** the tour client starts first and the observer 3 s later (its window takes the focus). In local mode the
  tour client retries the connection until the `-host -observe` window is up. The observer and the walker sync by
  what they see (observer at its spot and still; tour player starts moving after standing 0.5 s), no RPCs. In local
  mode the observer is the host, so it sees the server's copy of the tour player (smoothed), not a pure remote client's
  view; `--server wsl` gives the exact remote view (not run here, MTU issue).
- **Extras:** `LookAt`, `WaitObserver` steps; `sheet.txt` maps cell numbers to names; walks stop when blocked (no
  progress for 1.5 s: fine within 0.5 m, else a timeout). Observer routes use their own hub (1.75, 0): in the first
  run both players met at (-1.75, 0) and blocked each other. Walk segments are x 16 to 22 to 14 to 16 (camera at
  x 18 sees about +-4.9 m at 4 m).
- **Run (2026-11-09, local, a focused observer window in front of the tour client):**
  `.verify/visual/look/sheet.png`, `.verify/visual/walk/sheet.png`, `.verify/visual/walk-observer/sheet.png`,
  `.verify/visual/cook/sheet.png`, `.verify/visual/cook-observer/sheet.png` (gitignored, regenerate with the script).
  Full `--tours look,walk,cook` run passed (exit 0 everywhere, 0 Exception lines); look and cook were then rerun after
  moving the scale person to the first worktop (the queue crowded the last one) and reframing the cook observer.
  The walk-observer sheet shows the person crossing the frame in all 72 frames (walk, sprint, crouch).
- **AC5:** `-host -view first -skip-whatsnew -demo-cookware` with a second client window in front finished the
  sequence ("cookware demo done ... pot holds 2 ingredients", same log lines). One step logs "aiming gave PutInto
  instead of Place" (the potato next to the pan: the aim lands on the pan) and sends the place request directly, as
  the demo's fallback is meant to.
- **Tests:** 669/669 EditMode (598 before + 71: TourRules, ContactSheet, ScriptedIntentSource, DevBuild, LaunchArgs).
- **Follow-ups seen in the shots (feel/layout):** see the builder report; the main ones are the queue standing on the
  cook's side of the counter, overlapping floating dish labels, and walk/sprint poses.

## Blockers
(Builder fills this in after 3 failed attempts.)
