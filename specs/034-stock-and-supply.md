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
Builder (Opus), spec 034, with the 033 review fixes (below). No new RPC and no new RPC parameter (`RpcSurfaceTests` unchanged).
New synced fields, presentation only, pinned in `StockSupplyTests.TheSyncedSurface_...`:
- `Shop.StockCounts.Counts` (`SyncList<byte>`): one byte per stock line, on one global scene object (like the feed): the slots'
  samples and tags, the terminal's list, the HUD's "Low stock".
- `Shop.SupplyBox.Contents` (`SyncVar<ushort>`, the line and the units). **Deviation:** the spec allows a byte per slot only, but
  a carried box must show what it holds (its label, "Click · unpack 6 tomato", the wrong-slot reason); nothing else carries it.

**Sim (`sim/Runtime/Economy`, pure, immutable, tested).**
- `StockLine` (count, capacity, % of a grocer item per unit, the opened unit's %) and `ShopStock`:
  - `TryTake` (one sealed unit, OutOfStock when none);
  - `TryDraw` (a portion: opens the next unit when the opened one is short; the last drops when less is left);
  - `Fill` (an unpack: up to the room, the rest stays in the box);
  - `Return` / `ReturnPercent` (the refund backstop; never refused, may pass the capacity);
  - `IsLow` (< 25 %); `StartingCount` (half).
- `Supplier`:
  - `OrderCost` = grocer price × grocer items × 0.8, rounded up;
  - offers: pack 6, a keg 1 per box; delay 60 game minutes; at most 3 packs an order.
- `SupplyOrder`, `SupplyBook` (`Place`, `TakeDue` oldest first, `Cancel`, at most 12 on the way).
- `EconomyEngine.TryBuySupply` (one debit to the grocer/supplier) and `Refund` (moves it back; the grocer's revenue falls by the
  same amount, never more than it took).
- **Anti-faucet:** `SurplusCap` now counts `PriceRules.StockCost`:
  - every component at `Supplier.BulkUnitPrice`;
  - a squeeze or a pour at the least share of a bottle or a cup its bucket draws, rounded down;
  - the cup itself is not counted.
  `ItemCost` (grocer prices) is unchanged.
- **Deviation (anti-faucet floor):** a bulk unit never costs less than B + 1 = $32 (at most its grocer price). With 0.8 × $35 = $28,
  wrap paper, the bag and the cup would be worth less than the byproducts B = $31 a component can make, which is the exact faucet the
  catalog's rule forbids. So those three cost $32 a unit in bulk; everything at $40 or more gets the full 20 %.
- Test pins updated to the bulk cap (spec-mandated):
  - `PriceRulesTests` (fries cap 28 → 16, tomato+cashew 157 → 113, salt 3 → 0, apple 48 → 32);
  - `SaleTests` (the salt plate is now refused by the wholesaler);
  - `FaucetTests` (975 → 963).
  The invariant tests still hold, plus new ones at the bulk price (`StockAndSupplyTests`, 13 facts).
- Hashes and ratings: unchanged (no op changed but the 033 plate fix, whose added tags are not hashed).

**Stock design (spec 034 decision).** `Shop.StockCatalog`, 24 lines in a fixed order:
- the 8 fridge and 9 shelf goods: vegetables, fruit, seitan, falafel mix 12; bread 8; sauce bottles 6;
- wrap paper, boxes, bags: 20 each;
- cups: 20;
- three drink kegs: 2 per tap, a keg is 20 cups.
How each station draws:
- A slot (fridge, shelf), the rack (packaging) and the cup stack give one sealed unit per take or use.
- A sauce dispenser draws its squeeze's share (10 / 20 / 30 % of a bottle, `PortionPercent`) from the opened bottle of that sauce. The
  next bottle opens from the dry shelf's slot, so the shelf's bottles are the dispensers' reserve: "a bottle has 10 portions".
- A tap draws its pour's share of a cup (`FillPercent`, 1 % per 4 ml) from the opened keg: "a keg has 20 cups".
- The lid stack stays free (a cup includes its lid).
- No money moves at the stations any more: the board and the dispenser use `Shop.StockWallet`, which implements the 031b/033 wallet
  interfaces through the stock. OutOfStock is its `Short` answer at a press. The old money wallets stay as the fallback when a scene
  has no `SupplyService`, as "free without an economy" did.
- A new game (the server starting) begins at half capacity (sealed). "Shift start" is read as the game's start; the stock is not
  reset between days.
- The cook tour's host (`-tour-orders`, dev builds) starts the tomato line at 1 (the tour runs it empty). The other tours' goods
  start at 6 or more, which is enough for every take in them.

**Server (`Scripts/Shop`, `Scripts/Cooking`).**
- `StockRoom` (pure): stock, book and boxes; every step checks first and changes nothing on a refusal.
  - `Take`, `Draw`.
  - `Order`: check (the good, the packs, the orders on the way, `CanAfford`), charge once, book; if the booking fails after the
    payment, the backstop refunds.
  - `Deliver`: due orders become one box per pack.
  - `Unpack`: WrongBox / StockFull, fills up to capacity, the rest stays in the box.
  - `WriteCounts`.
- `SupplyService` (MonoBehaviour next to the NetworkManager):
  - publishes the counts on change;
  - every 0.25 s delivers what is due by `EconomyService.GameMinute` (new: the economy tick × 60 plus the minutes into the hour);
  - spawns boxes on free delivery spots (a feed line "Delivery at the back door: …"; boxes wait for a free spot);
  - takes the terminal's orders (`PlayerSupplyWallet`: `EconomyService.TryBuySupply`, `Refund`; feed line "Player 1 ordered 1 pack
    of tomato ($336), due at 09:12");
  - unpacks a carried box (an emptied box is despawned).
- `KitchenService`:
  - a fridge/shelf take: `StockTake.Pay` takes the unit from the slot's stock (no charge, OutOfStock when empty);
  - a cup from the stack: new `CupTake` through `StockTakeSequence`;
  - squeezes, packaging and pours draw through `StockWallet`;
  - a carried box clicked on a slot's unpack option (`StockCatalog.UnpackSlotOf`: a fridge or shelf slot, the rack's packaging
    option, a tap's press, the cup stack) goes to `UnpackInto` (reach to the slot's anchor, WrongBox);
  - `StationKind.Terminal` (appended, station index 11) routes to `SupplyService.Order`.
- **Refund backstop:**
  - `IStockTakeSteps.Refund`: `StockTakeSequence` calls it once when the hand-over fails after `Pay`. The stock take puts the unit
    back (or refunds the money in the no-supply path); the cup take puts the cup back.
  - `IAssemblyWallet.Refund` / `IDrinkWallet.Refund`: `KitchenState.CommitBuild` / `CommitCup` now return false when the spot or the
    cup is gone; `AssemblyDesk.End`, `Package` and `DrinkDesk.End` then refund what they charged. Money for the money wallets, the
    drawn stock for `StockWallet`.
  - `ISupplyWallet.Refund` for an order that could not be booked.
  - Tested with wallets that change the world while they charge (the board cleared, the cup removed, the book filled).
- New `CookOutcome`s with HUD texts: OutOfStock, StockFull, WrongBox, TooManyOrders.

**Terminal and back door (layout).**
- **Terminal** (graybox `Art_StationTerminal`, `TerminalArtRules`): a flat 1.0 × 0.46 m wall screen, 2 cm deep, on the back wall's
  inner face (z 14.90–14.92) above the landing worktop by the staff door (x 1.11), its middle at 1.36 m.
  - Every slot is in reach from the aisle (FloorPlanTests), and the screen reads over the boxes below it.
  - Six rows on the left (Charcoal bars, "tomato  6/12  · $336", Mustard when low, Terracotta "!" when empty), a header
    ("ORDER STOCK · page 1/4 · 1 pack · 1 h"), and a button column (< PAGE, PAGE >, PACKS +).
  - The page and the pack count are the client's own (`TerminalView`). Only a row's click is sent, as
    `RequestStation(terminal, TerminalRules.OrderOption(line, packs))`.
  - The row lights Mustard at the click, then Sage (taken) or Terracotta with "nope" (refused).
  - Third person: E orders the row nearest to the player on its page.
- **Back door** = the staff gap at the aisle's west end: a WoodDark door frame and a sign "Back door · deliveries".
- **Delivery spots**: three on the landing worktop's front half, right inside the back door (x 0.72 / 1.13 / 1.54, z 13.92).

**Client.**
- `InteractionRules`:
  - an empty slot, rack, cup stack or tap is None with "Out of stock: order more at the terminal";
  - a slot's take says "Click · take tomato (4 left)" (no price: it is free now; a crate keeps its price), and the rack "(N left)";
  - a carried box: `Unpack` "Click · unpack 6 tomato (4 fit)" only on its own slot with room, else "This box is for the tomato" or
    "That slot is full";
  - terminal rows `Order`, buttons `Page`.
- `AimTarget.StockLine/StockCount` (+ `WithStock`, `TerminalSlot`), `Held.Box`.
- `PickupInteractor`:
  - describes stock from the synced counts with our own pending take or unpack first;
  - `UnpackWithLook` (the unpack animation, at the click): the unpack sound and the slot's samples pop in. When the whole box fits,
    a collider-free copy of it (`Art.UnpackLook`) tips towards the slot, moves in and shrinks away over 0.36 s, while the real box
    hides until the server despawns it; it shows again on a refusal. When only part fits, the box tips and shakes in the hands
    (`BoxLookRules`);
  - a box's pick-up prompt names it ("Click · pick up the box of 6 tomato");
  - the terminal click;
  - third-person E unpacks into the station's slot for the box's good;
  - a refused take, unpack or order takes its look back with "nope".
- `Art.StockSlotLook` on every fridge and shelf slot:
  - shows min(count, samples) of its 1–3 samples; a new one pops in over 0.18 s;
  - the tag reads "tomato 4/12", Mustard below a quarter, Terracotta "tomato · empty";
  - the owner's take or unpack is predicted until the synced byte moves, a refusal or 2 s.
- Carry pose:
  - first person: a box is drawn low in the middle of the view (`HoldViewRules.Box*`: centre at 12 % height, at most 42 % of the
    view, its top below the centre);
  - third person: in front of the chest (1.0 m up, 0.34 m forward), not in one hand.
- Sounds: `Delivery` (two knocks on the door and the boxes set down, played when a box arrives) and `Unpack`.
- HUD: a "Low stock: tomato, cups" line under the orders, rebuilt only when the counts change.
- The terminal's texts and the box's label use the 027d depth-tested world-text material (they never show through the fridge door).
- No per-frame allocations in the new views: texts change on count, page or pack changes; the terminal's prompt words are cached
  per page and pack count.

**Scope beyond the listed folders** (as in 029–033):
- `sim/Runtime/Cooking` (the 033 plate fix);
- `Scripts/Economy` (`TryBuySupply`, `Refund`, `GameMinute`, `CanAfford`);
- `Scripts/Net` (rules, aim, interactor, Pickup's carry pose);
- `Scripts/Player` (`HoldViewRules`, `HeldItemView`);
- `Scripts/Npc` (the 033 tab fixes);
- `Scripts/Audio` (two sounds);
- `Scripts/Dev` (tours);
- `Editor` (the terminal, the back door, the box prefab, the catalog);
- `docs/art-direction.md` (two rows);
- `scripts/visual-check.sh` (cook timeout 1320 → 1440 s).
Fixtures updated for the new station: ArtCatalogTests, ScaleSceneTests, CookingRulesTests, TourRulesTests (two look shots),
WhatsNewTests (033 no longer newest). `PrefabRegistrationTests` now also reads the SupplyService's prefabs (the box) and checks the
kitchen's `_supply`. The new prefab `DeliveryBox.prefab` is the one new DefaultPrefabObjects entry.

**033 review fixes.**
1. The plate op adds only `"plated"` (it had the cup tag in its added tags). Test `DrinkTests.APlatedOldDish_HasExactlyItsOldTags_NoCup`.
2. `AssemblyRules.CanAdd` refuses any cup (NotFood): no cup onto a plate or into a bowl, an empty bowl too. The bowl case is in
   `AnOpenCup_IsNotSoldAndGoesIntoNoCookware`.
3. Pour record:
   - the press stores the cup's `Item`, and the release requires `ReferenceEquals`;
   - `Register` and `TryRemove` cancel pours into that id;
   - every `_items.Remove` path goes through `Forget` (the ml and the pours go with the cup).
   Tests in `StockSupplyTests`.
4. GuestTab:
   - a completed order's combined bill is kept until `CustomerService` calls `Close` after `RegisterFlow.HandOver` (the desk's
     `Open`);
   - should the hand-over throw in between, the counter's fallback pays the whole order once (`FallbackBill`), never only the last line.
5. `GuestTab.Serve` refuses a line already served (false, nothing changes; `IsDuplicate`). `OnHandedOver` returns false for it, and
   the fallback pays that dish (`FallbackPaysFor(handed, billedBefore, billedAfter)`). Tests.
6. A refused sauce squeeze start no longer ends the cup's predicted level (`_squeezeStartIsPour`).
7. `DrinkTests.TheWholesalersCap_NeverCountsMoreThanAPourCost`: multi-pour top-ups (and their stock cost).

**Tests.** `scripts/verify.sh` PASS: 1045 EditMode (baseline 1017), 328 sim (baseline 314).
- New EditMode `StockSupplyTests` (27), plus the 034 What's-new test.
- New sim `StockAndSupplyTests` (13) and `DrinkTests` +1.

**Visual check (AC4).** `bash scripts/visual-check.sh --tours look,cook`: PASS. Cook: 754 images, timeouts 0, aim fallbacks 0.
`.verify/visual/cook/sheet.png`:
- 01 `stock-last-tomato` ("Click · take tomato (1 left)", HUD "Low stock: tomato"), 02–11 `stock-last-take-*`.
- 12 `stock-empty-slot` ("Out of stock: order more at the terminal").
- 13 `stock-empty-slot-view` (no samples, Terracotta "tomato · empty").
- 14 `terminal`, 15–25 `terminal-next-aim` / `terminal-page-*`.
- 26 `terminal-order-aim` ("Click · order 1 pack of tomato ($336, 1 h)"), 27–36 `terminal-order-*` (the row lights), 37
  `terminal-ordered` ($500 → $164).
- 172 `delivery-boxes`: the box "6 tomato" on the landing worktop by the back door, about 60 s after the order; the log says
  "Delivery at the back door: 1 box of 6 tomato".
- 173–183 `delivery-box-*` (the pick-up arc), 184 `delivery-box-carried` (low in the middle of the view).
- 185 `unpack-aim` ("Click · unpack 6 tomato"), 186– `unpack-*` (the box's copy shrinks into the slot, the samples are back).
- 196 `tomato-restocked` ("Click · take tomato (6 left)"), 197 `tomato-restocked-view` ("tomato 6/12", the low-stock line gone).
Then the fries, the wrap (squeeze and paper from stock), the lemonade (cup and pour from stock) and the payments as in 033.
`.verify/visual/look/sheet.png`: 57 `terminal-first`, 58 `back-door-first`.

**Still off / follow-ups.**
- The terminal's rows are small at arm's length (about 3 cm letters), and the prompt under the dot covers a row of the screen.
- The page and pack buttons are Ink on SoftBlue and read dim.
- Kegs, packaging and cups show their counts only in the prompt and on the terminal. The rack, the cup stack and the taps have no
  tag or sample change; the slots' looks are on the fridge and the shelf only.
- A sauce dispenser cannot tell the client whether its opened bottle still has some (the byte counts sealed bottles), so its press
  is not refused on the client; the server answers OutOfStock with "nope".
- Third person: a carried box sits in front of the chest, but the arms do not hold it (no arm pose); the box's label hangs at the
  hold point above the head for others.
- The delivery knock plays at the box when it spawns, also for a client that joins after a delivery (any box newer than 3 s into
  the scene); boxes beyond the three spots wait unseen at the supplier until a spot is free.
- Money: the starting $500 buys one or two packs; orders are dear next to it (bulk prices from today's grocer table; prices are spec 037).
- `EconomyService.TryBuy` / `TryBuyPortion` / `TryBuyFill` and the money wallets now serve only scenes without a SupplyService
  (kept as the fallback; candidates for removal).
- Stock is not reset between days (a new game starts at half); spoilage is out of scope.

## Blockers
(Builder fills this in after 3 failed attempts.)
