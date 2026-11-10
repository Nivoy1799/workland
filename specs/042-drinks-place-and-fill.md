---
id: 042
title: Drinks 2 (put the cup under the tap, then fill it)
status: approved
owner: architect
builder: opus
---

# 042: Drinks 2: put the cup under the tap, then fill

## Goal
The human, after playing (2026-11-10): "the drink station also works, but it would be nice if you had to place the cup
at the machine and then click to fill." Today you hold the cup in your hand under the tap (spec 033). Real machines and
the genre's best games work the other way round:
1. take a cup;
2. **put it on the drip tray under a tap** (a fixed spot, so it snaps into place);
3. **hold left click on that tap's lever** to fill; the level rises in the cup on the tray;
4. lid it there, or take it off and lid it;
5. take the full cup to the order.

Your hands are free while it pours, so you can turn to the next task (a short pour is about 1.8 s).

## Design
- Each of the three taps gets a **cup spot** on its drip tray. It is a fixed vessel like a fryer basket, with capacity
  one cup. Only an empty or open cup of the same drink, or an empty cup, fits there. The spot works with the 039
  placement rules (left click with the cup in hand, the ghost preview, the right-click put down from 041).
- **Pour:** hold left on the lever. The server pours into the cup on that tap's spot, keeping the 033 rules:
  - server-measured ml;
  - the auto-stop at the spill;
  - the re-press keeps its start;
  - a player who leaves is freed;
  - the same money and stock pattern (034).
  The cup no longer needs to be in your hands, and the pour record references the cup on the spot.
- **Lid:** left click the lid stack while the filled cup stands on the spot (or with the cup in hand, as today).
- **Take:** left click the cup on the spot with empty hands.
- Third person: E (or left click) on the tap with a cup on its spot pours, through the 0.2 s hold gate.
- Spill: an overfill spills over the tray and sets the 035 spill flag, as today.
- The 033 "cup in hand under the tap" path is removed. One way to do it, and the tutorial step 10 is updated.

## Scope
- `Scripts/Cooking/` (DrinkRules, DrinkDesk, KitchenState pours, three cup spots as fixed vessels).
- `Scripts/Art/` (tray spot, the cup snapping onto it, the level rising there, the lever).
- `Scripts/Net/` (InteractionRules prompts and aim for the spots).
- `Editor/GrayboxSceneBuilder.cs`, `Scripts/Tutorial` (step 10), tests, `WhatsNew.json`, this spec.

## Acceptance criteria
- [ ] AC1: `verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Server tests:
  - a pour needs a cup on that tap's spot;
  - the wrong drink is refused;
  - money and stock exactly once;
  - a cup taken away mid-pour stops it with no charge;
  - another player can't take the cup during a pour.
- [ ] AC3: Visual check: cup put on the tray, lever held, level rising, lid, taken to the order.
- [ ] AC4 (manual, human): fill a drink while doing something else in between.
- [ ] AC5: `WhatsNew.json` entry `spec-042`.

## Open questions (answered 2026-11-10: human approved all defaults)
1. Must you keep holding the lever, or does one click fill to the line by itself (an auto-fill machine, an upgrade
   later)? **Hold to fill now**; auto-fill as a later equipment upgrade (spec 037 shop).

## Implementation notes
Built 2026-11-10 (builder, opus). `verify.sh` PASS: EditMode 1172/1172 (baseline 1161), sim 356/356. No new RPC, SyncVar or RPC
parameter, and no new synced byte at all: `RpcSurfaceTests` and `MoveDataSurfaceTests` unchanged and green. Not committed.

**Design choice: the cup spot is a place target, not a fixed vessel** (the orchestrator allowed either). Each tap gets a one-spot
`PlaceSurface` ("Cup spot 0..2", appended after the 039 tops, so no earlier index moves) with `_isCupSpot`, `_dispenserIndex`, `_tap`,
a child of the dispenser at `DrinkArtRules.CupUnderTap` (exactly the tap view's `CupPose`, where the stream lands). The cup stays its
own Pickup: the 039 place, ghost, predicted rest, snap and "tock", the 041 right click and wheel all work unchanged, and its level and
lid are its own `Food.Look`. A fixed vessel would have taken the cup out of the registry and needed new bytes for its level.
Not an exception in `PlaceCoverage` (it is a surface; `PlaceSceneTests` checks it is not named there).

**Server (`Scripts/Cooking`, `Scripts/Net/PickupService`).**
- `KitchenState` keeps which cup stands on which tap's spot (`PutCupOnSpot`, `TakeCupOffSpot`, `CupOnSpot`, `IsOnSpot`), fed by
  `PickupService.ItemMoved` (`KitchenService.OnFoodMoved` -> `DrinkDesk.Moved`). `Register`, `Forget` (every removal) and `Clear`
  drop the entry.
- A press pours into `CupOnSpot(station, tap)`, whatever the hands hold (`DrinkDesk.Press/Release/End` lost their cup argument;
  `TryEndPour` checks the cup of the press still stands on that spot). Every 033/038b rule is the same code: server ticks, the 500 ml
  auto-stop with the spill mark, a re-press keeps its start, `DropLeftPours`, one charge with the 038b short-keg cut, refusals change
  nothing.
- Taken off the spot mid-pour (only the pourer can): `TakeCupOffSpot` cancels the pour, nothing charged, the tap's bit goes off at once.
- New vetoes in `PickupService` (`SetGuards`, set by KitchenService): `TryPickup` refuses (`Taken`) a cup another player pours into
  (`KitchenState.CanTakeCup`); `TryPlace` refuses (`Taken`) on a cup spot anything but an empty cup or an open cup of that tap's drink
  (`DrinkRules.CanStand`, a full one too so it can be lidded there).
- Lid: `DrinkDesk.LidTarget`: the cup in the hands when it is a cup, else the first tap's (0, 1, 2) filled open cup on a spot nobody
  pours into. Clients use the same order.
- The old "cup in hand under the tap" path is gone (no held-cup pour anywhere; `HeldItemView.SetPourPose` is now only the wipe's).

**Client (`Scripts/Net`, `Scripts/Art`).**
- `DrinkRules.SpotByte`: what a client sees of a spot's cup (present, lid, drink, level) in `AimTarget.Mode` of a tap or the lid stack;
  `CanPourOnSpot`, `CanLidOnSpot`, `CanStand`. `AimTarget.CupSpot(spot, tap)` and `AimTarget.BusyCup(name)`.
- Prompts: "Left click · put empty cup under the lemonade tap" (no "Wheel · turn" for a round cup), "Hold left click · fill lemonade";
  grey: "Put a cup under the tap first", "Put the cup on the tray under the tap" (holding a cup), "Only a cup goes under the tap", "It
  has its lid", "That cup has another drink", "A cup is already there", "Someone is pouring into it" (another player's pour).
- Aim: the dispenser's aim box contains its tray, so the exact ray that hits the body asks `AddDrinkSpotHit`: empty hands near a cup on a
  spot (closer to the ray than any tool) target the cup; a cup in the hands aimed at a tap (or its spot) targets that tap's free spot.
  `InSight` lets an item inside a station's aim box compete in the cone. The ray put-down and the cone skip cup spots the held item does
  not fit.
- Our own place on a cup spot counts as standing there for 2 s before the server's answer, so a hold right after it pours.
- `DrinkTapView` draws the level rising and lands the stream on the liquid for the pourer (its press, its own ml) and, from the byte's
  rise, for everyone else (the cup found by the spot probe, from the level they see, same rate); at the byte's fall the drawn level stays
  until the bucket arrives, or goes at once when the bucket already came and differs. The lever now also tips 22° towards the cook.
  No per-frame allocations (static probe buffers, per-frame spot cache).
- Third person: E with an empty cup puts it under the nearest free tap; with a filled open cup lids it; empty hands lid a filled cup on
  a spot, else take a lidded one off, else take a cup. Holding E (0.2 s gate) pours at the nearest tap with a cup it can fill (kept while
  it pours).

**Tutorial and tours.** Step 10: "Put a cup on the lemonade tap's tray, hold left click on the tap until it is full, left click the
lids, then take it to the wrap's guest." Its target follows the cup on the lemonade spot (`TutorialSnapshot.LemonadeSpot`, new
`TutorialTarget.LemonadeCup`). The cook tour (and so the tutorial tour) places the cup by a scripted left click, holds the tap with free
hands, lids the cup on the tray, takes it off with empty hands; the cleaning act puts its overfilled cup on the tray and takes it off
for the trash.

**041 review follow-ups** (orchestrator, in this round):
1. The ghost shows the right click's put-down pose whenever the left click would not place (`TryGetLandingPose`; the "into the pot" pose
   is gone).
2. `PutDownWithLook` returns early while the held item has a pending place or drop (`Pickup.HasRestPrediction`, from `ItemLook`).
3. `TutorialSteps` "Card: left click the terminal" (and "and hand it over" to stay within the 140-character test). The test now fails
   on any "click" not preceded by "left " or "right ".
4. End-to-end place test: TryPlace's checks and rest pose moved into the pure `PickupService.PlanPlace` (TryPlace runs it, then moves);
   `PlaceServiceTests.TryPlace_TurnTwo_...`: turn 2 turns the rest 90° about the vertical (tilt kept), step 8 is NotFound before any
   probe, with no pose and no spot taken. It runs on a real `PlaceSurface`, not through the network.

**Tests.** New: `DrinkStationTests` (7: a pour needs a cup on that tap's spot; the wrong drink refused at the spot and the tap; money
once with the auto-stop; a cup taken away mid-pour stops it uncharged; another player cannot take it; the lid target; the spot byte
and rules) and 2 client-rule tests rewritten for the spot; `PlaceSceneTests` cup spots under the spouts; `PlaceServiceTests` (041
follow-up 4); `TutorialTests` spot targets; `WhatsNewTests` spec 042 newest. Spec-mandated updates: the 033/034/035 pour tests put
their cup on the spot first and lost the cup argument; the "cup put down" refusal became "taken off the spot" (still nothing
charged); surface counts 24 -> 27; the cup spot's height case in `ScaleSceneTests`; the out-of-stock tap check has a cup on the spot.

**Runs (2026-11-10).** `BuildAll` (Linux server, Windows) succeeded. `visual-check.sh --tours look,cook,tutorial --out
.verify/visual042`: PASS, 0 exceptions; look 133 images, cook 994, tutorial 724, all with 0 timeouts and 0 aim fallbacks.
- Cook (`.verify/visual042/cook/sheet.png`): `443 cup-place-aim` shows the ghost cup on the lemonade tray with "Left click · put empty
  cup under the lemonade tap"; `444-453 cup-place` shows the cup snapping onto the tray; `454-476 lemonade-pour` shows the lever down,
  the stream and the level rising in the cup on the tray, with empty hands. The log says "held the tap 1.80 s (drawn level 0.90) ... cup
  of lemonade, level 2; the hands hold ''". Then `478-488 lid` (the lid on the tray), `489-499 lemonade-take` ("pick up cup of lemonade
  to go"), `500` the cup in the hand, and the serve ("drink order: exact order"). The cleaning act's overfill on the tray makes the
  spill, and the cup goes to the trash.
- Tutorial (`.verify/visual042/tutorial/sheet.png`): card 10 (cell 263) has the new text and points at the cup stack. Step 11 starts
  with "cup of lemonade to go" in the hands, then "[Tutorial] finished".
- The observers (`cook-observer`, `tutorial-observer`) film the board, so no sheet shows the other players' view of a pour. That view
  (`DrinkTapView` others' path) is not checked visually.

**Still feels off / follow-ups.**
- The cup is drawn about 2 cm into the tray grid at rest (the food prefab's collider bottom; the same as on any worktop).
- In the tour, the prompt during our own pour says "pick up empty cup" while aimed at the cup. That is because the synced name lags and
  the tour looks at the cup. A real player aims at the lever.
- Another player's view of the rising level is extrapolated from when its byte goes on, so it starts about one round trip late. It snaps
  to the bucket if that differs.
- Third person: E with empty hands cannot take an empty cup back off the tray. E then takes a new cup from the stack.
- `HudView` still says "Take a cup first" for a refused tap press.

**Scope beyond the list:** `Scripts/Player/ItemLook.cs` (one property, follow-up 2), `Scripts/Dev/CookSequence.cs` (the tours, as the
brief asked). `Scripts/UI/HudView` not touched: a refused press still says "Take a cup first" for NeedsCup (the prompt never offers it).
