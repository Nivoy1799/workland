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
**Layout** (GrayboxSceneBuilder constants; x left to right as seen from the aisle):
- **Back line** at z 14.3 (bodies z 13.7 to 14.9), fronts facing -z:
  - landing worktop x -2.0 to -0.74;
  - crates at x -0.12, 1.12, 2.36, 3.60;
  - prep worktop 4.22 to 6.02 (board and knife);
  - stove A 6.64, worktop, stove B 9.14, worktop with the plates;
  - plate shelf 11.64, worktop with the bowl to 13.52.
- **Aisle:** z 12.2 to 13.7 (1.5 m between bodies, 1.43 m clear in front of the stove buttons), centre line z 12.95.
- **Front counter:** z 11.5 to 12.2, 0.7 m deep, 1.05 m high.
  - The serving counter sits at x 11.64, opposite the plate shelf (2.45 m centre to centre). It is turned 180 degrees, so its cook side and label face the aisle. Its body and model are squashed from 1.2 to 0.7 m deep, like the worktop modules.
  - Plain runs (cabinet modules squashed to 0.7 m, one collider each, no place surface) fill x -2.0 to 11.02 and 12.26 to 13.52.
- **Ticket rail:** (11.64, 2.5, 11.7), turned to face the aisle.
- **Walls** (Plaster):
  - back wall z 14.92 to 15.12, 2.4 m, x -2.22 to 13.74;
  - low side walls 1.2 m at x -2.22 to -2.02 and 13.54 to 13.74, from the back wall to z 11.5;
  - the left one leaves the 1.5 m staff gap (z 12.2 to 13.7).
- **Guests:** entry (24, 11.1); queue head (11.64, 11.1); slots 1 m apart towards -x (to x 6.64). Exit is back to the entry, all on z 11.1.
- **Spawns:** (0.6 / 2.2 / 3.8 / 5.4, 12.95), facing +z.
- **Player controller radius:** 0.3 m (was 0.4; orchestrator decision), so two cooks pass in the aisle. The 1.43 m clear width is at least 2 x (0.3 + 0.08 skin) + 0.1 (FloorPlanTests).
- The street, the houses and the fence are unchanged; the fence at z 15.5 now stands 0.4 m behind the back wall.

**Deviations and trade-offs**
- **Queue direction.** The queue runs from the head towards -x, along the counter, not to the right. The plate shelf is the rightmost back-line station (dish-flow order), and the serving counter must be opposite it. A queue running right would leave the counter after one slot. Running left, all six slots have the counter between them and the aisle.
- **Landing worktop.** A fifth worktop (the landing by the staff gap) keeps the existing counts: 5 worktops and 11 place surfaces in ScaleSceneTests and PlaceSceneTests. The front counter has no place surface, because every place surface stays at 0.90 m. Serving still works through the serving counter station, within reach from the aisle (AC3 test).
- **Art fronts were backwards.** The station models and cabinet modules showed their backs to the cook: knobs and oven door faced the wall, which is the human's "buttons are on the back". They are now turned 180 degrees in the builder (`ArtFrontRotation`), and FloorPlanTests checks the art fronts. The wrong "fronts face +Z" comment in ArtCatalogBuilder is fixed (one line, allowed by the orchestrator).
- **Guests face the counter.** Waiting guests now turn to face the counter: `CustomerService._queueFacing` drives `Customer.ServerSetTarget(target, facing)`, server side.
- **Tours walk out first.** Every tour still parks outside the staff gap at (-5, 12.95) before it waits for its observer, and the observers stand outside the aisle. The two players could now pass each other, but the tours' straight-line steering would only slide past, so the robust walk-out stays.
- **CookSequence** (Dev, outside the literal scope) now walks on the aisle line (`TourRules.AisleOffset`). It had to change: the old lane (z - 1.7) lies inside the front counter.

**Fixtures updated** (old hard-coded values):
- `TourRulesTests` spawns;
- `ArtCatalogTests` station positions and the serving counter's 0.7 m body depth;
- `CharacterArtTests` controller radius 0.4 to 0.3 (orchestrator decision);
- `ScaleSceneTests` customers' path x min 8.5 to 6.1;
- `WhatsNewTests`: spec 026 is no longer the newest; a new spec 027a test.

**Tests:** new `FloorPlanTests` (11 tests: AC2 to AC4, plus "two players can pass").

**Sheets (AC5):**
- `.verify/visual/look/sheet.png`;
- `.verify/visual/cook/sheet.png`;
- `.verify/visual/cook-observer/sheet.png`.

**Follow-ups (spec 027d, labels):** World TextMesh labels draw through geometry, so guests see mirrored aisle-facing text: the station labels and the rail. The order labels are large and overlap. The crate labels are big against the back wall.

## Blockers
(Builder fills this in after 3 failed attempts.)
