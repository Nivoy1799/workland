---
id: 062b
title: Build mode in the world (close the shop, move pieces with the mouse, counters join, co-op with undo)
status: approved
owner: architect
builder: opus
---

> Approved by the human on 2026-11-11 night ("feel free to do everything") with the recommended defaults of its Open questions.

# 062b: Build mode

## Goal
While the shop is closed, the crew rearranges the kitchen in the world, with the same mouse controls as cooking (041):
left click picks a piece up, right click places it where the ghost shows, the wheel turns it. Pieces snap to a 0.25 m
grid and 90°; counter segments join into one worktop and appliances attach to a counter run. Several players can build
at once; the server validates every move against 062a's rules; each player can undo. "Your restaurant, your mix"
(pillar 4) becomes something you can see and touch.

**Integration spec** (/sim and /game). Netcode owner: Opus.

## Depends on
062a (layout data, ids, `LayoutRules`, `NpcRoutes`, `PosePacked`), 041 (mapping, ghost, `PlaceTurn` prompts), 039
(surfaces, `PlaceCoverage`), 053 (stage, autosave), 057 (stage 2). D31, D33.

## Scope
**/sim** `sim/Runtime/Layout/` (new files): `BuildSnap.cs` (grid and turns), `CounterRuns.cs` (which pieces join),
`AttachRules.cs` (appliances need a run with a counter), `LayoutEdit.cs` (pure apply: Move, Store, TakeOut; returns
the new layout and the inverse edit), `LayoutEditKind.cs`, `UndoHistory.cs` (per player, last 10). Changes:
`LayoutRules.Validate` gains rule 8 (attach); `PieceCatalog` gains a `storage_pallet` row (Floor, not movable, not
required). Tests in `sim/Sim.Tests~/Layout/`.

**/game** (under `game/Assets/_Project/`):
- `Scripts/Layout/` (new files): `ShopDoor.cs` (NetworkBehaviour, the open/closed state), `ShopDoorRules.cs` (pure),
  `BuildInteractor.cs` (NetworkBehaviour on the player: the one new ServerRpc and its answer), `BuildIntent.cs`
  (struct), `BuildService.cs` (server: claims, validation, apply, undo, items that travel, autosave), `BuildModeView.cs`
  (client: mode state, aim, prompts), `BuildControlRules.cs` (pure: 041 mapping in build mode), `BuildGhost.cs`
  (client: uses `Art/PlacementGhost`), `CounterRunView.cs` (client: hides seams between joined segments),
  `StoragePallet.cs` (the pallet piece where stored pieces wait).
- `Scripts/Layout/PieceBehaviour.cs` (change: `SyncVar<byte> ClaimedBySlot`, pose applied when `PosePacked` changes).
- `Scripts/Npc/Shift.cs`, `CustomerService.cs` (change: no new shift while the door is Closed; the clock holds at 08:00;
  `ApplyRoute` on opening).
- `Scripts/Net/PickupInteractor.cs` (change: idle in build mode), `Scripts/Player/PlayerInputReader.cs` (B, Ctrl+Z),
  `Scripts/Art/PlacementGhost.cs` (change: a piece mesh and a red tint).
- `Scripts/Save/SaveTrigger.cs` (`LayoutChanged`), `SaveService.cs`, `AutosaveRules.cs`.
- `Resources/Layouts/default.json` (add `door_sign` by the front door and `storage_pallet` by the back door, both
  flagged for `Backfill`); `Editor/PiecePrefabBuilder.cs` (their prefabs); `Scripts/Net/ProtocolSurface.cs` (+1).
- `Scripts/UI/ControlsHelp.cs` (F1 lines), `Scripts/Dev/BuildTourRunner.cs` (new), `scripts/visual-check.sh` (`build`).
- Tests in `Tests/EditMode/`; `Resources/WhatsNew.json`; this spec.

## Out of scope
Buying, selling and tiers (062c). The office terminal entry and its Services app (059 adds a row that calls the same
door and build entry). Walls, doors or the room shell. Free placement off the grid. Guest-area furniture (tables come
with stage 3). Gamepad bindings beyond 041's mapping.

## Design

### The shop door (open and closed)
- `ShopDoor.State`: `Open` (today's loop: services follow each other), `ClosingAfterService`, `Closed`.
- The `door_sign` piece by the front door is a station: left click toggles. Open: "Close after this service" (during a
  service) or Closed (between services). Closing: back to Open. Closed: Open, which starts the next service at once.
- While Closed: no shift opens, the in-game clock stays at 08:00, no hour passes (no demand, no upkeep, no rent). This
  follows "time runs only while playing" without new rules. A loaded save starts Open (today's behaviour).
- `ShopDoorRules.Next(state, serviceOpen, press)` and `AfterSummary(state)` are pure and tested.

### Entering and leaving
- Key **B** (rebindable later through 048) toggles build mode for the local player when: the door is Closed, the stage
  is 2 or higher (stage 0, the open kitchen, too, for dev and tours), and the hands are empty. Otherwise the HUD says
  why ("Close the shop first", "Put down what you hold"). 059 adds a terminal row that does the same.
- In build mode the aim targets pieces and the floor only; cooking inputs do nothing. Leaving releases a claim.

### Controls (041, the same meaning)
| Input | Build mode |
|---|---|
| Left click on a piece | pick it up (claim). On the storage pallet: take the next stored piece. |
| Right click | place the held piece at the ghost. On the storage pallet: store it. |
| Wheel | turn the held piece 90°. On the pallet with empty hands: cycle which stored piece is next. |
| Ctrl+Z | undo your last build action. |
| B or Esc | leave build mode. |
Prompts name the button: "Left click · pick up fryer", "Right click · place here   Wheel · turn", "Right click · store".

### Snap and attach (/sim, pure)
- `BuildSnap.Snap(def, aimXMm, aimZMm, turns, lengthMm)`: the footprint's min corner snaps to the 250 mm room grid;
  turns 0..3. A pose within 250 mm of a counter run's end with the same turns snaps flush to that end (magnet).
- `CounterRuns.Of(layout, catalog)`: InLine pieces with the same turns, on the same line, touching end to end, form
  one run (ordered, deterministic).
- Rule 8 (`AttachRules`): every appliance (`stove`, `fryer`, `griddle`, `sink`, `dispenser_module`, `spit_module`,
  `register_module`, `dish_return_module`, `serving_counter`) is in a run that holds at least one `worktop` or
  `front_counter`. Text: "Attach it to a counter".
- `CounterRunView` hides the touching end panels and draws one continuous top edge per run (graybox stand-in). Surfaces
  stay one per segment; with 250 mm modules their spot grids line up across seams.
- Cookware and tools keep 039's surface snapping on any counter; `PlaceCoverage` holds for any valid layout (test).

### Server flow (`BuildService`, every check on the server)
- `RequestBuild(BuildIntent intent, int request)`; `BuildIntent { byte Kind; int PieceId; long Pose; }` with Kind
  Claim, Release, Move, Store, TakeOut, Undo. The answer is a TargetRpc `BuildAnswer(int request, byte outcome)`
  (Ok, NotClosed, NotBuilding, Claimed, Invalid + problem kind, Busy, NothingToUndo).
- Claim: refused if another player holds it, if the piece is not `Movable`, or if its station is on (a burner, the
  fryer power, the spit knob: "Turn it off first"). One claim per player; released on Release, leaving the mode, a
  disconnect, or 60 s without input.
- Move: snap, build the candidate layout, `LayoutRules.Validate`; refused with the first problem. On success: write the
  layout, set `PosePacked` (clients move the piece), push the inverse onto the player's `UndoHistory`.
- **Items travel:** items on the piece's surfaces keep their surface id and spot and move with it. Loose items inside
  the new footprint move to the nearest free floor cell. A held item is never touched.
- Store and TakeOut: a stored piece leaves the layout (its items go to the nearest free worktop spot, else the floor);
  `Required` pieces cannot be stored. TakeOut puts the piece in the hands as a claim; it must be placed or stored again.
- Undo: applies the inverse if it is valid now, else "Something is in the way now". History is per player, last 10,
  cleared when the shop opens.
- Rate limit: the existing `RequestBucket`. Several builders at once: each move is checked against the current layout.
- Opening the shop: routes rebuilt (`NpcRoutes.Build`, then `CustomerService.ApplyRoute`), autosave `LayoutChanged` once.

### Prediction and ghost (feel.md 1, 4)
The client keeps its own `ShopLayout` from the pieces' poses and runs the same `BuildSnap` and `LayoutRules`. The ghost is
Sage when valid and Terracotta with the problem's text when not (and "Tight aisle" under 1.2 m). On right click the
piece moves at once on the client; if the server refuses, it slides back with the "nope" sound.

### Wire
One ServerRpc (`RequestBuild`), one TargetRpc (`BuildAnswer`), `ShopDoor.State` (SyncVar<byte>), `PieceBehaviour.ClaimedBySlot`.
`ProtocolSurface.Revision` + 1; surface tests change only by these lines.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes.
- [ ] AC2: /sim: `BuildSnap` literals (grid, turns, magnet); `CounterRuns` (touching, gap, other turns); rule 8 pass
      and fail; `LayoutEdit` apply then inverse gives the original layout; `UndoHistory` keeps 10.
- [ ] AC3: `ShopDoorRules`: every state and press; Closing becomes Closed at the summary end; while Closed no hour passes
      in a server test over 600 simulated seconds.
- [ ] AC4: `BuildService` server tests: claim by two players; move into overlap refused with the problem; a hot stove
      refuses the claim; items on a moved worktop keep their surface id and spot; a stored required piece is refused;
      undo after another player blocked the spot is refused; a disconnect releases the claim.
- [ ] AC5: `PlaceCoverageTests` and the reach test pass on the default layout and on 3 valid layouts made by a seeded
      generator (moves of the back-line modules).
- [ ] AC6: `BuildControlRules`: each input gives the table's action; right click never picks up.
- [ ] AC7: Surface tests: only the listed lines change; `Revision` + 1.
- [ ] AC8: `--tours build` (host and one client): close the shop, both enter build mode, the host moves the fryer one
      module along the back line, the client swaps two worktops, a worktop is stored and taken out again, the client
      undoes once, the shop opens, a guest walks the rebuilt route, fries from the moved fryer are served. 0 Exception lines.
- [ ] AC9: `--tours look,walk,cook,menu,corner,tutorial` still pass (the door starts Open).
- [ ] AC10 (manual, human): rearrange the kitchen with a friend; it feels like moving things, not filling a form.
- [ ] AC11: `WhatsNew.json` entry; F1 help lists B and Ctrl+Z.

## Test plan
- `sim/Sim.Tests~/Layout/`: `BuildSnapTests`, `CounterRunsTests`, `AttachRulesTests`, `LayoutEditTests`, `UndoHistoryTests`.
- `Tests/EditMode/`: `ShopDoorRulesTests`, `BuildServiceTests`, `BuildControlRulesTests`, `CoverageOnLayoutsTests`,
  surface tests. Commands: `bash scripts/verify.sh`; `bash scripts/visual-check.sh --tours build --out .verify/visual062b`.

## Human steps
AC10 play-test; answer or accept the open questions.

## Open questions (recommended default in brackets)
1. Earliest stage for build mode. [stage 2, the rented restaurant; the food corner is a fixed cart. Stage 0 for dev]
2. How the shop closes. [the door sign: "close after this service"; the default loop stays as today unless someone closes]
3. Where stored pieces wait. [an in-world storage pallet by the back door, so no new keys or menus are needed]
4. Undo depth and scope. [10 actions per player, cleared when the shop opens]
5. Items on a piece that moves. [they travel with it; loose floor items under the new footprint are pushed aside]
6. Can a player move a piece another player placed? [yes; only a held claim is locked]
7. Claim timeout. [60 s without input]

## Implementation notes
Built by the Opus builder on 2026-11-12 as **Part A** of the brief's split (move and turn with validation, undo, the save; the door,
store and take out, the magnet), on spec/062b-build-mode after merging main with 059 (Revision 8, save v5). **Part B is not built**:
rule 8 (`AttachRules`, "Attach it to a counter"), `CounterRunView` (hidden seams), AC5's seeded-generator coverage test, and AC8's
"fries from the moved fryer are served" (the tour serves no dish).

- **Wire (Revision 9, 059 took 8).** `BuildInteractor.RequestBuild(BuildIntent, int)` and `BuildAnswer(conn, int, byte)` (the byte
  packs the outcome and a refused move's problem kind, `BuildDesk.Pack`), `ShopDoor.State`, `PieceBehaviour._claimedBySlot`. The
  player prefab gains `BuildInteractor` as its **last** behaviour (the others keep their indexes); door_sign and storage_pallet are
  new spawned pieces (DefaultPrefabObjects: main's order plus those two). A piece's `PosePacked` now changes while spawned (clients
  re-apply on change). Slot = client id + 1.
- **Server.** `BuildService` (added at runtime by `LayoutSpawner.Awake`, no scene change) gates every intent (rate limit, door
  Closed, stage 0 or 2+, empty hands) and asks the pure `BuildDesk` (claims, `BuildSnap.Pose`, "no new problem" validation: problems
  the layout already had do not count, store, take out, per-player `UndoHistory`, 60 s lapse). An undo that no longer fits is
  refused **and kept** on the history (a later try can work). Opening the shop releases every claim, clears the histories and saves
  once (`SaveTrigger.LayoutChanged`) if the layout changed.
- **Items travel.** Items lying on a moved piece keep their pose in the piece's frame (`PickupService.ServerCarry`: same surface id
  and spot). Loose floor items under the new footprint go to the nearest free walk-grid cell. A stored piece's items go to the
  nearest free worktop spot (`ServerPutOnSpot`), else the floor. Held items are never touched.
- **Store and take out (deviation).** Stored pieces are despawned; `ShopLayout` gains `StoredPieces` (records: type, length, the
  pose they were stored from) because 062a's `Stored` ids lost the type. A taken-out piece spawns on the pallet, claimed, until it
  is placed (or stored again / released: back into storage). The wheel on the pallet picks the n-th stored piece (the client cannot
  see the stored list: no synced list, to keep AC7's lines).
- **Pallet (deviation).** `storage_pallet` is a **Yard** row (the spec said Floor): the room has no free floor by the back door and
  rule 1 keeps floor pieces inside. At (-0.9, 14.3) in the yard. The door sign is on the right side wall near the front at
  (14.785, 12.80), facing the kitchen; `door_sign` is now `HasStation` (rule 4 checks it). Both added to default.json (ids 30, 31,
  nextId 32) and to older saves by **save v6** (`V5ToV6`, `AddedInV6`).
- **Snap.** Grid from the room's floor corner; a footprint poking out by at most one step is pushed back flush; wall pieces snap to
  the wall; the magnet joins runs with `CounterRuns.JoinGapMm` = 50 (the default kitchen keeps 20-40 mm between modules, so its back
  line and front counter are one run each). Without the magnet nothing in the off-grid default could be moved back into line.
- **Clock.** `EconomyService.ClockHeld` (new, set by `CustomerService` from the door) stops the hours while closed at 08:00;
  `Shift.OnClock(..., mayOpen)` opens no shift while Closed. The tour host starts Closed with `-tour-build` (dev builds only).
- **062a review items.** Rule 4: a wall or overhead station is unreachable over a `Tall` floor piece (new `PieceDef.Tall`, the fridge
  and the dry shelf). `LayoutReport` sorts by kind, piece, other (stable). `WalkGrid` refuses over 1M cells. `DefaultLayout.Band`
  removed. `ServerBegin` tracks its items and despawns them on a re-begin (held ones stay). `NpcRoute.LeaveFrom(start)`: each
  leaving guest gets its own grid path. The delivery spots follow the piece they lie on (`SupplyService`).
- **Client.** `BuildModeView` (added to the owner by `BuildInteractor`): B / Esc / Ctrl+Z (fixed keys in `PlayerInputReader`), the
  camera-centre aim, the ghost (`BuildGhost` over `PlacementGhost`, Mint / Terracotta with the reason, "Tight aisle" under 1.2 m),
  the predicted move with a jump back and the nope sound on refusal. Prompts and refusals are IMGUI stand-ins until the HUD pass.
  `PickupInteractor` idles in build mode. The door sign is pressed like the goal board (first person) or with E.
- **Outside the listed files (same folders):** `EconomyService`, `KitchenService` (`StationIsOn`, the door press), `StationKind`,
  `StationRules`, `ShopStageRules`, `InteractionRules`, `SupplyService`, `PickupService`, `ShopSave`, `SaveMigrations`, the art key and
  catalog builder (the door sign's graybox), `GrayboxSceneBuilder` (one line), Dev `TourRules`/`TourRunner`.
- **Checks (2026-11-12).** verify.sh green: EditMode 1650/1650, sim 892/892, gateway 216/216. BuildAll ok. `visual-check.sh --tours
  look,cook,restaurant,build --out .verify/visual062b`: all ok, 0 Exception lines, 0 crash entries, 0 timeouts, 0 aim fallbacks; every
  host "[Layout] spawned 31 pieces and 9 items". Build tour cells: 03 the worktop by stove 2 stored, 04 the Mint ghost of the other
  worktop there, 05 it moved with its board and knife on it, 06-07 the stored one taken out to the fridge side, 08 the undo, 09 the
  Terracotta ghost in the aisle ("Too close to the front counter: aisles need 0.9 m"), 10 the door opened, 11 a guest at the queue.
- **Follow-ups.** Part B above; ChangeLog action 5 (059 reserved it for the door) is not used yet; the taken-out piece on the pallet
  keeps its stations registered (usable while held); stored pieces lose their number ("Worktop" not "Worktop 3") when spawned again.

### 062b review fixes
Built 2026-11-12 on top of ef6cc8b.
1. **The door sign cannot be stored** (`BuildDesk.NeverStored`: refused as Required); a test.
2. **Tools of a stored worktop are re-homed.** `LayoutEdit` can carry the layout's new item homes (its inverse the old ones);
   `BuildService` plans each home on the stored piece onto the nearest free worktop spot that is no other home, moves the tool there,
   and an undo moves it back. Tests: the save round trip keeps every home on a placed piece; the undo restores the homes.
3. **Items travel on the host.** `BuildService.Carry` starts from the piece's synced pose, not its transform; the host's client no
   longer predicts its own move. The test drives the real `Carry` after a prediction.
4. **One rate limit and spam kick** for both interactors: `Net/ServerRequestGate` (PickupInteractor's code moved there, not copied).
5. **A piece taken out of storage is parked** (`PieceBehaviour.Parked`, from the synced pose over a pallet): its stations and surfaces
   are not registered until it is placed; going back to storage moves its items off first. Heat (`StationIsOn`) is re-checked on a move
   and an undo.
6. A disconnect forgets the slot's history (`BuildService.ServerPlayerLeft`, tested through the service).
7. The ghost is recomputed only when `LayoutRegistry.Version`, the held piece, its turns or its grid place change; the prompt only when
   its words change.
8. Items on a piece come from the spot table's surface id (the home items enter the spot table at spawn), not geometry.
9. The clock hold is `ShopDoorRules.ClockHeld` (tested), used in both places in `CustomerService`.
- Nits: take out skips fixed types; moves and undos use plain `Validate`; one walk grid per store; delivery spots fall back to their own
  place (on the floor) while worktop 19 is stored; the 059 re-pin comment is back; the door is logged at the office terminal
  (ChangeLog action 5: "Mira: closed the shop").
- Not done: a host-side move in the build tour (the tour's host is the observer window; not cheap); the carry test covers the host case.
- Checks: verify.sh green (EditMode 1658/1658, sim 893/893, gateway 216/216); BuildAll ok; `visual-check.sh --tours build,restaurant
  --out .verify/visual062b2`: both ok, 0 Exception lines, 0 timeouts, 0 aim fallbacks.

## Blockers
(Builder fills this in after 3 failed attempts.)
