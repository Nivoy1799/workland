---
id: 013
title: Plating table and mixing bowl: combine ingredients into meals
status: draft
owner: architect
builder: opus
---

# 013: Plating table and mixing bowl

## Goal
Players can combine several prepared ingredients into one dish, the step that turns cooking into
real gameplay: a plate with fries and tomato slices is a **meal** and sells for about 3x a single
snack (sim numbers below). Uses the existing `/sim` operations `plate` (up to 4 inputs) and `mix`
(up to 6 inputs); the game only adds the stations and the hand-over. Same interaction pattern as
other cooking games: put items down on a station one by one, then pick up the finished result.

## Sim check (2026-11-07, default catalog, noon demand)
| Dish | Market | Payout |
|---|---|---|
| plate of crispy fried potato sticks | snack | $478 |
| plate of tomato slices and crispy fried potato sticks | meal | $1,426 |
| plate of tomato slices, crispy fried potato sticks and crispy fried onion sticks | meal | $1,502 |
| plate of crispy fried potato sticks and (tomato slices and onion cubes mix) | meal | $1,489 |
| plate of tomato slices and potato (raw) | meal | $1,083 |

The last row shows that raw ingredients are carried by the meal ceiling; that is spec 008's
provisional balancing data and stays out of scope here (noted for a balancing pass).

## Scope
Only `game/` (branch stacked on `spec/012-nonverbal-communication`).
- `game/Assets/_Project/Scripts/Cooking/StationKind.cs` (change: `PlatingTable`, `Bowl`).
- `game/Assets/_Project/Scripts/Cooking/AssemblyRules.cs` (new, pure): what may be added (food only,
  limits from `CookingLimits`, a plated item cannot go onto a plate again), what "take" produces.
- `game/Assets/_Project/Scripts/Cooking/AssemblyState.cs` (new, pure): server-side contents per
  station (list of sim items), add / take / clear.
- `game/Assets/_Project/Scripts/Cooking/StationContents.cs` (new NetworkBehaviour on the two new
  stations): synced list of the names on the station, for labels and the HUD.
- `game/Assets/_Project/Scripts/Cooking/KitchenService.cs` (change: handle the new stations; the
  counter sells an already plated dish without plating it again).
- `game/Assets/_Project/Scripts/Cooking/StationRules.cs` (change: prompts for the new stations).
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: plating table and bowl in the kitchen).
- `game/Assets/_Project/Resources/WhatsNew.json` (change: entry).
- `game/Assets/_Project/Tests/EditMode/AssemblyTests.cs` (new).

## Out of scope
- Any `/sim` change (names, prices, balancing). Long names ("plate of tomato slices, ...") are shown
  as they come; a naming pass is a separate spec.
- Taking a single item back off a station (only "take all as one dish" or clear by taking).
- Recipes, orders, NPC customers.

## Design
- **Plating table:** `E` with food in hand puts it on the plate (up to 4). `E` with empty hands takes
  the plate: the server applies `plate` to all items on it and hands the result over as one Food.
  The label above the table lists what is on it ("Plate: fries, tomato slices").
- **Bowl:** same, with `mix` (2 to 6 items); taking a bowl with one item gives that item back
  unchanged.
- **Counter:** a dish that is already plated sells as it is; anything else is plated alone as today.
- **Authority:** as in spec 007: station index and reach checked on the server, the server owns the
  contents, the sim validates every operation. Food put on a station is despawned from the world
  and kept as a sim item on the station, so it cannot be picked up twice.
- **Shared stations:** anyone can add to or take from a table (co-op cooking). Contents are synced
  to everyone who observes the station.
- **Server restart:** contents are cleared like the kitchen state (spec 007 review finding).

## Acceptance criteria
- [ ] AC1: `AssemblyRules`/`AssemblyState` (EditMode tests): add up to the limit then refuse; a
      plated item cannot be added to a plate; take returns `plate`/`mix` of the items in order and
      empties the station; bowl with one item returns it unchanged; empty take is refused.
- [ ] AC2: Plate of golden fries + tomato slices sells to the meal market and pays more than the
      fries alone (EditMode test with the real engine).
- [ ] AC3: Two players on the WSL server build a plate together (each adds one item), one takes it and
      sells it; the feed shows the meal sale (manual, server log `[Economy] ... to meal`).
- [ ] AC4: `bash scripts/verify.sh` passes with `UNITY_PATH` set; What's-new entry added.

## Test plan
- EditMode tests for the pure assembly logic and the meal sale.
- Manual co-op test with two clients; screenshots of the table label.

## Open questions (human decides; recommended default in brackets)
1. Can everyone use a table, or only the player who started it? [Everyone: co-op is the point.]
2. Where do the stations go? [Plating table and bowl next to the counter at the end of the kitchen row.]

## Human steps
- Approve, answer the open questions, cook a meal with a second person.

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
