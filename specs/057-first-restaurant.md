---
id: 057
title: Stage 2, the first restaurant (rent, fast-food menu, card, stock ordering, cleaning, the stage 2 gate)
status: approved
owner: architect
builder: opus
---

> Approved by the human on 2026-11-11 (inbox questions 25 and 26) with the recommended defaults of its Open questions.

# 057: The first restaurant

## Goal
The crew pays the rent goal and moves from the food corner (stage 1, spec 053) to its first rented restaurant, a
fast-food service (D34 stage 2): a small menu chosen by the crew, card payments, stock ordering at the terminal and
cleaning, rent and upkeep per service, and a visible stage 2 gate. Every amount is read from /sim `ClimbCatalog`, so
052b's tuning flows in without game changes.

## Depends on
D23, D27, D33, D34, D37; specs 032 (card), 033 (drinks), 034 (terminal), 035 (cleaning), 052 (`ClimbCatalog`), 053
(save v1, `ShopStage`, `FoodCornerRules.Scale`, covers, one shop wallet). Not 037 (prices stay the market's).
**Builds on 062a** (moved behind it with the human's approval, 2026-11-11): the restaurant is the default layout data,
not a fixed scene. Stations are spawned pieces with stable ids; the goal board is a layout piece.
**Numbers warning:** 052b is not needed for the mechanics, but neither 052 nor 052b calibrates stage 2 to the game
(052b open question 3: the game serves about 4x fewer guests per service than the sim assumes). **Updated 2026-11-12 to
today's code (037, 052b, D39, D40):** money is cents and the corner's scale is 1, so the rent is the catalog's $50 per
service for the 4-player reference crew, and D40's crew factors (measured at each service start) scale it: $12 solo, $200
for 16 (052b GAME NUMBERS). **Recommended order:
approve 052b first, build 057, run its measurement (AC12), then a small /sim spec calibrates stage 2.** Until then the
rent reserve and the owed cap below keep a small crew from getting stuck.

## Scope
Only `/game` (plus the tour hook in `scripts/visual-check.sh`). Paths under `game/Assets/_Project/`.
- `Scripts/Climb/` (new): `RestaurantRules.cs` (pure: stage 2 amounts from the catalog), `RestaurantMenu.cs` (pure: 052
  dish id to game dish, menu masks), `RentRules.cs` (pure: charge, reserve, owed), `GateRules.cs` (pure: service stars,
  star window, the gate check), `GoalBoardRules.cs` (pure: rows, texts, what a press does), `BoardConfirm.cs` (pure:
  the two-press confirm per player).
- `Scripts/Climb/` (change): `ShopStageRules.cs` (`FirstRestaurant = 2`, `HighestStage = 2`, stage 2 opens every
  station), `ShopStage.cs` (new synced fields, the move, rent at service end, board presses), `FoodCornerRules.cs`
  (`StageOrder` and `WantsMarket` delegate to `RestaurantMenu` for stage 2).
- `Scripts/Cooking/StationKind.cs` (change: `GoalBoard` appended last), `KitchenService.cs` (route `GoalBoard` presses to
  `ShopStage.ServerPressBoard`).
- `Scripts/Economy/EconomyService.cs` (change: `TrySpend(long amount, string what)`, an exact debit of the shop wallet,
  refused when short, published like every money step).
- `Scripts/Shop/RegisterDesk.cs` (change: `AcceptsCard` from the stage), `SupplyService.cs` (change: no drop at stage 2).
- `Scripts/Npc/CustomerService.cs` (change: the stage 2 menu filter, frozen at service start).
- `Scripts/Save/` (change): `ShopSave.cs`, `ShopSaveCodec.cs`, `SaveMigrations.cs` (v1 to v2), `SaveService.cs`
  (capture and apply the new fields), `SaveTrigger.cs` (`StageChanged`), `AutosaveRules.cs`.
- `Scripts/UI/HudView.cs`, `ShiftSummaryView.cs` (stage 2 lines), `GoalBoardView.cs` (new: the board's text, client).
- `Scripts/Net/ProtocolSurface.cs` (change: `Revision` + 1).
- `Resources/Layouts/default.json` (change: the `goal_board` piece, 062a's reserved catalog row, flagged for 062a's
  `Backfill` so older saves get it) and `Editor/PiecePrefabBuilder.cs` (change: its prefab).
- `Scripts/Dev/RestaurantTourRunner.cs` (new), `scripts/visual-check.sh` (change: `restaurant` tour).
- Tests in `Tests/EditMode/`; `Resources/WhatsNew.json`; this spec.
- No change in `/sim` (052b owns it), no change to specs 040, 046, 050, 051.
- **Scope additions (2026-11-12, the builder; needed because the goal board is a new station kind and stage 2 needs 037's
  board):** `Scripts/Climb/StageEnd.cs` (new), `Scripts/Art/GoalBoardArtRules.cs` (new), `Scripts/Art/ArtKey.cs`,
  `ArtCatalogRules.cs`, `ArtSizeTable.cs`, `Editor/ArtCatalogBuilder.cs` (the board's graybox art), `Scripts/Cooking/CookOutcome.cs`
  (four answers appended), `StationRules.cs` (the prompt), `Scripts/Net/PickupInteractor.cs` and `Net/Rules/InteractionRules.cs` (a
  board row is aimed and pressed like a menu board button), `Net/Rules/LaunchArgs.cs` (the `restaurant` tour), `Scripts/Shop/MenuBoardRules.cs`
  (stage 2 prices the 052 dishes and the drinks). `RegisterDesk`, `SupplyService` and `AutosaveRules` needed no change (see the notes).

## Out of scope
- Stage 3 and later: buying the service licence, delivery, tables, staff. Reaching the gate only shows a message.
- NPC rivals and guest choice between shops (spec 058). Crew-size scaling of guests or rent (a 052b follow-up).
- `bulk_supplier` and `decor_1` (other stage 2 purchases): a later spec. Player-set prices and a price board (037).
- A stage 2 tutorial, a new restaurant building or new art (040), shop name entry (O6 OPEN, the name stays fixed).
- Copying saves to crew members, Steam Cloud, a load list.

## Design

### Where the restaurant is (recommendation)
**Recommended: the same place, the default layout (062a).** Stage 1 is the default layout with closed stations
covered; at stage 2 every station opens and its `ClosedCover` (part of each piece prefab since 062a) disappears (they
already read the synced stage). The move is made
visible by the sign: the shop name changes from "Your food corner" to "Your restaurant", the feed says "Moving day:
the whole kitchen is yours", and the covers vanish at once.
Trade-off: a separate small corner area (a cart on the street) would make the move a real place change and status
(pillar 3), but it needs a second fryer and counter, new spawn points, a second queue path and a second layout the 049
budget must hold, and the stage 1 tours and covers would be redone. The same layout costs nothing new and keeps every
053 test. A separate cart can come with 040's art as a later spec.

### Numbers from the catalog (`RestaurantRules`, pure; every amount via `FoodCornerRules.Scale`)
**Updated 2026-11-12 (D39, D40):** the scale is 1 (cents). Every amount below is for the 4-player reference crew; at each
service start the crew factors of `CrewScaling.For(climb, players)` apply: running costs (rent, upkeep) x demand n/4, one-time
costs (the rent goal of stage 1, recipes, the licence) x n x h(n)/4, floored to whole dollars like the GAME NUMBERS. The
reserve does not scale. Today: rent $50, recipes $200 / $250 / $200 / $150, licence $1,200, reserve $25, owed cap $100,
stage 1 goal $300 (solo: $12, $27 / $34 / $27 / $20, $164, $25, $24, $41).
`S = climb.StageOf(ClimbStage.FirstRestaurant)`; `scale(x) = FoodCornerRules.Scale(x, GameDishPrice, SimDishPrice)`.
- `RentPerService = scale(S.Upkeep)` (today $50); `UpkeepPerService(owned) = scale(climb.Upkeep(FirstRestaurant,
  owned) - S.Upkeep)` (channels and purchases; $0 today, rises later).
- `MaxDishes = S.MaxDishes` (4). `Recipes = climb.Purchases` with `Stage == FirstRestaurant` and `Effect.UnlocksDish !=
  null`, in catalog order; `RecipeCost(id) = scale(cost)` (today $150 to $250).
- Gate: `GateStarsTenths = S.GateStarsTenths` (35), `GateWindow = S.GateWindow` (5), `GateCost = scale(cost of
  S.GatePurchaseId)` (today $1,200).
- `RentReserve = FoodCornerRules.StartingMoney` (today $25); `OwedCap = 2 x RentPerService` (open question 4).
- No amount is hard-coded in /game. A test rebuilds a catalog with other values and sees them in every rule; the
  catalog is built in one test helper only, so 052b's constructor change is a one-line fix there.

### The move (stage 1 to 2)
- At stage 1 the goal board's row 0 reads "Rent the restaurant: $300.00" (4 players; D40 scales it) (the 053 goal). A press with the balance >=
  the goal arms a confirm; a second press by the same player within 4 s (`BoardConfirm`) pays it
  (`EconomyService.TrySpend`) and sets `MovePending`.
- If no service is open, the move happens at once; otherwise at that service's end, after its pitch fee. The move:
  `Stage = 2`, unlocks += `rent_restaurant`, `GoalReached = false`, `GoalMoney = GateCost`, shop name "Your restaurant",
  the stage 1 drop stops, autosave `StageChanged`. Feed: "You rented the restaurant!" then "Moving day: the whole kitchen
  is yours".
- **Carries over:** the shop wallet (less the rent cost), every stock count (all slots), hygiene, day, play time,
  stats (guests, stars, best service), `saveId` and the file (same save, new version), unlocks. Nothing is granted.
- A stage is never lost (052 step 1).

### Stage 2 rules
- **Stations:** `ShopStageRules.IsOpen(2, ...)` and `IsOpenVessel(2, ...)` are true for everything (the menu limits what
  guests order, not what the crew may cook). The card terminal accepts cards (`RegisterDesk.AcceptsCard = stage != 1`).
- **Menu** (`RestaurantMenu`): fries always on (052b: the stage 1 dish stays). Mapping: `falafel_wrap` to
  `Menu.FalafelWrap`, `seitan_wrap` to `Menu.SeitanWrap`, `grilled_veggies` to `Menu.GrilledVeggies`, `lemonade` to
  drinks (the 033 taps; off means guests order no drink). An unmapped recipe id makes a test fail. Masks are bytes, bit i
  = recipe row i. A recipe must be owned to be on; on-count + 1 (fries) <= `MaxDishes`.
- **Guests at stage 2:** draws stay as in the open kitchen (same RNGs, same count). The menu is frozen at service start.
  `WantsMarket(2, m)`: m has an on dish. `StageOrder(2, ...)`: a drawn dish on the menu is kept with its modifiers; one
  not on the menu becomes the on dish of that market at index `drawnDishId % count` (modifiers cleared); the drink is
  dropped when drinks are off; eat-in and `PaysByCard` stay as drawn.
- **Stock:** no drop at stage 2; the crew orders at the terminal (034). The board and the HUD show "Order stock at the
  terminal" during the first stage 2 service.
- **Cleaning:** sink, dish return and trash as in 035; nothing new.

### Rent and upkeep (`RentRules`, at each stage 2 service end, after the summary numbers)
`Charge(balance, due, owed, reserve, cap) -> (paid, owedAfter, forgiven)`: `total = due + owed`; `paid = min(total,
max(0, balance - reserve))`; `owedAfter = min(cap, total - paid)`; `forgiven = total - paid - owedAfter`. `due =
RentPerService + UpkeepPerService`. Feed: "Rent and upkeep: -$50.00" and, when owed, "Rent owed: $30.50". While `owed >
0` every board purchase is refused ("Pay the rent first"); stock orders are always allowed. No bankruptcy; the balance
never goes below 0 (053).

### The stage 2 gate (how it is shown)
- `GateRules.ServiceStarsTenths(summary)`: 052 step 5 semantics, a missed guest counts 1 star at half weight:
  `w = 2 x Served + Missed`; `tenths = (20 x StarsTotal + 10 x Missed + w/2) / w`; a service with `w == 0` is skipped.
- `StarWindow`: the last `GateWindow` counted services (newest last, kept up to 8). `GateHolds = window full &&
  average >= GateStarsTenths && balance >= GateCost`.
- HUD (stage 2): "Next: service licence · stars 3.2 / 3.5 (last 5) · $412.00 / $1,200.00". The shift summary adds the
  same line plus five pips (one per window service, filled when >= 3.5). Board row 0 shows it too.
- At a service end where `GateHolds` and not `GoalReached`: `GoalReached = true` once, autosave `GoalReached`, feed
  "Licence within reach! Delivery and more services come in a later update." The licence is not bought in this build.

### The goal board (`StationKind.GoalBoard`, one new station, no new RPC)
A `goal_board` wall piece of the default layout, inside by the front door (graybox panel, Cream with Ink text, D32).
It uses the existing station-use intent (its station id is its piece id, 062a); the slot is the row. Rows: 0 the stage step; 1..4 the recipes. Stage 0 (open kitchen): every row refuses
(`NotOpenYet`). Stage 1: row 0 rents; rows 1..4 refuse (`NotOpenYet`). Stage 2: row 0 shows the gate (no action);
a recipe row not owned buys it (confirm, money, owed = 0); owned toggles it on/off (refused over `MaxDishes`: "The menu
holds 4 dishes"). Purchases and toggles apply from the next service start ("On the menu from the next service").
Server-side checks only; the client shows texts from the synced fields.

### Synced state and wire (`ShopStage`, server writes, no RPC)
New: `SyncVar<bool> MovePending`, `SyncVar<byte> RecipesOwned`, `SyncVar<byte> MenuNext`, `SyncVar<byte> MenuNow`,
`SyncVar<short> WindowAverageTenths`, `SyncVar<byte> WindowCount`, `SyncVar<long> RentOwed`. **Updated 2026-11-12:** plus
`SyncVar<byte> WindowPips` (the summary's pips need each window service, not only the average) and `SyncVar<byte> CrewPlayers`
(D40: the client derives every crew-scaled amount from the crew size of the last service start). `ProtocolSurface.Revision`
is 7 (spec 054b takes 6; coordinate at merge). The surface tests change only by these nine lines.

### Save format v4 (updated 2026-11-12: main is at v3 since 062a)
`formatVersion = 4`; `climb` gains `bool movePending`, `long rentOwed`, `int[] starWindow` (tenths, newest last, at
most 8), `string[] menuOff` (owned recipe ids turned off). Migration v3 to v4 (`SaveMigrations.V3ToV4`): fills the defaults and
backfills the goal board into the layout (`DefaultLayout.Backfill` with `goal_board` flagged), keeps everything else. Codec clamps: `stage` 1..2 (`ClampSaved`), `rentOwed` 0..cap, window values 10..50, unknown
`menuOff` ids ignored but kept. A v4 file in an older build shows the existing "newer version" card.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; `git diff --stat main -- sim` is empty.
- [ ] AC2: `RestaurantRules`: with `CreateDefault()` rent $5,750, gate cost $138,000, four recipes with their scaled
      costs (updated 2026-11-12 for D39/D40: rent $50, licence $1,200 for 4 players; solo $12 and $164); with a test catalog (rent 80, window 3, stars 30, one recipe changed) every value follows.
- [ ] AC3: `RentRules.Charge` literals: paid in full; short above the reserve; balance under the reserve pays 0;
      owed carried and paid first next time; owed capped, the rest forgiven; never a negative balance.
- [ ] AC4: `GateRules`: half-weight missed guests (literals); empty services skipped; the window keeps the newest 5;
      the gate flips once and survives a save and load.
- [ ] AC5: The move: a press below the goal refuses; one press only arms; a second press by another player does not
      confirm; the confirm after 4 s expires; during a service the move waits for the service end; carry-over fields equal
      before and after; the shop name changes; autosave `StageChanged` written.
- [ ] AC6: Stage 2 rules: every station and vessel open; the card is accepted; no stock drop; with the same seed the
      guests draw the same values as in the open kitchen, and every order is on the frozen menu; drinks off means no drink.
- [ ] AC7: Board: buy refused when short, when owed > 0, at stage 0 and 1 (rows 1..4); toggles respect `MaxDishes` and
      fries stays on; changes apply only at the next service start.
- [ ] AC8: Save: a v1 fixture (053 format, stage 1, goal reached) migrates to v2 with defaults (updated 2026-11-12: to v4); a v2 fixture with every
      new field round-trips equal; stage 3 clamps to 2; a version 99 file is `NewerVersion`.
- [ ] AC9: Surface tests change only by the seven synced fields; no new RPC or broadcast; Revision + 1 (updated 2026-11-12:
      nine fields with WindowPips and CrewPlayers; Revision 7, as 054b takes 6).
- [ ] AC10: `bash scripts/visual-check.sh --tours look,walk,cook,menu,corner` still pass with 0 Exception lines.
- [ ] AC11: `--tours restaurant` (a v1 fixture save with the goal reached, copied into the tour's `-save-dir`):
      Continue, rent at the board (two presses), covers gone, buy the falafel wrap, the next service serves a wrap paid by
      card, a terminal order, a washed plate, the summary with rent and the gate line, Close the shop, Continue at stage 2.
      0 Exception lines.
- [ ] AC12 (measurement): the restaurant tour logs per stage 2 service: guests chosen, served, missed, revenue, rent paid,
      owed; the builder writes them in Implementation notes for the stage 2 calibration.
- [ ] AC13 (manual, human): play the move and three stage 2 services alone and with a friend.
- [ ] AC14: `WhatsNew.json` entry; every new string passes the D27 word test; nothing refers to the past (D28).

## Test plan
- EditMode: `RestaurantRulesTests` (AC2), `RentRulesTests` (AC3), `GateRulesTests` (AC4), `ShopMoveTests` (AC5, pure
  parts plus `SaveService` over real temp files as in 053 AC8), `RestaurantMenuTests` and updated `ShopStageRulesTests`
  (AC6), `GoalBoardRulesTests` and `BoardConfirmTests` (AC7), updated `ShopSaveCodecTests` (AC8), updated surface tests
  (AC9), the D27 word test (AC14). One `TestCatalogs` helper builds custom `ClimbCatalog`s.
- Fixtures as string constants: v1 stage 1 goal reached, v2 full, v2 stage 3, version 99.
- Commands: `bash scripts/verify.sh`; `bash scripts/visual-check.sh --tours restaurant --out .verify/visual057`, then
  `--tours look,walk,cook,menu,corner`.

## Human steps
- Approve 052b first (recommended), then this spec. AC13 play-test. Read the AC12 numbers and say whether the rent feels
  right before the stage 2 calibration.

## Open questions (recommended default in brackets)
1. Where the restaurant is. [same place, the 062a default layout, covers come off; a separate cart later with 040]
2. Who may spend the shop's money (rent, recipes). [any player, two presses within 4 s; the feed names who bought]
3. When the move happens. [at once between services, else at the end of the current service]
4. Rent the crew cannot pay. [owed, paid first; spending blocked; capped at 2 rents, the rest forgiven; the reserve
   ($25) is never taken, so stock can always be ordered. Alternative: the balance goes to 0 and the rest is owed]
5. Opening stock at the move. [none; every slot keeps its count, the first terminal order uses 034's delay]
6. Menu size and lemonade. [052's `MaxDishes` 4 including fries; lemonade stands for all drinks]
7. Dishes outside the 052 catalog (soups, salad, flatbreads). [not on the stage 2 menu; a later spec adds catalog dishes]
8. Rent split for display. [rent = the stage upkeep, upkeep = the rest; one charge]
9. Stage 2 numbers before calibration. [read as they are; 052b first, then a stage 2 calibration from AC12]

## Implementation notes
Built 2026-11-12 by the Opus builder on `spec/057-first-restaurant` (cut from main 50a90fa). Not committed.

**Numbers (D39 cents, D40 crew factors; read from `ClimbCatalog.CreateDefault()`, scale 1).** Crew = players connected at each
service start (`ServerManager.Clients.Count`, clamped 1..16), synced as `CrewPlayers`; the goal, the recipe costs and that
service's rent are fixed with it. Running costs x demand n/4, one-time costs x n x h(n)/4, both floored to whole dollars
(`CrewScaling.ScaleMoney` / `ToReal` with `MoneyScale`), matching the 052b GAME NUMBERS:

| crew | stage 1 goal | rent per service | owed cap | falafel / seitan / veggies / drinks | licence |
|---|---|---|---|---|---|
| 1 | $41 | $12 | $24 | $27 / $34 / $27 / $20 | $164 |
| 4 | $300 | $50 | $100 | $200 / $250 / $200 / $150 | $1,200 |
| 16 | $1,200 | $200 | $400 | $800 / $1,000 / $800 / $600 | $4,800 |

Reserve: the starting money, $25, unscaled (stock prices do not scale). Upkeep beyond the rent: $0 with today's catalog. Gate:
3.5 stars over the last 5 counted services. MaxDishes 4 with the fries.

**Deviations and decisions (reviewer, please check):**
- **Stage 1's goal is now crew-scaled** (052b game-side follow-up 1, as the coordinator asked: D40 applies to the rent goal).
  Progress is the real balance against the goal of the crew at the last service start, not D40's "fraction of the goal" (a
  crew that saved for $300 and then plays one service solo reaches the solo $41 goal). The pitch fee ($5) and the stage 1 drop
  are still 053's (not crew-scaled; follow-ups 2 and 4).
- **Wire: nine fields, not seven.** `WindowPips` (the summary's pips need each window service) and `CrewPlayers` (D40: the client
  derives every crew-scaled amount). `Revision` 7 (054b takes 6; re-pin at merge). The goal board adds a spawned piece prefab
  (NetworkObject, registered in DefaultPrefabObjects, appended to main's order). No RPC, no broadcast.
- **Save v4** (main was at v3): `V3ToV4` fills the new fields and backfills the goal board into the saved layout
  (`DefaultLayout.Backfill`, flagged by `SaveMigrations.AddedInV4`); v2 to v3 already brings default.json, which now holds it.
  `rentOwed` clamps to two rents of a 16-player crew ($400); window values to 1.0..5.0, at most 8. A save written while the move
  was pending moves in at the load (the rent was already paid). The shop's name follows the stage ("Your restaurant").
- **The move** happens at once between services, else at the service end after the pitch fee (save trigger `StageChanged` either
  way, through `StageEnd.Moved` or `ShopStage.StageChanged`).
- **Board answers** are four new `CookOutcome` values (enum values are not hashed): `ConfirmAgain`, `PayRentFirst`, `MenuFull`,
  `MoveWaits`; the HUD toasts them. A bought recipe goes on the next menu when it fits, else stays off.
- **Stage 2 menu board** (037) prices the fries, the three 052 dishes and every drink, so a recipe's price is set before it goes on.
- **Placement:** the goal board hangs on the back wall over worktop 2 (x 4.60, z 14.91, 1.05-1.55 m high), between the fridge and
  the griddle, every row in reach from the aisle; graybox WoodDark frame and Cream face with Ink text. No front door exists in the
  graybox, so "by the front door" became "by the cooks' worktop". 1.8 cm deep (the catalog row is 30 mm; within the footprint test's
  10 mm) so it stays clear of the back line and the wall.
- **Scope additions** (listed in Scope): the board is a new station kind, so the art key and graybox, the prompt, the aim (a row is
  pressed like a menu board button), the tour's launch argument and the menu board's stage 2 lines. `RegisterDesk` (the card follows
  `ShopStageRules.IsOpen` through CustomerService), `SupplyService` (only stage 1 drops, in ShopStage) and `AutosaveRules` (every
  trigger but Periodic saves) needed no change.
- **/sim:** `PieceCatalog`'s `goal_board` row has `HasStation = false`; nothing reads it today, but 062b's reachability rule will skip
  the board until a /sim change flips it.
- **Existing tests adapted** (new kind, piece, save version, wire revision; none weakened): ShopStageRules (GoalBoard open),
  CookingRules (the kind list), ArtCatalog (a goal board check, 19 stations), ScaleScene (its height), DefaultLayoutParity (the board
  is new: counted apart), PieceFootprint (the reserved goal board is now placed), PrefabRegistration (20 types), LayoutSave and
  LayoutSpawn (28 pieces, v4), ShopSaveCodec (stage 3 clamps to 2, version 4), MenuBoard (v4, revision 7), LayoutSurface and
  NetReview054 (revision 7), FoodCornerSurface (the nine fields).
- **AC11 (restaurant tour) is partial:** it continues a v1 fixture (copied by `visual-check.sh` into `<out>/restaurant-saves`),
  rents with two presses (ConfirmAgain, then Ok), moves at the service end, checks every station open, buys the falafel wrap
  during the summary, orders at the terminal, checks the next service's frozen menu and that every guest orders from it, shoots
  the summary with the rent and the gate line, closes and continues at stage 2. It does **not** cook a wrap, take a card payment
  or wash a plate: the cook tour covers those mechanics in the open kitchen, which stage 2 matches (every station open).
- **Runs.** `verify.sh`: EditMode 1553/1553 (baseline 1514, +39), sim 833/833, gateway 216/216 (one run had a gateway flake,
  `AuthRateLimitTests.InvalidConfigFailsStartupNamingTheVariable`; nothing here touches the gateway; it passed alone twice and in
  the final run). `BuildAll` succeeded (both builds). `visual-check.sh --tours look,cook,menu,corner,restaurant --out
  .verify/visual057`: all ok, 0 Exception lines, 0 crash log entries; every host logged "[Layout] spawned 28 pieces and 9 items".
  The corner tour's solo goal read "$22.12 / $41.00". Regeneration churn (re-saved materials, the fryer basket wrapper, the 19
  piece prefabs, the scene, DefaultPrefabObjects' order) was reverted; only the goal board's prefab, art wrapper, two materials,
  the ArtCatalog and PiecePrefabs rows and one appended DefaultPrefabObjects entry are new.
- **AC12 (one solo stage 2 service of the tour, nobody cooking):** guests 6, served 0, missed 6, revenue $0.00, rent due $12.00,
  rent paid $12.00, owed $0.00, stars 1.0 (window 10). This only proves the log line (`[Stage] restaurant service: ...`); the
  calibration needs AC13's played services, whose lines land in the host's log.
- **Cells:** restaurant `02-corner-goal-board` (the board over worktop 2), `03-rent-armed` ("Press again to pay"), `05-summary-moving-day`,
  `08/09-covers-gone`, `07-recipe-bought`, `12-summary-rent-gate` ("Rent and upkeep: $12.00 per service", the gate line, pips
  "o - - - -", feed "Rent and upkeep: -$12.00"), `14-continued-restaurant`; look `06-griddle-first` shows the board's corner.

### 057 review fixes
Built 2026-11-12 on top of 56c482c and 2223270, after merging main (054b, revision 6) into the branch. 057 stays revision 7.
1. **Must-fix: the crew was solo right after a load.** One-time costs (the rent goal, recipes, the licence) and the stage's goal
   now use `RestaurantRules.OneTimePlayers` = max(players at the last 3 service starts, players connected now), clamped 1..16
   (the coordinator's default; inbox question 32). It is re-measured at a load, at each service start and end, and in
   `ServerPressBoard` before `Decide` (then `Push`, so clients see the prices charged). The rent stays at the crew of the
   service's start (a running cost, D40). The last 3 starts are saved as `climb.crewStarts` in v4 (the codec keeps the newest 3,
   each 1..16; `V3ToV4` sets it empty). `CrewPlayers` syncs the one-time crew. A tenth synced field, `RentLast`, carries the
   rent the last service end paid, so the summary shows what was really charged, not a price derived from the crew.
   Tests: a load with one player, then 4 connect: a press charges $200; 4 leave: the 4-player licence holds for 3 services and
   then drops; a short drop to 1: still the 4-player price; a load keeps the saved crew before anyone joins.
2. **Second confirm on a row just bought by another player:** the press disarms and does nothing (no toggle). Tested.
3. **Two players confirming the rent in one window:** tested. It is paid once, and the second confirm gets `MoveWaits`.
4. **No soft-lock at the move:** renting needs the balance to cover the rent goal plus the $25 reserve. The row reads "Rent the
   restaurant: $41.00 + $25.00 kept for stock (press twice)". Tests adapted.
5. **AC6:** `ShopStageRules.DropsStock` (only stage 1), which ShopStage uses. A test shows a stage 2 service start drops and pays
   nothing.
6. **Nits:**
   - `V3ToV4`'s doc is single again, and `V2ToV3`'s doc is back on its method.
   - `RestaurantMenu.StageOrder` documents that a market with no dish on the menu sends no guest (`WantsMarket`).
   - `BoardConfirm` is disarmed when a player disconnects (`ShopStage.ServerPlayerLeft`, from `OnRemoteConnectionState`).
7. **Merge:** `ProtocolSurface` keeps main's revision 6 line and adds 057's line for 7. `NetReview054Tests` pins 7 and refuses
   4, 5 and 6. `LayoutSurfaceTests` and `MenuBoardTests` pin "revision 7".
8. **Checks:**
   - `verify.sh` is green: EditMode 1589/1589 (with main's 054b tests and 13 new ones), sim 833, gateway 216.
   - `BuildAll` succeeded.
   - `visual-check.sh --tours restaurant,corner,cook --out .verify/visual057b` passed with 0 Exception lines. The corner's solo goal reads
     $22.12 / $41.00; the restaurant tour has 0 failures.
   - DefaultPrefabObjects came back from the runs reordered only (no new entries), so it was reset to the committed order.

## Blockers
(Builder fills this in after 3 failed attempts.)
