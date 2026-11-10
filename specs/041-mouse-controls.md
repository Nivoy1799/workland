---
id: 041
title: Mouse-first controls (left click does, right click puts down; no Q)
status: approved
owner: architect
builder: opus
---

# 041: Mouse-first controls

## Goal
The human, after playing (2026-11-10): "Q = drop, I am not a fan of. Should it not also be a mouse click, like left
picking and right dropping? Left with an item on something: interacting." The hands should live on the mouse, like in
the genre's best games. docs/feel.md 8 says one input has one meaning everywhere.

## The mapping (the contract)
| Input | Meaning everywhere |
|---|---|
| **Left click** | Do the obvious thing with what you aim at. Empty hands: pick up, take, press. Item in hand: use it on the target (put into the pot, slide onto the plate, place on the counter, cut, flip, scoop, serve). |
| **Hold left** | The hold actions: squeeze, pour, scrub, shave, wipe (as today). |
| **Right click** | **Put down what you hold**, at the aimed spot if you aim at a surface (with the 027b ghost preview), else drop at your feet. |
| **Mouse wheel** | Turn: the food on the cutting board (today on right click), and the held item's rest rotation before you put it down (in 45° steps). |
| **E** (third person) | Same as left click on the nearest target. In third person the mouse orbits the camera, so left click also works on the nearest target. |
| **Q** | Removed from the prompts and the tutorial. It is kept only as a hidden alias for keyboard-only players, unless the human says remove it entirely (question 1). |
| **Gamepad** | Right trigger = left click, left trigger = right click, bumpers = turn. |

- Every prompt names the mouse button: "Left click · put into pot", "Right click · put down here".
- The right-click flip on the griddle (spec 030) goes away: a flip is left click with the spatula, like every tool use.
- The tutorial (038) and the What's-new texts are updated, and the F1 help shows the new mapping.
- Server authority is unchanged: the same requests go out, only the input that triggers them changes. No new RPCs.

## Scope
- `game/Assets/_Project/Scripts/Player/PlayerInputReader.cs` and the input actions asset.
- `Scripts/Net/PickupInteractor.cs` (input routing), `Scripts/Net/Rules/InteractionRules.cs` (prompts).
- `Scripts/Player/CutGesture` (turn on the wheel).
- `Scripts/Tutorial/*` (step texts), `Scripts/UI` (help).
- Tests, `WhatsNew.json`, this spec.

## Acceptance criteria
- [x] AC1: `verify.sh` passes; RPC surface tests unchanged.
- [x] AC2: Pure mapping tests: each input and context gives the expected action; right click never does anything but
      put down or drop.
- [x] AC3: Visual check: put down by right click on the front counter (with the ghost), drop on the floor, turn food on
      the board with the wheel, flip on the griddle with left click.
- [ ] AC4 (manual, human): play a shift with the mouse only.
- [x] AC5: `WhatsNew.json` entry `spec-041`.

## Open questions (answered 2026-11-10: human approved all defaults)
1. Keep Q as a hidden alias for put down? **Yes, hidden** (not in prompts or tutorial).
2. Right click while aiming at a pot or pan with food in hand: put the food **next to it on the counter** (yes) or into
   it? Left click puts it in, so right click stays "put down here".

## Implementation notes
Built 2026-11-10 (builder, opus). `verify.sh` PASS: EditMode 1161/1161 (baseline 1140), sim 356/356. No new RPC, SyncVar or RPC parameter: `RpcSurfaceTests` and `MoveDataSurfaceTests` are unchanged and green.
Not committed.

**The mapping as built (pure `Net/Rules/ControlRules.IntentFor(HandInput, firstPerson, action, holding, putDownSpot)`; the interactor
routes every input through it):**

| Input | First person | Third person |
|---|---|---|
| Left click (short, `ClickGesture`) / RT | `Act`: the click's `InteractionAction` (flip = left click with the spatula). None/Cut: nothing. A hold action: `Hold` | `Act` = E on the nearest target (the old E logic, the drop fallback included) |
| Hold left / RT held | Cut: `Stroke` (drag down). Squeeze, Fill, Scrub, Wipe, Shave: `Hold` | `Hold` through the 031b `SqueezeHoldGate` (E or the left button) |
| Right click / LT | holding: `PutDown` on `PutDownSurface`, else `Drop` at the feet. Empty hands: nothing | holding: `Drop`. Empty hands: nothing |
| Wheel / Y (one step) | Cut: `TurnFood` (one `ActionTurn` request). Holding: `TurnRest` (45° steps). Else nothing | camera zoom (unchanged) |
| E | nothing (spec 020 AC7) | `Act` |
| Q / X | hidden alias of the right click (both views), not in any prompt | as the right click |

- **Put-down spot** (`PickupInteractor.UpdatePutDown`): the click's own spot when the left click places. Otherwise the free spot nearest
  to where the aim ray meets a surface, also behind cookware or a station on it (Q2: aiming at the pot puts it next to the pot), never
  through a wall (an `Other` hit in front clears it). Otherwise the best free spot in the aim cone (in sight and reach). Otherwise a
  drop at the feet. A queued put-down (inside the request interval) only goes out if the surface and spot are still the same.
- **Ghost:** as before for Place and PutInto, now turned by the wheel. With nothing for the left click (action None), it shows
  the right click's spot.
- **Prompts:** `ClickKey` is "Left click", `HoldKey` "Hold left click". Place is "Right click · put X here   Wheel · turn" (the left click
  still places too). Cut is "Drag down · cut   Wheel · turn". The HUD's fallback with full hands (was "Q · drop") is
  `InteractionRules.PutDownPrompt`: "Right click · put down here" or "Right click · drop".
- **Right-click flip removed:** `QueuedKind.Flip` is gone. `RightClickFlips` is renamed `IsFlipTarget` (`PredictsFlip` uses it) and
  `DropKeyActs` is removed. `FirstPersonKeysTests.QDrops_InFirstPersonOnly` became `Q_IsTheRightClicksHiddenAlias_InBothViews`
  (spec-mandated change of meaning).

**How the turn reaches the server (no new RPC parameter):** `PlaceTurn.Pack(spot, turn)` puts the 0-7 step above bit 16 of the
existing `RequestPlace(int surface, int spot)` spot int. A turn of 0 gives the old int unchanged.
- `PickupService.TryPlace` unpacks it first. A step of 8 or more makes the spot -1, so `CheckPlace` refuses it (NotFound) and nothing
  moves. Every check then runs on the real spot as before.
- The server's own rest is `ToolRest.RestRotation` for tools and the surface yaw for the rest. `PlaceTurn.Apply` turns it about the
  world vertical, so yaw changes and tilt stays.
- The predicted pose and the ghost use the same `PlaceTurn.Apply`.
- The turn resets to 0 whenever the item in the hands changes.
- A drop at the feet ignores the turn (the drop request has no int to carry it; 039's drop rule stays).

**Scripted inputs (tours):**
- `PickupInteractor.IHandIntentSource` (left click, right click, wheel steps; inside the dev guard), implemented by
  `ScriptedIntentSource`. `TourActor` sets it on the interactor.
- The interactor consumes them once per frame and routes them like the device inputs.
- New `TourActor.PutDown(...)` and `Use(..., viaInput: true)`.

**Scope beyond the spec's list:**
- `Scripts/Net/PickupService.cs`: unpacking the turn, as the orchestrator asked.
- New pure files `Net/Rules/ControlRules`, `HandInput`, `HandIntent`, `PlaceTurn`, `Player/WheelSteps`, `UI/ControlsHelp`.
- `Scripts/Dev` (tours, scripted source).
- The input actions asset is unchanged: the hand inputs were read from the devices before and still are.
- `CutGesture` is unchanged: the wheel turn lives in `WheelSteps` plus the router.

**Open question (needs the human): gamepad bumpers.**
- The contract says "bumpers = turn", but LB is the emote wheel (hold, spec 012) and RB the ping (`SocialInteractor`, out of scope).
- The turn stays on Y (one step per press). X stays a hidden put-down alias.
- Moving the emote wheel or the ping is a decision for the human.

**F1 help:** `ControlsHelp.Text()` (cached) is shown in the F1 menu above "F1 closes": left click, hold left, right click, wheel, E,
movement keys, gamepad. Q is not listed.

**Tutorial:** step texts say "left click", "hold left click", "the mouse wheel turns it". `FryKnifeText` is "Put the knife down first
(right click). ...". No step names Q.

**Tests:**
- New `MouseControlsTests` (AC2), 15 tests:
  - every input × view × action × hands × spot;
  - the right click and Q only ever give PutDown/Drop/None;
  - PlaceTurn pack/unpack, the refusal and Apply;
  - WheelSteps for 120-count, 1-count and touchpad values;
  - prompts, F1 help and tutorial texts.
- `ScriptedIntentSourceTests` +1. `WhatsNewTests`: the 041 entry is the newest; 039's assert flipped to "041 is newer".
- About 70 prompt assertions changed from "Click ·"/"Hold ·" to "Left click ·"/"Hold left click ·" (spec-mandated text).

### 039 review fixes
1. **Must-fix:**
   - `AimRules.Pick(hits, player, pickupsTransparent, spotsTransparent, out outOfReach)`. The interactor passes `holding, !holding`,
     so with empty hands the strips' thin aim colliders are transparent and the ray reaches the station.
   - The old overloads keep spots solid, so the existing `AimRulesTests` are unchanged.
   - New `EmptyHandsAimTests` (built scene, real raycasts sorted like `FindExactTarget`): the plate shelf's centre gives the PlateShelf
     station (Take a plate), the sink front gives the Sink (Scrub). With full hands the strip is still the first target.
   - Tutorial tour re-run: see the runs below.
2. **`PlaceCoverageTests`:**
   - `IsFurniture` skips a PlaceSurface collider only below 0.05 m, so worktop bodies are sampled.
   - "Standing on the top" needs `min.y >= top - 0.02` (the Rim is kept).
   - New meta test `AShrunkWorktop_FailsTheScan_EvenUnderAFullFootprintBox`: a worktop's `_size.x` halved fails the scan and names it.
     Adding a 1.3 m box from the floor over its whole footprint still fails.
   - The full scan still passes with 0 dead points.
3. **`PhysicsLayers.Interact`:** the doc now says it equals `DefaultRaycastLayers`. The tautological assert in `ItemsLayerTests` is
   removed.
4. **`ToolNearRay`:** only the assembly board; the dead Register and DrinkDispenser cases are removed.
5. **`FrontCounterTops_SpotsOnTheCookSideOnly`** also asserts the aim collider's z centre equals the run's.
6. **`ToolRestTests.BuiltScene_HomeTools_HandleIntoTheAisle`:** the aisle is the -Z of the surface each tool lies on (asserted to exist).
7. **`CookSequence.ClearTheBoard`:** only surfaces named "Worktop ".
8. **Spec 039:** AC4 text fixed (no fridge top, Q2); AC2, AC3, AC4 and AC6 ticked (AC1 now green with this round; AC5 is manual). The
   `ToolNearRay` note was updated.

**Runs (2026-11-10):**
- `BuildAll` (Linux server, Windows) succeeded.
- **Look tour** (`.verify/visual041/look/sheet.png`): ok, 133 images, 0 timeouts, 0 fallbacks.
  - `61-rightclick-front-west`: a right click on Counter top 1 with the ghost; the prompt reads "Right click · put knife here
    Wheel · turn".
  - `64`/`65`: two wheel notches turn the ghost 90°. The server laid the knife 97° off the cook: ToolRest's 7° plus the 90° turn.
  - `88-rightclick-drop` and `89-96`: "Right click · drop" aimed at the aisle floor; the tomato fell at the feet, at the same spot as
    039's Q drop. The walk over it stayed on the floor.
- **Cook tour:**
  - First run (`.verify/visual041/cook`): 0 timeouts, 0 fallbacks, but one exception line, a `NullReferenceException` inside
    Cinemachine's `UpdateTracker.OnUpdate` during the falafel fry. That is package code, and this spec changes no camera code.
  - Re-run (`.verify/visual041b/cook/sheet.png`): ok, 972 images, 0 exceptions, 0 timeouts, 0 fallbacks.
  - The fries' turn is one scripted wheel notch (`38-44 wheel-turn`): "Drag down · cut   Wheel · turn". Slices became sticks after
    the next stroke.
  - The griddle flips are scripted left clicks (`582-592 seitan-flip`): "Left click · flip half-grilled seitan strips"; flips 0 → 1.
- **Tutorial tour** (`.verify/visual041/tutorial/sheet.png`): ok, 702 images, 0 timeouts, 0 fallbacks, "[Tutorial] finished".
  - Card 4 shows "Put the knife down first (right click)".
  - The plate shelf, dish return and sink (with empty hands) all worked after the 039 must-fix: "Hold left click · scrub the
    plate", 1.8 s, one more clean plate.

**Still feels off / follow-ups:**
- The Place prompt now says "Right click" even though a left click places too. That is deliberate (right click is the put-down
  button), but the human may prefer "Left click".
- "Wheel · turn" is appended to every Place prompt, which makes it long.
- The ghost does not show the right click's spot while the left click has its own target (e.g. the pot). The spot "next to the pot"
  is only seen after the click.
- A turned rest is not shown on the item while it is still in the hands; only the ghost turns.
- The gamepad bumper conflict (above).
- docs/feel.md 8 still says "right click ... rotate"; that doc is out of this spec's scope.

## Blockers
(None.)
