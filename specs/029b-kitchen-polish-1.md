---
id: 029b
title: Kitchen polish 1 (stock take fixes, readable food labels, basket and fryer touches)
status: approved
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
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
