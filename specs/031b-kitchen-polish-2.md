---
id: 031b
title: Kitchen polish 2 (calm first-person view, fair prices for portions, readable held food, cue timing)
status: approved
owner: architect
builder: opus
---

# 031b: Kitchen polish 2

## Goal
After the fryer, griddle and assembly the kitchen has many moving parts. This round makes it calm and fair. The
first-person view must not drown in rings and labels, a portion of sauce must not cost more than the wrap, and food
you hold must be visible. Items come from the "still feels off" lists of 030 and 031, plus the 031 review follow-ups
(added below by the orchestrator). Approved by the orchestrator under the human's 24 h grant.

## Scope (files as needed in Scripts/*, sim, Editor, tests)
1. **Calm view** (feel.md 3/7). Show rings and heat labels only for:
   - the aimed spot or vessel;
   - spots in Warning, Overdone or Burnt;
   - and in third person, at most 4 nearest.
   Six rings and labels at once over the griddle is the failure case. Keep 027d priorities. Test with LabelRules-style
   pure rules.
2. **Warning lead** (orchestrator decision). Lead = clamp(0.4 × good-window length, 1 s, 3 s), so toast no longer warns
   before it is Good. A sim or game rule with tests, used by the server's zone bytes.
3. **Portion prices** (orchestrator decision).
   - A squeeze charges a portion: 10 % of the bottle's grocer price per started 0.5 s, at most 30 %.
   - Packaging stays at its grocer price.
   - The server charges once on release (unchanged).
   - Tests.
4. **Held food visible in first person.**
   - The hold pose shows held food (and builds) fully in the lower right, about 25 % of the view height for a wrap.
   - Large items are scaled to fit the view, never covering the centre.
   - The pan rule from feel.md stays.
5. **Box model.** The Kenney styrofoam clamshell reads as two trays. Close it (rotate the lid part shut in the wrapper,
   or use a graybox box). Add box packing to the cook tour.
6. **Short names for builds.** A build without a dish name uses at most 3 words plus "…" in labels ("wrap with
   falafel…"). The full name stays in the sim and the ticket.
7. **Client caches.** The griddle-spot and basket lists refill while empty or when a member is destroyed, like the
   build-spot fix.
8. **Grill marks stay on plated food;** a spatula motion in the hand on flip.
9. **The tour sheet:** the squeeze frames end before the fold frames (separate captures).
10. **Review findings of 031** (2026-11-09; file:line refers to commit 91d8b6d):
   - **0. Must-fix first: a repeated press restarts the squeeze**
     (`KitchenState.cs:1402`, `TryStartSqueeze`). A client pressing every 2.9 s never hits the 3 s auto-stop. The
     dispenser is then busy for everyone, nothing is charged, and the ribbon shows forever.
     - Fix: a same-owner press while a squeeze runs keeps the original StartTick (return Ok, change nothing).
     - Test: press at t0, press at t0+2.9 s, overdue at t0+3 s.
   - **Squeeze timing** (`SqueezeHold.cs:45`). Record how late the start went out and delay the stop by the same
     amount, so the server's measured time equals the hold.
   - **BuildStep races** (`FeelRules.cs:292`, `BuildStackView.cs:87-96`). Snapshot the target bits and confirm
     only on equality (like FlipStart). Each prediction gets its own token in the view, so there is no wrapped →
     folded → wrapped flicker and no double Paper sound. Test the two-predictions case.
   - **Freeing the dispenser.** Cancel a squeeze at once when its player despawns (`KitchenService.cs:362`), with
     no charge.
   - **Builds stay builds** (sim):
     - forbid the `build` tag as input to cut, the heat ops, mix and plate (`OperationCatalog.cs:80-88`);
     - in `RecipeTemplates.Fits`, a layer must be a single-ingredient chain (no Mix or Plate).
     Old hashes unchanged.
   - **Client offers only what the server allows:**
     - `ToolAction` respects the dispenser busy bit;
     - `BuildAction` offers putting only a finished build back on an empty board, not a plate of fries;
     - no put prompt for a held open build on a non-empty board;
     - the predicted ribbon ends at once on refusal (`SauceDispenserView.cs:93`).
   - **Wrong comment** (`KitchenState.cs:1316`, "only an unplated one"): fix the comment or the rule.
   - **Inputs:**
     - gamepad and third-person players can squeeze (hold the use input on the dispenser);
     - a press on the other dispenser while the first stop is still waiting is queued, not dropped
       (`SqueezeHold.cs:31`).
   - **Tests:**
     - `GriddleStationTests.cs:453-464` asserts Ok on each step and `AreEqual(OrderMatch.Close)`;
     - `AssemblyTests` pins hash and rating values for a few pre-031 items (fries, grilled seitan strips,
       a pan potato);
     - remove the always-true assert at `AssemblyTests.cs:316`;
     - service-level charging tests: charged exactly once, a refusal charges nothing, release out of reach,
       the auto-stop, a player who left;
     - `PrefabRegistrationTests` checks `_foodPrefab` by name too.
   - **Scope note (orchestrator):** 031's widening into Net, Player, Audio, UI, Dev, Editor and
     sim/Runtime/Economy is approved explicitly (same pattern as 029/030).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Tests for items 1, 2, 3, 6, 7.
- [ ] AC3: Visual check `look` and `cook`:
  - the first person over the full griddle shows at most the aimed ring plus warnings;
  - held wrap visible;
  - box closed;
  - box packing in the tour.
- [ ] AC4: `WhatsNew.json` entry `spec-031b`.

## Implementation notes
Builder (Opus), spec 031b. No new RPC, SyncVar or RPC parameter; `RpcSurfaceTests` unchanged. New tests: `KitchenPolishTwoTests`
(EditMode), `PortionPriceTests` and three `AssemblyTests` facts (sim).

**10.0 (must-fix).** `KitchenState.TryStartSqueeze`: a press by the player who already squeezes that dispenser returns Ok and
changes nothing (the first StartTick stays); another player still gets DispenserBusy. Test: press t0, press t0+2.9 s, not
overdue at 2.95 s, overdue at 3.0 s.

**1. Calm view.** Pure `LabelRules.IsAlarm` (warning bit, Overdone, Burnt), `ShowsRing(aimed, alarm, firstPerson, nearRank)`,
`NearRanks`, `ThirdPersonRings` 4. The 027d label manager applies them: `WorldLabelFade.CalmRings` runs after every ring's
LateUpdate (execution order 1000) and turns off the rings it does not want (`HeatRingView.ApplyCalm`) before they enter the
overlap cull, so the 027d priorities are unchanged (aimed, held, ring, labels). Nearness is from the player's body. Item and
heat labels were already aimed-only (027d), so only the rings changed. Pans and pots on the stove follow the same rule (no
ring unless aimed or alarming in first person).

**2. Warning lead.** `HeatZoneRules.WarnLeadRealSeconds(window)` = clamp(0.4 × window, 1, 3) and `WarnLeadSimSeconds(timeline)`
(window = Good to Overdone); `Warn` uses it, so the server's zone byte carries it. Long windows keep exactly 3 s (the old
tests pass unchanged). Real sim: toast warns at 5.0 s (Good 4.0, Overdone 6.0), the grill vegetables at 12.4 s (Good 10).

**3. Portion prices.** Sim: `PriceRules.PortionPercent(seconds)` (10 % per started 0.5 s, at least one step, at most 30 %),
`PortionCost` (rounded half up, at least $1), `EconomyEngine.TryBuyPortion` (+ `TownEconomy`, `EconomyService`). The server
computes the percent from its own measured seconds of *this* squeeze (`KitchenState.TryEndSqueeze` overload with the own
seconds; a merged top layer is charged per squeeze). Packaging charges 100 %. **Deviation (anti-faucet):** with portions a
sauce costs less than the bottle, so `PriceRules.ItemCost` (the wholesaler's cap) now counts a squeezed portion at the least
any squeeze in its amount bucket was charged (dash / right 10 %, lots 30 %) instead of the bottle price; otherwise a sauce
build sold to the wholesaler would pay more than it cost. Items without a squeeze are unchanged (pinned test).

**Server charging (10).** The board's money steps moved into the pure `AssemblyDesk` (press, release, end, cancel, package)
with an `IAssemblyWallet`; `KitchenService` implements the wallet through `EconomyService` and only routes, refreshes and logs.
`AssemblyDesk.NeedsReach` keeps the release reachable from anywhere. A squeeze whose player despawned is cancelled in the next
server tick (`StopOverdueSqueezes` checks `_squeezers` for destroyed or unspawned players), no charge. Service-level tests:
charged once, second release free, wallet refusal and an emptied board charge nothing and change nothing, the other player's
release charges nobody, release out of reach, the auto-stop (30 %) then a late release free, the player who left, packaging
(FoldFirst and NotEnoughMoney free, paper 100 % once, a second packaging refused before charging).

**4. Hold pose.** Pure `HoldViewRules`: the item's middle at 75 % / 21 % of the screen, 0.714 m in front of the eye (a 0.25 m
wrap fills 25 % of the view height at 70° vertical FOV), tilted 28° towards the eye; anything over 32 % of the view height is
scaled down (`FitScale`), so `ScreenBound` stays right of and below the centre for any size (tested up to 3 m).
`HeldItemView.TryGetViewPose(item, out pose, out scale)` measures the model (mesh filters only, in root space, every 15 frames)
and uses the diagonal as the size. `ItemTween.Apply(pose, size)` scales the visual children and restores them; `Pickup` eases
the drawn scale (40 ms). The knife keeps its own pose; the arcs into and out of the view are unchanged ("the pan rule").

**5. Box.** `ArtCatalogBuilder` `CloseLid`: turns the Kenney model's `lid` part about the base's hinge edge (tries every 5°,
keeps the turn that leaves the lid squarely on top; the build logs "lid turned 120 degrees", score 0). Only
`Art_PropTakeawayBox.prefab` changed. The cook tour packs a whole lettuce into a box (`PackABox`: frames `box-*`, `boxed`,
`box-held-level`) and puts it back on the empty board (a boxed raw lettuce is no dish: the counter answers CannotSell, which in
the first run left the box in the hands and broke the griddle part).

**6. Short names.** `BuildRules.LabelName(name, isBuild)`: a build whose name has the sim's " with " form (no dish) shows its
first 3 words and "…" in the spot's and the food's world label; dishes, prompts, the HUD, the ticket and the sim keep the full
name. `Food` refreshes its label when the look changes too.

**7. Caches.** `CacheRules.ShouldRefill(neverFilled, memberDestroyed, count, now, nextEmptyLookup)`; the interactor's basket,
griddle-spot and build-spot lists all use it (again at most once a second while empty).

**8. Grill marks, spatula.** New look byte value `FoodLook.HeatGrilled` (4) for the griddle and toast ops (still golden:
`FoodArtRules.IsFried`); `HasGrillMarks` from the medium bucket. `FoodVisual` draws two Charcoal bars on each of the first
three pieces of a grilled pile anywhere (hands, plate, build), except under a `GriddleSpot` (GriddleSpotView draws its own).
The spatula in first person scoops and turns over on a flip (`HeldItemView.PlayFlip`, 0.25 s, local like the knife's chop).

**9. Tour.** `CookCapture.WaitForFrames` (TourRunner passes its running-frames count): the squeeze's frames end before the fold's
start; the box frames are separate too. Cook timeout 960 → 1080 s.

**10, the rest.**
- BuildStep: `FeelRules.BuildStepStart(atClick, targetBits)` puts the target bits in the start; `Judge` confirms only on
  equality (a start without target keeps the old "changed" rule). `BuildStepLook` (pure) gives each prediction a token; only the
  newest ends the look. Each prediction is its own book entry. Test: fold then paper, the fold arriving confirms only the fold
  and the view stays wrapped.
- Squeeze timing: `SqueezeHold` records each squeeze's lag (start sent − press) and sends the stop no earlier than release + lag.
  A press on the other dispenser while the first stop waits is queued (sent after that stop) instead of dropped; this changed one
  assertion of 031's `TheHeldSqueeze_...` test from `IsFalse(Press)` to the queued behaviour (spec-mandated).
- Client offers: the dispenser's busy bit (not our own: held, queued, or our stop still in flight) makes the tool None with
  "Someone is using that sauce"; `Held.IsBuild` lets only a build go back, only on an empty board ("Take the other build off
  first"); a plate of fries is never offered. The squeeze start's answer is matched by request number
  (`PredictionBook.Answer` now returns it): a refusal ends the predicted ribbon at once.
- Inputs: first person, the gamepad's right trigger holds a squeeze; third person, holding E (or the right trigger) at the board
  squeezes its nearest dispenser while the rules offer it (E then does nothing else there).
- Builds stay builds (sim): cut, fry, saute, boil, deep_fry, griddle, toast and mix forbid the `build` tag. **Deviation:** the
  plate refuses a build only together with other food; a lone build may still be plated, because the counter plates every
  unwrapped build to sell it (031's close/pack penalties, `BuildNode` through a one-item plate). Forbidding it outright would
  make an open or folded build unsellable. `RecipeTemplates.Fits` requires `Assembly.IsSingleChain` (no mix, plate or build in
  the layer's chain).
- Comment at the old `KitchenState.cs:1316` fixed (finished builds do go back; a plate of a build does not).
- Tests: GriddleStationTests' seitan block asserts Ok on every step and `Close`; sim `AssemblyTests.Pre031Items_...` pins fries,
  grilled seitan strips (13 kitchen s a side) and a sauteed potato (hash, quality, name) measured at commit 1710193; the
  always-true assert is removed; `PrefabRegistrationTests` checks `_foodPrefab` by name.

**Tests.** `scripts/verify.sh` PASS: 958 EditMode (baseline 933), 277 sim (baseline 268).

**Visual check (AC3).** `bash scripts/visual-check.sh --tours look,cook`: PASS (cook: 446 images, timeouts 0, aim fallbacks 0).
- `.verify/visual/cook/sheet.png`: squeeze frames 146–160 end before `fold-aim` 161 (separate captures); the held falafel wrap
  `wrap-held-level` 187 (whole, lower right; log `[Hold] wrap ... size 0.306 m, drawn x1.00`); the box: `box-stack` 215, `box-aim`
  216, `box-01..10` 217–226 (the closed box comes down), `boxed` 227, `box-held-level` 228, `box-back-on-board` 229; the griddle
  238–268: only the aimed ring (e.g. `seitan-flip-04` 243) and the alarms (log `rings shown 1 of 3 (Griddle Perfect warn)`;
  never more than aimed + warnings); the spatula's turn in `seitan-flip-01..03`; grill marks on the held plate
  `grilled-veggies-on-held-plate` 268. Log: squeeze 1.00 s charged $16 (20 %), "Grilled veggies order: exact order".
- `.verify/visual/look/sheet.png`: `pan-first` 20 (the held pan scaled into the lower right; before this round it was not seen at
  all), `assembly-first` 55 (two closed boxes on the rack), `griddle-first` 6.
- `.verify/visual/cook-observer/sheet.png`: the remote squeeze, fold, paper and box from the guests' side.
- The tour fills 3 of the 6 griddle spots; the six-spot case is the EditMode test.

**Still off / follow-ups.**
- With the calm view a ring is rarely on screen in first person while you look elsewhere; the warning pulse and cue then carry
  the news. Third person (4 nearest) has no tour shot with food on the heat.
- Grill marks are two Charcoal bars laid on each piece's bounds: they float a little over the paprika's stem and cross a pile of
  strips at odd angles (the pile's yaw is not followed).
- The held pan in view overlaps the counter edge and looks a bit like it lies in the world (the tilt); the knife keeps its 019
  pose, so a held knife and other items sit differently.
- The box is the Kenney model with its lid turned shut: it reads as one closed box, a little pale lavender under the kitchen
  light.
- A boxed raw lettuce cannot be sold (CannotSell); the tour puts it back on the board instead.
- Third-person E at the board now squeezes when a squeeze is offered, so E there no longer drops what you hold.

## Blockers
(Builder fills this in after 3 failed attempts.)
