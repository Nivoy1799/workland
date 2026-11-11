---
id: 057
title: Stage 2, the first restaurant (rent, fast-food menu, card, stock ordering, cleaning, the stage 2 gate)
status: draft
owner: architect
builder: opus
---

# 057: The first restaurant

## Goal
The crew pays the rent goal and moves from the food corner (stage 1, spec 053) to its first rented restaurant, a
fast-food service (D34 stage 2): a small menu chosen by the crew, card payments, stock ordering at the terminal and
cleaning, rent and upkeep per service, and a visible stage 2 gate. Every amount is read from /sim `ClimbCatalog`, so
052b's tuning flows in without game changes.

## Depends on
D23, D27, D33, D34, D37; specs 032 (card), 033 (drinks), 034 (terminal), 035 (cleaning), 052 (`ClimbCatalog`), 053
(save v1, `ShopStage`, `FoodCornerRules.Scale`, covers, one shop wallet). Not 037 (prices stay the market's).
**Numbers warning:** 052b is not needed for the mechanics, but neither 052 nor 052b calibrates stage 2 to the game
(052b open question 3: the game serves about 4x fewer guests per service than the sim assumes). With today's catalog
the rent is $5,750 per service (sim $50 x 115) against about $2,700 net for a solo fries cook. **Recommended order:
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
- `Editor/GrayboxSceneBuilder.cs` (change: the goal board) and the regenerated scene.
- `Scripts/Dev/RestaurantTourRunner.cs` (new), `scripts/visual-check.sh` (change: `restaurant` tour).
- Tests in `Tests/EditMode/`; `Resources/WhatsNew.json`; this spec.
- No change in `/sim` (052b owns it), no change to specs 040, 046, 050, 051.

## Out of scope
- Stage 3 and later: buying the service licence, delivery, tables, staff. Reaching the gate only shows a message.
- NPC rivals and guest choice between shops (spec 058). Crew-size scaling of guests or rent (a 052b follow-up).
- `bulk_supplier` and `decor_1` (other stage 2 purchases): a later spec. Player-set prices and a price board (037).
- A stage 2 tutorial, a new restaurant building or new art (040), shop name entry (O6 OPEN, the name stays fixed).
- Copying saves to crew members, Steam Cloud, a load list.

## Design

### Where the restaurant is (recommendation)
**Recommended: the same graybox scene, as 053 built it.** Stage 1 is the kitchen with closed stations covered; at
stage 2 every station opens and its `ClosedCover` disappears (they already read the synced stage). The move is made
visible by the sign: the shop name changes from "Your food corner" to "Your restaurant", the feed says "Moving day:
the whole kitchen is yours", and the covers vanish at once.
Trade-off: a separate small corner area (a cart on the street) would make the move a real place change and status
(pillar 3), but it needs a second fryer and counter, new spawn points, a second queue path and a scene the 049 budget
must hold, and the stage 1 tours and covers would be redone. The same scene costs nothing new and keeps every 053
test. A separate cart can come with 040's art as a later spec.

### Numbers from the catalog (`RestaurantRules`, pure; every amount via `FoodCornerRules.Scale`)
`S = climb.StageOf(ClimbStage.FirstRestaurant)`; `scale(x) = FoodCornerRules.Scale(x, GameDishPrice, SimDishPrice)`.
- `RentPerService = scale(S.Upkeep)` (today $5,750); `UpkeepPerService(owned) = scale(climb.Upkeep(FirstRestaurant,
  owned) - S.Upkeep)` (channels and purchases; $0 today, rises later).
- `MaxDishes = S.MaxDishes` (4). `Recipes = climb.Purchases` with `Stage == FirstRestaurant` and `Effect.UnlocksDish !=
  null`, in catalog order; `RecipeCost(id) = scale(cost)` (today $17,250 to $28,750).
- Gate: `GateStarsTenths = S.GateStarsTenths` (35), `GateWindow = S.GateWindow` (5), `GateCost = scale(cost of
  S.GatePurchaseId)` (today $138,000).
- `RentReserve = FoodCornerRules.StartingMoney` (today $2,875); `OwedCap = 2 x RentPerService` (open question 4).
- No amount is hard-coded in /game. A test rebuilds a catalog with other values and sees them in every rule; the
  catalog is built in one test helper only, so 052b's constructor change is a one-line fix there.

### The move (stage 1 to 2)
- At stage 1 the goal board's row 0 reads "Rent the restaurant: $34,500" (the 053 goal). A press with the balance >=
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
RentPerService + UpkeepPerService`. Feed: "Rent and upkeep: -$5,750" and, when owed, "Rent owed: $3,050". While `owed >
0` every board purchase is refused ("Pay the rent first"); stock orders are always allowed. No bankruptcy; the balance
never goes below 0 (053).

### The stage 2 gate (how it is shown)
- `GateRules.ServiceStarsTenths(summary)`: 052 step 5 semantics, a missed guest counts 1 star at half weight:
  `w = 2 x Served + Missed`; `tenths = (20 x StarsTotal + 10 x Missed + w/2) / w`; a service with `w == 0` is skipped.
- `StarWindow`: the last `GateWindow` counted services (newest last, kept up to 8). `GateHolds = window full &&
  average >= GateStarsTenths && balance >= GateCost`.
- HUD (stage 2): "Next: service licence · stars 3.2 / 3.5 (last 5) · $41,200 / $138,000". The shift summary adds the
  same line plus five pips (one per window service, filled when >= 3.5). Board row 0 shows it too.
- At a service end where `GateHolds` and not `GoalReached`: `GoalReached = true` once, autosave `GoalReached`, feed
  "Licence within reach! Delivery and more services come in a later update." The licence is not bought in this build.

### The goal board (`StationKind.GoalBoard`, one new station, no new RPC)
A wall board inside by the front door (graybox panel, Cream with Ink text, D32). It uses the existing station-use
intent; the slot is the row. Rows: 0 the stage step; 1..4 the recipes. Stage 0 (open kitchen): every row refuses
(`NotOpenYet`). Stage 1: row 0 rents; rows 1..4 refuse (`NotOpenYet`). Stage 2: row 0 shows the gate (no action);
a recipe row not owned buys it (confirm, money, owed = 0); owned toggles it on/off (refused over `MaxDishes`: "The menu
holds 4 dishes"). Purchases and toggles apply from the next service start ("On the menu from the next service").
Server-side checks only; the client shows texts from the synced fields.

### Synced state and wire (`ShopStage`, server writes, no RPC)
New: `SyncVar<bool> MovePending`, `SyncVar<byte> RecipesOwned`, `SyncVar<byte> MenuNext`, `SyncVar<byte> MenuNow`,
`SyncVar<short> WindowAverageTenths`, `SyncVar<byte> WindowCount`, `SyncVar<long> RentOwed`. `ProtocolSurface.Revision`
+ 1; `RpcSurfaceTests` and `ConnectionSurfaceTests` change only by these lines (documented).

### Save format v2
`formatVersion = 2`; `climb` gains `bool movePending`, `long rentOwed`, `int[] starWindow` (tenths, newest last, at
most 8), `string[] menuOff` (owned recipe ids turned off). Migration v1 to v2 (`SaveMigrations`): fills the defaults,
keeps everything else. Codec clamps: `stage` 1..2 (`ClampSaved`), `rentOwed` 0..cap, window values 10..50, unknown
`menuOff` ids ignored but kept. A v2 file in a 053 build shows the existing "newer version" card. If question 17
(cents) is decided later, its step becomes v2 to v3.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; `git diff --stat main -- sim` is empty.
- [ ] AC2: `RestaurantRules`: with `CreateDefault()` rent $5,750, gate cost $138,000, four recipes with their scaled
      costs; with a test catalog (rent 80, window 3, stars 30, one recipe changed) every value follows.
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
- [ ] AC8: Save: a v1 fixture (053 format, stage 1, goal reached) migrates to v2 with defaults; a v2 fixture with every
      new field round-trips equal; stage 3 clamps to 2; a version 99 file is `NewerVersion`.
- [ ] AC9: Surface tests change only by the seven synced fields; no new RPC or broadcast; Revision + 1.
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
1. Where the restaurant is. [same scene, covers come off; a separate cart later with 040]
2. Who may spend the shop's money (rent, recipes). [any player, two presses within 4 s; the feed names who bought]
3. When the move happens. [at once between services, else at the end of the current service]
4. Rent the crew cannot pay. [owed, paid first; spending blocked; capped at 2 rents, the rest forgiven; the reserve
   ($2,875) is never taken, so stock can always be ordered. Alternative: the balance goes to 0 and the rest is owed]
5. Opening stock at the move. [none; every slot keeps its count, the first terminal order uses 034's delay]
6. Menu size and lemonade. [052's `MaxDishes` 4 including fries; lemonade stands for all drinks]
7. Dishes outside the 052 catalog (soups, salad, flatbreads). [not on the stage 2 menu; a later spec adds catalog dishes]
8. Rent split for display. [rent = the stage upkeep, upkeep = the rest; one charge]
9. Stage 2 numbers before calibration. [read as they are; 052b first, then a stage 2 calibration from AC12]

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
