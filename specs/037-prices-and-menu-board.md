---
id: 037
title: Street-food prices and a menu board (rescale the economy, set your own prices)
status: approved
owner: architect
builder: opus
---

> Inbox question 17 answered on 2026-11-11: cents (D39 revises D23). Build after 053 and 054; the climb catalog (052) must use the same scale.

# 037: Street-food prices and a menu board

## Goal
Inbox question 7, default A: today a wrap costs about $1,525 and a guest pays with a $1,900 note. That reads like a
luxury restaurant. This spec rescales all money to street-food values and lets the cook set dish prices on a menu board.

## Design
- **Cents internally.** All money in the sim and game is integer cents.
  - A wrap is about $9.00, fries $4.00, a drink $3.00.
  - Ingredients cost $0.30–2.00, packaging $0.05–0.20.
  - The player starts with $200.
  - Bulk, portion, floor, SurplusCap and anti-faucet rules carry over, scaled. Every pinned number is re-pinned with a
    written reason.
- **Cash and change.** Coins are 0.10, 0.20, 0.50, 1 and 2; notes 5, 10, 20. Change stays exact in cents (032 rules).
- **Menu board** (a station above the counter): the cook sets each dish's price in steps (±$0.50).
  - Guests compare it with a market reference: too expensive means fewer guests and lower tips; cheap means more
    guests and less money.
  - Deterministic demand curve in the sim.
- **Shop rating** from reviews unlocks more menu items over the days (the roadmap pillar 6 start).
- Existing guarantees stay: pay once, refunds, money from stock orders.

## Acceptance criteria
- [ ] AC1: `verify.sh`; RPC surface tests unchanged.
- [ ] AC2: Sim tests:
  - cents everywhere;
  - anti-faucet for every item at the new scale;
  - the demand curve;
  - determinism (every pinned value re-pinned with a reason).
- [ ] AC3: Visual check: the board, a guest paying $9.50 with a $10 note, change counted in coins.
- [ ] AC4 (manual, human): prices feel like a street-food shop.
- [ ] AC5: `WhatsNew.json`.

## Defaults to review
Every price above; ±$0.50 steps; the reference price is the start price; rating unlock thresholds.

## Implementation notes

Built 2026-11-11 on `spec/037-street-food-prices`, applied to today's code (053 shop wallet, 052 climb catalog).

**Scale rule (D39).**
- Economy catalog: one old luxury-scale dollar is one cent, so every integer stays. Ingredients read $0.35-1.50,
  packaging and cups $0.35-0.40, the snack market $1.00-4.00. Bulk, portion, floor, SurplusCap and anti-faucet carry
  over unchanged (B = $0.31).
- Climb catalog: its data was already street-food dollars (fries 4, wrap 9). `CreateDefault()` now uses
  `MoneyScale = CentsPerDollar` (100). The engine rounds revenue, ingredient cost, the menu's average reference and offset,
  and the idle-money mean to whole units, so the climb is the same at any scale. `climb-report.sh` before and after: identical
  text (the report prints net $/h in whole dollars). Test: `CentsGiveTheSameClimbAsWholeDollars`.
- Guests pay the menu board's price (reference = start price). Dishes the climb knows take the climb's price: fries $4.00,
  falafel wrap $9.00, seitan wrap $10.00, grilled veggies $8.00, lemonade $3.00. The rest are defaults in `MenuPrices`.
- Food corner: the scale is the board's fries over the climb's fries, which is 1 (was 115). Rent goal $300.00, pitch fee
  $5.00, start $25.00. The potato stays $0.60 ($0.48 bulk), so the corner earns about 10 % less per guest than before.
- Open kitchen grant: $200.00 (20000; 500 cents would be $5.00).

**Cash.** Tray of $0.10 to $20 (8 compartments; options shift: back 8, give 9, card 10). Guests hand $5/$10/$20 notes.
The bill and the tip are rounded down to whole $0.10 coins (`CashRules.ToCoins`), so change is always exact in coins.

**Demand.** The board reuses 052's `GuestChoice.Attraction` (price term only) against the outside option:
`MenuPricing.DemandPermille` scales a market's new NPC demand through `TickInput.DemandPermille`. It is applied before the
hour's division, so fractions carry over and the RNG draws stay the same. Above the reference, tips scale by the attraction
ratio. At the defaults, a $0.50 step either way earns less than the reference. Rivals are not in the game's model (spec 058).

**Saves.** Format version 2. `SaveMigrations.V1ToV2` converts money x 100 / 115 (old corner dollars to cents, overflow
safe), so progress to the goal stays exact ($34,500 becomes $300.00). Board prices that differ from the reference are saved
as `menu` (id, cents) and normalised on load.

**Wire.** `ProtocolSurface.Revision` 4. `Register.Tray` is now a ushort in cents (`MaxTray` $100). There are two new synced
fields on the `MenuBoard` scene object, `Prices` (SyncList<int>, cents) and `Selected` (byte). No new RPC: the buttons are
`RequestStation` options, so `RpcSurfaceTests` is unchanged.

**Deviations.**
- Packaging is not $0.05-0.20, because the anti-faucet rule needs every grocer price above B.
- The rating unlocks are not built: D34/D37 give the food corner one dish, and 052/057 unlock dishes as purchases. Open
  question for the human.
- AC3 tour guest: the cook tour's grilled-veggies guest is scripted (dev only, `TourOrders`) to pay cash with a $10 note and
  not tip. The tour raises the dish to $9.50 on the board, so the guest owes exactly $9.50 and the change is one 50c coin.
  Tips depend on play, so no real guest owes an exact amount.
- Old `Graybox_Cash1/2/5.mat` are now unused (not deleted).

### 037 review fixes (2026-11-11, after 572cae0)
1. **Price at join** (must-fix). A guest keeps a `BoardQuote` from when it joins: the dish price, the drink price and the tip
   share (`MenuBoardDesk.Quote`, stored on the guest in CustomerService). Its bill and tip use that quote, never the board at
   hand-over (`RegisterFlow.HandOver` takes the stored tip share). Test: `Review1_...`.
2. **A bill never exceeds the board** (must-fix). On board prices the share is `BoardBill.Permille`: the match share times the
   stars factor, capped at 1000. 032's order-over-item quality ratio stays only on the no-board path.
   - While re-measuring I found a 033 bug: a guest without a drink passed its line on as the whole pick, so the register applied
     the share twice. `GuestTab.Serve` now hands on a 100 % pick.
   - Tests: `Review2_...` (real items through `OrderRules.Fit`) and `Review2b_...`. The AC3 test now goes through Quote,
     BoardBill and LineBill. Its guest is served exactly as its patience ends, so no tip.
3. **Wrong dish** (must-fix). Billed on what was served: min(stored price, its own sim payout) × the wrong share
   (`BoardBill.Base`). Test: `Review3_...` (fries to a wrap guest).
4. **Quality** (should-fix, the orchestrator's call; the human gets an FYI). The bill is the stored price times the share times
   the stars factor: 1★ 500, 2★ 625, 3★ 750, 4★ 875, 5★ 1000. Tips are unchanged. Re-measured in the corner tour, one cook:
   4★ fries pay $3.50 + $0.70 tip, $4.20 a guest. Before the review it was $4.00 + about $0.90; before 037, $5.03 + $1.12 of
   the old scale.
5. **Unknown menu lines** survive a save. SaveService keeps them like the stock's unknown slots. Test: `Review5_...`.

Nits:
- `Graybox_Cash1/2/5.mat` are deleted.
- The demand permille's rounding is documented: less than one carry step per market and hour.
- `FoodCornerRules.GameDishPrice` reads the given climb catalog (`MenuPrices.ReferenceOf`).
- Left for 052b (sim/Runtime/Climb is not touched): `ClimbReport.Format` moneyScale, and a catalog check that money % MoneyScale == 0.

**AC3 in the tour.** Under fixes 1 and 4, an exact $9.50 needs a 5★ dish at a $9.50 join price. The tour raises the veggies
after their guest joined, so that guest pays $8.00 × its stars, with change in coins. The exact $9.50 with a $10 note is pinned
in EditMode.
