---
id: 062a
title: Layout as data (the kitchen is a list of pieces, spawned by the server; stable surface and station ids)
status: approved
owner: architect
builder: opus
---

> Approved by the human on 2026-11-11 night ("feel free to do everything") with the recommended defaults of its Open questions.

# 062a: Layout as data

## Goal
Today `GrayboxSceneBuilder` hand-builds the kitchen, and place surfaces (039: 0-23, cup spots 24-26) and stations
(0-17) are array indices that tours, NPC paths, tutorial targets and tests rely on. This spec turns the kitchen into
data: a list of pieces (type, position, rotation) stored in the host save and spawned by the server at start. Surfaces
and stations get stable ids. Today's kitchen becomes the default layout, so the game plays the same. It is the base for
build mode (062b), the furniture shop (062c) and the restaurant as layout data (057).

**Integration spec** (/sim and /game). Netcode owner: Opus. This is the largest of the 062 specs; the cut line, if
one session is not enough, is "Part A" (data, ids, spawn, save, parity) then "Part B" (validation rules, NPC routes).

## Depends on
D29, D32, D33, D37; specs 020/021 (`PlaceSurface`, `PlaceSpots`), 027a (floor plan), 039 (coverage, `ToolRest`), 041
(`PlaceTurn`), 042 (cup spots), 053 (save, `ClosedCover`), 037 (save v2, `Revision` 4), 054 (the revision rule).

## Scope
**/sim** (`sim/Runtime/Layout/`, new, namespace `Worktown.Sim.Layout`, one public type per file):
`PieceDef.cs`, `PieceMount.cs` (enum), `PieceCatalog.cs`, `LayoutPiece.cs`, `ItemHome.cs`, `ShopLayout.cs`,
`RoomDef.cs`, `LayoutRules.cs`, `LayoutReport.cs`, `LayoutProblem.cs`, `LayoutProblemKind.cs`, `WalkGrid.cs`,
`GridPath.cs`. Tests in `sim/Sim.Tests~/Layout/`.

**/game** (paths under `game/Assets/_Project/`):
- `Scripts/Layout/` (new, `Worktown.Game.Layout`): `PieceBehaviour.cs` (NetworkBehaviour on every piece prefab),
  `LayoutSpawner.cs` (server spawn; offline instantiate for tests and the Editor), `LayoutRegistry.cs` (both sides:
  piece, surface and station by id), `SurfaceId.cs` (pure), `PosePacking.cs` (pure), `PiecePrefabs.cs`
  (ScriptableObject: type id to prefab), `DefaultLayout.cs` (loads the resource, `Backfill`), `LayoutCodec.cs` (pure:
  save part to `ShopLayout`), `NpcRoute.cs` and `NpcRoutes.cs` (pure: guest anchors and waypoints), `LayoutQuery.cs`
  (tours and tests: find by piece type, nth piece, local surface name).
- `Resources/Layouts/default.json` (new, written once by the exporter, then hand-maintained data).
- `Editor/LayoutExporter.cs` (new: today's builder kitchen to `default.json` and the parity snapshot),
  `Editor/PiecePrefabBuilder.cs` (new: one prefab per piece type, from the builder's station code, covers included),
  `Editor/GrayboxSceneBuilder.cs` (change: no kitchen stations, worktops, front counter or their surfaces; keeps the
  room shell, street, spawn points, services; registers the piece prefabs), the regenerated scene,
  `DefaultPrefabObjects.asset`.
- `Scripts/Net/`: `PlaceSurface.cs` (local index, `Id`), `PickupService.cs` (registry, no array), `PickupInteractor.cs`
  (ids in `RequestPlace` and `RequestStation`), `ProtocolSurface.cs` (`Revision` + 1). `Rules/PlaceSpots.cs`,
  `PlaceTurn.cs`, `PlaceCoverage.cs` keep their logic (keys and exceptions unchanged).
- `Scripts/Cooking/Station.cs` (`Index` becomes `Id`), `KitchenService.cs` (dictionary by id; home slots by surface id),
  `Scripts/Shop/CleaningService.cs`, `Scripts/Tutorial/TutorialTargets.cs`, `Scripts/UI/ServeRewardView.cs`,
  `Scripts/Climb/ShopStage.cs` (covers live in the piece prefabs).
- `Scripts/Npc/CustomerService.cs` (anchors from `NpcRoute`), `Customer.cs` (walks a waypoint list).
- `Scripts/Save/`: `ShopSave.cs`, `ShopSaveCodec.cs`, `SaveMigrations.cs` (next version), `SaveService.cs`.
- `Scripts/Dev/*` (tours look things up through `LayoutQuery`, never by index).
- Tests in `Tests/EditMode/`: new `LayoutScene.cs` helper (opens `Graybox.unity` and instantiates the default layout
  offline); every built-scene test (17 files today) opens through it. `Resources/WhatsNew.json`; this spec.
- Not touched: specs 040, 046, 050, 051; `game/Library`, FishNet sources.

## Out of scope
Build mode, moving pieces, attach rules (062b). Buying, tiers, selling (062c). Any new piece type that is not in
today's kitchen, except the reserved catalog rows below. A NavMesh. New art. Saving item positions (items are not saved).

## Design

### Data (/sim, all integers: millimetres and quarter turns, so validation is deterministic)
```
enum PieceMount { InLine, Floor, Overhead, Wall, Yard }   // InLine: a counter-line module; Yard: outside, fixed
record PieceDef(string TypeId, int WidthMm, int DepthMm, PieceMount Mount, bool IsCounter, bool HasGuestSide,
    bool HasStation, bool Required, bool Movable, int MinLengthMm, int MaxLengthMm)   // length: counters only
record LayoutPiece(int Id, string TypeId, int XMm, int ZMm, int Turns, int LengthMm, int Tier, long PaidCents)
record ItemHome(string ItemKind, int PieceId, int LocalSurface, int Spot)          // cookware and tools at start
record ShopLayout(int Version, ImmutableArray<LayoutPiece> Pieces, ImmutableArray<int> Stored,
    ImmutableArray<ItemHome> Homes, int NextId)
record RoomDef(RectMm Floor, RectMm CookArea, RectMm GuestArea, ImmutableArray<RectMm> KeepOut,
    PointMm StaffSpawn, PointMm GuestEntry)                                       // the fixed shell
```
- `XMm, ZMm` is the footprint centre; `Turns` 0..3 (90° steps, 0 = cook side towards -Z as today's back line).
  `Tier` (1) and `PaidCents` (0) are reserved for 062c; `Stored` (empty) is reserved for 062b.
- `PieceCatalog.CreateDefault()` rows, one per type in today's kitchen: `shelf_dry`, `fridge`, `stove`, `fryer`,
  `griddle`, `assembly`, `plate_shelf`, `sink`, `worktop` (back line, variable length), `front_counter` (variable
  length, guest side), `serving_counter`, `register_module`, `dispenser_module`, `spit_module`, `dish_return_module`
  (each a counter body plus its appliance on top, as 039's segments already imply), `trash`, `terminal` (Wall),
  `menu_board` (Overhead), `bin` (Yard). Reserved rows for later specs (not in the default layout): `goal_board` (Wall,
  057), `door_sign` (Wall, 062b), `office_terminal` (Wall, 059).
- Footprints match the prefabs: a test compares each prefab's collider bounds with its row (±10 mm).

### Stable ids (the migration away from indices)
- **Piece id:** `int`, 1..2^24, assigned once (`NextId`), saved, never reused. The default layout's ids are fixed in
  `default.json`, in today's station order (so logs stay readable).
- **Station id = piece id.** `Station.Index` is removed; `Station.Id` reads its `PieceBehaviour`. Burners' `StoveIndex`
  and cup spots' `DispenserIndex` become `StovePieceId` and `DispenserPieceId`.
- **Surface id:** `SurfaceId.Of(pieceId, local) = pieceId << 6 | local`, `local` 0..63 in the prefab's creation order
  (the order the builder uses today). `SurfaceId.None = -1`. `PlaceSurface.Index` is removed; `Id` is computed when its
  piece registers.
- **Every user of indices, and what it becomes:**

| User today | After |
|---|---|
| `PickupService._surfaces[]`, `SurfaceAt(int)`, `SurfaceCount` | `LayoutRegistry.Surface(int id)`, `Surfaces` |
| `PlaceSpots` key `(surface << 32 \| spot)` | unchanged, the surface part is the id |
| `PlaceTurn.Pack(spot, turn)` in the spot int | unchanged; a test pins every prefab's spot count < 2^16 |
| `RequestPlace(int surfaceIndex, int spot, int request)` | same types, `surfaceId` (meaning change: Revision + 1) |
| `RequestStation(int stationIndex, int option, int request)` | same types, `stationId` |
| `KitchenService._stations[]`, `HomeSlot`, assembly `board` index | `LayoutRegistry.Station(id)`; homes by surface id |
| `PickupOrigin` (leaving player's item goes back) | surface id |
| Tutorial `DispenserIndex == _drinks.Index` | `DispenserPieceId == _drinks.Id` |
| Tours (`CookSequence`, `PlaceAnywhereSequence`, corner, perf) | `LayoutQuery.Surface("stove", 1, "StoveTop")` etc. |
| Tests that assert indices 12-26 | assert by `LayoutQuery` and the parity snapshot |

### Spawning
- Server start (before the first player and before `CustomerService` opens a shift): load the save's layout (or the
  default), `LayoutRules.Validate`; if invalid, use the default and raise the feed line "The kitchen was reset to the
  standard layout" (the bad layout is logged, not kept). Spawn pieces in ascending id; each `PieceBehaviour` gets
  `SyncVar<int> PieceId` and `SyncVar<long> PosePacked` (x 24 bits, z 24 bits, turns 2, length 14) before `Spawn`, so
  late joiners get them in the spawn payload. Then the item homes are spawned (as `PlaceOnSpot` does today).
- Nested NetworkObjects (fryer baskets, stove buttons, stock slots, the plate shelf's byte) live inside the piece prefabs.
  If FishNet's nested prefab spawn misbehaves, the fallback is that the spawner spawns them separately and parents them
  (netcode owner's call; write it in Implementation notes).
- `LayoutRegistry` fills on `OnStartServer` and `OnStartClient` of each piece. Aim code keeps using the components.
- `DefaultLayout.Backfill(saved, default)`: a type with `Required` or flagged in a later spec that is missing from a
  save is added at its default pose when that is valid, else to `Stored`. This is how 057, 059 and 062b add pieces.

### Validation (`LayoutRules.Validate(layout, room, catalog) -> LayoutReport`, pure, problems in a stable order)
1. **Inside:** every footprint inside `room.Floor` (Yard pieces: outside it, `Movable = false`); Wall pieces touch a wall.
2. **No overlap:** footprints of the same band (Floor and InLine together; Wall; Overhead) do not intersect; touching
   is allowed. Nothing intersects a `KeepOut` rect (doors, the staff gap, the back door's delivery spots).
3. **Aisles:** in front of each piece's access side (the cook side; counters with a guest side also that side), a strip
   `AisleMm = 900` deep across the piece's width is free of other floor pieces and walls. Strips may share space.
4. **Every station reachable:** on `WalkGrid` (100 mm cells; a cell is free when its centre is at least `ClearanceMm =
   400` from every floor footprint and wall), each station's access strip has a free cell reachable from `StaffSpawn`.
5. **Guests reach a counter:** at least one `serving_counter` has its guest strip inside `GuestArea`, reachable from
   `GuestEntry` through free cells of `GuestArea` only (guests never walk in the kitchen, feel.md 6).
6. **The counter has a guest side:** every piece with `HasGuestSide` has its guest strip in `GuestArea` and its cook
   strip in `CookArea`.
7. **Required:** one of each `Required` type is placed (serving counter, register, sink, trash, plate shelf, dry
   shelf, fridge).
- `LayoutProblem(Kind, PieceId, OtherId, XMm, ZMm)` with a short text per kind ("Too close to the fryer: aisles need
  0.9 m") for 062b's ghost. The default layout passes every rule (test). feel.md 6 says 1.2 m aisles: see question 2.

### NPC routes (`NpcRoutes.Build(layout, room, catalog) -> NpcRoute`, pure; the existing straight-line walker)
- `NpcRoute`: `Entry`, `QueueHead`, `QueueDirection`, `QueueFacing`, `PayHead`, `PayDirection`, and `Waypoints` from
  the entry to the queue head and from the pay head to the exit.
- Anchors come from the serving counter's and the register's guest side (pose plus fixed offsets taken from today's
  builder values). Waypoints: `GridPath.Find(grid, from, to)` (A*, 4-neighbour, ties broken by cell index, so it is
  deterministic), reduced to corner points. For the default layout it gives today's straight lines.
- `Customer` walks a list of points at `CustomerRules.WalkSpeed`; `CustomerService.ApplyRoute(route)` is called after
  spawning (and, from 062b, after a layout change while no guest is inside).

### Save (next free format version, today v3; migration from v2)
`layout` part: `pieces[] {id, type, xMm, zMm, turns, lengthMm, tier, paidCents}`, `stored[]`, `homes[] {item, piece,
local, spot}`, `nextId`. Migration: the default layout. Codec: unknown types are kept in the file but not spawned;
out-of-range values clamp; a layout that fails validation falls back as above. Stock slot keys (`shelf:potato`) stay.
**Note for later specs:** 057 and 058 were written with save v2 and v3; they take the next free versions at build time
(a mechanical renumber, no design change).

### Parity (today's kitchen is the default)
Before removing builder code, `LayoutExporter` writes `Tests/EditMode/Data/kitchen-snapshot.json`: every station and
surface with name, kind, old index, world position, rotation, size, top height and spot count, plus the entry, queue,
pay and spawn transforms. The snapshot is committed and is the parity reference.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes.
- [ ] AC2: /sim: each of rules 1-7 has a passing and a failing literal layout; the default layout (loaded from the same
      JSON) passes; same input gives the same report; `GridPath` literals (straight, around a block, no path).
- [ ] AC3: `DefaultLayoutParityTests`: the offline-spawned default layout matches the snapshot: same stations and
      surfaces by name and kind, positions within 1 mm, yaw within 0.1°, same sizes, top heights and spot counts.
- [ ] AC4: `SurfaceIdTests` (pack, unpack, `None`, limits); every prefab's spot count < 2^16; `PlaceTurn` tests unchanged.
- [ ] AC5: Reflection: `PlaceSurface` and `Station` have no `Index` member; no `PlaceSurface[]` or `Station[]` field
      remains in `PickupService` or `KitchenService`.
- [ ] AC6: `PlaceCoverageTests`, `FloorPlanTests`, `ToolRestTests`, `EmptyHandsAimTests` and the other built-scene
      tests pass through `LayoutScene` with their assertions unchanged except index literals.
- [ ] AC7: `NpcRoutesTests`: the default route equals the snapshot's entry, queue and pay transforms within 10 mm; a
      layout with the serving counter 1 m to the west moves the queue head 1 m west; a block in the guest area gives a
      path around it.
- [ ] AC8: Save: a v2 fixture migrates to the default layout; a full layout round-trips equal; an overlapping layout
      falls back with the feed line; an unknown type is kept in the file; version 99 is `NewerVersion`.
- [ ] AC9: Surface tests change only by the `PieceBehaviour` lines; no new RPC; `Revision` + 1 (documented).
- [ ] AC10: `bash scripts/visual-check.sh --tours look,walk,cook,menu,corner,tutorial` pass with 0 Exception lines, 0
      timeouts, aim fallbacks no more than the baseline; the walk tour's client joins after the host spawned the layout
      and places an item on the front counter.
- [ ] AC11: `WhatsNew.json` entry `spec-062a` ("the kitchen is the same; under the hood it can now be rebuilt").

## Test plan
- `sim/Sim.Tests~/Layout/`: `LayoutRulesTests`, `GridPathTests`, `WalkGridTests`, `PieceCatalogTests`,
  `ShopLayoutTests`. Command: `dotnet test sim/Sim.sln`.
- `Tests/EditMode/`: `DefaultLayoutParityTests`, `SurfaceIdTests`, `PosePackingTests`, `LayoutSpawnTests` (offline),
  `PieceFootprintTests`, `NpcRoutesTests`, `LayoutSaveTests`, updated built-scene tests, surface tests.
- `bash scripts/verify.sh`; then the AC10 tours.

## Human steps
None to build. Play one service after it lands; it should feel exactly like before.

## Open questions (recommended default in brackets)
1. NPC paths: NavMesh at runtime or the existing walker. [the existing straight-line walker plus a /sim grid path:
   deterministic, testable without Unity, and it shares the grid with rule 4; NavMesh only if tables need it later]
2. Minimum aisle: the human said about 0.9 m, feel.md 6 says 1.2 m. [0.9 m is the hard rule; 062b's ghost shows a soft
   "tight aisle" note under 1.2 m]
3. Storage format: JSON resource for the default, the save for the rest. [yes; no ScriptableObject layout assets]
4. Front-counter appliances: modules with their own counter body, or items standing on a counter. [modules; 039's
   segments already split the run this way, and it keeps every surface on one piece]
5. Positions off the 0.25 m grid in the default layout. [allowed; 062b snaps a piece only when it is moved]
6. What an invalid saved layout does. [fall back to the default with a feed line; no repair attempt]

## Implementation notes
Built by the Opus builder on 2026-11-11 as **Part A** (data, ids, spawn, save, parity) of the spec's own split. **Part B is not
built**: `LayoutRules`, `LayoutReport`, `LayoutProblem(Kind)`, `RoomDef`, `WalkGrid`, `GridPath`, `NpcRoute(s)`, the waypoint
walker and the AC2 rules tests, AC7, and AC8's "an overlapping layout falls back" case. The guests still use the scene's
`StreetEntry`, `QueueHead` and `PayHead` (the parity test pins them).

- **Migration proof.** Before any builder change, the unchanged builder rebuilt the scene in batch mode (main 075c101) and a
  first `LayoutExporter` (since rewritten) wrote `Tests/EditMode/Data/kitchen-snapshot.json` (18 stations, 27 surfaces with old
  index, piece, local, pose, size, top, spots; 9 item homes; 7 anchors) and `Resources/Layouts/default.json` (27 pieces, 9 homes,
  `nextId` 28). `DefaultLayoutParityTests` compares the offline-spawned default with it (1 mm, 0.1 deg, sizes, tops, spots, names).
- **Ids.** Station id = piece id = old index + 1 (ids 1-18); worktops 19-22 (old surface order), front counter runs 23-27 west to
  east (27 is a 90 mm filler between the dispenser and the serving counter: a run under 100 mm has no top, as 039's gaps).
- **Front counter (Q4).** Modules carry their own counter body named exactly `Front counter` (PlaceCoverage's exception matches
  it unchanged); module width = appliance + 2 x 20 mm. A 10 mm gap by the register gets no filler (under 20 mm).
- **Numbering (deviation).** A type placed more than once shows its number ("Stove 1", "Worktop 3", "Counter top 2"): the
  prefab marks it with `#`, and `PieceBehaviour` has a third SyncVar `_number` (byte) next to `PieceId` and `PosePacked`,
  because the client cannot know the rank. Worktop names lost their old surface index ("Worktop 5" is now "Worktop 3").
- **Spawning.** `SaveService.Begin` calls `LayoutSpawner.ServerBegin` (the save's layout, else the default) before the first
  player; nested NetworkObjects (baskets, griddle spots, the build spot, spit parts, station NOBs) spawn inside their piece
  prefab: no fallback was needed. Counters stretch at spawn (colliders, top, cabinet modules instantiated at run time).
  Items at the homes are spawned objects now (no scene cookware), so spec 045's "scene plates back to their start" happens by
  despawn and respawn.
- **Registry.** `LayoutRegistry` is a static per-process registry (both sides; `Version` invalidates the client's aim caches in
  PickupInteractor, VesselSteam's burners and TutorialTargets). CleaningService (its four stations), CustomerService (the register)
  and EconomyService (the menu board; it republishes the prices when the layout changes) look stations up by kind; ServeReward's
  counter and ServeRewardView's lookup use the station id.
- **Spawned items.** `LayoutSpawner.ServerBegin` despawns the pieces it spawned before, but it does not track the items it spawned
  at their homes yet (the KitchenService owns them once spawned): spec 062b, which changes the layout while a game runs, must decide.
- **Save v3.** `layout` part; `V2ToV3` loads default.json; the codec clamps through `LayoutCodec`; unknown types are kept and
  not spawned; a layout missing a required piece after `DefaultLayout.Backfill` falls back to the default with the feed line.
- **Builder.** `GrayboxSceneBuilder` keeps the station code (`BuildKitchenParts`, now internal) and its scene holds only the room
  shell and services; `PiecePrefabBuilder` cuts that kitchen into 19 prefabs (+ `PiecePrefabs.asset`). Closed covers are in the
  prefabs; a stove's cover spans what rests on both stoves.
- **Tests.** The 16 built-scene test files open through `LayoutScene`; assertion changes beyond index literals: FloorPlanTests'
  front-run count (2 runs became the 9 front counter bodies), RegisterSceneTests and MenuBoardTests (no serialized station
  fields: the registry), the save version (3) and `Revision` (5) re-pins. The test assembly now references FishNet.Runtime.
- **AC10 (visual check, 2026-11-11, `.verify/visual062a`):** look, walk, cook, tutorial, menu and corner all ok: tour and observer
  exit 0, 0 Exception lines, 0 crash log entries, 0 timeouts, 0 aim fallbacks. Each host logged "[Layout] spawned 27 pieces and 9
  items" (8 hosts). The joined client (the look tour joins the observer's host after it spawned the layout) put the knife on
  Counter top 1 spot 7 and Counter top 2 by right click, so the late joiner's surface ids, poses and counter lengths match the host's.
  The walk tour has no place step.
- **Follow-ups.** `ProtocolSurface.Revision` is 5; spec 054b may also bump it (merge: take the next number). DefaultPrefabObjects
  keeps main's order with the 19 pieces appended.

### 062a review fixes
1. PrefabRegistrationTests: every `PiecePrefabs` piece and item prefab is in DefaultPrefabObjects; the scene's spawner uses that
   table and the save service begins it.
2. LayoutSurfaceTests (AC9): the wire grows only by `PieceBehaviour._pieceId` (SyncVar Int32), `_posePacked` (Int64) and `_number`
   (Byte), no RPC, no broadcast; RequestPlace and RequestStation keep their int parameters; revision 5.
3. AC10 recorded above.
4. `nextId` clamps to 1..`ShopLayout.MaxPieceId` (LayoutCodec, noted); `DefaultLayout.Backfill` adds no piece past the limit.
5. The parity snapshot also holds main's 56 stock slots, 13 buttons, 11 nested objects and 6 body colliders (4 worktops, the 2
   front counter runs), added once from main 075c101's scene by `LayoutExporter.ExtendFromMainScene` (the existing entries are
   unchanged). Parity asserts each slot, button and nested object (inside its station's piece) within 1 mm, the worktop bodies, and
   that the 9 front counter bodies cover the 2 old runs at the same height and depth with no gap of 21 mm or more; stations' piece =
   old index + 1.
6. Each worktop's new name is pinned (Worktop 0, 1, 5, 9 became Worktop 1 to 4).
7. FloorPlanTests pins 9 front counter bodies; RegisterSceneTests checks CustomerService's registry lookups; a new LayoutSpawnTests
   case checks EconomyService, CustomerService and CleaningService find their stations through the registry.
8. `ServerBegin` logs why it spawns nothing; a rejected layout logs its notes and the layout itself (as JSON).
- Nits: the dead `IsSceneObject` branch and `_plateStarts` are gone from KitchenService (SceneStartSpots stays, with its tests);
  RectMm.Contains is tested in ShopLayoutTests; `PointMm` is removed (Part B adds it with `RoomDef`); the stale "no FishNet
  reference" comments are fixed.
- Checks: verify.sh green (EditMode 1514, sim 542, gateway 216); visual check look and cook in `.verify/visual062a2`: both ok, 0
  Exception lines, 0 timeouts, 0 aim fallbacks, "[Layout] spawned 27 pieces and 9 items" on both hosts, the joined client put the
  knife on Counter top 1.

## Blockers
(Builder fills this in after 3 failed attempts.)
