---
id: 029
title: Deep fryer (basket, oil, timer by ring and sound, shake, drain, burn)
status: approved
owner: architect
builder: opus
---

# 029: Deep fryer

## Goal
The first real fast-food station: a deep fryer for fries and falafel, the core of the vegan street-food menu
(docs/cooking-roadmap.md). Its rhythm:
- load the basket;
- lower it into the oil;
- watch the ring and listen to the bubbling;
- shake once for an even fry;
- lift and drain;
- tip the food into a fries box or onto a plate.

It reuses the 027c heat zones and ring and the 027b feel layer (arcs, ghost, sounds, prediction). Approved by the
orchestrator under the human's 24 h grant.

## Depends on
- 027a (floor plan; the fryer joins the back line between prep and the stoves), 027b (feel), 027c (heat zones,
  ring, cues), 028 (falafel mix, potato; fries-empty and styrofoam models).

## Scope
- `sim/Runtime/Cooking`: a `deep_fry` operation (oil temperature parameter, time), falafel mix to falafel balls
  (shaped by the basket: one portion becomes 4 balls), potato sticks to fries. Sim tests.
- `game/Assets/_Project/Scripts/Cooking/`: new station kind `Fryer` with two baskets. Each basket is a vessel with
  capacity 1 portion, and has a raised, lowered and draining state. A basket button lowers and raises it. KitchenService
  runs the server rules; the existing ServerRpcs are used (`RequestStation` and `RequestUse` with actions), no new
  RPCs.
- Shake: a use action on a lowered basket (the server gives an even-fry bonus once per fry, within the good window).
- Drain: a raised basket drips for 2 s; food taken out before draining ends gets a small greasy penalty.
- `Scripts/Art/`: the fryer model (Kenney Furniture Kit stove or graybox with an oil well), basket visuals
  (lowered or raised arcs), oil bubbles particles scaling with heat, the 027c ring per basket.
- `Scripts/Audio/`: deep bubbling loop by load and heat, a shake rattle, a drain drip, and the cues from 027c.
- Menu: add "Fries" (already exists, now from the fryer) and "Falafel (4)". Price defaults in "Defaults to review".
- `Editor/GrayboxSceneBuilder.cs`: place the fryer on the back line (dish-flow order: stock, prep, fryer, stoves,
  plates), FloorPlanTests updated for the new order.
- Tests, visual check (extend the cook tour: fries from potato to box), `WhatsNew.json`, this spec.

## Out of scope
- Assembly and packaging stations (031); fries boxes beyond plating fries into a box at the plating table.
- Oil quality and oil change (later, with stock and supply in 034).

## Design notes
- Timings: tuned to the cooking-pace inbox question. Default A: about 2x faster than today's pan. Fries: good at
  about 25 s, perfect 30 s, burnt 45 s. Falafel: good at 20 s, perfect 25 s, burnt 38 s.
- A basket can only be lowered when it holds food and the fryer is on. Raising is always allowed.
- All state is server-side. The client predicts only the basket arc (027b prediction pattern).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: deep_fry makes fries and falafel with doneness by time and temperature; shake bonus once;
      determinism.
- [ ] AC3: Server tests: lower or raise rules, capacity, the shake window, the drain penalty, zones and ring bytes.
- [ ] AC4: FloorPlanTests pass with the fryer in the dish flow; the fryer controls face the aisle.
- [ ] AC5: Visual check `cook` sheet: potato, then sticks, then basket, lower, the ring filling, shake, raise,
      drain drips, fries in a box at the plating table; the observer sees the basket arcs and bubbles.
- [ ] AC6: An order of "Fries" can be served end to end in the tour.
- [ ] AC7: `WhatsNew.json` entry `spec-029`.

## Defaults to review
Timings above; shake bonus +10 % quality; drain 2 s; greasy penalty -10 %; two baskets; prices Fries 6, Falafel 8.

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
