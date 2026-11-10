---
id: 039
title: Place anywhere (every counter top accepts items, enforced by a test)
status: approved
owner: architect
builder: opus
---

# 039: Place anywhere

## Goal
The human, after playing (2026-11-10): "item placement is not compatible still on all counters. This needs to work all
the time, everywhere." Today only some tops are place surfaces:
- the back-line worktops;
- stove tops outside the burners;
- the griddle deck;
- the sink drainboard.

The two front counter runs were built without one on purpose (spec 027a: "no place surface"). Several station tops
are dead too, for example the counter area around the register and the drink dispenser, the fryer's front deck, and
the tops of the fridge and shelf. This spec makes every flat top a player can reach and see as a counter accept items.
A scene test makes sure no future station can silently add a dead top again.

## Rule (the contract)
1. Every horizontal top in the shop at counter height (0.85–1.10 m) that a player can reach from the aisle or the
   front-counter cook side is covered by a `PlaceSurface`. The only exceptions are the parts with a special function:
   - burners (already their own one-spot surfaces);
   - the fryer's oil wells and baskets;
   - the griddle plate spots;
   - the sink basin;
   - the spit cone and tray;
   - the assembly build spot;
   - the dispenser taps;
   - the register tray.
   These keep their own behaviour. Every exception is listed in one place (`PlaceCoverage.Exceptions`), with a reason.
2. Items rest on the real top height of that surface (front counter 1.05 m, worktops 0.90 m), never floating or
   sunk.
3. The 0.25 m spot grid (spec 020) applies everywhere. A surface narrower than one spot still gets one spot.
4. Server authority is unchanged: the server checks reach, free hands and the spot exactly as today. No new RPCs. New
   surfaces are new indices appended after the existing ones, so no index moves.

## Part 2: items are never stairs (added after the human's second note, 2026-11-10)
The human: "the character walks onto items and then over them; a tomato behaving like stairs looks wrong". The player's
CharacterController treats every food and cookware collider as ground and climbs it through its step offset.
- Loose items (Pickup, Food, cookware, tools, boxes and bags) go on their own physics layer (`Items`). The player
  layer ignores it in the collision matrix, so the body walks through items on the floor instead of climbing them.
  This is the same project setting on server and client, so prediction and the server agree.
- The aim ray, the place spots and every server reach or overlap check still see items: they query that layer
  explicitly.
- Customers and NPCs also ignore items.
- Items still rest on counters and the floor, because Items collide with the static world.
- Test: a scene test steps the player's controller over a tomato on the floor and asserts its height never rises
  above the ground (plus the skin). Every Pickup prefab is on the `Items` layer.
- Scope addition: `game/ProjectSettings/TagManager.asset` and `DynamicsManager.asset` (the layer and its matrix only;
  never `UnityConnectSettings.asset`), the prefabs through the builders, and the aim and overlap layer masks in
  `Scripts/Net`.

## Part 3: tools lie handle towards you (added after the human's third note, 2026-11-10)
The human: "utensils like the knife and the griddle flip tool spawn without the handle facing the aisle". Tools at their
home spots (knife on the board, spatula on the griddle deck, cloth on the drainboard), and tools you put down, must lie
the way a cook leaves them:
- **Home spawn and respawn:** the handle points straight at the aisle (the cook side of that surface), the blade or head
  points away. The knife lies flat with the edge down.
- **Placed by a player:** the handle points towards where that player stood when placing it, snapped to the nearest 45°.
  So a tool put down on the front counter faces the cook, not the guests. The server decides the rest rotation from the
  server-side player position. The predicted look (027b) uses the same rule, so nothing jumps.
- **Pure rule:** `ToolRest.Rotation(surfaceFacing, placerPosition, spotPosition)`, tested. A scene test checks that every
  home spot's tool handle points into the aisle (within 15°).
- Applies to every tool with a handle: knife, spatula, cloth, and any later tool. Ingredients and cookware keep their
  rules (pans keep their handle towards the aisle too, from spec 021).

## Scope
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs`: surfaces for the front counter runs, and for every station top
  that is not an exception. Appended indices.
- `game/Assets/_Project/Scripts/Net/` (only if `PlaceSurface` or `PickupService` need a small change, for example
  surfaces on rotated parents).
- New `game/Assets/_Project/Scripts/Net/Rules/PlaceCoverage.cs` (the exception list).
- Tests: a new `PlaceCoverageTests` that scans the built scene, plus the existing place and floor-plan tests.
- The visual-check `look` tour: place an item on the front counter, the register counter and the fryer deck.
- `WhatsNew.json`, this spec.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: `PlaceCoverageTests`:
  - it samples a 0.1 m grid over the top face of every counter-height collider in the shop;
  - each sample is either inside a PlaceSurface's footprint at that top height, or inside a listed exception;
  - it fails and names the object and the point otherwise.
- [ ] AC3: Every new surface has at least one spot reachable from where a cook can stand (2.5 m reach).
- [ ] AC4: Visual check: an item placed and then picked up again on the front counter (both runs), next to the
      register, next to the drink dispenser, on the fryer deck and on the fridge top.
- [ ] AC5 (manual, human): put things down on every counter you find; nothing refuses without a reason.
- [ ] AC6: `WhatsNew.json` entry `spec-039`.

## Open questions (answered 2026-11-10: human approved all defaults)
1. Should the guest side of the front counter (the side customers stand at) also accept items? **No**, only the cook
   side. Customers don't place things.
2. The tops of the fridge and shelf are 1.8 m high: above counter height, so they are not in the rule. **Leave them**
   (out of reach for a comfortable place).

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
