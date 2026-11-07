---
id: 009
title: Buy, cook, sell: money loop in the graybox (playable demo)
status: approved
owner: architect
builder: opus
---

# 009: Buy, cook, sell (money loop demo)

## Goal
Make the circular economy (spec 008) visible next to the kitchen (spec 007): players pay the NPC grocer
for raw ingredients at the crates, cook, and sell plated dishes to NPC demand at the counter. Better
cooking earns more, burnt food earns less, and selling too much of one thing lowers the price. Every
number comes from `/sim` on the server; the game only shows it.

## Scope
Only `game/` (branch stacked on `spec/007-cooking-stations`).
- `game/Assets/_Project/Scripts/Economy/` (new):
  - `EconomyRules.cs`: pure constants and mappings (account id per client, starting grant,
    real seconds per in-game hour, clock text "Day 1, 14:00").
  - `TownEconomy.cs`: pure wrapper around `EconomyEngine` + current `EconomyState` (open account
    with grant, buy base item, plate-and-sell, hourly tick, read balances and market prices).
  - `Wallet.cs` (NetworkBehaviour on the player): synced balance for the overlay.
  - `MarketBoard.cs` (NetworkBehaviour, global scene object): synced clock and market prices.
  - `EconomyService.cs` (server, next to the NetworkManager): opens accounts on join, runs the
    hourly tick on a real-time timer, publishes wallets and the market board.
- `game/Assets/_Project/Scripts/Cooking/KitchenService.cs` (change: crates charge the grocer
  price; the counter plates and sells instead of only serving).
- `game/Assets/_Project/Scripts/Net/DebugOverlay.cs` (change: wallet, clock, market prices).
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: Wallet on the player prefab,
  MarketBoard scene object, EconomyService; crate labels show the price).
- `game/Assets/_Project/Tests/EditMode/` (new tests listed below).

## Out of scope
- Persistence (wallets reset when the server restarts or the player reconnects), backend.
- Player-to-player trade, selling byproducts (scraps, used oil are kept in the account and shown
  only as a count), mixes and multi-ingredient plates, NPC visuals, UI beyond the overlay.
- Any change to `/sim` (no new rules, prices or catalog values; `EconomyCatalog.CreateDefault()` is used as is).

## Design
- **Accounts:** one sim account per connected player, id `p<clientId>`. On join the server opens it and
  grants the starting money (open question 1) with `EconomyState with { ... }` the same way the
  spec 008 tests grant money. The grant is logged: `[Economy] player N joined with $500`.
- **Crate:** `TryBuyBase(state, account, baseId, 1)` before the food spawns. Not enough money -> refused,
  nothing spawns. Labels show the grocer price ("Potato crate $60").
- **Counter (E):** server applies `plate` to the held item (single input), then
  `TrySellItem(state, account, instanceId = food object id, platedItem)`. The payout is added by the
  engine. Feed entry: "Player 1 sold crispy fried potato sticks ★★★★☆ for $231 (snack)".
  A refused sale (quality below minimum, no market) keeps the food in the player's hands and shows why.
- **Clock:** the server calls `Tick` every N real seconds (open question 2) with a seeded RNG and the
  default population, so demand refills and prices move. Clock text "Day 1, 14:00", starting at 08:00.
- **Sync:** `Wallet.Balance` (SyncVar long) on each player object; `MarketBoard` syncs clock text and
  the current price of each Item market (meal, snack, drink). Clients send no money data, only the
  existing E-key intents.

## Acceptance criteria
- [x] AC1: `TownEconomy` opens an account with the grant, buying a potato costs the grocer price, and
      buying without enough money is refused with no state change (EditMode tests with the real engine).
- [x] AC2: Plate-and-sell of golden fries pays more than plate-and-sell of dark fries made from the same
      potato (EditMode test, fresh state for each).
- [x] AC3: Selling the same dish type repeatedly in one in-game hour does not raise the payout, and
      after ticks the demand refills (EditMode test, numbers come from the engine).
- [x] AC4: `EconomyRules` clock text and account id mapping (EditMode tests).
- [x] AC5: In a running game two players buy, cook and sell; wallets, the clock and the feed update on
      both clients (manual on the WSL server; server log `[Economy] player N sold ... for $X`).
- [x] AC6: `bash scripts/verify.sh` passes with `UNITY_PATH` set.

## Test plan
- EditMode tests `Economy*Tests.cs` with `EconomyCatalog.CreateDefault()` and `DemoIngredients`.
- Manual demo: Linux server in WSL, two Windows clients.

## Open questions (answered 2026-11-06: human approved both defaults)
1. Starting money per player for the demo. [$500: about 8 potatoes; enough to make mistakes, small next to dish payouts]
2. Real time per in-game hour. [60 s, so a demo of 10 minutes shows most of a day's demand curve]

## Human steps
- Approve this spec and answer the open questions.
- Play it with a second person for 5 minutes.

## Implementation notes
- Review 2026-11-07: sale instance ids come from a monotonic counter in `TownEconomy`, not from the
  food object id (object ids are reused by FishNet); the Design section's wording is superseded.
Done 2026-11-06 on `spec/009-sell-dishes` (stacked on spec 007).
- AC1-AC4: `EconomyDemoTests` with the real engine and default catalog: grant once, grocer price,
  refused buy without money, golden fries pay more than dark fries, raw potato refused
  (QualityTooLow), repeated sales never raise the payout, demand refills after ticks, clock text.
- AC5: two clients on the WSL server, human confirmed "money works". Server log:
  `player 1 bought potato for $60`, `sold crispy fried potato cubes for $478 to snack q=0.70`,
  `sold crispy fried onion sticks for $551`, `Day 1, 09:00 ... snack $388 (7 orders)`.
- AC6: verify.sh PASS, Unity EditMode 93/93, /sim 191/191.
- Observed: a q=0.18 dish ("soft boiled potato slices") still sells for $261 because the snack
  minimum is 150 permille. That is the spec 008 provisional data, not a game bug; a balancing pass
  may want a higher minimum or a steeper quality curve.
- Known demo limits: FishNet reuses client ids, so a new player can inherit a previous player's
  account (no second grant); wallets reset on server restart (no persistence, out of scope).

## Blockers
(Builder fills this in after 3 failed attempts.)
