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
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
