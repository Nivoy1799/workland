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
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
