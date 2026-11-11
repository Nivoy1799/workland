---
id: 048
title: Settings and comfort (graphics, audio, controls, rebinding)
status: approved
owner: architect
builder: sonnet
---

# 048: Settings and comfort

## Design
A settings menu (title screen and Esc) with:
- **Graphics:** quality presets, resolution, window mode, vsync, a frame cap, FOV 70–100, the motion-blur and head-bob
  toggles.
- **Audio:** master, effects, ambience and UI volume.
- **Controls:** mouse sensitivity, invert Y, key and button rebinding (the 041 mapping as the default), gamepad.
- **Comfort:** UI scale, colour-blind friendly ring colours (an alternative palette set), and a labels on/off toggle
  (the 040 test).

Saved per player (PlayerPrefs, the same pattern as the tutorial flag) and applied live.

## Acceptance criteria
- [x] AC1: `verify.sh`.
- [x] AC2: Tests that settings save, load and apply; rebinding conflicts are detected.
- [x] AC3: Visual check of every page.
- [ ] AC4 (manual, human): rebind a key and play.

## Implementation notes

The spec names no folders; the work stayed in `game/Assets/_Project` (Scripts, Tests, Editor/GrayboxSceneBuilder, UI/Worktown.uss, the
regenerated scene) plus three asmdef references (game: URP Core and Universal runtime for the motion-blur volume and the quality
preset; Dev and EditMode tests: Unity.InputSystem for `Key`). No RPC, SyncVar or broadcast changed. Two roadmap items folded in: the
frame cap (045: the host ran at FishNet's 500 fps cap) and `WheelSteps` reading `InputSystem.settings.scrollDeltaBehavior` (041 review).

**Design.**
- `Settings/GameSettings` (plain fields, JsonUtility): graphics (quality preset Low/Medium/High, resolution and window mode, vsync,
  frame cap, FOV 70-100, motion blur, head bob), audio (master, effects, ambience, UI), controls (mouse and gamepad sensitivity,
  invert Y, bindings), comfort (UI scale 75-150 %, colour-blind ring colours, labels). `Sanitized` clamps everything; `Use` makes a
  copy current, bumps `Version` and hands on the shared parts (`Audio/AudioMix`, `Art/RingPalette`, the parsed bindings).
- `Settings/SettingsStore` over `ISettingsPrefs` (`PlayerPrefsSettings` in the game, a fake in the tests): one JSON text under
  `worktown.settings`. **Scripted runs** (`-tour`, `-observe`, `-bot`; `SettingsRules.UsesSavedSettings`, the 038b lesson) neither read
  nor write: they run on the defaults (their pictures never depend on this PC) and changes stay in memory; they also never touch the
  window (the visual check sets 1280x720 itself).
- `Settings/SettingsApplier` (on the UI object, off in batch mode): loads at start and applies on every change: quality (URP render
  scale, shadow distance and cascades; High = the asset as it is, restored on destroy), display (only once the player picked a
  resolution or window mode), vsync and the frame cap (re-asserted every frame, because FishNet's `UpdateFramerate` sets
  `targetFrameRate` whenever a server or client starts), AudioListener volume, the panel's UI scale, motion blur (a global URP volume,
  created only once it is turned on). Defaults: vsync on, cap 120.
- `Settings/ControlBindings` (pure): one key and one gamepad button per `ControlAction` (walk x4, run, sneak, jump, view, use, put down,
  turn, quick chat, emote wheel, ping, players); defaults are the 041 mapping. A key or button that another action has is detected
  (`ConflictFor`) and the two swap, reported (`RebindResult`, the screen says "E was Use: swapped, Use now uses Space"); fixed keys are
  refused (Esc, Enter, F1-F12, Alt, Windows, arrows, Backspace, Print Screen, beyond OEM5). Saved as changed entries by name; a save
  with a conflict loads as the defaults. `Settings/GameInput` reads an action from the devices; PlayerInputReader (WASD now from the
  bindings, the Move action masked to its gamepad stick; arrows always walk), PickupInteractor (E, Q/X, Y, T, G/LB), SocialInteractor
  (G/LB, T, RB) and LobbyView (Tab) use it. Mouse buttons, wheel, triggers, sticks and d-pad stay fixed (041).
- `UI/SettingsView`: four tabs, live changes, Reset this page, Back (saves). Title screen Settings opens it over the town (the old
  controls-text page is gone); in the shop Esc opens it (cursor freed; Esc or Back closes). Click a binding, press a key or button
  (Esc keeps it, Backspace clears). Comfort shows the four ring colours live.
- Readers: PlayerCameraRig (FOV first person, third person 10 narrower; sensitivity and invert Y via `SettingsRules.LookDegrees`; head
  bob = the setting or the F2 switch; third-person zoom now per notch), WorldLabelFade (`LabelRules.ShownWithLabels`: off hides signs
  and item names, orders, rail and buttons stay), HeatRingRules/PatienceRingRules (colours from `RingPalette.Current`; the colour-blind
  set is Okabe-Ito sky blue, yellow, orange, vermillion, added to Palette.cs; the ring views rebuild on `RingPalette.Version`),
  SoundCatalog (`GainOf` = `BaseGainOf` x the group volume; `SoundRules.CategoryOf`: fryer and motor hums are ambience, nope, perfect,
  warning, burnt and coin are UI cues, the rest effects).
- `WheelSteps(countsPerNotch)` + `CountsPerNotch(uniform, windows)`: 1 for the Input System's uniform deltas (its default, which this
  project uses), else 120 on Windows; the old per-frame guess stays for the parameterless constructor.

**Tests (AC2).** `SettingsTests` (defaults, sanitizing, JSON round trip, store save/load over a fake prefs, scripted run never reads or
writes, `UsesSavedSettings`, `Use` -> volumes per group, ring colours, bindings; frame cap/vsync, window modes, FOV, look speed and
invert Y, quality presets, resolutions, labels switch, the wheel notch), `ControlBindingsTests` (041 defaults, bind, conflict detected
and swapped for keys and buttons, fixed keys refused, unchanged/cleared, serialize/parse, a conflicting save loads the defaults, pad
mapping, words). Existing tests unchanged.

**Checks.** `verify.sh` PASS: EditMode 1322 (was 1273), sim 400. `BuildAll` succeeded (Windows, Linux server).
`visual-check.sh --tours look,cook,tutorial,menu --out .verify/visual048`: PASS, 0 Exception lines in every log; no host freeze.
Menu sheet (`menu/sheet.png`): 02 settings-graphics, 03 settings-audio, 04 settings-controls, 05 settings-rebind-swap (Jump onto E,
the swap message), 06 settings-comfort, 07 settings-comfort-live (UI scale 125 % and colour-blind swatches applied live), 24
settings-in-shop (what Esc opens), 25 labels-on, 26 labels-off. The tour fails if `SettingsApplier.Persistent` is true. Afterwards
`HKCU\Software\DefaultCompany\Worktown` has no `worktown.settings` value (every window logged "scripted run: the defaults").

**Not covered / follow-ups.**
- AC4 (rebind a key and play) is the human's. Gamepad capture, Esc in a real shop, resolution/window changes and motion blur were
  not exercised by a tour (scripted runs never touch the display).
- Ring colours were only checked in the comfort swatches and tests; no cook-tour shot with the colour-blind set.
- Esc that closes the settings in the shop also hits PlayerInputReader's Esc (cursor free): one click locks it again.
- The UI and cue sound groups are a judgement call (only the feedback cues are "UI"; no menu click sounds exist).
- Quality presets change a runtime copy of the URP asset (048 review, fixed in the 049 round), never the shared asset.

**Follow-ups (2026-11-11)** (branch `spec/followups-042-043-048`):
6. Esc that closes the settings in the shop now leaves the cursor locked. `PlayerInputReader` records the frame on which the last
   open menu closed. Its Esc/Alt check (`CaptureGate.FreesCursor`) does not free the cursor on that frame, so the close locks
   it no matter which script's Update runs first. The lock on close is the pure `CaptureGate.LocksOnClose(playing, anyMenuOpen)`,
   which is only true while the player's input is active, so the title screen keeps a free cursor. Test:
   `Review048Tests.CaptureGate_EscThatClosesTheSettingsInTheShop_LeavesTheCursorLocked_TheTitleScreenKeepsItFree`.
   Not exercised by a tour: the menu tour presses buttons, it sends no real Esc.
7. The Controls page fits at 1280x720:
   - the bindings sit in two columns (`SettingsRules.BindingsPerColumn`: 8 and 7, Walk forward to First/third person, then Use
     to Players), each column with its own header;
   - the binding buttons are 116 x 24 px (13 px text), the rows 26 px high, and the action name takes the rest of the column;
   - the settings box is wider on every page (left/right 15 % instead of 20 %).
   The menu tour logs how far the Controls page reaches past the visible area (`SettingsView.ScrollOverflow`) and fails if it
   needs scrolling. Test: `Review048Tests.ControlsPage_TwoBindingColumns_TheFirstTakesTheExtraOne`.
   Checks: `verify.sh` PASS (EditMode 1353, sim 403, gateway 175); `BuildAll` succeeded. The first full visual check
   (`.verify/visual-fu1`) failed only on the menu tour's new overflow check: the page was 4 px too tall. The rows went from 28 to
   26 px, then a menu re-run (`.verify/visual-fu1b`) passed: "reaches 0 px past the visible area at 1280x720", sheet cell 04
   settings-controls, 0 exceptions.

**Follow-ups (2026-11-11 evening)** (branch `spec/followups-053-044-048`):
8. Gamepad capture in the menu tour. The pad half of the capture is now one pure step, `CaptureGate.PadCapture(beganFrame, frame,
   padPresent, held, pressed, ref releasedSince)` over `CaptureGate.PadBit` masks. `SettingsView` reads the real pad into those masks,
   or in a development build the tour's simulated pad (`SettingsView.DevSimulatePad` / `DevEndSimulatedPad`, under
   `DEVELOPMENT_BUILD || UNITY_EDITOR`; a press counts for one frame). The behaviour is unchanged: not on the frame the capture began,
   and only after every pad button was up once. The menu tour clicks Jump's gamepad binding with A still held, checks the capture
   waits, releases, presses Y, and checks Jump = Y and Turn swapped onto A. Then it resets the page. Test:
   `FollowUpsEveningTests.PadCapture_WaitsForTheClickingAToBeReleased_ThenTakesTheFirstPressedButton`.
   Runs: see spec 053, "Follow-ups (2026-11-11 evening)". Menu tour `.verify/visual-fu4b`: cell 06 settings-pad-waiting (the capture
   waits while A is held) and 07 settings-pad-capture ("Y was Turn one step: swapped, Turn one step now uses A"), log "pad capture:
   Jump Y, Turn A".

**Review nits (2026-11-11).**
- `SettingsView.ScrollOverflow` is tour-only: now under `DEVELOPMENT_BUILD || UNITY_EDITOR`, and its one caller (the menu tour's check) too.
- The menu tour's "needs scrolling" check only means something at 720p: at any other height it logs a warning that it was skipped. The
  limit is tight, with 8 px of slack (comment says so).
- Correction to follow-up 6: `CaptureGate.LocksOnClose(_active, IsUiOpen)` behaves exactly like the old branch. The real fix is
  `CaptureGate.FreesCursor` not freeing the cursor on the closing frame; the lock on close is only the playing-only guard. Comment in
  `PlayerInputReader.SetUiOpen` says so.
