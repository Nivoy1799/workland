---
id: 033
title: Drinks (cups, dispenser with fill timing, lids, drinks on orders)
status: approved
owner: architect
builder: sonnet
---

# 033: Drinks

## Goal
Roadmap pillars 1 and 3: guests order a drink with their food. You take a cup, hold it under the dispenser
(lemonade, iced tea, soda), stop at the fill line, put a lid on, and serve it with the order. Overfilling spills and
needs a wipe (a hook for the cleaning spec 035). It is a small, satisfying station that teaches timing without heat.
Approved by the orchestrator under the human's 24 h grant.

## Depends on
027b (feel), 031/031b (hold-to-dispense pattern with server-measured time and portion prices, AssemblyDesk money
pattern), 032 (orders with modifiers and register).

## Scope
- `sim/Runtime/Cooking`: a `fill` operation (drink kind, millilitres from the server-measured hold time) and
  `lid`; fill level buckets (low, right, full, spilled); drink items lemonade, iced_tea, soda (vegan, D27 content check).
  Pure, deterministic, tested.
- `game/Assets/_Project/Scripts/Cooking/`:
  - a station kind `DrinkDispenser` with 3 taps, each pressed and released through `RequestStation` options, like
    the 031 sauce dispenser;
  - a cup stack (take a cup) and a lid stack (lid a filled cup);
  - spills leave a spill decal flag on the station for spec 035.
  - No new RPCs.
- `Scripts/Art/`: the dispenser (graybox with three coloured taps), the Kenney `cup` / `soda-glass` models (in the
  project), a liquid level inside the cup rising while held, the fill line, a lid, and a spill puddle.
- `Scripts/Audio/`: pour loop, lid click, spill splash.
- Orders: drinks as part of an order (032 order model: an optional drink line). The register charges for them.
- Layout: the dispenser sits on the front counter's cook side, near the serving point. FloorPlanTests stay green.
- Tests, visual check (the tour fills a lemonade to the line, lids it, serves it with a wrap), `WhatsNew.json`,
  this spec.

## Out of scope
- Cleaning the spill (035). Hot drinks. Ice.

## Design notes
- A cup holds 400 ml. The right fill is 340–380 ml. The pour rate is 200 ml/s, so a good hold is about 1.7–1.9 s.
- The server stops pouring at 500 ml: a spill, the cup is full, quality drops.
- The predicted liquid level rises from the press frame. Others see it from the station byte.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: fill buckets, lid, drink rating, content check.
- [ ] AC3: Server tests: press and release timing, the auto-stop at 500 ml, charging once (cup plus portion), a lid
      only on a filled cup.
- [ ] AC4: Visual check: the liquid rises to the line, lid, served with an order.
- [ ] AC5: `WhatsNew.json` entry `spec-033`.

## Defaults to review
Cup 400 ml, right fill 340–380 ml, pour rate 200 ml/s, prices (drink 3).

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
