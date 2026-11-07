---
id: 007
title: Cooking stations in the graybox, playable demo
status: approved
owner: architect
builder: opus
---

# 007: Cooking stations (playable demo)

## Goal
Make cooking visible and playable for a stakeholder demo: in the graybox, players take a raw
ingredient from a crate, cut it on a board, fry or boil it, and serve it. Names and ratings come
from the `/sim` cooking model (spec 005), computed on the server. Two players can cook at the
same time and see each other's food and results.

## Scope
Only `game/` (builds on spec 003, branch stacked on `spec/003-netcode-graybox`).
- `game/Assets/_Project/Scripts/Cooking/` (new):
  - `DemoIngredients.cs`: base item definitions for the demo (potato, apple, tomato, onion),
    stats taken from the spec 005 test fixtures.
  - `StationKind.cs`, `StationAction.cs`, `StationRules.cs`: pure mapping from station + option
    key to a sim operation and its parameters, plus the reach check.
  - `KitchenState.cs`: pure server state, network object id -> sim `Item`.
  - `CookOutcome.cs`: result enum for station requests.
  - `StarRating.cs`: quality (0..1) -> 1..5 stars.
  - `Food.cs` (NetworkBehaviour on a food object): synced name, stars, hash; world label.
  - `Station.cs` (MonoBehaviour on station objects): kind, display name, prompt text.
  - `KitchenService.cs` (server, next to the NetworkManager): handles crate, station and serve
    requests with the sim `ItemFactory`; spawns and despawns food.
  - `ServeFeed.cs`: last served dishes, synced to everyone for the overlay.
- `game/Assets/_Project/Scripts/Net/PickupInteractor.cs` (change: route E/1-4 near a station
  or crate to the kitchen instead of pickups; held food behaves like a pickup).
- `game/Assets/_Project/Scripts/Net/PickupService.cs`, `Pickup.cs` (change only if needed so
  food objects use the same holding rules).
- `game/Assets/_Project/Scripts/Net/DebugOverlay.cs` (change: station prompt, serve feed).
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: kitchen corner with 4 crates,
  board, pan, pot, serving counter; Food prefab).
- `game/Assets/_Project/Tests/EditMode/` (new tests listed below).
- `game/README.md` (change: how to play the demo).

## Out of scope
- AI names (spec 006), economy, selling or money (spec 008 integration), recipes, timers that
  run in real time (an operation is instant with preset parameters), mix and plate (multi-input),
  art, sounds, persistence, Steam-only features.

## Design
- **Controls** (owner client, keys only; the overlay shows the prompt for the nearest station):
  - Crate: `E` with empty hands takes one ingredient (the server spawns a Food in your hands).
  - Board (`cut`): `1` halves, `2` slices, `3` sticks, `4` cubes.
  - Pan (`fry`): `1` light (180 C, 40 s), `2` golden (190 C, 150 s), `3` dark (200 C, 400 s).
  - Pot (`boil`): `1` short (60 s), `2` medium (300 s), `3` long (900 s).
  - Counter: `E` serves the held food: everyone sees "<player> served <name> ★★★★☆ (q 0.82)",
    the food is despawned.
  - Away from stations, `E` keeps the spec 003 pickup and drop behavior.
- **Authority:** the client sends `RequestStation(stationIndex, option)` or `RequestCrate(index)`
  or `RequestServe()`. The server checks: station exists, player within 2.5 m of it, holds a Food,
  option is valid for the station, and the sim accepts the operation (`OperationResult.Error`).
  Only then it replaces the held item. Clients never send item data, only intents.
- **Sim use:** one `ItemFactory(new ItemCatalog(DemoIngredients.All), OperationCatalog.CreateDefault())`
  on the server. `CreateBase(id)` at crates, `Apply(op, [item], params)` at stations,
  `ItemNaming.FallbackName(item, NameCatalog.Empty)` for the name, `ItemRater.Rate(item).Quality`
  for the rating.
- **Food object:** a networked prefab (cube, colored by ingredient) with `Pickup` holding behavior,
  `NetworkTransform` (server-authoritative), SyncVars `Name`, `Stars`, `Hash`. A world-space
  label above it shows name and stars to every client.
- **Determinism:** same ingredient + same station options give the same hash and quality on every
  run (the sim guarantees it; tests pin one case).

## Acceptance criteria
- [x] AC1: `StationRules` maps every station + option to the sim operation and parameters above,
      and rejects unknown options (EditMode test).
- [x] AC2: Server-side kitchen logic (pure part, EditMode test): crate potato -> board `3` ->
      pan `2` gives the same `Item.Hash` as `Fry(Cut(potato, sticks), 190, 150)` from the sim,
      and its name is "crispy fried potato sticks" (whatever `FallbackName` returns, pinned in the test).
- [x] AC3: Requests are refused (outcome, no state change) when: too far from the station,
      hands empty, unknown station or option, or the sim returns an error (EditMode tests).
- [x] AC4: `StarRating` maps quality to 1..5 stars at fixed boundaries (EditMode test).
- [x] AC5: In a running game, a client takes a potato, cuts, fries and serves it; all clients see
      the label change and the serve message (manual, two clients on the WSL server; server log
      line `[Kitchen] player N served <name> q=<quality>` as evidence).
- [x] AC6: `bash scripts/verify.sh` passes with `UNITY_PATH` set (Unity tests run).

## Test plan
- EditMode tests in `game/Assets/_Project/Tests/EditMode/Cooking*Tests.cs` for AC1 to AC4,
  using `DemoIngredients` and the real sim types.
- Manual demo run for AC5: Linux server in WSL, two Windows clients.
- Command: `UNITY_PATH=... bash scripts/verify.sh`.

## Human steps
- Approve this spec.
- Play the demo for 5 minutes with a second person or two windows; say if it is fun to show.

## Implementation notes
- Review 2026-11-07: kitchen state is cleared when the server stops (FishNet reuses object ids on a
  restart in the same process); serving requires a Food component; a failed hand-over after buying
  despawns the food and logs an error. Hands-empty / hands-full refusals live in the networked
  `KitchenService` and are covered by the manual test, not by EditMode tests.
Done 2026-11-06 on `spec/007-cooking-stations` (stacked on spec 003).
- AC1-AC4: `CookingRulesTests`, `KitchenStateTests` (potato -> sticks -> golden fry gives hash
  `2ab1f862...` and "crispy fried potato sticks", same as the sim).
- AC5: two Windows clients on the WSL Linux server; human confirmed it works. Server log, e.g.
  `[Kitchen] player 1 served crispy fried tomato slices q=0.77`.
- AC6: verify.sh PASS with UNITY_PATH, Unity EditMode 81/81, /sim 191/191.
- Observed: cutting an already cut item stacks the names ("onion halves slices sticks").
  That is the spec 005 naming of operation chains, not a game bug; worth a naming pass
  (or AI names, spec 006) before a wider demo.

## Blockers
(Builder fills this in after 3 failed attempts.)
