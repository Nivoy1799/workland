---
id: 034
title: Stock and supply (running out, ordering terminal, deliveries in boxes, unpacking)
status: approved
owner: architect
builder: opus
---

# 034: Stock and supply

## Goal
Roadmap pillar 4 and the economy's circular idea. Today every fridge and shelf slot is endless: the grocer
is paid the moment you take an item. In a real shop you **stock** goods. Slots hold a counted amount and run empty.
You order a refill at an ordering terminal in the shop (supplier prices, a delivery time). A delivery arrives as
boxes at the back door, and you **unpack** them into the right slots. Money goes out when you order, not when you
cook. This also adds the refund backstop that earlier reviews asked for. Approved by the orchestrator under the
human's 24 h grant.

## Depends on
028b (fridge and shelf slots, StockRules, StockTakeSequence), 031b and 032 (money-step pattern behind a wallet
interface, pay exactly once), docs/economy-and-professions.md (suppliers), the sim economy (grocer prices, demand).

## Scope
- `sim/Runtime/Economy`:
  - stock per slot (count, capacity) as part of the shop state;
  - supplier offers (price per unit and a pack size from the grocer table, delivery delay in game minutes);
  - an order and delivery model;
  - deterministic and tested.
- `game/Assets/_Project/Scripts/Cooking/` and `Shop/`:
  - taking from a slot decrements its count and no longer charges (the order already paid);
  - an empty slot refuses with "Out of stock";
  - an **ordering terminal** station (a tablet or screen on the back wall): pick an item and a pack count through
    `RequestStation` options (or a small paged list); the order is paid on placing it;
  - a **delivery**: after the delay, boxes spawn at the back door (a pickup per pack); carrying a box to its fridge
    or shelf slot and clicking unpacks it into the slot, with the count rising up to capacity;
  - a **refund backstop** (028b/029b reviews): any server path that charged but could not hand over an item refunds
    through the wallet (tested).
  - No new RPCs. New synced fields only for the presentation of slot counts (a byte per slot, pinned).
- `Scripts/Art/`:
  - a slot's visible items reflect its count (0 to 3 samples);
  - an "empty" look;
  - the terminal (graybox screen with a simple list);
  - delivery boxes (the Kenney cardboard box, already in the project), a box carry pose, an unpack animation.
- `Scripts/UI/`: the terminal's list UI (first person, world-space or a small panel); the HUD shows "Low stock: X"
  when a slot falls below 25 %.
- Shift start: slots begin at a starting stock (default half capacity), so a new game is playable at once.
- Tests, visual check (the tour sees a slot run empty, orders it, waits for the delivery, unpacks), `WhatsNew.json`,
  this spec.

## Out of scope
- Spoilage. Several suppliers with different prices (one supplier now). Player-set menu prices (037). Price-scale
  rescale (the human's inbox question 7; 037).

## Design notes
- Capacity per slot: 12 for vegetables, 8 for bread, 6 for sauces, 20 for packaging.
- A pack is 6 units. The delivery delay is 1 game hour (about 1 real minute).
- Order cost = the grocer price × units × 0.8 (a bulk discount). The anti-faucet rule holds: SurplusCap counts the
  bulk unit price.

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes; RPC surface tests unchanged.
- [ ] AC2: Sim tests: stock counts, order, delivery timing, bulk price, anti-faucet, determinism.
- [ ] AC3: Server tests:
  - a take decrements stock;
  - empty refuses;
  - an order charges once;
  - a delivery spawns boxes;
  - unpacking fills up to capacity (overflow stays in the box);
  - the refund backstop.
- [ ] AC4: Visual check: a slot runs empty, the order is placed at the terminal, the delivery boxes arrive, and the
      unpack fills the slot.
- [ ] AC5: `WhatsNew.json` entry `spec-034`.

## Defaults to review
Capacities, pack size 6, delivery 1 game hour, bulk discount 20 %, starting stock half capacity.

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
