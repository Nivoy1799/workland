---
id: 067
title: Haven Nine graybox in /game (terrain bowl, district blocks, paths, NavMesh, the Dome shell, border set pieces, colliders, the server safe-spot check)
status: approved
owner: architect
builder: sonnet       # Parts A and B; Part C (the safe-spot check) is netcode: opus
---

> Approved by the human on 2026-11-11 night ("feel free to do everything"; the Haven Nine town plan is approved as D41)
> with the recommended defaults of its Open questions. Work package W5 of docs/town-plan.md section 10.

# 067: Haven Nine graybox

## Goal
The whole bowl under the Dome as a walkable graybox around today's shop: terrain, paths, district massing, landmarks,
the Dome shell with its ribs, the four border set pieces as blocks, the colliders, a NavMesh, and the server check that
returns anyone outside the play area to a safe spot. Everything is built from spec 064's data, so the numbers (walk
times, slopes, borders) are tested once in /sim and once in the built scene. The shop and its 062a layout stay exactly
where they are.

## Depends on
**Specs 062a and 064 built.** D29, D31, D33, D45, D48; docs/town-plan.md section 3; spec 051 sections 1-5b and 8;
specs 054 and 054b (prediction, soft separation, reconcile snap above 0.30 m), 058 (rival fronts, rankings board),
026 and D32 (palette). Not 065 or 066.

## Scope
Only `/game` (plus the tour hooks in `scripts/visual-check.sh`). Paths under `game/Assets/_Project/`.
- **Part A, data to world (pure):** `Scripts/Town/` (new, namespace `Worktown.Game.Town`): `TownFrame.cs` (town mm to
  world metres), `TownTerrainRules.cs` (heights), `TownMassing.cs` (building blocks per district), `DomeShape.cs` (shell
  and ribs), `BorderPieces.cs` (set pieces and invisible walls), `TownLook.cs` (the graybox colours, one place).
- **Part B, the builder:** `Editor/TownGrayboxBuilder.cs` (new), `Editor/GrayboxSceneBuilder.cs` (change: calls it;
  the 100 x 100 m ground plane becomes a flat shop pad inside the terrain; nothing else moves), the regenerated
  `Scenes/Graybox.unity`, its NavMesh data asset, materials under `Settings/Town/`; `Scripts/Town/RibSegment.cs` (a
  plain component: rib index, segment index, the district below).
- **Part C, the safe-spot check (netcode, opus):** `Scripts/Net/Rules/PlayAreaRules.cs` (pure), `Scripts/Town/TownBounds.cs`
  (builds the rule's inputs from 064 once), `Scripts/Net/PlayerController.cs` (change: apply the rule in the replicate).
- **Dev and tests:** `Scripts/Dev/TownTourRunner.cs` (new: `town` and `edges` tours), `scripts/visual-check.sh` (change);
  `Tests/EditMode/`: `TownFrameTests`, `TownTerrainRulesTests`, `TownMassingTests`, `DomeShapeTests`, `BorderPiecesTests`,
  `PlayAreaRulesTests`, `BuiltTownTests`; `Resources/WhatsNew.json`; this spec.
- No change in `/sim` (064 owns the data), in the 062a layout, the kitchen, the shop's street, or the 058 fronts.

## Out of scope
Final art and dressing (the art pass, question 29); revival looks (068); light moods, night and weather (069); story
text (070); town NPC crowds and their routes; moving lifts, the funicular, the monorail and cars (blocks only); interiors
beyond today's shop; swimming; the backdrop city and hills beyond the Storm Ring stand-in; moving the shop.

## Design

### Frame (`TownFrame`, pure)
The shop stays at world origin, so 062a's parity snapshot and every built-scene test hold. `TownFrame.WorldOf(TownPoint
p, int heightMm)`: `x = (p.XMm - s1.Anchor.XMm) / 1000 + ShopFrontX`, `z = (p.ZMm - s1.Anchor.ZMm) / 1000 + ShopFrontZ`,
`y = (heightMm - s1Node.HeightMm) / 1000`, with `ShopFront` the shop's front door in today's scene (the builder reads it
from the generated shop). Inverse `TownOf(Vector3)`. A test pins that Main Street's axis lands on today's road centre
line (world z 8 within 1 m) along the shop pad.

### Terrain (`TownTerrainRules`, pure; the builder makes meshes)
- A height grid of 4 m cells over the Dome footprint, built in 64 x 64 m chunks (culling). Heights: inside the **shop
  pad** (world x -5..55, z -5..35) exactly 0; within 4 m of a 064 path edge, the edge's linear height between its nodes
  minus 0.05 m; elsewhere the inverse-distance weighted height (power 2) of the 4 nearest nodes. Outside the play area
  the ground rises 3 m to the Dome's foot. East of the shoreline the lake bed drops to -3 m town height; a water plane at
  town height 0.
- **Path ribbons:** one strip per `Path`, `Ramp` and `Stairs` edge at its `WidthMm`, 0.05 m above the terrain line,
  rounded joins at nodes; stairs get steps and their ramp twin comes from 064. Not drawn inside the shop pad (today's road
  is Main Street there).

### Massing (`TownMassing.Blocks(TownCatalog) -> IReadOnlyList<Block>`, pure, deterministic, no RNG)
- Every Small, Restaurant, StageFive and Civic place gets a block with a 3 m door gap at its anchor, its footprint from
  `AreaM2` (Civic: per type below), facing its node.
- Filler blocks line each district's paths (8-20 m wide, gaps of 3-6 m, by a fixed pattern from the edge index), never
  within 2 m of a ribbon, 4 m of an anchor, or inside the shop pad.
- Heights by district: Main Street 7-11 m, Station Square 10-16 m (the station hall, the Town Hall with a clock tower),
  Tower Rise five towers of 60, 80, 95, 110 and 140 m (the Sky Deck on the tallest, all below the Dome), Market Hall
  one hall 60 x 40 x 12 m, Homes 6-9 m, Workshop Lane 6-8 m, Greenhouse Hill terraced greenhouses 5 m plus the glass dome
  landmark (25 m), Festival Meadow the Arena ring (80 m across, 15 m), Lakefront the pier and the lighthouse (25 m), Low
  Ring hard boxes 8-14 m in the towers' shade (art direction v2 section 8: hard boxes only there).
- The four compass landmarks (051 section 1) stay visible: the hill's glass dome (north), the lighthouse (east), the
  Arena (south), the tallest tower and the Wreck (west).

### The Dome (`DomeShape`, pure)
- Plan ellipse from 064 (770 x 640 m, centre (-60, 40)); crown 250 m, shifted 40 m west; profile: height at normalised
  radius r is `H * sqrt(1 - r^4)` (steep at the foot, round at the top).
- Shell: a mesh in `TownLook.Glass` (Palette `SoftBlue` at 15 % alpha until the art pass), **no collider, no shadow**.
- Ribs: 18 main ribs from the foot to the crown, each branching once at 40 % height into two (like leaf veins), and
  three rings at 25, 55 and 80 % of the height; box chains 1.5 m thick that cast shadows, cut into segments of about 20 m.
  Each segment has a `RibSegment` with the 064 district under its plan point (or "none"); 069 lights them.

### Borders (`BorderPieces`, pure; town-plan 3.3)
- **West, the Wreck:** the Albatross hull as a box chain (about 180 m long, 40 m across) half inside the wall, the
  riveted collar ring at the wall, the welded **bulkhead** with a porthole block (the night market place in its tail
  is a hall block); **South, the Wildbelt:** a 3 m fence along the south edge, the lookout platform at
  `wildbelt_lookout`; **East, the lake:** the wall line into the water with **water gate 3** as a gate block, a kerb or
  rail along the shore and the pier; **North, the tunnel:** the round pressure door (12 m) at `tunnel_door`; **all
  round:** the three service airlocks; a 2 m concrete sill ring where the Dome meets the ground.
- Every piece has colliders. A closed ring of **invisible box colliders 15 m outside** every play-area edge, 30 m tall
  (town-plan 3.3 safety net).
- **Storm Ring stand-in:** an inward-facing cylinder (1.5 km radius, 300 m, `TownLook.Storm` = Palette `Charcoal`,
  unlit, no collider). 069 adds the weather on it.

### NavMesh
One `NavMeshSurface` (AI Navigation 2.0, installed) over terrain, ribbons and floors; agent radius 0.3 m, step 0.3 m,
max slope 20 degrees; baked by the builder and committed. It is for later town NPCs and for the walk-time test; today's
guests keep 062a's walker.

### The safe-spot check (Part C, netcode)
- `PlayAreaRules` (pure; inputs from `TownBounds`): the play-area polygon in world x/z, `FallY` (064 `fallHeightMm` in
  world y) and the safe spots (064 `SafeSpot` nodes in world space). `bool TryReturn(Vector3 position, out Vector3
  target)`: true when the position is more than **2 m outside** the polygon or below `FallY`; the target is the
  nearest safe spot by x/z distance (ties by spot id).
- Applied in `PlayerController`'s replicate, after `_controller.Move`, on the server and on the owner (the same rule and
  data on both sides, as 054b's separation): position set with the controller disabled, velocity and vertical velocity
  zeroed. The server's result is the authority (AGENTS rule 6); an owner that skips the rule is snapped by the reconcile
  (054b: errors over 0.30 m snap). Other players are ignored; 054b's soft separation pushes two players apart if they
  land on one spot (D48, no rubber-banding). No wire change, no `ProtocolSurface.Revision` bump.
- Dev only (`DEVELOPMENT_BUILD || UNITY_EDITOR`): `TownTourRunner` runs in the host process and calls a server-side
  dev method that places a player at a given point, to test the check; no new RPC. If the tour's bot is a remote
  client, the netcode owner picks the cheapest dev-only path and notes it.

### Tours
- `town`: from Fountain Cross towards the four landmarks, the Dome from Station Square, each border piece, Main Street
  from the shop pad's edge; contact sheet.
- `edges`: the bot walks into each of the four borders and logs its farthest point outside the polygon; then the dev
  placement puts it 30 m outside (behind the invisible ring) and 30 m below the terrain; it logs the return time.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; `git diff --stat main -- sim` is empty; RPC and connection surface tests
      are unchanged.
- [ ] AC2: Every existing built-scene test passes unchanged, including 062a's `DefaultLayoutParityTests`; nothing on the
      shop pad moved (a test compares the pad's objects with the pre-067 scene by name and transform within 1 mm).
- [ ] AC3: `TownFrame` round-trips; Main Street's axis is on today's road centre (z 8 +- 1 m) along the pad; each 064
      node's ground height in the built scene is within 0.10 m of its frame height.
- [ ] AC4: Ribbons: every `Path` and `Ramp` ribbon, sampled every 1 m, has a slope below 5 %; every massing block
      keeps 2 m from every ribbon and 4 m from every anchor; every Small, Restaurant, StageFive and Civic place has a
      block with a door gap at its anchor.
- [ ] AC5: NavMesh: a complete path from Fountain Cross to every core place with length / 2.5 m/s <= 90 s; for 20
      fixed node pairs the NavMesh length is 0.9-1.3x 064's `TownPaths` length.
- [ ] AC6: Borders: along each play-area edge a collider piece (fence, wall, sill, bulkhead, gate or door) stands on the
      edge or at most 1.5 m outside it, so normal walking never triggers the 2 m return;
      72 rays from the play-area centre each hit the invisible ring at 15 +- 1 m outside the polygon; the Dome ellipse
      contains the play area; the shell has no collider and casts no shadow; there are 18 main ribs and 3 rings, and
      every `RibSegment` has a district id or "none".
- [ ] AC7: `PlayAreaRules` literals: inside, no return; 1.5 m outside, no return; 2.5 m outside, the nearest spot;
      below `FallY`, the nearest spot; a tie goes to the lower id; the same input always gives the same target.
- [ ] AC8: `--tours edges`: at each border the bot never gets more than 1.5 m outside the polygon; after each dev
      placement it is at a safe spot within 2 s; 0 Exception lines; owner corrections p95 stay within 054b's limit.
- [ ] AC9: `--tours town` and the existing `look,walk,cook,menu,corner` tours pass with 0 Exception lines.
- [ ] AC10: Budget: every town renderer is static; the town adds at most 4,000 renderers; the load test
      (`tools/bots/run-loadtest.sh`, 15 bots plus the host) keeps 054's bandwidth budget and the host frame time p95
      within 10 % of the pre-067 run (the netcode owner runs it and notes both numbers).
- [ ] AC11: Colours come only from `Palette` through `TownLook`; `WhatsNew.json` entry `spec-067` ("the town around
      your shop, in grey blocks for now").

## Test plan
- EditMode pure tests: `TownFrameTests`, `TownTerrainRulesTests` (pad flat, edge heights, IDW literal), `TownMassingTests`
  (determinism, clearances on a fixture town), `DomeShapeTests` (profile literals, rib count, segment districts),
  `BorderPiecesTests` (ring closed, offset 15 m), `PlayAreaRulesTests` (AC7).
- `BuiltTownTests` opens `Graybox.unity` (through 062a's `LayoutScene`) for AC2-AC6.
- Commands: `bash scripts/verify.sh`; `bash scripts/visual-check.sh --tours town,edges --out .verify/visual067`; then
  `--tours look,walk,cook,menu,corner`; the AC10 load test.

## Human steps
Walk the town once in a dev build: is it easy to find the Market Hall, the pier and the Town Hall without a map (051
AC6, manual)?

## Open questions (recommended default in brackets)
1. One scene. [yes: the town is built into `Graybox.unity` around the unchanged shop (051 section 8)]
2. The shop's place. [`s1` on Main Street; the shop stays at world origin so 062a's snapshot holds]
3. Terrain method. [a generated mesh from 064's node heights, not a Unity Terrain: deterministic and testable]
4. Lifts, funicular and monorail. [blocks only; 064 makes every place reachable by ramp]
5. Return margin. [2 m outside the polygon; the colliders hold first, the check is the last resort]
6. Safe-spot occupancy. [ignored; 054b's soft separation spreads players]
7. The lake. [no swimming: a kerb and rails at the shore and the pier]
8. Colours. [graybox greys and the existing palette until the art pass (question 29)]

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
