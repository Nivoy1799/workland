---
id: 020
title: Mouse interaction, place spots and a clear highlight (first person)
status: approved
owner: architect
builder: opus
---

# 020: Mouse interaction, place spots and a clear highlight

## Goal
In first person you use the kitchen with the mouse, like picking things up with your hand: left
click picks an item up, puts what you hold down on a counter top, puts food into the pan or pot,
presses a button, takes an ingredient from a crate. E is gone in first person. Before you click you
always see **what** you will interact with (the object lights up) and **what** will happen (a short
prompt under the dot: "Click · put potato into the pot", "Click · pick up knife"). One pure rule
decides the action from what you hold and what you look at, so prompt, highlight and click can never
disagree.

Asked by the human on 2026-11-07 after playing spec 019: "use the mouse to interact, E is strange",
"the knife should be placeable on the counter top", and "it is crucial that when I hold an item that
fits into a pot, it goes into it ... best a highlighted object so it is clear how you will interact
... and when you have free hands you pick up the pot". Movable pans, pots, plates and bowls with
several ingredients in a pot follow in spec 021 (see "Next" below); this spec builds the
interaction they will use.

The server stays the authority (rule 6). The client sends the same intents as today plus one new
intent "place on spot N of surface S" (two ints). It never sends a position.

## Depends on
- Specs 003 (pickups, drop), 007/013/016/019 (stations, pan/pot put and take, buttons, board,
  knife, cut gesture), 018 (first person, aim, crosshair prompt, held-item view).

## Scope
- `game/Assets/_Project/Scripts/Net/`:
  - `Rules/InteractionRules.cs` (new, pure): `InteractionAction ActionFor(Held held, AimTarget target)`,
    see Design. `Held` and `AimTarget` are small value types (new files, one public type each).
  - `Rules/InteractionAction.cs` (new): the action enum.
  - `Rules/AimRules.cs`, `AimKind.cs`, `AimHit.cs` (change: a `Spot` kind carrying surface and spot
    index; a station button still wins over its station).
  - `Rules/PlaceSpots.cs` (new, pure): spot grid of a surface and which spots are taken.
  - `PickupInteractor.cs` (change: first person uses the left click, right click and Q; no E, no
    digits; third person unchanged).
  - `PickupService.cs` (change: `Place(player, surfaceIndex, spotIndex)` on the server).
  - `Pickup.cs` (change: `ServerPlaceAt(Vector3, Quaternion)`, server only).
  - `PlaceSurface.cs` (new, MonoBehaviour, data only): a surface index and its spot grid size.
- `game/Assets/_Project/Scripts/Player/`:
  - `ClickGesture.cs` (new, pure): tells a click from a drag (the cut stroke from spec 019).
  - `InteractionHighlight.cs` (new, owner only): lights up the current target and shows a ghost
    marker on the chosen spot.
  - `PlayerInputReader.cs` (change only if the click needs a new read; Q for drop).
- `game/Assets/_Project/Scripts/UI/HudView.cs` (change: prompt text from `InteractionRules`, toasts).
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: plain worktops in the gaps between
  the kitchen stations with `PlaceSurface`; spots on the free half of the board station; world
  labels smaller, see Design).
- `game/Assets/_Project/Resources/WhatsNew.json` (entry `spec-020`), tests in `Tests/EditMode/`.
- This spec (implementation notes).

## Out of scope
- Movable pans, pots, plates, bowls; several ingredients in a pot (spec 021).
- Placing on the floor at a chosen point, throwing, physics.
- Third person: keeps E, the digits and the nearest-target logic (decided by the orchestrator,
  as the human left it open: "third you are free").
- Hands, arms, animations (human characters, spec 022).
- Key rebinding, settings menu.

## Design
### Mouse map (first person)
| Input | Action |
|---|---|
| Left click | the action under the dot (table below) |
| Left button held + drag down | cut stroke: only with the knife, aiming at a board with food (spec 019) |
| Right click | turn the food: only while cutting; otherwise nothing |
| Q | drop what you hold at your feet (today's drop) |
| Gamepad right trigger / Y / X | click / turn / drop |

A **click** is a press and release within 0.3 s with less than 15 counts of mouse movement
(`ClickGesture`). A press that turns into a downward drag over a board with food is a cut, not a
click. While the cursor is unlocked, the first click only locks it (as today) and does nothing else.

### One rule for the action (pure, `InteractionRules`)
`ActionFor(held, target)` returns one `InteractionAction`. Prompt, highlight colour and click all
use it. Rows are checked top to bottom; the first match wins.

| Held | Target | Action | Prompt |
|---|---|---|---|
| anything | station button | `Press` | "Click · switch on/off", "Click · fry → sauté" |
| knife | board with food | `Cut` (drag) | "Drag down · cut   Right click · turn" |
| food | board, empty | `PutOnBoard` | "Click · put <food> on the board" |
| food that fits | pan / pot (station, empty, sim accepts it) | `PutInto` | "Click · put <food> into the pan" |
| food that fits | plating table / bowl | `PutInto` | "Click · put <food> on the plate" / "into the bowl" |
| plated food | counter | `Sell` | "Click · sell <food>" |
| anything | free spot on a surface | `Place` | "Click · put <item> here" |
| nothing | free item (food, knife) | `PickUp` | "Click · pick up <item>" |
| nothing | crate | `Buy` | "Click · take <ingredient> ($60)" |
| nothing | pan / pot / board / plate / bowl with food | `Take` | "Click · take <food>" |
| food that does not fit | pan / pot / plate / bowl | `None` | "<food> can't go in there" (grey) |
| anything else | anything | `None` | no prompt |

"Fits" is checked on the client with the same pure `KitchenState`/sim rule the server uses (plated
food never goes into a pan, a full pan takes nothing more), so a red "can't" is shown before the
click, not after. The server checks again and decides; a refused click shows today's toast.

### Highlight (owner only, `InteractionHighlight`)
- The target object gets a soft glow (emission or a rim tint via a MaterialPropertyBlock on its
  renderers, no new materials per frame): **white** for `PickUp`/`Take`/`Buy`, **green** for
  `PutInto`/`PutOnBoard`/`Place`/`Sell`/`Press`, **grey** for a `None` with a reason.
- For `Place`, a flat translucent ring (or a ghost of the held model, builder's choice) marks the
  exact spot the item will land on.
- Only one object glows at a time; the glow follows the dot without flicker (target kept while the
  ray stays on the same object).

### Place spots (server decides positions)
- A `PlaceSurface` has a grid of spots on its top, 0.25 m apart, 0.12 m from the edges. The spot
  positions are computed by `PlaceSpots` from the surface's box (pure, tested).
- The aim ray hits the surface top; the client picks the nearest **free** spot to the hit point.
- `RequestPlace(int surfaceIndex, int spotIndex)` (new ServerRpc, ints only). The server checks:
  the player holds something, the surface exists and is in reach (2.5 m, the same rule), the spot
  index is valid and free, the request rate. Then the held item is placed on the spot (spot
  position, the item's resting rotation) and the spot is marked taken by that object. Picking the
  item up frees the spot. The client never sends a position.
- Surfaces: plain worktops filling the gaps between the board, pan, pot, counter, plating table and
  bowl (same height as the stations), and the free half of the board station next to the cutting
  board (the knife's home spot). Stations themselves (pan, plate, bowl) are not place surfaces;
  holding food over them means `PutInto`.
- The scene knife starts on its board's home spot.

### World labels
In first person the station labels are very large up close (seen in the spec 019 screenshots). They
get half their size and fade out within 1.5 m of the camera, where the prompt under the dot says
everything. Third person is unchanged.

### Network
- One new ServerRpc on `PickupInteractor`: `RequestPlace(int surfaceIndex, int spotIndex)`.
  `RpcSurfaceTests` stays green (ints are allowed); `MoveDataSurfaceTests` is updated on purpose to
  the four RPCs RequestDrop, RequestPickup, RequestPlace, RequestStation.
- Everything else uses the existing requests: `RequestPickup`, `RequestStation(index, 0)` for
  put/take/buy/sell, `RequestStation(index, StoveSwitch / PanMode / KnifeStroke / KnifeTurn)`,
  `RequestDrop` for Q.

## Next: spec 021 (movable cookware), noted here so this spec does not block it
- Pan, pot, plates and bowl become items you carry. Stoves get burners with knobs; a pan or pot
  cooks only while it stands on a burner that is on.
- **Several ingredients in a pot** (asked by the human): the pot keeps a list of what was put in;
  boiling it makes a soup or stew from all of them. Today's sim `boil` takes one input, so 021
  needs a /sim change (for example mix the pot's contents, then boil the mix) and its own tests.
- The `InteractionRules` table gets the new rows: holding food that fits + pot → `PutInto` (even
  when the pot stands on a counter); empty hands + pot → `PickUp` (the pot with its contents);
  holding a pan with food + plate → `PourOnto`.

## Acceptance criteria
- [x] AC1: `bash scripts/verify.sh` passes.
- [x] AC2: `InteractionRules` tests cover every row of the table, including: food that fits + empty
      pot = `PutInto`; plated food + pan = `None` with a reason; empty hands + pan with food =
      `Take`; knife + board with food = `Cut`; knife + free spot = `Place`; button beats everything.
- [x] AC3: `ClickGesture` tests: short press = click; long press = no click; press with 15+ counts
      of movement = no click; a downward drag over a board with food = stroke, not click.
- [x] AC4: `PlaceSpots` tests: grid size and positions for a 1 m x 0.6 m top; nearest free spot to
      a hit point; taken spots are skipped; a full surface gives none; freeing a spot.
- [x] AC5: Server place tests (pure part): valid place marks the spot; a taken spot, a bad index, an
      out-of-reach surface and empty hands are refused; pickup frees the spot.
- [x] AC6: Reflection: `RequestPlace` has only int parameters; the interactor's ServerRpcs are
      exactly RequestDrop, RequestPickup, RequestPlace, RequestStation; `MoveData` unchanged.
- [x] AC7: Key map test: in first person E and the digits send nothing; in third person they work
      as before.
- [x] AC8 (2026-11-07, `-demo-place`: green glow and "Click · put potato into the pot", knife placed on the board-pan worktop, "Click · pick up knife"; the rest confirmed by the human play-test): Screenshots, two clients on the dedicated server (dev-only demo arg allowed, sending the
      normal intents): the knife placed on a worktop spot; the green glow and prompt on the pot
      while holding a potato; the second client sees the knife on the worktop.
- [x] AC9 (manual, human, play-tested 2026-11-07): play 5 minutes in first person with mouse only (plus Q): buy, cut, fry,
      plate, sell; place the knife on a worktop and pick it up again. It is always clear what a
      click will do.
- [x] AC10: `WhatsNew.json` entry `spec-020` with test steps.

## Test plan
- EditMode: `InteractionRulesTests`, `ClickGestureTests`, `PlaceSpotsTests`, `PlaceServiceTests`,
  `FirstPersonKeysTests` (updated), surface tests updated.
- Screenshot run with the WSL server and two clients; the dev demo arg runs on the first client
  alone (a second window steals keyboard focus, see spec 019 notes).
- Human play-test (AC9).
- Command: `bash scripts/verify.sh` (Windows, Editor closed, `UNITY_PATH` set).

## Open questions (answered 2026-11-07: human approved all defaults)
1. **E in first person:** removed completely (the mouse does everything)? **Default: yes.**
2. **Q drops** what you hold at your feet? **Default: yes.**
3. **Highlight style:** a soft glow on the target (white = take, green = put/use, grey = not
   possible) plus a ghost ring on the place spot? **Default: yes.**
4. **Worktops:** add plain worktops in the gaps between the stations as place surfaces?
   **Default: yes.**
5. **World labels** half size and fading out up close in first person? **Default: yes.**

## Human steps
- Answer the open questions and approve this spec.
- Play-test AC9.

## Implementation notes
- Built (builder, 2026-11-07): `Net/Rules/InteractionRules.cs`, `InteractionAction.cs`, `Held.cs`, `AimTarget.cs`,
  `PlaceSpots.cs` (grid + server spot table + `CheckPlace`), `Net/PlaceSurface.cs`, `Player/ClickGesture.cs`,
  `Player/InteractionHighlight.cs`, `Player/WorldLabelFade.cs` (new file outside the Scope list, in Scripts/Player as
  the orchestrator allowed). Changed: `AimKind` (`Spot`), `AimHit` (`SurfaceIndex`, `SpotIndex`, `AimHit.Spot`),
  `AimRules` (spot targets; overload with `pickupsTransparent`), `Pickup` (`ServerPlaceAt`, `BottomOffset`),
  `PickupService` (`TryPlace`, `_surfaces`, spot table, `IsSpotOccupied`), `PickupInteractor`, `HudView`,
  `GrayboxSceneBuilder`, `WhatsNew.json`. `PlayerInputReader` unchanged (Q is read like E was).
- Tests: `InteractionRulesTests` (every table row), `ClickGestureTests`, `PlaceSpotsTests`, `PlaceServiceTests`,
  `PlaceSceneTests` (scene surfaces by index, worktops do not overlap stations, knife on its home spot, labels without
  keys, player prefab, highlight colours, per-material block clears with null, label alpha), `FirstPersonKeysTests`
  (+3, AC7), `AimRulesTests` (+6), `MoveDataSurfaceTests` (updated on purpose: four RPCs, `RequestPlace(int, int)`),
  `WhatsNewTests` (+1). EditMode 415/415, `scripts/verify.sh` PASS; after the review fixes below 422/422, PASS.
- Click: fires on release (press + release within 0.3 s, path length < 15 counts). A press that starts while the cursor
  was free in the previous frame, a press while a menu/wheel/T has the keys, and a press that fires a cut stroke are
  cancelled. With the knife armed (`Cut`) a click does nothing; strokes and right-click turn as in spec 019. Gamepad:
  RT = click (or stroke when armed), Y = turn, X = drop. Q / X drop only in first person (`InteractionRules.DropKeyActs`);
  E and digits only in third person (`InteractionRules.KeysActInView`), third person otherwise unchanged.
- "Fits" on the client: `InteractionRules.Fits` uses only client-visible state: plated = `Food.Look.Kind == KindPlated`,
  board/pan/pot full = the station's synced `Names`/`Looks` count > 0, plating table/bowl full = count >=
  `AssemblyRules.MaxItems` (the server's constant). The sim dry runs (e.g. food the sim would not cut or heat) are
  server-only, so such food shows green and the server refuses with today's toast.
- Deviations / additions to the table (reviewer please check): (1) **Sell accepts any food**, not only plated food:
  the counter has plated food itself since spec 009 and the server sells it, so refusing on the client would have broken
  today's buy-fry-sell loop. (2) Extra grey reasons: knife + empty board "Put food on the board to cut", food + full
  board "can't go in there", full surface "No room here"; with full hands and nothing to say the HUD shows a grey
  "Q · drop". (3) While holding, pickups do not block the aim ray (`pickupsTransparent`), so you can place next to an
  item lying on a worktop; empty hands see items as before. (4) World labels: no key hints any more (name, and price on
  crates), in both views; half size and fade (1.5 m to 1.0 m) happen at runtime in first person only
  (`WorldLabelFade`), third person keeps the full size. (5) `StationRules.FirstPersonPrompt`, `ButtonPrompt` and
  `FirstPersonKeysHint` are no longer used by runtime code (kept with their tests; `StationRules` is outside Scope);
  the unused `PickupInteractor.HasContents` was removed.
- Place spots: reach is measured from the player's feet to the spot (not the surface centre), on client (AimRules) and
  server. The client cannot see the server's spot table, so a spot counts as taken when a tiny probe (sphere r 0.03 m
  just above the spot) touches a free pickup; the server checks its table (stale entries freed: object gone, held or
  moved > 0.1 m) and the same probe, which also covers the scene knife at start and a respawned knife. The item rests on
  the spot (collider bottom on the top) in its resting tilt turned by the surface's yaw (review fix 1 below). Picking up frees the spot. Place
  answers come back through a new TargetRpc `ReportPlace` (`LastPickupWasPlace` for the toast texts).
- Scene: surface 0 = board station's free half (x 0.08..0.6 local, 2 x 4 spots, thin collider 5 mm above the station
  body so the ray hits it first, items rest on the furniture top at 1.1 m); surfaces 1-5 = worktops in the gaps
  board-pan, pan-pot, pot-counter, counter-plating, plating-bowl (1.26 m or 1.76 m wide, 1.2 m deep, 1.1 m high = the
  furniture top; 5 x 4 = 20 or 7 x 4 = 28 spots), plain cubes with `Settings/Worktop.mat`. The knife starts on board
  spot 3 (local 0.465, -0.125, the spot nearest its spec 019 position); `KitchenService` respawns at the start
  position, which is now that home spot (no change there). Scene network objects: still 28.
- Highlight: per-material `MaterialPropertyBlock`s on the target's renderers: base colour lerped 35 % towards the glow
  colour and x1.15, plus `_EmissionColor` (only visible where a material has emission on; the pack materials do not,
  so the tint is what shows). It starts from the renderer-wide block, so FoodVisual tints and the stove button lights
  stay right; per-material blocks are owned by the highlight and cleared with `SetPropertyBlock(null, i)` (tested).
  Re-applied every frame without allocations; a station's own buttons and all TextMesh labels are skipped. Place:
  a 0.22 m flat translucent green disc (`Settings/PlaceMarker.mat`, URP Unlit transparent) on the spot.
- Known limits: food colliders are 0.4 m, wider than the 0.25 m spot spacing, so neighbouring placed food overlaps
  in collider terms (the models are smaller; the probe still sees the neighbour spot as free). The client probes every
  spot of the aimed surface each frame (up to 28 small overlap queries).
- Dev launch arg (AC8, development builds only): `-demo-place`. Walks like `-demo-cut` (simulated W/A/S/D) and turns the
  view with simulated mouse deltas (feedback loop on the camera, about 0.12 deg per count), so it needs `-view first
  -skip-whatsnew`, the first spawn point and window focus (run it on one client only). Steps: take a potato
  (`RequestStation(crate, 0)`); walk to x between pan and pot, z = kitchen - 1.3; aim at the pot until the action is
  `PutInto`, log the prompt, hold the aim 4 s; level the view, walk between board and pan; then three times "aim, wait
  for the expected action, `ExecuteAction()`" (the same code a click runs): put on the board (`RequestStation`),
  pick up the knife (`RequestPickup`), put it on the front middle spot of the board-pan worktop (`RequestPlace`). If an
  aim does not give the expected action within 3 s it logs a warning and sends the same request directly. Afterwards it
  keeps looking at the knife on the worktop for 30 s. Estimated timeline (every step logs `[Demo] ...`, the hold and
  done lines with `Time.time`): potato about 19 s, pot glow about 23-27 s, board about 30 s, knife about 32 s, knife on
  the worktop about 34 s. Example:
  `<client exe> -connect <server> -view first -skip-whatsnew -demo-place -screenshot shotA.png -screenshot-after 24 -screenshot-every 3`.
  The second client needs to look at the board-pan worktop (x 1.75, z 13) to see the knife.
- Review fixes (orchestrator, after the first screenshot run):
  1. Placed knife stood upright. `Pickup` now captures a server-side resting tilt in `OnStartServer`
     (`PlaceSpots.TiltOf(transform.rotation)`: the rotation without its yaw), so scene objects and a respawned knife keep
     how they lie and food spawned in the hands (yaw-only rotation) keeps identity. `TryPlace` uses
     `PlaceSpots.RestRotation(tilt, surface rotation)` (yaw follows the surface, tilt stays), `ServerDetach` (Q) uses
     `RestRotation(tilt, identity)` instead of `Quaternion.identity`. Pure and tested (`PlaceSpotsTests` +3). Note for the
     re-run: the scene knife's own rotation is identity too, so if the knife still stands up after this fix, the likely
     cause is client-side: `HeldItemView` (spec 018, outside this Scope) not restoring the knife's `Visual` local pose
     when it leaves the hands. Worth checking which client the upright knife was seen on. `BottomOffset` still assumes
     an untilted collider (true for every item today).
  2. Out of reach: `AimRules.Pick(..., out int outOfReach)` reports the closest station, button, item or spot that the
     3 m ray sees beyond the 2.5 m reach (walls still hide, full hands still ignore items). The interactor keeps it as
     `Target` with `TargetTooFar`; `AimTarget.TooFar()` makes `ActionFor` None and `Reason` "Too far" whenever the same
     target in reach would show an action or a reason (empty hands at an empty far pan stay silent). The highlight then
     glows grey, the prompt is grey "Too far"; nothing is sent. Tests: `AimRulesTests` +2, `InteractionRulesTests` +2.
  3. Demo: the pot step now walks to x = pot, z = pot - 1.2 (about 1.3 m in front of it, via the lane) before aiming and
     logs the distance; then back along z = kitchen - 1.3 to between board and pan. Rest unchanged. Timeline shifts by
     about 1 s (pot glow about 24-28 s).
- Screenshot check: the glow strength on the pot (35 % tint) and the disc size/alpha; the worktop colour and height
  against the station furniture (art may stick into the 2 cm gaps); label size and fade in first person; that the
  held potato does not cover the prompt.
- Batch rebuild: `DefaultPrefabObjects.asset` changed only in order and was reverted; the usual FishNet "same
  assetPath hash of 0" errors appeared.

## Blockers
