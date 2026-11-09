---
id: 029b
title: Kitchen polish 1 (stock take fixes, readable food labels, basket and fryer touches)
status: done
owner: architect
builder: opus
---

# 029b: Kitchen polish 1

## Goal
Collect the review follow-ups of 028b and 029, plus the "still feels off" items the builders found, into one small
round before the griddle. Feel first: no item may visibly change identity, no action may be offered that cannot happen,
and labels speak the player's language. Approved by the orchestrator under the human's 24 h grant.

## Scope (all items; files as needed in Scripts/Cooking, Net, Player, Art, Audio, Dev, Editor, tests)
1. **Take confirmed by the wrong item** (028b review 1). The Take prediction snapshots the held object's id and confirms
   only when a new held item with `Food` appears. A pending PickUp also counts as holding for `Hands()`.
2. **No actions while a take is pending** (028b review 2). While a take is pending and nothing is really held, every
   action except None maps to None, so Place, PutInto and Sell are not offered.
3. **Never charged without an item** (028b review 3). `KitchenService.TakeIngredient` checks that the hand-over can
   succeed before paying, or refunds if the spawn fails. Extract the check, create, pay sequence so a test can assert:
   TryBuy is called 0 times on every refusal and exactly once on success.
4. **StockTakeLook liveness** (028b review 5). The copy destroys itself when its interactor is gone (no
   MissingReferenceException).
5. **Readable food labels.** Bucket ids ("potato sticks · short") become player words that match the food's look:
   - fries: "pale fries", "golden fries", "dark fries";
   - pan and pot: "raw", "warm", "cooked", "well done", "burnt".
   The words come from the 027c zone where an item is on heat. Elsewhere they come from the sim's display name. No
   sim name changes unless trivial.
6. **Fryer touches** (029):
   - a quiet idle oil hum while the fryer is on and both baskets are up;
   - more visible drain drops;
   - "tip into box": a click on a raised basket while holding a plate or fries box tips the food onto it, using the
     existing put/take action rules, with no new RPC;
   - add `docs/art-direction.md` rows for the basket (0.45 real) and the falafel ball (0.035 real). The orchestrator
     commits the doc edit.
7. **Plate distance** (029). Fryer to plates is about 4.5 m. Swap the plating table and the serving counter order, or
   move the plate shelf so the fries path is at most 3.5 m. Every FloorPlanTests limit must hold for both the pan and
   fryer paths.
8. **028b nits:**
   - the NearestSlot test uses a point between two anchors;
   - the StockTakeLook test follows the real Finish path;
   - the click-to-visual log is measured, not hard-coded;
   - rename the crate variables in CookSequence;
   - the StockArt row arrays become read-only.
9. **Review findings of 029** (CHANGES REQUESTED, 2026-11-09):
   - **0. Must-fix first: the shake bonus stacks on re-frying.**
     - `ItemFactory.cs:195` multiplies `QualityFactor` on every fry, and putting fries back in the basket resets
       `Shaken`. That gives ×1.1 per 4 s loop, up to a perfect rating.
     - Fix in the sim, deterministically: cap the factor (at most 1 + ShakeBonus, at least 1 − GreasyPenalty), or apply
       the shake bonus only when the input has no `deep_fry` node.
     - Tests: a sim test "fry, fry again, shake twice: still 1.1", and a FryerTests case that re-loads fries.
   - **The basket button is a toggle.** Two players cancel each other. Send the intent in the existing option
     (lower b0/b1, raise b0/b1 as separate option values) and refuse a no-op. No new RPC.
   - **Owed shakes in `BasketView._ownShakes`** expire after the confirm timeout, or clear when the basket state
     changes, so a refused shake never swallows another player's real one.
   - **`BasketView._started` / `_seenShakes`** reset in OnEnable or the vessel's OnStartClient, so a stale counter
     plays no rattles after a respawn.
   - **Dead code:** remove `KitchenState.IsBasket` and `BasketVessel`, and the no-op `QualityFactor` tie-break, or
     comment it.
   - **`Item.QualityFactor` doc:** plates already include their inputs' factor; never multiply it twice.
   - **Tests:**
     - `DeepFry.DonenessAt`, `Doneness` and `DeepFryProfile.CurveFor` directly;
     - `AnswerFor(Basket) == Cook`;
     - the basket path of `KitchenService.TryUseVessel` (no Pickup, reach to the pivot, `HandsFull` on shake).
   - Later, not in this round: the menu bucket matching lets burnt falafel match an order (tuning); the per-frame
     `"the " + name` string in `Describe` (an older pattern).
   - **Scope note (orchestrator):** spec 029's widening into Net, Player, UI, Npc, Dev and Editor was needed for the
     use actions, prediction and labels. It is approved explicitly.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Tests for items 1–4 and 7 (as described above).
- [ ] AC3: Visual check `cook`:
  - the labels read in player words;
  - fries tipped from the basket into a box on a held plate;
  - one stock take while the knife pickup is pending shows no identity swap.
  Extend the tour to cover these.
- [ ] AC4: `WhatsNew.json` entry `spec-029b`.

## Implementation notes
Builder (Opus), spec 029b. No new RPC, SyncVar or RPC parameter; `RpcSurfaceTests` unchanged.

**9.0 Shake bonus stacking (sim).** `DeepFry.FactorAfter(input, shaken, greasy)` caps the factor at
1 − GreasyPenalty .. 1 + ShakeBonus; `ItemFactory` uses it for `deep_fry`. Every single fry is inside the cap, so no
existing rating, hash or the golden-fries pin changed. Shaken after greasy still gives 0.99. The no-op `QualityFactor`
tie-break in `ItemCanonical.CompareItems` is removed (equal hashes have equal factors; commented). `Item.QualityFactor`
doc: a plate's factor already includes its inputs'. Sim tests: fry, fry again, shake (three times) stays 1.1; greasy
twice stays 0.9; `FactorAfter`; `DonenessAt` / `Doneness`; `DeepFryProfile.CurveFor`. FryerTests: three re-loads with a
counted shake each, still ×1.1.

**1, 2. Take prediction.** The Take snapshot carries the held object's id (`ItemSnapshot.HeldId`,
`FeelRules.HandsSnapshot`); Judge confirms only a *new* held food. A pending PickUp counts as held in `Hands()`
(`_pendingPickUp`, cleared on resolve). While a take is pending and nothing is really held, `InteractionRules.WaitsForTake`
makes the action None and clears the aim, so no prompt and no Place/PutInto/Sell.

**3. Never charged without the item.** `StockTakeSequence.Run(IStockTakeSteps)`: check, create, `CanHandOver`, pay,
hand over. `CanHandOver` = food prefab with a Pickup and `PickupRegistry.CanClaim` (via `PickupService.CanHandTo`). The
hand-over after the payment was checked in the same server call, so it cannot be refused; no refund path (a refund would
need `Scripts/Economy`, outside the scope; if it ever failed the server logs an error). Test: TryBuy 0 times on every
refusal, exactly once on success, no hand-over when the payment is refused.

**4. StockTakeLook.** `Begin(..., owner)`: the copy finishes as soon as its interactor is destroyed (LateUpdate and the
render hook), never calling the dead hand.

**5. Labels.** `HeatLabelRules`: basket food by zone "pale / golden / dark fries" (falafel: "... falafel balls"); pan and
pot "name · raw / warm / cooked / well done / burnt". `KitchenState.HeatNames` uses it. Off the heat the sim's name stays.
The label is refreshed when the published zone changes (with the ring), not only once a second. Note: the food's colour
still follows the sim's time buckets, so for about 5 s around 20–25 s and 35–40 s the word and the colour differ by one
step (the ring and the word agree). HeatSessionTests/PotTests label strings updated to the new words (spec-mandated).

**6. Fryer touches.**
- Idle oil hum: `SoundRules.FryerIdleHum(flags)` (0.22 of the fryer loop at pitch 0.7) while on with both baskets up;
  `KitchenSounds` plays one per fryer.
- Drops: 36/s, 0.022–0.04 m, pale gold, stretched billboards.
- Tip into a plate: holding a plate or bowl with room, a raised basket with food gives the existing `Take` action
  ("Click · tip golden fries on the plate"), sent as the same `RequestUse(basket, 0)`. The server
  (`KitchenState.TryTipBasket`) pours the basket's take onto the held vessel (shake bonus, greasy while draining;
  BasketLowered in the oil). No prediction for the tip (the food moves on the server's answer).
- `docs/art-direction.md`: rows "Fryer basket (with handle)" 0.45 → 0.52 and "Falafel ball" 0.035 → 0.0525, now linked
  from `ArtSizeTable` (the guide test checks them).

**7. Layout.** Stoves side by side (the worktop that stood between them is now the last worktop at the right end,
created in the same order, so the 5 worktops and 11 surfaces keep their indices). Stove B x 8.72, plating worktop
9.34–10.60 (plates at ≈9.7 and 10.3), plate shelf and serving counter 11.22 (queue and rail with them); the kitchen
length is unchanged. **Fryer to the nearest plate: 3.28 m** (was 4.5). New FloorPlanTests assertion: plates after the
stoves, before the shelf, nearest plate ≤ 3.5 m from the fryer; every existing limit holds. TourRules and the
ArtCatalogTests fixture follow the new x.

**8. Nits.** NearestSlot tested with points between neighbouring anchors on one board; the StockTakeLook test runs the
real frame path (`Step`, what LateUpdate calls); click-to-visual is measured for takes (`StockTakeLook.DrawnFrame`) and
baskets (`BasketView.PredictionDrawnFrame`); CookSequence `potatoSlot` / `onionSlot`; `StockArt` row heights are
read-only lists.

**9. Review findings.**
- Basket intent: `FryerRules.LowerOption(b)` 2/3 (the scene buttons' option), `RaiseOption(b)` 4/5; the client sends
  `IntentOption(b, seenLowered)` (its own pending prediction first). Lowering a lowered basket gives BasketLowered,
  raising a raised or draining one NotLowered. `TryToggleBasket` is replaced by `TryLowerBasket` / `TryRaiseBasket`.
- Owed shakes: `BasketShakeLedger` (expire after the confirm timeout, clear on a state change).
- `BasketView.OnEnable` resets the start pose, the state and the ledger (a respawn plays no stale rattles).
- Dead code removed: `KitchenState.IsBasket`, `BasketVessel`, the tie-break.
- Tests: `AnswerFor(Basket) == Cook`; the basket path of TryUseVessel through the extracted
  `VesselRules.CheckUseTarget` (no Pickup, reach to the pivot) and `CheckShake` (HandsFull).

**Tests.** `scripts/verify.sh` PASS: 885 EditMode (baseline 865), 242 sim (baseline 238). New: `KitchenPolishOneTests`
(14), FryerTests +5, FloorPlanTests (assertions in two tests), WhatsNewTests +1, sim DeepFryTests +4.

**Visual check (AC3).** `bash scripts/visual-check.sh --tours look,cook`: PASS.
- `.verify/visual/cook/sheet.png`: the basket label in the log goes "pale fries" → "golden fries" in the frame the ring turns Good (at the shake, 23.8 s); cells `tip-aim`
  (prompt "Click · tip golden fries on the plate"), `fries-tipped-onto-plate-*`, `fries-box-on-held-plate` (the fries
  box on the held plate); served as "plate of golden fries ★★★★☆ · Fries order: exact order".
- `knife-pending-take-*`: the knife click, then a take in the same frame. The hands read "knife" and a slot offers None;
  the forced take was refused after 43–77 ms (two runs) ("Your hands are full", nothing charged), the copy went back, the hands hold
  the knife (no swap).
- `.verify/visual/look/sheet.png`: `kitchen-third`, `worktop-first` (the plating worktop after stove B), `counter-guests`
  show the new layout.

**Still off / follow-ups.**
- The tip has no predicted arc; the fries jump from the basket onto the plate on the server's answer (~80 ms).
- Word and colour can differ by one step for a few seconds (see 5).
- The drops read at close range and from the observer; from the aisle at a distance they are still small.
- The pan path: stove A's pan now has no worktop right next to it (stove B's top or the plating worktop instead).
- From the 029 review, left for later as the spec says: menu bucket matching for burnt falafel; `"the " + name` in
  `Describe`.

## Blockers
(Builder fills this in after 3 failed attempts.)
