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
- [ ] AC1: `bash scripts/verify.sh` passes.
- [ ] AC2: `OrderRules` tests: one Exact, one Close and one Wrong case per menu dish, built with real
      sim items (same helpers as the cooking tests).
- [ ] AC3: Serving picks the first Exact match in queue order, else the first customer; pay factors.
- [ ] AC4: Orders are deterministic for a given seed; only dishes whose ingredients are for sale.
- [ ] AC5: Shift tests: no new customers after 14:00, summary numbers correct for a scripted shift.
- [ ] AC6: Every menu dish can be cooked with spec 021's kitchen (a test cooks each one).
- [ ] AC7: Screenshots: tickets above customers and on the rail; the summary panel (dev arg may
      fast-forward the clock in development builds).
- [ ] AC8 (manual, human): play one shift, serve at least 3 different dishes.
- [ ] AC9: `WhatsNew.json` entry `spec-022`.

## Open questions (answered 2026-11-07: human approved all defaults, incl. the download)
1. **Menu** as in the table (builder may adjust to what 021 can cook)? **Default: yes.**
2. **Pay factors** 100 % + tip / 60 % / 25 %? **Default: yes.**
3. **Shift** 08:00 to 14:00 in-game, summary 15 s, then the next day? **Default: yes.**

## Human steps
- Approve; play one shift (AC8).

## Implementation notes

## Blockers
