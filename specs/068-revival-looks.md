---
id: 068
title: Revival in the game (server levels, save, the district component that swaps lights, signs, props, robots and people; stand-in looks for Station Square and Main Street)
status: approved
owner: architect
builder: sonnet       # Part A's synced state is reviewed by the netcode owner (opus)
---

> Approved by the human on 2026-11-11 night ("feel free to do everything"; the Haven Nine town plan is approved as D41)
> with the recommended defaults of its Open questions. Work package W6 of docs/town-plan.md section 10.

# 068: Revival looks

## Goal
The players see the town come back (D43). The server runs spec 065's revival from the meals the crew serves, saves it
in the host's save and syncs one small value; on every client a component per district switches its sets of lights,
signs, shutters, litter, plants, robots and people to the district's level. The building shells never change ("swaps,
not rebuilds"). Station Square and Main Street get all four looks with stand-in sets until the Blender art pass; every
other district gets the component with empty sets.

## Depends on
**Specs 065 and 067 built**; 053/057 (save, `ShopStage`, the move), 062a (pieces stay untouched), 048; D27, D32, D43,
D44; docs/town-plan.md 4.1-4.3; docs/art-direction-v2.md sections 6, 7 and 9 (draft: question 29). 069 reads this
spec's levels for the Dome's ribs.

## Scope
Only `/game` (plus the tour hook in `scripts/visual-check.sh`). Paths under `game/Assets/_Project/`.
- **Part A, server state:** `Scripts/Revival/` (new, namespace `Worktown.Game.Revival`): `RevivalRules.cs` (pure: the
  meal's district, crew size, texts), `RevivalPacking.cs` (pure), `RevivalService.cs` (server NetworkBehaviour),
  `RevivalLevels.cs` (reader on server and clients, `LevelOf(districtId)`, `Changed` event).
  Changes: `Scripts/Npc/CustomerService.cs` and `Scripts/Shop/RegisterFlow.cs` (both sale paths raise one server event
  `SaleRecorded(int stars)`), `Scripts/Climb/ShopStage.cs` (a `StageEntered` server event at the move),
  `Scripts/Save/ShopSave.cs`, `ShopSaveCodec.cs`, `SaveMigrations.cs` (next free version), `SaveService.cs`,
  `Scripts/UI/ShiftSummaryView.cs` (the revival line), `Scripts/Net/ProtocolSurface.cs` (`Revision` + 1),
  `Scripts/Net/Rules/LaunchArgs.cs` (dev flag `-revival <0..3>`).
- **Part B, looks (client):** `Scripts/Revival/`: `LookTable.cs` (pure: art direction section 9 as numbers),
  `SignState.cs` (pure), `DistrictLook.cs`, `LookSet.cs`, `LampLook.cs`, `SignLook.cs`, `RobotLook.cs`,
  `AmbientPeople.cs`. `Editor/RevivalLookBuilder.cs` (new: the stand-in sets), `Editor/TownGrayboxBuilder.cs` (change:
  calls it), the regenerated scene.
- **Dev and tests:** `Scripts/Dev/RevivalTourRunner.cs` (new), `scripts/visual-check.sh` (change: `revival` tour);
  `Tests/EditMode/`: `RevivalRulesTests`, `RevivalPackingTests`, `RevivalServiceTests`, `RevivalSaveTests`,
  `LookTableTests`, `SignStateTests`, `BuiltRevivalTests`; updated surface tests; `Resources/WhatsNew.json`; this spec.
- No change in `/sim`, in 062a's pieces, the kitchen interior or the 058 rival fronts' logic.

## Out of scope
Final art (the art pass); looks for districts other than Station Square and Main Street (later specs); Dome rib
lighting and light moods (069); story text, robot screen lines and NPC lines (070); applying 065's footfall bonus or
ingredient ceiling to demand and suppliers (a later spec); deliveries; robot repair (D47, after Early Access); the
Low Ring's look and its NPC arcs.

## Design

### Part A: the server state
- `RevivalService` holds 065's `RevivalState` (catalogs: `TownCatalog.CreateDefault()`,
  `RevivalCatalog.CreateDefault(town)`). At stage 0 (the open kitchen, tours, tests) it adds nothing.
- **Meals:** on `SaleRecorded(stars)` at stage >= 1: `AddMeals(state, catalog, ShopDistrict, stars, 1, players)`.
  `ShopDistrict` = the district of 064's `startPlaceId` (`s1`, so `main_street`). `players` = players connected at the
  service start (052b open question 5), fixed for that service.
- **Stage:** on `StageEntered(stage)`: `EnterStage`. A level never drops (065); loading only clamps.
- **Level up:** a serve-feed line from `RevivalRules.LevelUpText`: "Main Street is coming back: Patched" (the level
  names Grey, Patched, Lively, Thriving); the next autosave stores it (no extra save trigger).
- **Synced** (server writes, no RPC): `SyncVar<int> LevelsPacked` (2 bits per district in 064 order, 12 districts =
  24 bits; `RevivalPacking`), `SyncVar<short> ShopProgressPermille` (the shop district's ppm / 1000). Written only on a
  change. `ProtocolSurface.Revision` + 1; surface tests change only by these two lines.
- **Shift summary:** one line "Main Street: Patched · 40 % to Lively" (Thriving: "Main Street: Thriving").
- **Dev:** `-revival n` (development builds and the Editor only) sets every district to level n at server start and
  turns points off; never saved (the save keeps its own values).
- **Save** (next free format version at build time; 057/058/062a renumber mechanically): `revival { districtIds[],
  levels[], progressPpm[] }` from 065's `SavedRevival`. Migration from the previous version: 065's start state. Codec
  clamps as 065's `FromSaved`; unknown district ids are kept in the file but ignored.

### Part B: the looks (`DistrictLook`, client side; the server sends only levels)
- One `DistrictLook` per 064 district, with four `LookSet`s (0..3). A `LookSet` lists GameObjects per element: lamps,
  signs, shutters, litter, plants, robots, people. On `RevivalLevels.Changed` (and at start) the component activates the
  objects of its level and deactivates the rest. Lamps and lit signs fade in over 1.5 s (docs/feel.md: nothing pops);
  props swap at once. The same object may be in several sets (a lamp post that exists at every level).
- **What each level shows** (`LookTable`, pure, from art direction v2 section 9; numbers per Main Street segment of 40 m):

| Element | 0 Grey | 1 Patched | 2 Lively | 3 Thriving |
|---|---|---|---|---|
| Lamps | half dead, 1 flickering | all on | on + string lights | on + string lights + festoons |
| Signs (`SignState`) | Dead, about 1 in 4 letters dark | Flicker | Lit | Lit + one new neon |
| Shutters | 3 in 4 down | half | 1 in 4 | none, plants on sills |
| Litter, bins | 6 litter props, bins overflowing | 2, bins emptied | 0, a little patina | 0 |
| Plants | dead stalks | potted plants by doors | beds replanted | full beds, flowers |
| Robots | slumped (tilted 35 degrees, head down, no light) | 1 in 3 moving | all working | working, polished |
| People | 3, listless (sitting, staring) | 6, talking pairs | 12, walking and talking | 20, some groups |
- `SignState.For(level)`: Dead (palette colour at 25 % brightness, no emissive), Flicker (a seeded on/off pattern of
  0.1-0.6 s steps from the sign's id), Lit (emissive). `SignState.DarkLetters(string text, int seed)`: which letters stay
  dark at Dead, about 1 in 4, never a space, the same for the same input on every client ("HA EN NINE"). The text never
  changes, only its state (art direction 7); 070 asks this for its sign holders.
- **Stand-ins** (`RevivalLookBuilder`, until the art pass): lamps are the existing street-light art plus simple posts;
  signs are boards with 3D text (district names only; 070 brings the real texts); shutters and litter are flat boxes;
  plants are cones and cylinders in `Leaf` and `Sage`; robots are 1950s stand-ins (a capsule, a dome head, one tube arm)
  in `Steel` and `Cream`; people are 024 characters in idle loops, client only, not networked, animation culled beyond
  30 m. Today's street lamps on the shop pad join Main Street's lamp set (a component is added, nothing moves).
- **Always warm:** the shop, its kitchen and 062a's pieces are in no set; hand-made places stay lived-in at every level
  (art direction 6).
- **Respect (D44):** no prop shows drug use; listless people sit or stare, nothing more. The Low Ring look is later.
- **Colours** only from `Palette` (D32). If question 29 (art direction v2) is approved before the build, the builder
  adds its emissive entries (`LampWarm`, `NeonCherry`, `NeonTeal`) to `Palette.cs` and `Palette.uss`; otherwise lamp glow
  uses `Mustard` and lit signs `Cream`, both through one `RevivalLookPalette` map so the art pass changes one file.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; `git diff --stat main -- sim` is empty.
- [ ] AC2: `RevivalRules` and `RevivalService` (server tests): a 4-star sale at stage 1 adds 065's points to
      `main_street`; stage 0 adds nothing; crew size is taken at the service start and a join during the service does
      not change it; the move to stage 2 calls `EnterStage` once; a level-up raises exactly one feed line.
- [ ] AC3: Never back: after a level-up, 50 one-star sales and a crew change keep the level; a load never lowers it.
- [ ] AC4: `RevivalPacking` round-trips every district at every level; `LevelOf` on a client equals the server's.
- [ ] AC5: Save: a previous-version fixture migrates to 065's start state; a full revival part round-trips equal;
      out-of-range values clamp; an unknown district id is kept but ignored; version 99 is `NewerVersion`.
- [ ] AC6: `LookTable` matches the table above (literals per level); `SignState.DarkLetters` is deterministic, never
      darkens a space and darkens 20-30 % of letters over a 200-letter fixture.
- [ ] AC7: Built scene: one `DistrictLook` per 064 district; `station_square` and `main_street` have four non-empty
      sets; no `LookSet` holds a massing block, the shop, a 062a piece or a border piece (swaps, not rebuilds); every
      material colour comes from `Palette`.
- [ ] AC8: Surface tests change only by `LevelsPacked` and `ShopProgressPermille`; no new RPC; `Revision` + 1.
- [ ] AC9: `--tours revival`: with `-revival 0`..`3`, Main Street and Station Square from the same two cameras at each
      level (8 frames, art direction open question 4); then a run without the flag where a forced level-up (a test
      save one meal short of Patched) shows the feed line and the lamps fading in. 0 Exception lines. The existing
      `look,walk,cook,menu,corner` tours pass.
- [ ] AC10: Budget: at level 3 no more than 40 animated stand-in people within 30 m of any point of Main Street; the
      host frame time p95 at level 3 is within 10 % of level 0 (tour metric).
- [ ] AC11: `WhatsNew.json` entry; every new string passes the D27 word test and names no real place or era (D28).

## Test plan
- EditMode: the pure tests above; `RevivalServiceTests` drives `RevivalService`'s pure core with fake sale and stage
  events (as 053's `SaveServiceTests`); `BuiltRevivalTests` opens the scene through `LayoutScene`.
- Fixtures as string constants: previous-version save, full revival part, out of range, unknown id, version 99.
- Commands: `bash scripts/verify.sh`; `bash scripts/visual-check.sh --tours revival --out .verify/visual068`, then
  `--tours look,walk,cook,menu,corner`.

## Human steps
Look at the eight revival frames and say whether the four levels read apart. The real sets come with the art pass.

## Open questions (recommended default in brackets)
1. Where meals count. [the shop's district (`s1`, Main Street) for every sale; deliveries count at their address once
   delivery exists]
2. Crew size for the goals. [players at the service start, as 052b]
3. Showing progress. [the shift summary line only; no HUD bar]
4. The level-up moment. [at once, lights fade in over 1.5 s, one feed line; saved with the next autosave]
5. Dev override. [`-revival n` in development builds only, never saved]
6. Today's street lamps. [join Main Street's lamp set; the shop interior never changes]
7. Stand-in people. [024 characters, idle, client only]
8. Colours before question 29. [existing palette through one map, swapped by the art pass]

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
