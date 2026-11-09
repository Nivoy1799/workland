---
id: 035
title: Cleaning (dirty dishes, sink, trash, spills, hygiene score)
status: approved
owner: architect
builder: opus
---

# 035: Cleaning and hygiene

## Goal
Roadmap pillar 5: a clean shop. Plates and bowls come back dirty from eat-in guests. The sink washes them with a
short hold, so clean plates run out if nobody washes. Burnt food and food scraps go into the trash, which fills and
must be taken out. Spills (spec 033's drink overfill) need a wipe with a cloth. A visible **hygiene score**
(0–100 %) drops with dirt, and guests notice it in reviews and patience. Approved by the orchestrator under the
human's 24 h grant.

## Depends on
027b (feel, hold actions), 031b (hold gate), 032 (reviews, patience, eat-in vs take-away), 033 (spill flag:
`KitchenState.MarkSpill/HasSpill/ClearSpill`), 034 (stock and refund backstop, the plate and bowl counts).

## Scope
- `sim/Runtime/Economy` or a new `Shop` module (pure): the hygiene model.
  - Dirt sources: dirty dishes left out, a full trash can, an open spill, burnt smell.
  - Decay and recovery per game minute.
  - The effect on review stars and patience (small: −1 star below 50 %, patience −20 % below 30 %).
  - Deterministic, tested.
- `game/Assets/_Project/Scripts/Cooking/` and `Shop/`:
  - eat-in plates and bowls come back dirty to a return shelf (a station);
  - the sink station: put a dirty dish in, hold to scrub (server-timed like the dispensers, 1.5 s), and it goes to
    the clean plate shelf;
  - the trash can station: a "discard" action (food scraps, burnt food, empty packaging), a fill level, and "take
    out the trash" (carry the bag to the bin outside the staff gap);
  - a wipe cloth tool (a free item like the knife) for the spill flag; holding it on the spill clears it.
  - No new RPCs. New presentation bytes only where needed (pinned).
- `Scripts/Art/`:
  - dirty-plate looks (a smudge tint);
  - the sink with water and bubbles;
  - the trash fill level;
  - the bin bag;
  - spill puddles and a wipe animation;
  - the hygiene meter in the HUD (a small icon plus %).
- `Scripts/Audio/`: scrub, splash, trash rustle, a lid thump.
- Layout: the sink and dish return near the plates (the end of the line), and the trash at the staff gap.
  FloorPlanTests stay green.
- Tests, visual check (a dirty plate comes back, gets washed, the trash fills and is taken out, a spill is wiped,
  the hygiene meter moves), `WhatsNew.json`, this spec.

## Out of scope
- Health inspections and fines. Pests. Floor mopping beyond spills.

## Design notes
- The plate count is finite (from 034's stock: plates are stock). Without washing, plates run out.
- Scrub 1.5 s per dish (server-timed hold). The trash holds 12 items, and a bag is a carryable pickup.
- Hygiene starts at 100 %. Each dirty dish left more than 2 game minutes is −1 %/min, a full trash −2 %/min, an open
  spill −2 %/min. Recovery is +1 %/min when nothing is dirty.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: the hygiene model (sources, decay, recovery, effects), determinism.
- [ ] AC3: Server tests:
  - dishes return dirty;
  - the sink scrub is server-timed (the re-press rule as in 031b);
  - discard;
  - the trash fills and the bag is taken out;
  - the wipe clears a spill;
  - hygiene drives the review and patience modifiers.
- [ ] AC4: Visual check: the full loop above.
- [ ] AC5: `WhatsNew.json` entry `spec-035`.

## Defaults to review
All numbers in Design notes; the eat-in share (half of the guests eat in).

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
