---
id: 053
title: Stage 1 food corner (1 dish, walk-up guests, cash only, save for the rent) and the host's save file
status: draft
owner: architect
builder: opus
---

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
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
