---
id: 022
title: Customer orders and a short shift (a real game loop)
status: approved
owner: architect
builder: opus
---

# 022: Customer orders and a short shift

## Goal
Make the kitchen a game you can play for 10 minutes. NPC customers (spec 014) no longer only want
"Meal?": each one orders a **specific dish** from a small menu ("Fries", "Potato soup", "Tomato
salad", "Onion soup", ...), shown as a ticket above their head and on a ticket rail in the kitchen.
You cook it and serve the plate at the counter. A matching dish pays the full price plus a tip for
stars; a wrong dish pays little and the customer is unhappy. A **shift** lasts one in-game day part
(08:00 to 14:00 by default); at the end a summary shows dishes served, money earned, average stars
and missed customers.

Written on 2026-11-07 for the overnight build the human asked for ("write as many specs now ...
then you build all of them").

## Depends on
- Specs 008/009 (economy, selling), 014 (NPC customers and queue), 021 (movable cookware, pot with
  several ingredients, plates sold at the counter).

## Scope
- `game/Assets/_Project/Scripts/Npc/`:
  - `Menu.cs`, `MenuDish.cs` (new, pure data): the dishes, see Design.
  - `OrderRules.cs` (new, pure): `Match(Item served, MenuDish ordered)` → Exact / Close / Wrong,
    and the pay factor.
  - `CustomerRules.cs`, `CustomerQueue.cs`, `Customer.cs`, `CustomerService.cs` (change: each
    customer gets an order; serving picks the first customer whose order matches best).
- `game/Assets/_Project/Scripts/Economy/` (change: pay factor applied to the sale; no new money source).
- `game/Assets/_Project/Scripts/Cooking/KitchenService.cs` (change only where selling hands the item
  to the customer service).
- `game/Assets/_Project/Scripts/UI/` (new `TicketRailView.cs`, `ShiftSummaryView.cs`; `HudView.cs`
  change: shift clock and "orders open").
- `game/Assets/_Project/Editor/GrayboxSceneBuilder.cs` (change: a ticket rail board above the counter).
- `game/Assets/_Project/Resources/WhatsNew.json`, tests in `Tests/EditMode/`, this spec.

## Out of scope
- New ingredients or sim operations; recipes beyond what 021 can cook.
- Customer patience animations, emotions beyond a short text.
- Persistent progression, levels, unlocks (later spec).

## Design
### Menu (pure data, deterministic)
Each dish is defined only by what the sim already produces: required base ingredients, required
operations (cut shape, fry/sauté/boil, mix), and the minimum doneness bucket. Starting menu:
| Dish | Needs |
|---|---|
| Fries | potato, cut sticks, fried (medium or long) |
| Potato soup | potato (any cut), boiled in a pot, medium or long |
| Onion soup | onion + one more ingredient, boiled together in a pot |
| Tomato salad | tomato slices + onion (any cut), mixed in the bowl, not heated |
| Fried apple | apple slices or halves, sautéed |
| Veggie mix | any 3 different ingredients, cut, plated together |
The builder checks each dish can be made with spec 021 and adjusts names or rules if not (noted).

### Match (pure, `OrderRules`)
- **Exact**: everything the dish needs is there → pay 100 % of today's sale price + a tip of
  10 % per star above 2.
- **Close**: right main ingredient, one step wrong or missing (e.g. sticks not fried enough) →
  60 %.
- **Wrong**: anything else → 25 %, the customer leaves unhappy ("That's not what I ordered").
- Served at the counter: the server matches the plate against the queue in order and serves the
  first Exact match, else the first customer (Close or Wrong). Deterministic.

### Orders and shift
- A customer's order is drawn with the server's seeded RNG (rule 7) from the dishes whose ingredients
  the crates sell. At most 6 customers queue (spec 014).
- Shift: from 08:00 to 14:00 in-game time (clock from spec 008). At 14:00 new customers stop, the
  queue leaves, and every player sees the summary for 15 s (and on F3): served, earned, average
  stars, missed. Then the next shift starts at 08:00 the next day.
- Ticket rail: a board above the counter shows the open orders as cards (dish name, waiting time).

### Network
- No new RPC: orders are server state synced through `Customer` SyncVars (dish id byte, mood byte)
  and a small SyncList for the rail. The summary is a TargetRpc/Observers broadcast of 4 ints.

## Acceptance criteria
- [x] AC1: `bash scripts/verify.sh` passes.
- [x] AC2: `OrderRules` tests: one Exact, one Close and one Wrong case per menu dish, built with real
      sim items (same helpers as the cooking tests).
- [x] AC3: Serving picks the first Exact match in queue order, else the first customer; pay factors.
- [x] AC4: Orders are deterministic for a given seed; only dishes whose ingredients are for sale.
- [x] AC5: Shift tests: no new customers after 14:00, summary numbers correct for a scripted shift.
- [x] AC6: Every menu dish can be cooked with spec 021's kitchen (a test cooks each one).
- [x] AC7 (2026-11-08 night: HUD shift line and open orders, summary panel via `-demo-shift`, ticket rail with 6 cards and dish names above the queue in third person; no exceptions): Screenshots: tickets above customers and on the rail; the summary panel (dev arg may
      fast-forward the clock in development builds).
- [ ] AC8 (manual, human): play one shift, serve at least 3 different dishes.
- [x] AC9: `WhatsNew.json` entry `spec-022`.

## Open questions (answered 2026-11-07: human approved all defaults, incl. the download)
1. **Menu** as in the table (builder may adjust to what 021 can cook)? **Default: yes.**
2. **Pay factors** 100 % + tip / 60 % / 25 %? **Default: yes.**
3. **Shift** 08:00 to 14:00 in-game, summary 15 s, then the next day? **Default: yes.**

## Human steps
- Approve; play one shift (AC8).

## Implementation notes
Built 2026-11-07/08 (builder, overnight). `verify.sh` PASS, Unity EditMode 492/492 (baseline 458). Not committed.

**Pure layer (Npc/).** `Menu` (six dishes, `ById`, `NameOf`, `CanMake`, `Available(market, forSale)`, `Draw(IRandom, market,
forSale)`: one `NextInt` per order, none for a market without dishes), `MenuDish` + `DishNeed` (ingredient, allowed cut shapes,
heat ops, minimum time bucket; dish flags NeedsMix, NoHeat, MaxIngredients), `OrderRules.Match` (walks the item tree: plate
and mix are looked through; per base part the last cut and last heat op; named needs claim their ingredient first, "any other"
needs then take a different one; misses are counted: 0 Exact, 1 Close if the main ingredient is there, else Wrong),
`OrderRules.PayPermille` (1000 + 100 per star above 2 / 600 / 250), `OrderMatch`, `Shift` (08:00-14:00, close counts the
waiting queue as missed, 15 s summary, `HoursToNextOpen`; time is passed in), `ShiftSummary` (served, earned, stars, missed,
average in tenths). `CustomerQueue.TryPick` (first Exact in queue order, else the first in line; no change) and `Remove`;
`QueuedCustomer.Dish`; `CustomerRules`: order seed 22, moods, ticket pack (dish byte + minutes << 8), texts, and a
`DesiredWishes(demand, hasDishes, shiftOpen)` overload. The four extra files (DishNeed, OrderMatch, Shift, ShiftSummary) are
new public types in the Npc folder (one type per file).

**Menu as built** (AC6 test cooks each one through KitchenState vessels and sells it with the real engine to its market):
| Dish | Rule as implemented | Market |
|---|---|---|
| Fries | potato, cut sticks, fried >= medium (60 sim s = 10 real s) | snack |
| Potato soup | potato, cut (any shape), boiled >= medium (300 sim s = 50 real s) | snack |
| Onion soup | onion boiled >= medium + one *different* ingredient boiled >= medium, mixed (pot with both) | meal |
| Tomato salad | tomato slices + onion cut (any shape), mixed, nothing heated | meal |
| Fried apple | apple halves or slices, sauteed >= short (burner mode button first) | snack |
| Veggie mix | three different cut ingredients (at most 4 kinds), plated (mixing allowed, heat allowed) | meal |
Adjustments: "any cut" is read as "cut, in any shape" (a whole boiled potato is Close for potato soup); every dish allows at
most N different ingredients, one more is one step wrong (stops "everything on one plate" from matching every order); for a
dish with 2+ parts "not mixed" counts only when there are 2+ parts (a boiled onion alone is Close for onion soup, one step
missing). Not tested: a veggie mix with a raw potato may be refused by the sim's quality minimum ("Nobody wants to buy that").

**Server flow.** Counter sale (`KitchenService.Serve`): before the sale `CustomerService.TryPick(plated, stars, ...)` (pure
read, in try/catch; a failure means "no customer"), then `EconomyService.TrySell(player, plated, permille)`: the sim sells as
before; only a sale to NPC demand is scaled (`TownEconomy.TrySell(account, item, permille)` adjusts the balance by
`pay - payout` and `Totals.PaidToPlayers`; the result's `Payout` is what was paid). Wholesaler sales and sales with nobody
waiting (or outside the shift) pay the plain sim price. After the sale is complete, `OnServed` (try/catch, as in 014): the
customer leaves with "Thanks! +$X" / "Close enough. +$X" / "That's not what I ordered. +$X". The feed line adds
"· Fries order: exact order".
Orders: drawn with a `SeededRandom(22)` owned by `CustomerService` (reset on server stop) from the dishes of the customer's
market that the crates can make. Drink customers no longer appear: no menu dish is a drink (deviation from spec 014's
"Drink?" customers; they come back with a drink dish).
Shift: `CustomerService` feeds the economy tick to `Shift` every hour. At 14:00 the queue walks out ("Kitchen closed...",
counted missed), the rail's `SummaryOpen` is true for 15 s, then `EconomyService.SkipHours(18)` moves the clock to 08:00 of
the next day at once (otherwise players would wait 18 real minutes; the spec says the next shift starts at 08:00) and the next
shift opens. The server starts at 08:00 (tick 8), so customers walk in on the first server frame (about 8 s walk).
"Earned" counts what served customers paid; "missed" counts customers who left unserved ("Too slow..." from the spec 014
demand reconcile, plus the 14:00 queue).

**Network.** No new RPC. `Customer`: `SyncVar<byte> Dish`, `SyncVar<byte> Mood`; label = dish name (ticket yellow) while
waiting, then the leave text coloured by mood. New `TicketRailView` (UI/, a NetworkBehaviour on a global scene object):
`SyncList<int> Tickets` (dish id + whole in-game minutes waited, refreshed once per second), shift flags and the live and last
shift numbers as int SyncVars. The spec mentioned "a TargetRpc/Observers broadcast of 4 ints" for the summary; SyncVars were
chosen instead so late joiners and F3 see it and "no new RPC" holds. Clients get dish ids, moods, minutes and whole numbers only.

**Client.** HUD: clock line "+  ·  shift until 14:00" / "·  kitchen closed", second line "Orders open: 3 · served 2 · missed 1"
(or "Next shift 08:00 · F3 shift summary"). `ShiftSummaryView` (UI Toolkit, inline styles, no USS change): top centre, shown
while the server's summary flag is on, F3 toggles it; it shows the last finished shift, or "This shift so far" when the open
shift already has served or missed customers.

**Scene.** `TicketRail` (32 scene network objects now: 31 + the rail) at (8.5, 2.75, 13.75),
dark board 2.5 x 1.15 m, title "ORDERS"/"KITCHEN CLOSED", 2 x 3 cream cards 0.74 x 0.4 m with text, no colliders; wired to
`CustomerService._rail`; `ShiftSummaryView` on the UI object. New materials `Settings/TicketRail.mat`, `TicketCard.mat`
(generated by the builder; Settings/ is not in the listed scope). The rebuild's DefaultPrefabObjects reorder and re-saved
`PlaceMarker.mat` / `Worktop.mat` were reverted; FishNet's "same assetPath hash of 0" errors appeared as expected. No prefab
changed.

**Tests.** `OrderRulesTests` (AC2 Exact/Close/Wrong per dish with real items, AC6 cook + sell each dish, pay factors, D27 names),
`CustomerOrderTests` (AC3 pick and pay, AC4 seed determinism, for-sale filter, no drink orders, demand at 08:00, tickets; AC5
shift hours, scripted shift summary), `TicketRailSceneTests`, helper `MenuKitchen`. `WhatsNewTests`: the spec 021 test no
longer asserts "newest" (spec 022 is), a spec 022 test was added.

**AC7 screenshots (dev args, development builds only).** `-demo-shift` is client-only: it opens the summary panel 25 s after
start with the last known numbers (the server's clock is never touched; a release client cannot change server time). Tickets
over customers and on the rail: the queue stands in front of the counter from the first server frame.
`<client exe> -connect <server> -view first -skip-whatsnew -demo-cookware -demo-shift -screenshot shot.png -screenshot-after 15 -screenshot-every 3`
(the spec 021 cookware demo walks along the kitchen row towards stove 2, so frames around 15-30 s face the counter, the queue
and the rail; from 25 s the summary panel is open at the top). Without `-demo-cookware` the view from the spawn shows the row
from about 18 m (rail text is small there). To check: the yellow dish names above the customers, the rail cards (text size and
fit on 0.74 m cards, card order = queue order, minutes counting up), the rail's height above the counter and that it does not
hide the counter label, the HUD lines, the summary panel layout.

**Known limits / follow-ups.** (1) Serving a dish of another market (fries to a meal customer) lowers that other market's
demand; the next reconcile may send one of its customers away ("Too slow...", counted missed). Same root as spec 014.
(2) The pay share adjusts the balance outside the sim engine; the sim's per-market `DemandPaidThisTick` still holds the
unscaled payout. (3) Waiting minutes count from the spawn (the walk in included) on server `Time.time`.

## Blockers
