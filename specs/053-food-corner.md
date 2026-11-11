---
id: 053
title: Stage 1 food corner (1 dish, walk-up guests, cash only, save for the rent) and the host's save file
status: approved
owner: architect
builder: opus
---

> Approved by the human on 2026-11-11 (inbox questions 13 and 21) with the recommended defaults of its Open questions.

# 053: Food corner and the host's save

> **Depends on the human approving docs/game-design.md v1 (inbox question 13)** and on spec 052 being built (its
> `ClimbCatalog` holds the stage 1 numbers). Do not build this before both, even if this spec is approved.

## Goal
A new game now starts the D34 climb at stage 1: a food corner with **one dish**, walk-up guests, **cash only**, and one
goal on screen, "save for the rent". The host's game is saved on the host's PC (D33) so a crew can stop and continue
another evening from the title screen. This is the core of milestone "one great service" (docs/game-design.md, step 3).

## Depends on
Design v1 (question 13); D23, D27, D33, D36; specs 028b and 034 (shelf slots, stock counts, bulk price), 029 (fryer),
032 (register, cash, `GuestOrder.PaysByCard`), 035 (trash, hygiene), 038 (tutorial), 045 (restart paths), 047
(title screen, `MenuRules.HostChoices(hasSave)`, lobby, "Close the shop"), 052 (`ClimbCatalog.CreateDefault()`).
Not 037: every stage 1 amount is scaled to the game's own price level (below), so it works before and after 037.

## Scope
Only `/game` (plus the tour hook in `scripts/visual-check.sh`). Paths under `game/Assets/_Project/`.

**Part A, the save**
- `Scripts/Save/` (new): `ShopSave.cs` (the [Serializable] DTO and its nested types), `ShopSaveCodec.cs` (JSON,
  validation, migration), `SaveMigrations.cs`, `SaveError.cs`, `SaveLoadResult.cs`, `ISaveFiles.cs`, `DiskSaveFiles.cs`
  (real IO), `SaveStore.cs` (list, newest, atomic write, `.bak`), `SaveTrigger.cs`, `AutosaveRules.cs` (pure),
  `SaveService.cs` (server MonoBehaviour: capture, apply, triggers).
- `Scripts/Economy/EconomyRules.cs`, `EconomyService.cs`, `TownEconomy.cs` (change: one shop wallet, grant once per new
  shop, restore balance and day, publish the wallet to every player).
- `Scripts/Shop/StockCounts.cs`, `StockRoom.cs`, `ShopHygiene.cs` (change: capture and restore).
- `Scripts/Net/Rules/LaunchArgs.cs`, `Scripts/Net/NetBootstrap.cs` (change: `-new-shop`, `-continue`, `-save-dir`; start
  hosting with a save; save before "Close the shop" stops the server).
- `Scripts/UI/MenuRules.cs`, `MainMenuView.cs` (change: Continue with its line, error cards for a bad save).

**Part B, the food corner**
- `Scripts/Climb/` (new): `ShopStageRules.cs` (pure: what is open per stage), `FoodCornerRules.cs` (pure: the dish,
  scaled amounts, goal text), `ShopStage.cs` (NetworkBehaviour with the synced stage and goal).
- `Scripts/Cooking/CookOutcome.cs` (change: `NotOpenYet`), `KitchenService.cs` and `Scripts/Net/PickupInteractor.cs`
  (change: the server refuses closed stations and slots), `Scripts/Shop/RegisterDesk.cs` (change: card refused).
- `Scripts/Npc/CustomerService.cs`, `Menu.cs`, `TutorialOrders.cs` (change: stage dish filter, to go, cash).
- `Scripts/Tutorial/TutorialSteps.cs` (change: `ForStage`).
- `Scripts/UI/HudView.cs` (goal line, refusal text), `ShiftSummaryView.cs` (goal progress).
- `Scripts/Art/ClosedCover.cs` (new: a graybox cloth and sign over a closed station), `Editor/GrayboxSceneBuilder.cs`
  (covers, the `ShopStage` object) and the regenerated scene.
- `Scripts/Dev/CornerTourRunner.cs` (new) and `scripts/visual-check.sh` (change: `corner` tour).
- Tests in `Tests/EditMode/` (see Test plan); `Resources/WhatsNew.json`; this spec.

## Out of scope
- Stage 2 and later (renting, the restaurant, card, menus, services, rivals in the game). Reaching the goal only
  shows a message.
- Shop name entry (needs a name filter, O6 OPEN): the name is fixed, "Your food corner".
- Steam Cloud, copying the save to other crew members (critique P4), a load list, deleting or renaming saves.
- Per-player progress (cosmetics, titles), player-set prices (037), new art (040 owns models), any `/sim` change.
- Saving items in hands, pans, baskets or on counters (a load starts clean, before the next service).

## Design

### Part A: the save
**What is saved** (the business, not the players):
```csharp
[Serializable] public sealed class ShopSave {
  public string format = "worktown-shop-save"; public int formatVersion = 1;
  public string gameVersion; public string buildHash;        // informational (Application.version, 047 BuildHash)
  public string saveId;                                      // 32 hex chars, random at New shop
  public string createdUtc, savedUtc;                        // ISO 8601 "O", from an injected clock
  public string shopName;                                    // "Your food corner" (fixed, O6)
  public ulong worldSeed;                                    // random at New shop
  public ClimbPart climb;   // int stage (1); string[] unlocks (["dish_fries"]); bool goalReached
  public MoneyPart money;   // long balance; long earnedTotal; long spentTotal  (the game's money unit, D23)
  public ClockPart clock;   // int day (the next day to play, >= 1); int servicesPlayed; long playSeconds
  public StockPart[] stock; // string slot ("shelf:potato"); int count
  public int hygienePercent;
  public StatsPart stats;   // int guestsServed, guestsMissed, starSum; long bestServiceEarned
}
```
- **Format:** UTF-8 JSON through `JsonUtility` (as `GameSettings`), indented, < 8 KB. `formatVersion` starts at 1.
- **Versioning:** `SaveMigrations` is an ordered list of steps `(fromVersion, Func<ShopSave, ShopSave>)`, run in order
  up to the current version. Empty in v1; a test registers a fake step. If question 17 moves money to cents, that
  spec adds the step "v1 to v2: money x 100".
- **Loading** (`ShopSaveCodec.TryParse(string) -> SaveLoadResult(ShopSave?, SaveError)`): `SaveError { None,
  TooLarge (> 1 MB), NotJson, WrongFormat, NewerVersion, Invalid }`. Out-of-range values are clamped and logged
  (balance 0..`EconomyLimits.MaxBalance`, counts 0..slot capacity, hygiene 0..100, day >= 1); unknown slots and
  unlock ids are ignored but kept on the next save; `stage` is clamped to 1 (the only stage this build has).
- **Where:** `Application.persistentDataPath/saves/shop-<first 12 of saveId>.json`; on Windows
  `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Worktown\saves\`. `-save-dir <path>` overrides the folder (tests,
  tours). Only the server (the host) writes; clients never write or read a shop save.
- **Atomic write** (`SaveStore`): write `<file>.tmp`, then replace `<file>`, keeping the old one as `<file>.bak`. A
  failed write leaves the last good file. A load that fails with `NotJson` or `Invalid` tries the `.bak`. Files are
  never deleted.
- **Autosave** (`AutosaveRules.ShouldSave(trigger, isServer, hasSlot, secondsSinceLastSave)`): on `ServiceEnd` (the
  shift closes, after its summary numbers), `GoalReached`, `HostClosing` (047 "Close the shop", before the notice),
  `AppQuit` (best effort) and `Periodic` (every 300 s while a service is open). Never on a client, never without a
  slot.
- **Applying a save** (server, before the first player spawns): the shop wallet gets `money.balance` (no grant), stock
  counts, hygiene, stage, goal and stats are set; the economy starts fresh (`Initial()`, seed `worldSeed ^ day`,
  ticked to 08:00) and the shown day is `clock.day` (a day offset). A save written during a service loads at the start
  of that day's service, with the money and stock it had (time runs only while the game runs; nothing decays).
- **Title screen** (047): `hasSave` = `SaveStore` finds a file that parses. Continue loads the newest by `savedUtc`
  (ties: file name ordinal) and shows "Day 4 · $230". New shop always makes a new file and never overwrites. A
  broken newest save shows an error card ("This save could not be loaded. Your file is kept.") or, for
  `NewerVersion`, "This shop was saved by a newer version of Worktown."; Back returns to the menu.
- **Command line:** `-new-shop` and `-continue` host with a save and skip the menu. `-host`, `-connect`, `-server` and
  the tours keep working as today: no save and an **open kitchen** (every station, dev and tests only).

### One shop wallet (pillar 2: the money belongs to the group)
`EconomyRules.AccountId` returns one shop account for every player. The server opens it once: the scaled starting
money for a new shop, the saved balance on Continue, `EconomyRules.StartingGrant` in the open kitchen. A joining or
rejoining player gets nothing. Every balance change publishes `Wallet.Balance` to every player. Tests that pinned
per-player wallets are updated and listed in Implementation notes.

### Part B: the food corner (stage 1)
- **The dish:** the 052 stage 1 dish (fries; open question 1). Guests order only it. `GuestOrders`/`Menu` draws stay
  the same (same seed, same draw count); the game overrides the results: dish = fries, to go (no eat-in), `PaysByCard
  = false` (cash with change, 032).
- **Open stations** (`ShopStageRules.IsOpen(stage, StationKind, slot)`): the shelf's potato slot, the cutting
  board, the fryer, the plate shelf, the front counter, the register's cash tray, the trash, and whatever the
  hygiene score needs. Closed: stoves, griddle, spit, assembly, drink dispenser, fridge, the ordering terminal, the
  sink, the card terminal, every other shelf slot. The server refuses a closed station or slot with
  `CookOutcome.NotOpenYet` ("Opens with your restaurant"); the client shows the same reason in the prompt. Each closed
  station gets a `ClosedCover` (palette colours only, D32).
- **Stock without ordering:** at each service start the potato slot fills up to its capacity, paid from the shop
  wallet at the 034 bulk price, only as many as the money covers (the supplier's fixed drop; no terminal yet).
- **Scaled amounts** (`FoodCornerRules.Scale(simMoney, gameDishPrice, simDishPrice) = ceil(simMoney *
  gameDishPrice / simDishPrice)`): `gameDishPrice = PriceRules.DemandPayout(snack market Ceiling, dish base quality)`.
  Scaled from 052: the rent goal (`rent_restaurant` cost), the pitch fee per service (stage 1 upkeep, paid at the
  service end, the wallet clamped at 0), and the starting money.
- **Goal:** `ShopStage` syncs `Stage` (byte), `GoalMoney` (long) and `GoalReached` (bool). The HUD shows "Save for the
  rent: $230 / $400" (the wallet against `GoalMoney`); the shift summary adds the same line with a percent. At a
  service end with balance >= goal, `GoalReached` becomes true once: "Rent goal reached! Your first restaurant comes
  in a later update." Play goes on; the money stays.
- **Tutorial:** `TutorialSteps.ForStage(1)` keeps only the fries steps (potato, cut, fry, tip onto a plate, serve, cash)
  and the tutorial's first guest is the fries guest, to go, paying cash.
- **Words:** every new text passes the D27 word check (the existing catalog-text test gains the new strings); nothing
  refers to the past (D28).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes. The RPC and connection surface tests change only by the three `ShopStage`
      synced fields (documented); no new RPC or broadcast.
- [ ] AC2: Codec: a save with every field set round-trips equal; `NewerVersion`, `NotJson`, `WrongFormat`, `TooLarge`
      and `Invalid` are each produced by a fixture; out-of-range values are clamped; a fake migration step runs once,
      in order.
- [ ] AC3: Store (fake `ISaveFiles`): a write that throws midway leaves the old file readable; a corrupt file loads
      from `.bak`; newest is chosen by `savedUtc`, then file name; New shop never overwrites an existing file.
- [ ] AC4: `AutosaveRules`: each trigger saves on the server with a slot; a client or a run without a slot never
      saves; `Periodic` only after 300 s while a service is open.
- [ ] AC5: Wallet: two players, a sale by either changes the one shop balance and both wallets show it; a third
      player joining and a rejoin add no money; the starting money is paid once per new shop.
- [ ] AC6: Stage 1 rules: `IsOpen` matches the open list exactly; a request to each closed station is refused with
      `NotOpenYet` and changes nothing; with the same seed, stage 1 guests draw the same patience values as the open
      kitchen, but always order fries, to go, in cash; the card terminal refuses.
- [ ] AC7: Amounts: `Scale` is tested with the default catalog and the current snack market; the rent goal, the
      pitch fee and the starting money equal their scaled 052 values; `GoalReached` flips once and survives a save
      and load.
- [ ] AC8: Save and continue (server test): serve 3 guests, end the service, the written file's balance, day,
      stock, hygiene and stats equal the live values; applying it to a fresh server restores them with no grant.
- [ ] AC9: The open kitchen is unchanged: `bash scripts/visual-check.sh --tours look,walk,cook,menu` passes with 0
      Exception lines.
- [ ] AC10: Visual check `--tours corner` (`-save-dir` in the tour folder): title, New shop, the corner with covers,
      2 fries guests paid in cash, the HUD goal line, a closed-station refusal, the service summary with the goal,
      Close the shop, the title showing Continue with its line, Continue, the same balance and day. 0 Exception lines.
- [ ] AC11 (manual, human): play stage 1 alone and with a friend, quit, and continue another day.
- [ ] AC12: `WhatsNew.json` has an entry; the D27 word test covers every new string.

## Test plan
- EditMode: `ShopSaveCodecTests` (AC2), `SaveStoreTests` (AC3, fake `ISaveFiles` and a fixed clock), `AutosaveRulesTests`
  (AC4), `ShopWalletTests` (AC5), `ShopStageRulesTests` and `FoodCornerRulesTests` (AC6, AC7), `SaveServiceTests`
  (AC8), updated `RpcSurfaceTests`, `ConnectionSurfaceTests`, the tutorial step tests and the D27 word test.
- Test data: save fixtures as string constants in the tests (v1 full, v1 minimal, version 99, broken JSON, 2 MB).
- Commands: `bash scripts/verify.sh`; `bash scripts/visual-check.sh --tours corner --out .verify/visual053`, then
  `--tours look,walk,cook,menu`.

## Human steps
- AC11 play-test.
- Before Early Access: set the real company name in Player Settings (today "DefaultCompany"). The save folder then
  moves, so do it before outside players have saves, or ask for a migration spec.

## Open questions (recommended default in brackets)
1. The one dish. [fries: shelf, board, fryer, plate, register; it fits 1-4 cooks. Alternative: the falafel wrap
   (fryer plus assembly), richer but more stations]
2. One shop wallet for the whole crew, tips included. [yes, design v1 pillar 2 and critique P14]
3. Stage 1 stock. [a fixed drop that fills the potato slot at each service start, paid at the bulk price.
   Alternatives: pay per take at the grocer price (before 034), or the ordering terminal from the start]
4. Pitch fee at stage 1. [yes, the 052 stage 1 upkeep, scaled; it makes saving a choice, not a wait]
5. Save slots. [every New shop is its own file, Continue takes the newest, no load list yet]
6. Autosave moments. [service end, goal reached, Close the shop, quit, every 5 minutes during a service]
7. Loading a save made during a service. [restart that day's service with the saved money and stock]
8. The open kitchen. [only for `-host`, tours and tests; players always start at the food corner]
9. Shop name. [fixed "Your food corner" until O6 (moderation) is decided]
10. Steam Cloud and copies to crew members. [later; needs a real Steam app id]

## Implementation notes
Built 2026-11-11 on `spec/053-food-corner` (main merged in with `--no-commit`, so 052's `ClimbCatalog` is there; the merge is staged,
053's own changes are not). The 054 review fixes are folded into this round (see spec 054, "054 review fixes").

**Design as built**
- *Open kitchen vs food corner:* `ShopStage.Stage` 0 is the open kitchen (every station: `-host`, `-server`, the tours, tests), 1 the
  food corner. `NetBootstrap.HostStart` (`Save/ShopStart`: OpenKitchen, NewShop, Continue) says how the next hosted game starts; the
  title's New shop and Continue, `-new-shop` and `-continue` set it; `SaveService` applies it when the server starts (before the first
  player spawns).
- *One shop wallet:* `EconomyRules.AccountId` returns `ShopAccount` ("shop") for every player, so every existing money step (grocer,
  portions, pours, supply orders, sales, guest payments, refunds) works on the one account unchanged and keeps its charge-once and
  refund-backstop guarantees. `EconomyService` publishes the balance to every player's `Wallet` after every step. The account opens once
  per game: `StartGame(seed, day, opening)` (a new shop's scaled starting money, or a save's balance: no grant), or in the open kitchen
  with `StartingGrant` at the first spawn; `TryOpenAccount` refuses every later join or rejoin.
- *New game, fresh economy:* `StartGame` makes a new `TownEconomy` (seed `worldSeed ^ day`, ticked to 08:00) and shows the saved day
  through a tick offset of (day - 1) x 24 (`EconomyService.Tick` and `GameMinute` include it, so the shift, the clock text, deliveries
  and hygiene all see the same day). The stock and the cleaning reset on the new economy as before (`EnsureGame`, now public, so the
  save's counts and hygiene are applied after the reset, never undone by it).
- *Stage 1 rules* are pure (`ShopStageRules`, `FoodCornerRules`); `ClosedTargets.IsClosed` is the one component test the server
  (`KitchenService.TryUseStation`, `TryUseVessel`, the place guard) and the client prompt (`PickupInteractor.AimClosed`, HudView)
  share. Guests: `CustomerService` keeps every draw (same RNGs, same count) and `FoodCornerRules.StageOrder` overrides the result
  (fries, to go, no drink, cash, no modifiers); only the fries' market sends guests (`WantsMarket`), so a sale never makes a guest of
  another market leave. The register refuses the card terminal (`RegisterDesk.AcceptsCard`).
- *Service moments:* `CustomerService.ServiceOpened` / `ServiceClosed` (new events, guarded). `SaveService` drives them:
  `ShopStage.ServerServiceOpened` (the supplier's drop: `StockRoom.Drop`, the potato slot up to capacity, paid once from the shop
  wallet at the bulk price of n units, only what the money covers, refund backstop) and `ServerServiceEnded` (pitch fee, wallet clamped
  at 0, then the goal flips once), then the autosave (ServiceEnd, or GoalReached when it flipped).
- *Amounts (default catalogs):* game dish price 460 (snack ceiling 400 at 650 permille), sim 4, so x 115: starting money $2,875,
  pitch fee $575 per service, rent goal $34,500. The spec's "$230 / $400" was an example.

**Save format:** `ShopSave` as in the Design, `formatVersion` 1, JsonUtility, indented (about 3 KB). Stock slots are named
`shelf:<id>`, `fridge:<id>`, `stock:<id>` (`ShopSaveCodec.SlotOf`). `SaveError` has the five errors plus `None`. Files:
`<folder>/shop-<12 hex>.json`, `.tmp` then `File.Replace` with `.bak`. The folder: `-save-dir`, else none for any scripted run
(`LaunchArgs.IsScripted`: tours, observer, bots, metrics, the 049 test flags), else `persistentDataPath/saves`
(`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Worktown\saves`; the company name is still "DefaultCompany", O1, not changed).
The captured balance adds the cost of supply orders still on their way (they are refunded when the server stops).

**Deviations and scope (reviewer, please check)**
- Touched outside the listed files, each a few lines: `Shop/CleaningRoom.cs` and `Shop/CleaningService.cs` (the hygiene restore;
  `ShopHygiene` is only the synced byte), `Shop/SupplyService.cs` (the drop, the shop's supply wallet, `EnsureGame` public),
  `Tutorial/TutorialRunner.cs` (it picks `TutorialSteps.ForStage`), `Dev/TourRunner.cs` (skips the corner tour), `Climb/ClosedTargets.cs`
  (new: the shared test lives outside the NetworkBehaviour so tests can call it without FishNet), `Save/ShopStart.cs` (new enum).
- Not changed although listed: `StockCounts.cs`, `ShopHygiene.cs` (capture and restore go through StockRoom and CleaningRoom),
  `Menu.cs` and `TutorialOrders.cs` (the stage override in `FoodCornerRules.StageOrder` covers the tutorial's scripted guest too: at
  stage 1 only its fries guest comes, taken to go).
- `AutosaveRules.ShouldSave` has a fifth parameter `serviceOpen` (Periodic needs it). `SaveTrigger.Created` is a sixth trigger: New
  shop writes its file at once, so Continue exists before the first service ends.
- `-continue` without a loadable save logs the error, keeps the file and opens a new food corner (the game always starts).
- Tests updated because they pinned per-player wallets: `EconomyDemoTests.AccountIdAndMoney` (now the shop account) and
  `StockRulesTests.Money_NoMoneyNoIngredient_TheGrocerIsPaidThePrice` (two engine accounts by literal ids).
- AC5's "both wallets show it" is covered at the ledger level (`ShopWalletTests`: every player id is the one account); the publish to
  every Wallet is `EconomyService.PublishWallets` and is seen in the corner tour, not in an EditMode test (it needs spawned objects).
- AC8 is `SaveServiceTests` over the real economy, stock room, cleaning room and store on real temp files (SaveService's pure parts:
  `NewSave`, `Fill`, `AddService`, `CountsOf`, `SeedOf`), not a running FishNet server.

**Follow-ups:** boxes lying at the back door or waiting are not saved (a load starts clean; stage 1 has none). The menu tour now ends
in a food corner (its New shop), without a save (no `-save-dir`). Inbox question 17 (cents) stays open: its spec adds the v1 to v2 step.

### 053 review fixes (2026-11-11, after db5beb3)
1. **TransformGoals never throws** (must-fix): the body is in try/catch; the first failure logs one warning and turns the helper off
   for the run. The test now checks the shapes (the queue is an `ICollection`, the current goal a reference) and clears a real
   NetworkTransform added in EditMode (by reflection: the test assembly has no FishNet reference).
2. **One synced shop balance** (netcode): `Economy/ShopLedger.cs`, a global scene object with `SyncVar<long> Balance`; `EconomyService`
   publishes every change there once. `Wallet` keeps no SyncVar: it is a thin reader (`TryGetBalance`) for the HUD, the summary and
   the tours. Traffic no longer grows with N squared. Every money step is unchanged (still one sim debit, the refund backstops, server
   only). `ProtocolSurface.Revision` = 3; the ledger's sync line is pinned and no Wallet sync line may exist (`NetReview054Tests`).
3. **Pass-through by default** (inbox question 23, default A): see spec 054.
- `-continue` (or the title's Continue) with a broken save never opens a new shop: `NetBootstrap.FailSaveStart` exits a scripted
  run with code 3, otherwise the shop closes again (or never opens) and the title shows the save's error card.
- A newer-version save that is the only file still offers Continue (line "Saved by a newer version of Worktown"), so its card shows
  (`MenuRules.OffersContinue`, tested).
- `SaveStore` checks the file's length (1 MB) before reading it (`ISaveFiles.Length`; test: refused unread).
- `ShiftSummaryView` caches the stage and the local wallet (lookups at most once a second) and rebuilds the goal line only when its
  inputs change.
- `StockRoom.Drop`'s refund after a failed fill has a test (the charge goes through, the slot fills meanwhile, the payment comes back once).
- docs/architecture.md: "Every wire change bumps ProtocolSurface.Revision."

**The rent goal, measured (not changed).** The corner tour (one scripted cook, screenshots on) serves a fries guest every 82 s
(82.4 s and 164.8 s in `.verify/visual053b`); each guest paid about $615 (bill about $503 plus tip about $112), a potato costs $48
at the bulk price, the pitch fee is $575 per service, a service is 360 s plus the 15 s summary. From the starting $2,875 the goal
needs $31,625 more:
- one cook at the tour's pace: about 4.4 guests a service, about $1,920 net a service, **about 16.5 services, 1.7 h**;
- the stock ceiling: the potato slot holds 12 and stage 1 has no terminal, so at most 12 fries a service, about $6,235 net,
  **about 5.1 services, 0.53 h**, whatever the crew size (a crew of 2 or more reaches it).
Spec 052's stage 1 target (enter stage 2) is 0.75 to 1.5 h. A solo cook lands a little above it, any larger crew well below it,
and the slot's 12, not the goal, sets the crew's pace. The guests' demand per service was not measured (the tour served only 2).

### Follow-ups (2026-11-11 evening)
Branch `spec/followups-053-044-048` (builder), from the known limits and the review notes above. One verify for the 053, 044 and 048
follow-ups.
1. **Closed-station covers.** `Art/ClosedCoverRules` (pure) gives one fitted cloth box per closed station: the station's bounds plus
   everything resting on it (a pan, a pot: separate scene objects, found by `ClosedCover.TryRestingBounds`), 2 cm margin, 2 cm above
   the highest thing, a 12 cm skirt below the top. A small Cream sign card (0.44 x 0.075 m, Ink text about 2.6 cm high, it shrinks on
   a narrow cover) is pinned to the cook-side face. The cloth is Sage (`Closed_Cover.mat`, was Plaster, which read as grey); the card
   is a new `Closed_Sign.mat`. 10 covers. Tests: `FollowUpsEveningTests.CoverRules_...` and `BuiltScene_ClosedCovers_...` (in the built
   scene, nothing that rests on a covered station reaches out of its cloth; at least one station has a pan under it).
2. **Money display.** The shop balance cannot go below 0: every sim debit is refused with InsufficientFunds,
   `TownEconomy.AdjustBalance` keeps 0..MaxBalance and `PayFee` takes at most the balance. So the clamps are dropped (`ShopLedger.ServerSet`,
   `EconomyService.PublishWallets`, which now skips only the -1 "no account yet", `FoodCornerRules.GoalLine`, `MenuRules.ContinueLine`);
   a negative value would read "-$120" (no warning colour needed). The Continue card already shows the save point's balance (the tour
   checks it). The difference came from the drop: it is charged at the service start, and a Continue opens the next service at once.
   The HUD's line at the service start is now `FoodCornerRules.DeliveryText`: "Potatoes delivered -$96 (2 on the shelf)" (or "No
   potatoes delivered: ..."). The corner tour fails if the wallet after Continue is below the saved balance without that line.
3. **Menu tour.** Its own shop is now the open kitchen (`NetBootstrap.StartHost(ShopStart.OpenKitchen)`, as `-host`; AC9: the open
   kitchen is unchanged), not the title's New shop (a food corner that needs a save file; the corner tour presses New shop with its
   own `-save-dir`). The tour fails if its shop is not the open kitchen, if anything was saved, or if the title then offers Continue.
- Tests: new `FollowUpsEveningTests` (8 for all three specs; 4 for 053).
- **Runs (053, 044 and 048 follow-ups together).** `verify.sh` PASS: EditMode 1462/1462 (baseline 1454, +8), sim 483/483, gateway
  175/175. The scene was regenerated; the re-saved materials, the fryer basket wrapper prefab and the DefaultPrefabObjects reorder
  were reverted (only `Closed_Cover.mat` changes on purpose, `Closed_Sign.mat` is new). `BuildAll` succeeded (both builds).
  `visual-check.sh --tours look,cook,menu,corner --out .verify/visual-fu4`: look, cook and corner ok, with 0 exceptions. The menu
  tour failed only on a check I added (it expected the title page before hosting, but the menu was on the join page). I removed
  the check, rebuilt, and re-ran the menu tour (`.verify/visual-fu4b`): ok, 0 exceptions, "wrote 0 save(s); Continue on the title:
  False". Cells:
  - corner `03-closed-stove-prompt`: the stove's Sage cloth covers its pan, with the small sign card on the front;
  - corner `05-covers-left`: the griddle and fridge caps;
  - corner `318-continued`: "Potatoes delivered -$96 (2 on the shelf)" while the wallet reads $3,147 after Continue at $3,243;
  - menu `26-30`: the open kitchen, then the title with no Continue.
- **Review nits (2026-11-11).**
  - `BuiltScene_ClosedCovers_...` has an independent check: no renderer of the stove's Pan or Pot crosses the cloth's top face (renderer
    bounds against the cloth bounds, not the builder's rule).
  - `ClosedCoverRules`: the sign's `0.01f` / `0.02f` are `SignTopPad` / `SignTopGap`; the sign face is one helper
    (`FrontFace`, `FaceWidth`) used by the scene builder and `SignCentre`.
  - `FoodCornerRules.DeliveryText` with units > 0 and cost 0 leaves the amount out ("Potatoes delivered (2 on the shelf)", not "-$0"); tested.

## Blockers
(Builder fills this in after 3 failed attempts.)
