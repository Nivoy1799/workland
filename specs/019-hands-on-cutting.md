---
id: 019
title: Hands-on cutting (knife in hand, cut with the mouse)
status: draft
owner: architect
builder: opus
---

# 019: Hands-on cutting

## Goal
Cutting stops being "press 3 for sticks". In first person you put the potato on the cutting board,
take the knife from the knife block, and cut it with the mouse: each downward drag is one cut,
the potato on the board falls apart into halves, then slices; turn it with the right mouse button
and cut again for sticks, turn again for dice. When you put the knife back and take the food,
you hold exactly what you cut. This is step 3 of the human's order from 2026-11-07: 017 food you
can see, 018 first person, **019 hands-on cooking**, 020 human characters.

It starts with the board only, because the human asked for this exact scene ("hold the potato,
take the knife, cut"). The pan, pot, plating and bowl stay as they are; they can get hands-on
versions later with the same pattern.

The server stays the authority (rule 6). The client only sends intents: "one cut stroke" and
"turn the food". It never sends a shape, a count, a position or a quality. The server counts the
strokes, decides the shape with a pure rule, applies the existing sim `cut` operation when the food
is taken, and publishes only the coarse look (D21).

## Depends on
- Specs 007 (stations, board, cut op), 013 (station contents), 016 (pattern: food stays in the
  station, the server applies the op when it is taken), 017 (FoodLook, FoodVisual, Food Kit),
  018 (first person, aim, held-item view anchor).
- Kenney Food Kit (CC0), already downloaded for spec 017 (`kenney_food-kit.zip`). This spec copies
  three more models from it: `cutting-board.fbx`, `knife-block.fbx`, `cooking-knife.fbx`
  (Open question 1).

## Scope
- `game/Assets/ThirdParty/Kenney/FoodKit/` (change): the three models above, unmodified; the
  existing `ASSETS.md` row covers the kit (update its file list if it has one).
- `game/Assets/_Project/Scripts/Cooking/`:
  - `CutProgress.cs` (new, pure): value type for one food on the board, see Design.
  - `CutRules.cs` (new, pure): `Stroke`, `Turn`, `ShapeOf`, the stroke-to-shape table.
  - `StationKind.cs` (change: `KnifeBlock`).
  - `StationRules.cs` (change: `KnifeStroke` and `KnifeTurn` option codes, prompts and keys hints
    for the board and the knife block, `IsInReach` unchanged).
  - `KitchenState.cs` (change: board sessions and knife holders, see Design).
  - `KitchenService.cs` (change: handle the board, knife block, stroke and turn requests; return the
    knife on disconnect or when the holder walks away).
  - `CookOutcome.cs` (change: new outcomes, see Design).
  - `FoodLookRules.cs` (change: `WithShape(FoodLook, byte shape)` for the board preview).
  - `PlayerTool.cs` (new, NetworkBehaviour on the player): `SyncVar<byte> Tool` (0 none, 1 knife),
    written by the server only.
- `game/Assets/_Project/Scripts/Net/PickupInteractor.cs` (change: first-person board and knife
  block keys, mouse strokes and turns while holding the knife; refuse pickup while holding the knife
  on the client side too, so the HUD can say why).
- `game/Assets/_Project/Scripts/Player/`:
  - `CutGesture.cs` (new, pure): turns mouse deltas and the left button into stroke events.
  - `HeldToolView.cs` (new, owner only): draws the knife at the spec 018 view anchor in first person
    and plays a short local chop motion on each stroke.
- `game/Assets/_Project/Scripts/Art/` (change: `ArtKey` entries for board, knife block, knife;
  a small `ToolVisual` for the knife above other players' heads, like held food).
- `game/Assets/_Project/Scripts/UI/HudView.cs` (change: prompts and toasts for the new outcomes).
- `game/Assets/_Project/Editor/ArtCatalogBuilder.cs`, `GrayboxSceneBuilder.cs` (change: board gets
  the cutting-board model and a content anchor on top; a knife block station next to it; the
  player prefab gets `PlayerTool`).
- `game/Assets/_Project/Resources/WhatsNew.json` (entry `spec-019`), tests in `Tests/EditMode/`.
- This spec (implementation notes).

## Out of scope
- Hands and arms models, character animation (spec 020). The knife floats at the view anchor like
  the held food in spec 018.
- Cut quality or skill from how well you cut (no client data may decide quality; Open question 7).
- Hands-on versions of the pan, pot, plating table or bowl; more tools (spatula, rolling pin).
- Real mesh slicing, physics, sound, particles.
- Changes to /sim (the existing `cut` operation and its `shape` choices are used as they are).
- Gamepad cutting beyond the simple mapping in Design.

## Design
### Flow in first person
1. Hold a potato (as today), look at the board, press **E**: the potato lies on the board.
2. Look at the knife block, press **E** with empty hands: you hold the knife.
3. Look at the board and cut: **hold the left mouse button and drag down** = one stroke. The knife
   in your view chops down once. **Right mouse button** (or R) turns the food on the board by 90 degrees.
4. Look at the knife block, press **E**: the knife goes back.
5. Look at the board, press **E** with empty hands: you hold the cut food (`"potato sticks"`).
   Taking uncut food gives it back unchanged.

Anyone may take the food from the board or cut it (with the knife), like the pan in spec 016.

### Stroke to shape (pure, `CutRules`)
`CutProgress { byte Shape; byte Turned; }` (Turned 0 or 1: turned since the last shape change),
starts at whole, not turned.

| state | stroke | turn |
|---|---|---|
| whole | halves | stays whole |
| halves | slices | stays halves |
| slices, not turned | stays slices | slices, turned |
| slices, turned | sticks (Turned back to 0) | stays |
| sticks, not turned | stays sticks | sticks, turned |
| sticks, turned | dice | stays |
| dice | stays dice | stays dice |

In words: 1 stroke halves it, a second stroke gives slices, turn and cut once more for sticks,
turn and cut again for dice. Turning whole or halved food only turns the model. The table is the
source of truth for tests. Strokes beyond dice do nothing. The shape names map 1:1 to
the sim `cut` op's `shape` choices (halves, slices, sticks, dice), so the result is exactly the
item the old digit keys made.

### Server state (`KitchenState`, pure, tested)
- Board session per board station: the registered food item + its `CutProgress`.
  - `TryPutOnBoard(foodId, boardIndex)`: board empty, food not plated (same rule the cut op checks),
    else `StationFull` / `SimRefused`. The food leaves the hands, like `TryPutOnHeat`.
  - `TryBoardStroke(boardIndex, clientId)`, `TryBoardTurn(boardIndex, clientId)`: need food on the
    board (`StationEmpty`) and that client holding this board's knife (`NoKnife`).
  - `TryPreviewBoardTake(boardIndex, out Item)`: whole = the item unchanged; else
    `ItemFactory.Apply("cut", shape)`. `ClearBoard(boardIndex)` after a successful hand-over (same
    order as `Heat()` in spec 016: hand over first, clear only on success).
  - `BoardLook(boardIndex)`: the food's `FoodLook` with the progress shape (`FoodLookRules.WithShape`).
- Knife per knife block (paired with its board by index in the scene builder):
  - `TryTakeKnife(blockIndex, clientId, handsEmpty)`: knife on the block and empty hands, else
    `KnifeTaken` / `HandsFull`.
  - `TryReturnKnife(blockIndex, clientId)`; `ReleaseKnivesOf(clientId)` on disconnect.
- New `CookOutcome`s: `NoKnife`, `KnifeTaken`, `HandsFull`, `PutKnifeBack` (pickup or other
  station use while holding the knife).

### Network
- No new RPC. The existing `RequestStation(int stationIndex, int option)` carries the new intents:
  `option 0` (E) on the board = put food or take food; on the knife block = take or return knife;
  `StationRules.KnifeStroke` and `StationRules.KnifeTurn` (constants above the digit range, e.g.
  10 and 11) on the board. `RpcSurfaceTests` and `MoveDataSurfaceTests` stay green unchanged.
- Server checks on every request: reach (as today), the rate limit as today, plus a stroke limit of
  one stroke per 0.2 s per player (extra strokes are ignored, not errors).
- `PlayerTool.Tool` (SyncVar, server writes) tells every client who holds a knife. The board's look
  goes through the existing `StationContents.Looks` SyncList, so every player sees the potato fall
  apart. The knife block shows its knife only while nobody holds it (`StationContents.Status` or a
  SyncVar on the block, builder's choice).
- The server returns the knife when its holder disconnects or stands more than 4 m from the block
  for 2 s (checked once per second, like the heat labels).
- Picking up food or using another station while holding the knife is refused with `PutKnifeBack`.

### Client (first person)
- `CutGesture` (pure): while the left button is held and the board is the aim target, sum the
  mouse's downward movement; at 40 counts it fires one stroke and needs the button released or an
  upward move of 20 counts before the next. The camera does not turn while the button is held over
  the board with the knife (`PlayerInputReader.SuppressLook`, as the emote wheel does), so the drag
  feels like cutting, not looking. Gamepad: right trigger press = one stroke, Y = turn.
- `HeldToolView` (owner): knife at the view anchor, blade down, 0.1 s down and 0.1 s up on each
  local stroke (visual only, before the server answers). Others see the knife at the hold point
  above your head (`ToolVisual`, driven by `PlayerTool.Tool`), like held food today.
- HUD prompts (under the dot):
  - board, holding food: "E · put on board"
  - board, food on it, holding knife: "drag down · cut   right click · turn"
  - board, food on it, empty hands: "E · take <name>"
  - knife block: "E · take knife" / "E · put knife back"
- Toasts: "Put the knife back first", "Someone else has the knife", "Take the knife from the block".

### Third person
- The board keeps today's digit keys 1-4 as a quick cut of the food in your hands (Open question 4).
  The knife block and strokes work only in first person, as interaction is first-person focused (spec 018).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes (Unity EditMode, check-assets with the three new models).
- [ ] AC2: `CutRules` tests cover every row of the table: 1 stroke halves; 2 and 5 strokes slices;
      2 strokes, turn, stroke = sticks; then turn, stroke = dice; turns on whole and halves change
      no shape; a stroke on sticks without a turn stays sticks; strokes on dice change nothing.
- [ ] AC3: `KitchenState` board tests: put, stroke 2x, take = the same item hash as
      `ItemFactory.Apply("cut", shape "slices")` on that potato (and the same for halves, sticks,
      dice); take without strokes returns the item unchanged; stroke without the knife = `NoKnife`;
      stroke on an empty board = `StationEmpty`; second food on a full board = `StationFull` and the
      food stays in the hands; plated food is refused.
- [ ] AC4: Knife tests: take with empty hands; take with food in hands = `HandsFull`; a second player
      gets `KnifeTaken`; return; `ReleaseKnivesOf` returns it; pickup while holding the knife =
      `PutKnifeBack`.
- [ ] AC5: `CutGesture` tests: 40 counts down = one stroke; holding and dragging 120 counts without
      a reset = one stroke; release or 20 counts up re-arms it; moving up or sideways only = none.
- [ ] AC6: Reflection: no ServerRpc gained a parameter (`RpcSurfaceTests`, `MoveDataSurfaceTests`
      green); `PlayerTool` syncs only a byte; `CutProgress` has only byte fields.
- [ ] AC7: Two clients on the dedicated server, screenshots: the board with the cutting-board model
      and the knife block; a potato on the board as halves or slices after strokes (server-driven,
      so the screenshot run may use a dev-only launch arg to queue strokes, see Test plan).
- [ ] AC8 (manual, human): in first person put a potato on the board, take the knife, cut it into
      sticks with the mouse, put the knife back, take the sticks, fry and serve them. A second
      client sees the potato fall apart and cannot take the knife meanwhile.
- [ ] AC9: `WhatsNew.json` entry `spec-019` with test steps.

## Test plan
- EditMode: `CutRulesTests`, `BoardSessionTests` (KitchenState), `KnifeTests`, `CutGestureTests`,
  surface tests extended.
- Screenshot run: Linux server in WSL, two Windows clients. Batch clients get no input, so the builder
  may add a dev-only, client-side launch arg (e.g. `-demo-cut sticks`, only in development builds)
  that sends the normal requests in order: take potato, put on board, take knife, strokes, turns.
  It must use the same RequestStation intents a player sends; no server-side shortcut.
- Human play-test (AC8).
- Command: `bash scripts/verify.sh` (Windows, Editor closed, `UNITY_PATH` set).

## Open questions (defaults in bold; the human answers before approval)
1. **Models:** copy `cutting-board`, `knife-block` and `cooking-knife` from the already downloaded
   CC0 Food Kit? **Default: yes.**
2. **Cutting gesture:** hold the left button and drag the mouse down for one stroke (a plain click
   does nothing)? **Default: yes, drag down.**
3. **Shapes by strokes:** the table above (1 stroke halves, 2 slices, turn + cut sticks, turn + cut
   dice)? **Default: yes.**
4. **Digit keys on the board:** keep 1-4 as a quick cut in third person only, and switch them off in
   first person so the knife is the way to cut there? **Default: yes.**
5. **Others see your knife** above your head like held food, until spec 020 brings hands?
   **Default: yes.**
6. **Knife goes back by itself** when you walk more than 4 m away for 2 s or leave the game?
   **Default: yes.**
7. **Skill:** cutting quality stays the same however you cut (no precision bonus) for now?
   **Default: yes; a skill idea needs its own spec, computed by the server, never from mouse data.**

## Human steps
- Answer the open questions and approve this spec.
- Play-test AC8.

## Implementation notes
(Builder fills this in.)

## Blockers
