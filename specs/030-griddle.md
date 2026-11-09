---
id: 030
title: Griddle with flipping (two-sided doneness, spatula, toasting flatbread)
status: approved
owner: architect
builder: opus
---

# 030: Griddle

## Goal
The second fast-food station: a flat griddle for seitan strips and patties, grilled vegetables (paprika, eggplant,
mushroom), and toasting flatbread and wraps. The genre's signature action is the **flip**. Each side cooks on its
own; you flip with the spatula when the down side is done, and a perfect result needs both sides in the good window.
It reuses the 027c rings (one per item, split in two halves for the two sides) and the 027b feel layer. Approved by
the orchestrator under the human's 24 h grant.

## Depends on
027a/028b (floor plan, stock), 027b (feel), 027c (heat zones, rings, cues), 028 (seitan, flatbread, wrap, paprika,
eggplant, mushroom), 029 (shared heat-station patterns; build after 029).

## Scope
- `sim/Runtime/Cooking`: a `griddle` operation with two-sided doneness (side A and side B tracked per item; the
  component doneness is their combination), toasting for bread (short window). Sim tests.
- `game/Assets/_Project/Scripts/Cooking/`:
  - new station kind `Griddle` with a 0.9 × 0.6 m plate and 6 spots;
  - a spatula tool item (like the knife: a free item, placeable);
  - flip = use action with the spatula on an item on the griddle, validated on the server;
  - no new RPCs; `RequestUse(target, action)` carries it.
- `Scripts/Art/`: the griddle model (graybox steel plate on a cabinet, or a Kenney model), the spatula (Kenney Food
  Kit `cooking-spatula`), a flip animation (a 0.25 s arc with a turn, via the 027b tween), grill marks on the cooked
  side (decal or tint), a two-half ring.
- `Scripts/Audio/`: sizzle per item by heat, a flip "tssh".
- Menu: "Grilled seitan wrap" waits for assembly (031). In this spec, add "Grilled veggies" (plated) and
  "Toasted flatbread".
- Back line: the griddle goes next to the fryer (heat block). FloorPlanTests stay green.
- Tests, visual check (the cook tour grills seitan strips and flips them), `WhatsNew.json`, this spec.

## Out of scope
- Assembly and packaging (031). Oil or butter on the griddle. Griddle cleaning (035).

## Design notes
- Timings (default A, about 2x faster): seitan strips about 12 s per side, perfect 14 s, burnt 22 s. Vegetables 10 s
  per side. Bread toasts 4 s per side, burnt at 9 s.
- Flipping a side that is still raw is allowed. Without a flip the top side stays raw and the item scores low.
- A flip without the spatula, by hand, is refused with a prompt: "Use the spatula".

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: per-side doneness, flip, combined rating, toasting, determinism.
- [ ] AC3: Server tests: the flip needs the spatula and reach, spot capacity, zones per side.
- [ ] AC4: FloorPlanTests pass; the griddle front and its controls face the aisle.
- [ ] AC5: Visual check `cook`: seitan on the griddle, the ring's first half fills, flip (arc and turn), grill marks,
      the second half fills, take it off, plate it; the observer sees the flip.
- [ ] AC6: Serve "Grilled veggies" end to end in the tour.
- [ ] AC7: `WhatsNew.json` entry `spec-030`.

## Defaults to review
Timings above, 6 spots, prices Grilled veggies 7, Toasted flatbread 3.

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
