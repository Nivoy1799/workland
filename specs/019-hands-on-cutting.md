---
id: 019
title: Hands-on cutting and station buttons (first person without number keys)
status: approved
owner: architect
builder: opus
---

# 019: Hands-on cutting and station buttons

## Goal
In first person the kitchen is used with your hands and eyes, not with number keys. You put the
potato on the cutting board, pick up the knife (a free item you can carry around like food), and
cut with the mouse: each downward drag is one cut, the potato on the board falls apart into halves,
then slices; turn it with the right mouse button and cut again for sticks, turn again for dice. The
stoves get real buttons: look at the switch and press E to turn the stove on or off, look at the
pan's mode button to switch fry and sauté. This is step 3 of the human's order from 2026-11-07:
017 food you can see, 018 first person, **019 hands-on cooking**, 020 human characters.

Changed by the human on 2026-11-07 after the first draft: first person uses **no number keys** at
all (buttons on the machines instead), and the knife is a **free item**, usable only while food lies
on a cutting board.

The server stays the authority (rule 6). The client only sends intents: "press this button", "one
cut stroke", "turn the food". It never sends a shape, a count, a position or a quality. The server
counts the strokes, decides the shape with a pure rule, applies the existing sim `cut` operation
when the food is taken, and publishes only the coarse look (D21).

## Depends on
- Specs 003 (pickups), 007 (stations, board, cut op), 013 (station contents), 016 (stove on/off,
  fry/sauté, food stays in the station and the op runs when it is taken), 017 (FoodLook,
  FoodVisual, Food Kit), 018 (first person, aim, held-item view).
- Kenney Food Kit (CC0), already downloaded for spec 017. This spec copies two more models from it:
  `cutting-board.fbx` and `cooking-knife.fbx` (Open question 1).

## Scope
- `game/Assets/ThirdParty/Kenney/FoodKit/` (change): the two models above, unmodified; update the
  kit's `ASSETS.md` row if it lists files.
- `game/Assets/_Project/Scripts/Cooking/`:
  - `CutProgress.cs` (new, pure): value type for the food on a board, see Design.
  - `CutRules.cs` (new, pure): `Stroke`, `Turn`, the stroke-to-shape table.
  - `StationButton.cs` (new, MonoBehaviour, data only): marks a collider as a button of a station
    and names its option (`StationRules.StoveSwitch` or `StationRules.PanMode`).
  - `Knife.cs` (new, marker component on the knife's NetworkObject; no rules).
  - `StationRules.cs` (change: `KnifeStroke` and `KnifeTurn` option codes; prompts for buttons,
    board and knife; first-person key hints without digits).
  - `KitchenState.cs` (change: board sessions, see Design).
  - `KitchenService.cs` (change: board put/take, strokes and turns with a held knife; respawn a
    lost knife).
  - `CookOutcome.cs` (change: `NoKnife`, `NothingToCut`).
  - `FoodLookRules.cs` (change: `WithShape(FoodLook, byte shape)` for the board preview).
  - `StationContents.cs` (change: one `SyncVar<byte> StoveFlags` (bit 0 on, bit 1 sauté) so button
    lights need no string parsing).
- `game/Assets/_Project/Scripts/Net/`:
  - `PickupInteractor.cs` (change: first person has no digit keys; E on a button, the board and the
    knife; mouse strokes and turns while holding the knife over a board with food).
  - `Rules/AimRules.cs`, `Rules/AimKind.cs`, `Rules/AimHit.cs` (change: a `Button` kind that carries
    its station and option; a button wins over its station when the ray hits it first).
  - `PickupService.cs` (change only if the knife needs a case; it is a normal pickup).
- `game/Assets/_Project/Scripts/Player/`:
  - `CutGesture.cs` (new, pure): turns mouse deltas and the left button into stroke events.
  - `HeldItemView.cs` (change: also draws a held knife in view) and a local chop motion on each
    stroke (new small `ChopMotion.cs` or inside `HeldItemView`, builder's choice).
- `game/Assets/_Project/Scripts/Art/` (change: `ArtKey` entries for the cutting board and the knife;
  a `StoveButtonVisual` that colours the switch and mode buttons from `StoveFlags`).
- `game/Assets/_Project/Scripts/UI/HudView.cs` (change: prompts and toasts below).
- `game/Assets/_Project/Editor/ArtCatalogBuilder.cs`, `GrayboxSceneBuilder.cs` (change: board gets
  the cutting-board model and a content anchor; one knife per board as a scene NetworkObject lying
  next to it; switch and mode buttons on the pan's stove, switch on the pot's stove).
- `game/Assets/_Project/Resources/WhatsNew.json` (entry `spec-019`), tests in `Tests/EditMode/`.
- This spec (implementation notes).

## Out of scope
- Hands and arms models, character animation (spec 020). The knife floats at the view anchor like
  held food in spec 018.
- Cut quality or skill from how well you cut (Open question 6).
- Hands-on versions of plating, the bowl, the pan's stirring; more tools (spatula, rolling pin).
- Real mesh slicing, physics, sound, particles.
- Changes to /sim (the existing `cut` op and its `shape` choices halves, slices, sticks, dice).
- Third-person changes: third person keeps today's keys, including the digits (Open question 3).
- Quick chat (T + digits) and the emote wheel (G): social keys, not station keys, they stay.

## Design
### First person: no number keys
| Station | Before (digits) | Now in first person |
|---|---|---|
| Board | 1-4 cut the food in your hands | put food on the board (E), cut it with the knife, take it (E) |
| Pan | 1 on/off, 2 fry/sauté | look at the **switch** + E; look at the **mode button** + E |
| Pot | 1 on/off | look at the **switch** + E |
| Crate, counter, plating table, bowl, pan/pot food | E | unchanged, E |

`PickupInteractor` ignores digit keys for stations while in first person. Buttons are small
colliders on the stove front with a `StationButton` (station index + option). The aim ray hits the
button, and E sends the existing `RequestStation(stationIndex, option)` with that option, so the
server path is exactly spec 016's. Button look: the switch glows green when on, dark red when off;
the mode button shows "fry" or "sauté" (small label), both from `StoveFlags`.

### The knife (a free item)
- One knife per board, spawned with the scene next to the board. It is a normal pickup
  (`Pickup` + `Knife` marker + its model): pick it up with E, carry it anywhere, drop it with E like
  food. Others see it at your hold point like held food; you see it in your view (spec 018 view).
- One item in the hands at a time, as today: holding the knife you cannot pick up food, and stations
  refuse it as `NotFood` (existing check), with the toast "That's not food".
- **The knife only cuts** while you hold it, aim at a board **with food on it**, and are in reach.
  Anywhere else the drag does nothing and the camera turns as usual.
- Lost knife: if a board's knife has been out of the world (below -10 m) or not existed for 5 s, the
  server spawns a new one next to that board (Open question 5). There are never more knives than
  boards.

### Flow at the board (first person)
1. Holding a potato, look at the board: "E · put on board". The potato lies on the board.
2. Look at the knife next to it: "E · take knife".
3. Look at the board: "drag down · cut   right click · turn". **Hold the left mouse button and drag
   down** = one stroke; the knife in your view chops once. **Right mouse button** turns the food 90°.
4. Look at the floor or the counter top and press E: the knife is put down (normal drop).
5. Look at the board with empty hands: "E · take potato sticks". Taking uncut food gives it back
   unchanged. Anyone may take food from a board or cut it, like the pan in spec 016.

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

In words: 1 stroke halves it, a second stroke gives slices, turn and cut once more for sticks, turn
and cut again for dice. Turning whole or halved food only turns the model. The shape names are the
sim `cut` op's `shape` choices, so the result is exactly the item the digit keys make today.

### Server state (`KitchenState`, pure, tested)
- Board session per board: the registered food item + its `CutProgress`.
  - `TryPutOnBoard(foodId, boardIndex)`: board empty and food not plated, else `StationFull` /
    `SimRefused`; the food leaves the hands (like `TryPutOnHeat`).
  - `TryBoardStroke(boardIndex)`, `TryBoardTurn(boardIndex)`: `NothingToCut` on an empty board.
    `KitchenService` checks first that the player holds a knife (`NoKnife` otherwise) and is in reach.
  - `TryPreviewBoardTake(boardIndex, out Item)`: whole gives the item unchanged, else
    `ItemFactory.Apply("cut", shape)`. Hand over first, `ClearBoard` only on success (spec 016 order).
  - `BoardLook(boardIndex)`: the food's `FoodLook` with the progress shape.
- New `CookOutcome`s: `NoKnife`, `NothingToCut`.

### Network
- No new RPC. `RequestStation(int stationIndex, int option)` carries every intent: buttons use the
  existing `StoveSwitch` / `PanMode` options; `option 0` (E) on the board puts or takes food;
  `StationRules.KnifeStroke` and `KnifeTurn` (constants above the digit range, e.g. 10 and 11) on
  the board. `RpcSurfaceTests` and `MoveDataSurfaceTests` stay green unchanged.
- The server also accepts digits from third-person clients as today; it does not know or trust the
  view mode. "No digits in first person" is a client input rule, not a security rule.
- Server checks per request: reach and rate limit as today; strokes at most one per 0.2 s per player
  (extra strokes ignored, not errors); stroke and turn need a held knife.
- The board's look goes through the existing `StationContents.Looks`, so everyone sees the potato
  fall apart. `StoveFlags` syncs the button lights.

### Client (first person)
- `CutGesture` (pure): while the left button is held, the knife is held and the aim target is a board
  with food, sum the mouse's downward movement; at 40 counts fire one stroke, then need the button
  released or 20 counts upward before the next. While it is armed over the board the camera does not
  turn (`PlayerInputReader.SuppressLook`, as the emote wheel does), so the drag feels like cutting.
  Gamepad: right trigger = stroke, Y = turn, A (E) = buttons.
- Prompts under the dot: "E · switch on" / "E · switch off", "E · fry → sauté" / "E · sauté → fry",
  "E · put on board", "E · take knife", "drag down · cut   right click · turn", "E · take <name>",
  "E · drop".
- Toasts: "That's not food", "Take a knife to cut", "Put the knife down first" (E on food while
  holding the knife).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes (Unity EditMode, check-assets with the two new models).
- [ ] AC2: `CutRules` tests cover every row of the table: 1 stroke halves; 2 and 5 strokes slices;
      2 strokes, turn, stroke = sticks; then turn, stroke = dice; turns on whole and halves change no
      shape; a stroke on sticks without a turn stays sticks; strokes on dice change nothing.
- [ ] AC3: `KitchenState` board tests: put, 2 strokes, take = the same item hash as
      `ItemFactory.Apply("cut", shape "slices")` on that potato (also halves, sticks, dice); take
      without strokes returns the item unchanged; stroke on an empty board = `NothingToCut`; second
      food on a full board = `StationFull` and it stays in the hands; plated food is refused.
- [ ] AC4: `AimRules` tests: a button hit before its station gives the button (station + option); a
      button out of reach gives no target; existing aim tests stay green.
- [ ] AC5: `CutGesture` tests: 40 counts down = one stroke; 120 counts down without a reset = one
      stroke; release or 20 counts up re-arms; up or sideways only = none; not armed (no knife or no
      food on the board) = none.
- [ ] AC6: Reflection: no ServerRpc gained a parameter (`RpcSurfaceTests`, `MoveDataSurfaceTests`
      green); `CutProgress` has only byte fields; `StoveFlags` is a byte.
- [ ] AC7: Unit test of the first-person key map: no digit key produces a station request in first
      person; in third person digits still do.
- [ ] AC8: Two clients on the dedicated server, screenshots: the board with the cutting-board model
      and the knife next to it; the pan's stove with switch and mode button, the switch green after
      it is pressed; a potato on the board as slices after strokes. Batch clients get no input, so
      the builder may add a dev-only launch arg (development builds only) that sends the normal
      `RequestStation` intents in order; no server-side shortcut.
- [ ] AC9 (manual, human): in first person, without touching a number key: take a potato, put it on
      the board, take the knife, cut sticks with the mouse, put the knife down, take the sticks,
      switch the pan on with its button, fry, plate, serve. A second client sees the potato fall
      apart and the switch light change.
- [ ] AC10: `WhatsNew.json` entry `spec-019` with test steps.

## Test plan
- EditMode: `CutRulesTests`, `BoardSessionTests`, `CutGestureTests`, `AimRulesTests` (extended),
  `FirstPersonKeysTests`, surface tests extended.
- Screenshot run: Linux server in WSL, two Windows clients, the dev-only intent script (AC8).
- Human play-test (AC9).
- Command: `bash scripts/verify.sh` (Windows, Editor closed, `UNITY_PATH` set).

## Open questions (answered 2026-11-07: human approved all defaults)
1. **Models:** copy `cutting-board` and `cooking-knife` from the already downloaded CC0 Food Kit?
   **Default: yes.**
2. **Cutting gesture:** hold the left button and drag down for one stroke (a plain click does
   nothing)? **Default: yes.**
3. **Third person** keeps the digit keys (quick cut, stove keys) as a fallback, since the human said
   interaction is first-person focused? **Default: yes; can be removed later.**
4. **Shapes by strokes:** the table above? **Default: yes.**
5. **Lost knife** respawns next to its board after 5 s out of the world or missing? **Default: yes.**
6. **Skill:** cutting quality stays the same however you cut, for now? **Default: yes; a skill idea
   needs its own spec, computed by the server, never from mouse data.**

## Human steps
- Answer the open questions and approve this spec.
- Play-test AC9.

## Implementation notes
(Builder fills this in.)

## Blockers
