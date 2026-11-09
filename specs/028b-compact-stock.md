---
id: 028b
title: Compact stock (fridge and dry shelf next to prep, pick the ingredient)
status: approved
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
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
