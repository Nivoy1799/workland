---
id: 028b
title: Compact stock (fridge and dry shelf next to prep, pick the ingredient)
status: done
owner: architect
builder: opus
---

# 028b: Compact stock

## Goal
Spec 028 added 10 ingredients as a bank of narrow crates. The back line is now 5.6 m longer, and the first crate
is about 8 m from the cutting board. That breaks docs/feel.md section 6: one dish needs no more than 3–4 steps
between its stations. Real shops keep cold stock in a fridge and dry stock on a shelf, both next to prep.

This spec replaces the crate bank with two stock stations:
- a **fridge** (cold: vegetables, seitan, falafel mix, lemon);
- a **dry shelf** (bread, wraps, sauces, potato, onion).

You take an ingredient by aiming at its slot on the station and clicking. Approved by the orchestrator under the
human's 24 h grant.

## Depends on
027a (floor plan), 027b (feel: aim, prompt, ghost, prediction), 027d (signs), 028 (ingredients).

## Scope
- `Scripts/Cooking/` (station kind `Fridge` and `Shelf`, server-side take rule using the existing
  `RequestStation(int index, int option)` with option = slot index), `Scripts/Art/` (fridge with an open door and
  visible items in slots; shelf with items on boards; per-slot highlight and prompt "Left click: take cucumber
  ($50)"), `Editor/GrayboxSceneBuilder.cs` (remove the crate bank and old crates, put fridge and shelf next to the
  board, kitchen length back to about the 027a footprint), FloorPlanTests and ArtCatalogTests updated, the tours.
- No new RPCs or SyncVars beyond what a station already has (RPC surface tests unchanged).

## Out of scope
- Stock counts, running out, ordering and deliveries (spec 034).
- Cold or warm food quality.

## Design
- Each slot is a small trigger collider with a world anchor on the station; aim (027b cone and stickiness)
  treats a slot like any interactable. The server checks reach to the slot, the player's free hands and money,
  exactly as the crates do today.
- Fridge: about 1.2 m wide, 1.8 m high (art-direction table), 2 shelves × 4 slots visible through the open door.
  Shelf: 1.2 m wide, 3 boards × 3 slots. Both against the back wall, fronts facing the aisle.
- Dish flow: stock (fridge, shelf) → prep (board) → heat → plates. The centre of the fridge to the board is at
  most 2.5 m, and the shelf to the board at most 2.5 m.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Server tests: taking from a slot works exactly like a crate (money, free hands, reach), wrong slot
      index refused, unknown option refused.
- [ ] AC3: FloorPlanTests: stock to board at most 2.5 m; the kitchen is no longer than 027a's plus 1.5 m; controls
      and slots face the aisle.
- [ ] AC4: Visual check `look`: fridge and shelf with readable items and a slot highlight with prompt; `cook`
      tour takes potato and cucumber from the new stations.
- [ ] AC5: `WhatsNew.json` entry `spec-028b`.

## Defaults to review
Which item goes where; the fridge door is always open (no open/close interaction yet).

## Implementation notes
Builder (Opus), spec 028b.

**Layout** (x left to right as seen from the aisle; back line z 14.3, its back z 14.9):
- landing worktop x 0.48 to 1.74;
- **dry shelf** at x 2.36 (1.2 × 1.8 m, 0.5 m deep, centre z 14.65);
- **fridge** at x 3.60 (1.2 × 1.8 m, 0.62 m deep, centre z 14.59);
- prep worktop from x 4.22 (027a's place), the **board** at x ≈ 4.60 (moved 0.25 m towards the stock).
  Board to fridge 1.0 m, to the shelf 2.25 m, centre to centre.
- Everything from the prep worktop rightwards keeps its 027a place (stoves, plates, counter, queue, rail).
- The left end moved 2.48 m in: side walls at x 0.26 to 0.46. The aisle is 13.08 m long (027a: 15.56; 028: 21.1).
- Spawns are now x 1.2, 2.6, 4.0, 5.4 (0.6 was too near the new wall).
- Station indices: shelf 0, fridge 1, stoves 2 and 3, counter 4, plate shelf 5.

**Server.**
- `StockRules` (Scripts/Cooking, pure) is the only take path for a fridge or shelf slot and for a crate (one implicit slot, option 0). The checks run in this order:
  1. the slot exists on that station (`Station.TryGetSlot`): a wrong slot index or an unknown option gives `InvalidOption`;
  2. reach to the slot's anchor (`TooFar`), not to the station's centre;
  3. free hands;
  4. a known ingredient.
- `KitchenService.TakeIngredient` then creates the sim item and has the grocer paid (`StockRules.PaidOutcome`: no money gives `NotEnoughMoney`).
- Same RPC `RequestStation(station, option = slot)`. No new RPC or SyncVar; `RpcSurfaceTests` unchanged.
- `StockLayout` lists which ingredient is in which slot (row by row from the top):
  - fridge, top shelf: lettuce, cucumber, red cabbage, tomato;
  - fridge, lower shelf: seitan, falafel mix, lemon, apple;
  - dry shelf: sauces on the top board, potato and onion in the middle, flatbread and wraps at the bottom.

**Client (aim, prompt, prediction).**
- **Aim.** A ray hit on a stock station's body means its slot nearest to the hit point. The cone (027b) also aims at every slot's anchor, so stickiness and the minimum show time apply as for buttons. Reach is to the anchor.
- **Prompt and highlight.** The prompt keeps the existing format, "Click · take cucumber ($50)" (the spec's "Left click: …" wording would differ from every other prompt). It now uses display names, so "red cabbage", not "red_cabbage". The highlight outlines the slot's item models (children of the `StockSlot`).
- **Predicted look.** A click plays the pick sound and starts `StockTakeLook` (Scripts/Art): a collider-free copy of the slot's item flies on the pick-up arc into the hand. The prediction is the new `PredictionKind.Take`:
  - confirmed when food is in the hands;
  - a refusal or the timeout settles the copy back into the slot, with "nope";
  - if the server is faster than the 0.15 s arc, the real item stays hidden until the copy lands;
  - while a take is pending the hands count as full, so no double take.
- **Logs.** The cook tour logs click to visual in the same frame and click to confirm in 50–78 ms for Take.
- **Third person.** E does nothing on the fridge or shelf. The bottom prompt says "Fridge: pick an item in first person (V)", like the stove.

**Art.**
- `game/Assets/ThirdParty/Kenney/FurnitureKit/` has no fridge, bookcase or shelf model (only cabinet, drawer, stove, bar, blender, box and trashcan). Nothing was downloaded. Both stations are graybox models built by `ArtCatalogBuilder` (`Fit.Graybox`, `Art_StationFridge` / `Art_StationShelf`) in palette colours:
  - **Fridge:** a Cream shell, SoftBlue lining, a Steel glass shelf and a closed drawer block below. Two 0.6 m doors are opened 90° (`StockArt.DoorOpenAngle`), so they end at the worktops' front line.
  - **Shelf:** WoodLight boards and sides, a WoodDark back.
- Items are the existing food models at their `ArtSizeTable` size. There are 1–3 per slot (`StockArt.SamplesPerSlot`); they are not static and have no colliders.
- Each slot has a small price tag (a `StationSign`) on the board edge. The station sign sits on the top panel.
- New size rows: `StationFridge` 1.80 (the guide's fridge row), `StationShelf` 1.80 (no guide row). `ArtSizeTable.StockHeight`.

**Scope beyond the listed folders** (needed for the brief's "slots behave like interactables"):
- `Scripts/Net/PickupInteractor.cs`: aim, cone, describe and click; client only, no RPC change.
- `Scripts/Player/PredictionKind.cs` and `FeelRules.Judge`: the Take verdict.
- `Editor/ArtCatalogBuilder.cs`: the graybox models.

**Tests.** `scripts/verify.sh` passes: 847 EditMode tests (the baseline was 831) and 227 sim tests.
- New:
  - `StockRulesTests` (8): layout, slot, wrong slot and unknown option, reach to the anchor, the same as a crate, money with the real grocer, tour slot points;
  - `StockTakeLookTests` (5);
  - `FloorPlanTests`: stock to the board ≤ 2.5 m and the kitchen ≤ 027a + 1.5 m; slots face the aisle, one ingredient each, are in reach and have no colliders.
- Fixtures updated:
  - `FloorPlanTests` (crates replaced by stock);
  - `ArtCatalogTests` (station positions, bodies, the cabinet measured without the doors);
  - `ScaleSceneTests` (stock is 1.80 m high);
  - `CookingRulesTests` (two new kinds);
  - `TourRulesTests` (spawns, look shots);
  - `WhatsNewTests` (028b is the newest).

**Tours.**
- `look`: `kitchen-third` now looks west from x 10 (the stock stands where its camera was). New shots: `stock-third`, `stock-first`, `fridge-slot` (cucumber highlight and prompt), `shelf-slot` (potato). The scale observer moved to x 5.0 so it does not block the stock shot.
- `cook`: the potato and onion come from the shelf (highlight shot and frames of the take), the cucumber from the fridge (same), then it is cut to slices.
- `ParkX` is now -2.5, the observer exit x -0.7.

**Follow-ups and what still looks off.**
- The Crate_*.mat materials are no longer used (left in place, not deleted). `StationKind.Crate` still works but no scene uses it.
- From the shelf's right side the open left fridge door hides part of the shelf's right column.
- The lining of the fridge's right door is in shade and reads dark blue.
- The shelf sits 0.7 m behind the landing worktop's front, so the worktop's side panel shows.
- Price tags hide near the view centre in first person (027d label rules); the prompt names the item there.
- The fridge and shelf are graybox. The full Furniture Kit has a fridge and a bookcase, if the human wants to add them.

## Blockers
(Builder fills this in after 3 failed attempts.)
