---
id: 031
title: Assembly station and packaging (build wraps in order, wrap paper, boxes, bags)
status: approved
owner: architect
builder: opus
---

# 031: Assembly and packaging

## Goal
The heart of a street-food shop (roadmap pillar 3): you build a dish **in order** on an assembly board:
- the base (flatbread or wrap);
- the fillings (grilled seitan, falafel, fries);
- the salad (lettuce, cucumber, red cabbage, tomato, onion);
- the sauces (garlic-herb, chili);
- then you close it (fold the wrap, wrap it in paper) and pack it (box or bag).

The build stacks visibly, layer by layer. Order and completeness matter for the score. Approved by the orchestrator
under the human's 24 h grant.

## Depends on
027b/c/d (feel, rings, labels), 028/028b (ingredients, stock), 029/029b (fryer, tip), 030 (griddle, spatula). The
label rule from 030: a label shows the name the item would get.

## Scope
- `sim/Runtime/Cooking`:
  - an `assemble` operation that adds a layer to a build in order;
  - `close` (fold or wrap) and `pack` (box or bag) operations;
  - a `Build` item kind with ordered layers;
  - rating by recipe template: required layers, order, sauce amount, and the quality of every layer.
  - Pure, deterministic, tested.
- `game/Assets/_Project/Scripts/Cooking/`:
  - station kind `Assembly` (a board with one build spot and nearby sauce dispensers);
  - sauce dispensers as a hold-to-dispense use action (hold left click; the amount by time, server-measured from
    repeated use requests within the token bucket, or a start/stop pair through the existing use action);
  - packaging stock (wrap paper, box, bag) on the shelf or in a small rack.
  - No new RPCs. The RPC surface tests stay unchanged.
- `Scripts/Art/`:
  - the visible layer stack (each layer is the ingredient's model or a flat layer in its palette colour, offset in
    height);
  - a wrap fold animation and a paper wrap;
  - the Kenney `styrofoam` box and `bag`, which are already in the project.
- `Scripts/Npc/` and the menu:
  - recipe templates: "Seitan wrap", "Falafel wrap", "Seitan flatbread", "Veggie box" (fries plus grilled veggies plus
    sauce, in a box);
  - customers order these, and order matching compares layers.
- Layout: the assembly board sits between the plates and the serving counter (the end of the dish flow).
  FloorPlanTests stay green, with plates to assembly at most 1.5 m.
- Tests, visual check (the cook tour builds one falafel wrap end to end and serves it), `WhatsNew.json`, this spec.

## Out of scope
- Orders 2.0 (custom modifiers, patience, register: spec 032). Here an order is just the recipe name.
- Drinks (033).

## Design notes
- A wrong layer order costs score but is allowed (forgiving). Missing required layers cost more. Extra layers are
  neutral.
- Sauce: hold for 0.5–1.5 s for the right amount. Too little or too much costs a little.
- Close: a wrap must be closed before packing. A box needs no close.
- Feel: every layer drops onto the stack with a short arc and a soft "plop", and the sauce makes a squeeze sound
  while held. The predicted look follows 027b.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: assemble order, recipe scoring (order, missing, extra, sauce amount), close, pack, determinism.
- [ ] AC3: Server tests: layer rules, the dispenser amount, packaging stock, wrong-step refusals.
- [ ] AC4: FloorPlanTests: the assembly position and distances.
- [ ] AC5: Visual check `cook`: a falafel wrap built layer by layer (frames show the stack), sauce held, folded,
      wrapped, served; the observer sees the stack.
- [ ] AC6: `WhatsNew.json` entry `spec-031`.

## Defaults to review
Recipe templates and prices (wrap 9, flatbread 8, veggie box 10); the sauce window 0.5–1.5 s; the score weights.

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
