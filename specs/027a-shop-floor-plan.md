---
id: 027a
title: Shop floor plan (a kitchen with a counter between cook and guests)
status: approved
owner: architect
builder: opus
---

# 027a: Shop floor plan

## Goal
The human: "counter is wrong side, check other side, buttons are on the back, everything feels off".
Today the kitchen is one line of stations along z = 13 (x 0 to 14). The cook and the guests share the open
floor in front of it: guests queue at z 10.8 between x 8.5 and 24, so they stand in the cook's space. This
spec rebuilds the kitchen as a real street-food shop, following docs/feel.md section 6:
- a **back line** (stock, prep, heat) along the wall;
- a **cook aisle**;
- a **front counter** that separates the cook from the guests;
- guests on the street side, facing the cook across the counter.

Approved by the orchestrator under the human's 24 h grant (docs/cooking-roadmap.md).

## Depends on
- Spec 026 (sizes: worktops and stations 0.90 m high, counter 1.05 m, palette, worktop modules).
- Specs 014/022 (customers, queue, serving at the counter), 019/021 (stations, stove buttons, place surfaces).
- Spec 027 (visual check tool) for AC5; build 027a after 027.

## Scope
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs`: kitchen layout, station positions and facing, the
  counter line, the walls, the spawn point, the queue head and direction, the customer entry and exit.
- `game/Assets/_Project/Scripts/Npc/` (only if the queue path needs a new rule; for example
  `CustomerRules.QueueSlot` stays as it is, the head and direction change).
- `game/Assets/_Project/Scripts/Art/` (only for a wall or counter art key, if needed).
- Tests under `game/Assets/_Project/Tests/EditMode/` (new `FloorPlanTests`), `WhatsNew.json`, this spec,
  and the "look" tour shot positions in spec 027's `TourRules` if they depend on the layout.

## Out of scope
- New stations, new food, new mechanics.
- Interaction feel (spec 027b).
- Building interiors beyond the shop's kitchen.
- Gameplay rule numbers: reach, aim range, place grid and capacities stay unchanged.

## Design (target layout, numbers are guidance; the tests are the contract)
- **Back line** along the back wall: crates (stock), cutting-board station (prep), stove A, stove B, plating
  table, in **that order from left to right as seen from the cook aisle**. This is the dish flow
  (stock, prep, heat, plate). Worktops fill the gaps (spec 026 modules).
- **Cook aisle** between the back line and the front counter: clear width 1.2–1.5 m (feel.md 6).
- **Front counter** parallel to the back line, 1.05 m high (D29). It holds the serving counter station (where
  the dish is handed over) opposite the plating table, plus a plain counter for the rest of the run. The ticket
  rail hangs above the front counter, readable from the aisle.
- **Facing.** Every station's controls and open side face the cook aisle:
  - stove knobs and buttons;
  - crate openings;
  - the board's working edge;
  - the serving station's cook side.
  Labels face the aisle too.
- **Walls.** Low graybox side walls close the kitchen at both ends. A staff gap of at least 1.0 m at one end lets
  the cook leave towards the town without crossing the guest area.
- **Guests.** They enter from the street, queue along the front counter's guest side (the queue runs
  parallel to the counter, head at the serving station, slots 1 m apart), get served across the counter, and
  leave back to the street. No guest path crosses the cook aisle.
- **Spawn.** Players spawn in the cook aisle, facing the back line.
- **Distances.** The walk from the plating table to the serving station is at most 2.5 m. Each consecutive pair
  in the dish flow (crates to board, board to stove A, stove B to plating) is at most 3 m apart, centre to
  centre, measured along the aisle.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; all existing interaction, cookware, order and scale tests pass
      unchanged (fixtures that hard-code old positions may be updated and are listed in the notes).
- [ ] AC2: `FloorPlanTests` on the built scene check:
  - the aisle's clear width is 1.2–1.5 m;
  - the front counter lies between every guest queue slot and the aisle;
  - the guest path (entry, queue, exit) never enters the aisle;
  - each station's control normal (StationButton, crate opening, board edge) points into the aisle (dot > 0.7);
  - the dish-flow order and the distance limits hold;
  - the spawn point is in the aisle;
  - the staff gap is at least 1.0 m;
  - nothing overlaps (stations, walls, counter, props).
- [ ] AC3: Server rules unchanged: reach and aim tests pass, every place surface and button is reachable from
      the aisle (the 2.5 m reach from a point in the aisle).
- [ ] AC4: A customer can be served end to end in an EditMode or scene test: queue, order, serve across the
      counter, leave to the street.
- [ ] AC5: Visual check (`scripts/visual-check.sh --tours look,cook`) sheets show:
  - the kitchen from the aisle;
  - guests across the counter;
  - knobs facing the cook;
  - the cook demo still completing.
  The sheets are linked in the notes.
- [ ] AC6: `WhatsNew.json` entry `spec-027a`.

## Defaults to review
- Aisle 1.3 m. Staff gap at the left end. Guests queue to the right along the counter, towards the street.

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
