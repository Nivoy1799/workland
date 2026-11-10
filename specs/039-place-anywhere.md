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
- [x] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [x] AC2: `PlaceCoverageTests`:
  - it samples a 0.1 m grid over the top face of every counter-height collider in the shop;
  - each sample is either inside a PlaceSurface's footprint at that top height, or inside a listed exception;
  - it fails and names the object and the point otherwise.
- [x] AC3: Every new surface has at least one spot reachable from where a cook can stand (2.5 m reach).
- [x] AC4: Visual check: an item placed and then picked up again on the front counter (both runs), next to the
      register, next to the drink dispenser and on the fryer deck (not the fridge top: Q2 keeps the 1.8 m tops out).
- [ ] AC5 (manual, human): put things down on every counter you find; nothing refuses without a reason.
- [x] AC6: `WhatsNew.json` entry `spec-039`.

## Open questions (answered 2026-11-10: human approved all defaults)
1. Should the guest side of the front counter (the side customers stand at) also accept items? **No**, only the cook
   side. Customers don't place things.
2. The tops of the fridge and shelf are 1.8 m high: above counter height, so they are not in the rule. **Leave them**
   (out of reach for a comfortable place).

## Implementation notes
Built 2026-11-10 (builder, opus). `verify.sh` PASS: EditMode 1140/1140 (baseline 1120), sim 356/356. Not committed.

**Part 1, new surfaces** (appended, indices 0-11 unchanged; 24 surfaces now):
- 12 `Counter top 1` front counter west run, x 1.04-7.80 (26 spots);
- 13 `Counter top 2` between the spit and the drink dispenser, x 8.40-10.05 (6);
- 14 `Counter top 3` the 16 cm gap between the register and the dish return (1 spot, rule 3);
- 15 `Counter top 4` after the dish return, x 14.11-14.78 (2);
- 16/17 `Stove front` (stoves 1 and 2): the strip in front of the burners (4 each);
- 18 `Fryer deck`: behind the oil wells (3);
- 19 `Assembly back`: behind the dispensers and the rack (3);
- 20 `Sink front`, 21 `Sink back` (4 each);
- 22 `Plates front` (8), 23 `Plates back` beside the stack (4).

The front counter tops are children of their run (turned 180°, so a pan's handle and a tool face the cook). Their spots lie
on the cook half only (Q1). Their thin aim collider spans the whole depth, so aiming at the guest half shows the ghost on the
nearest cook-side spot. Items rest at 1.05 m. The segments are computed from the widths of the spit, dispenser, register and
dish return, with a 2 cm margin. Gaps under 10 cm get no top: 1 cm at the register, 9 cm between the dispenser and the
serving counter.

**Exceptions (`PlaceCoverage.Exceptions`, each with its reason):** Burner, Fryer basket, Fries box, Griddle spot, Basin,
Faucet, Spit cone, Spit tray, Build spot, Tool (the assembly dispensers and rack), Drink tool, Till (register), Counter (the
serving counter, a click sells), PlateStack, Trash (lid), and Front counter with guest half only. Matching is by object name:
exact, or the name followed by " <digit>", so "Counter" does not match "Counter module 3".

**Coverage test (`PlaceCoverageTests`):**
- It samples cells of at most 0.1 m over the top face of every non-trigger furniture collider whose top is 0.85-1.10 m, inside
  the walls' bounds. It skips items, characters and the surfaces' own aim colliders.
- A sample passes when it lies in a surface footprint at that height (±3 cm), or in a listed exception.
- Deviation, reviewer please check: a sample also passes within `PlaceCoverage.Rim` (0.15 m = spot edge margin 0.12 plus 0.03)
  of an exception's footprint or of something standing on that top. Without it, the slivers between the burners, the 10-15 cm
  aprons in front of the register, dispenser and dish return, and the 2 cm gaps between griddle spots would each need a one-spot
  surface no item fits on.
- A meta test runs the same scan with only the old 12 surfaces. It must name the front counter runs, the stove, fryer deck,
  assembly, sink and plate shelf, with a point for each.
- Other tests: rule 2 (front counter 1.05 m, every surface on the furniture under it), cook-side-only spots, every exception
  names something in the shop.
- AC3 is covered by the existing FloorPlanTests reach test, which checks every spot of every surface from the aisle.

**Rule 3:** `PlaceSpots.CountAlong` now gives 1 (not 0) for a side shorter than both margins. Two old assertions in
`PlaceSpotsTests` encoded the old rule and were updated to it (0.2 m → 1; a 0.1 m top has its one spot); 0 / NaN still give 0.

**Layout changes for room:**
- The plate stack moved from the shelf's middle to its back-left corner.
- The fries boxes moved from the fryer deck's middle (z 0.36) to its back edge (0.51). The builder constant is `FryerBoxesZ`;
  `FryerArt.BoxesZ` (Scripts/Art, out of scope) is now unused, a follow-up.
- The fryer got a `Deck` collider behind the wells (its body collider stops at 0.6 m). The aim then finds the deck, and the
  coverage test samples it.

**Tools on station tops (aim):** the assembly's back strip lies behind the bottles, so a ray aimed at a bottle hits the strip.
When a ray hits a place surface on the assembly board, `PickupInteractor.ToolNearRay` asks `ToolOnRay` (the register and dispenser cases were dead: removed in the 041 round).
The tool wins when the ray passes it closer than the hit lies to the nearest free spot. The first run, where the tool always
won, made the middle back spot (straight behind the chili bottle) unreachable; that was the one look-tour aim fallback.

**Part 2, layers:**
- TagManager: 8 `Items`, 9 `Player`, 10 `Npc`.
- DynamicsManager matrix: Items ignores Player and Npc (rows 8/9/10: `fff9ffff`, `fffeffff`, `fffeffff`); everything else stays
  on.
- The builders put every Pickup prefab on Items (recursive, 13 prefabs), Player.prefab on Player, Customer.prefab on Npc.
- `PhysicsLayers.Interact` (DefaultRaycastLayers | Items) is now explicit in the aim ray, the sight check, the pickup search and
  the server's spot probe.
- `PlayerController`'s ground and headroom probes use the player layer's matrix row (`PhysicsLayers.BodyMask`), so they ignore
  items like the controller does. The same project setting runs on the server and the clients.
- Tests (`ItemsLayerTests`): a controller with the player prefab's settings walks over a tomato and a plate stand-in (the
  prefabs' colliders and layer): it never rises and gets past. A control run with the stand-ins on Default must climb or be
  stopped.

**Part 3, tools:**
- `ToolRest.Home/Rotation/RestRotation`, pure and tested. The tool models' handle is their local +Z (seen in the old sheets: the
  knife and spatula lay blade towards the aisle at identity).
- `Pickup._handleTool` is set by the builder for the knife, spatula and cloth. For the cloth, the "handle" is its striped hem.
- Home: `PlaceOnSpot` turns them with `ToolRest.Home`. The respawn reuses the scene pose.
- Place: `PickupService.TryPlace` uses the server's player position. The predicted pose and the ghost (`PlacePose`) use the
  client's own position with the same rule. Near a 45° boundary the two can differ by one snap; this has not been seen.
- A Q-drop on the floor keeps the old rule (identity yaw); the spec only names placing.

**Tours:**
- New act `place-anywhere` in the look tour (`Dev/PlaceAnywhereSequence.cs`): the three tools at home; the knife put down and
  picked up on 9 new tops; the knife back home; a tomato dropped in the aisle and walked over in third person. The walk logs
  the highest y.
- Look tour timeout 240 → 480 s, `visual-check.sh` look 360 → 600 s.
- The cook tour now picks stove B's top by name (`StoveTop`), since stove B also has the `Stove front` strip. My first
  version of that edit sent the front strip down the `else` branch as the "free burner". The pan landed on the strip, and the
  cook tour failed with "pan heat setup missing". Fixed and re-run.
- AC4's "fridge top" is not done: Q2 keeps the 1.8 m tops out.

**Visual check (2026-11-10):**
- Look tour (`.verify/visual039b/look/sheet.png`, cells 58-119): ok, 0 timeouts, 0 aim fallbacks.
  - The knife was put down and picked up again on all 9 tops. On each, the handle came to rest 3-7° off the direction to the
    cook.
  - The front counter tops hold the knife on the cook side at 1.05 m.
  - The tomato walk logged ground y 0.080 and highest y 0.080.
- Cook tour (`.verify/visual039c/cook/sheet.png`): ok, 965 images, 0 timeouts, 0 fallbacks (the same as the baseline).
- The cook run before it (`visual039b`) failed. The host window ended with no exception and no crash dump, 8 s after the pan's
  burner went on. The client then timed out. A rerun of the same build passed the same step, so it looks flaky (not seen
  before, worth watching).

**Still feels off (for the human):**
- The fryer deck and the assembly back strip lie behind the baskets and the bottles; a tool put there is partly hidden.
- The stove front strip covers the hob's drawn front edge.
- The one spot between the register and the dish return is narrower than a plate.
- Food colliders (0.4 m) still overlap neighbouring spots, as before.

## Blockers
(Builder fills this in after 3 failed attempts.)
