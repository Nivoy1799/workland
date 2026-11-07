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
- [x] AC1: `bash scripts/verify.sh` passes (Unity EditMode, check-assets with the two new models).
- [x] AC2: `CutRules` tests cover every row of the table: 1 stroke halves; 2 and 5 strokes slices;
      2 strokes, turn, stroke = sticks; then turn, stroke = dice; turns on whole and halves change no
      shape; a stroke on sticks without a turn stays sticks; strokes on dice change nothing.
- [x] AC3: `KitchenState` board tests: put, 2 strokes, take = the same item hash as
      `ItemFactory.Apply("cut", shape "slices")` on that potato (also halves, sticks, dice); take
      without strokes returns the item unchanged; stroke on an empty board = `NothingToCut`; second
      food on a full board = `StationFull` and it stays in the hands; plated food is refused.
- [x] AC4: `AimRules` tests: a button hit before its station gives the button (station + option); a
      button out of reach gives no target; existing aim tests stay green.
- [x] AC5: `CutGesture` tests: 40 counts down = one stroke; 120 counts down without a reset = one
      stroke; release or 20 counts up re-arms; up or sideways only = none; not armed (no knife or no
      food on the board) = none.
- [x] AC6: Reflection: no ServerRpc gained a parameter (`RpcSurfaceTests`, `MoveDataSurfaceTests`
      green); `CutProgress` has only byte fields; `StoveFlags` is a byte.
- [x] AC7: Unit test of the first-person key map: no digit key produces a station request in first
      person; in third person digits still do.
- [ ] AC8 (partly checked 2026-11-07 with `-demo-cut`: cutting board, knife in view, potato slices on the board and "on · fry" seen by the cutting client; the stove buttons were out of frame and the second client spawned facing away, so the button light and the second view are left to the AC9 play-test): Two clients on the dedicated server, screenshots: the board with the cutting-board model
      and the knife next to it; the pan's stove with switch and mode button, the switch green after
      it is pressed; a potato on the board as slices after strokes. Batch clients get no input, so
      the builder may add a dev-only launch arg (development builds only) that sends the normal
      `RequestStation` intents in order; no server-side shortcut.
- [ ] AC9 (manual, human): in first person, without touching a number key: take a potato, put it on
      the board, take the knife, cut sticks with the mouse, put the knife down, take the sticks,
      switch the pan on with its button, fry, plate, serve. A second client sees the potato fall
      apart and the switch light change.
- [x] AC10: `WhatsNew.json` entry `spec-019` with test steps.

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
- Built (builder, 2026-11-07): `Cooking/CutProgress.cs` (2 bytes), `CutRules.cs` (table, `ShapeId`, lost-knife rule:
  below -10 m or gone for 5 s), `StationButton.cs`, `Knife.cs` (marker + serialized board index), `Player/CutGesture.cs`,
  `Art/StoveButtonVisual.cs`. Changed: `StationRules` (`KnifeStroke` 10, `KnifeTurn` 11, stove flag bits,
  `DigitOption`, `FirstPersonKeysHint`, `ButtonPrompt`, `FirstPersonPrompt`), `KitchenState` (board sessions:
  `TryPutOnBoard`, `TryBoardStroke`, `TryBoardTurn`, `TryGetBoard`, `TryPreviewBoardTake`, `ClearBoard`, `BoardLook`,
  `BoardName`), `KitchenService` (board E/stroke/turn, `NoKnife` check, stove flags, knife watch), `CookOutcome`
  (`NoKnife`, `NothingToCut` appended), `FoodLookRules.WithShape`, `StationContents.StoveFlags` (`SyncVar<byte>`),
  `AimKind.Button` / `AimHit.Button(...)` (station index + option) / `AimRules` (buttons are targets, reach to their
  station, a button wins a distance tie), `PickupInteractor`, `HeldItemView` (knife pose + chop), `HudView`,
  `ArtKey` (`ToolCuttingBoard`, `ToolKnife`), both builders, `WhatsNew.json`.
- No new RPC: everything goes through `RequestStation(int, int)`; `RpcSurfaceTests`, `MoveDataSurfaceTests` unchanged and green.
  Strokes are limited by the existing per-player 0.2 s request limit (extra requests are dropped silently, as before);
  no second limiter was added. The client waits 0.23 s between knife sends so a chop is never shown for a dropped stroke.
- Server rules: put refuses plated food and food the sim would not cut (dry run, food stays in the hands); a stroke whose
  new shape the sim would refuse is refused before it changes anything, so "take" can never lock the board. Take = one sim
  `cut` with the reached shape (same hash as the digit keys, tested), whole = unchanged. Food already cut by a digit key keeps
  its look on the board until a stroke changes the shape. Anyone may take or cut. Label: "+ <name of what you would get>",
  status line "turned" after a turn.
- Client: first person ignores digits for stations; E acts on the button, station or pickup under the dot. Holding the knife:
  E on food gives the note "Put the knife down first"; E on a station goes to the server (NotFood -> "That's not food").
  `SuppressLook` is held only while the knife is armed and the left button is down (release to look around); the emote wheel
  keeps ownership of the flag while G / LB is held. Gamepad: right trigger stroke, Y turn.
- Deviations: (1) gamepad "A (E) = buttons" is not wired: A is Jump (spec 010) and E has no gamepad binding today; needs a
  decision. (2) World labels now use `FirstPersonKeysHint` ("Board · E · knife", "Pan · E · buttons", "Pot · E · switch");
  `KeysHint` (with digits) stays for its tests and the third-person prompt keeps the digits. (3) Third person is unchanged,
  so E on the board in third person still drops/picks up (food put on a board can only be taken in first person or by V).
  (4) Turning whole/halved food has no visible model turn (no state, nothing synced); slices/sticks show "turned" on the label.
  (5) The knife's world rotation on drop is identity at the normal drop height (0.25 m), like any pickup.
- Tests (62 new, 350/350 green): `CutRulesTests`, `BoardSessionTests`, `CutGestureTests`, `FirstPersonKeysTests`,
  `HandsOnSurfaceTests`, `HandsOnSceneTests`, `AimRulesTests` (+5), `WhatsNewTests` (+1). Adjusted: `FoodArtCatalogTests`
  (the board now also has a content anchor and tool: 5 stations), `FoodLookSurfaceTests` (allows exactly the `StoveFlags` byte).
- Assets: `cutting-board.fbx` (fitted to 0.5 m: 0.354 x 0.037 x 0.5, long side along Z) and `cooking-knife.fbx` (0.3 m,
  turned so its long side runs along Z: 0.056 x 0.017 x 0.3), default importer settings like the other Food Kit models.
  New `Prefabs/Knife.prefab` (collider 0.12 x 0.08 x 0.34, `Pickup`, `Knife`, NetworkTransform, `Visual` child) added as one
  entry at the end of `DefaultPrefabObjects.asset` (the rebuild's reorder was reverted, only the new line kept).
  `Settings/StoveButton.mat` (white, emission on; `StoveButtonVisual` tints by property block). Scene network objects: 28.
- Numbers for the visual check (all single values): cutting board at x -0.12 on the board station (`CuttingBoardOffsetX`),
  knife at (0.42, top, -0.1) (`KnifeSpot`); buttons 0.14 x 0.14 x 0.06 at local (-0.3 / +0.3, 0.3, -0.64) with labels
  "on/off" and "fry"/"sauté" 0.09 m above; colours on (0.2, 0.95, 0.3) glowing, off (0.4, 0.05, 0.05), fry orange, sauté
  yellow (`StoveButtonVisual`); knife in view: offset (-0.05, 0.05, 0.05) from ViewHold, rotation (15, -10, 90) - the roll
  puts the blade on an edge, but which edge is down and which end is the tip depends on the model and must be checked; chop
  0.18 s, 35 degrees, 0.08 m (`HeldItemView`). Board food at part scale 1.0 on top of the cutting board.
- Dev launch arg (AC8, development builds only, `Debug.isDebugBuild`): `-demo-cut`. The owner walks with simulated W/A/S/D
  key states (Input System state events, so movement is predicted and server-checked like a human's) to the potato crate,
  then sends the normal requests: `RequestStation(crate, 0)`, walk to the board/pan lane, `RequestStation(board, 0)`,
  `RequestPickup(knife)`, `RequestStation(board, KnifeStroke)` twice (with the local chop), `RequestStation(pan, StoveSwitch)`.
  No server shortcut. It expects the first spawn point (the first client to connect, facing the kitchen) and needs
  `-skip-whatsnew` (an open banner blocks movement). Estimated timeline from the player's spawn: starts after 6 s, potato
  taken at about 19 s, on the board at about 24 s, knife about 27 s, slices about 29 s, switch green about 31 s (walking at
  4 m/s; every step logs `[Demo] ...` with the real times). Example:
  `<client exe> -connect <server> -view first -skip-whatsnew -demo-cut -screenshot shot.png -screenshot-after 22 -screenshot-every 3`.
  The second client can stay at its spawn (spawn 2 faces +X) or walk there by hand; it sees the board and switch via sync.

## Blockers
