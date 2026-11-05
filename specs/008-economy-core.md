---
id: 008
title: Economy core (markets, NPC demand and supply, sales, byproducts, hourly tick) in /sim
status: done
owner: architect
builder: sonnet
---

# 008: Economy core in /sim

## Goal
The deterministic circular economy (D9) run by the game server: aggregated NPC demand per market, NPC
businesses as price ceiling, an NPC wholesaler as floor, an NPC grocer selling base items, server-side
player sales, byproducts that always have a buyer, and a pure hourly tick. No money faucet: surplus dumping
always loses money; profit only comes from finite open NPC demand. Later professions add data, not code paths.

## Scope
Only `/sim`. Runtime in `sim/Runtime/Economy/`, namespace `Worktown.Sim.Economy`, one public type per file:
- New: `EconomyLimits.cs`, `MarketKind.cs`, `DemandProfile.cs`, `MarketDef.cs`, `CategoryRule.cs`,
  `ByproductTrigger.cs`, `ByproductRule.cs`, `EconomyCatalog.cs`, `GoodAmount.cs`, `Account.cs`,
  `ConsumedInstance.cs`, `MarketReport.cs`, `MarketState.cs`, `EconomyTotals.cs`, `EconomyState.cs`,
  `TickInput.cs`, `EconomyError.cs`, `SaleChannel.cs`, `SaleResult.cs`, `LedgerResult.cs`,
  `PriceRules.cs`, `Byproducts.cs`, `EconomyRng.cs`, `EconomyEngine.cs`, `EconomySnapshot.cs`,
  `GrocerPrice.cs`, `PurchaseResult.cs`.
- Tests in `sim/Sim.Tests~/Economy/`: `TestEconomy.cs` (fixtures), `PriceRulesTests.cs`, `EconomyCatalogTests.cs`,
  `SaleTests.cs`, `LedgerTests.cs`, `TickTests.cs`, `ByproductTests.cs`, `DeterminismTests.cs`, `GrocerTests.cs`,
  `FaucetTests.cs`. This spec file (notes).
- **No changes to spec 005 code** (`sim/Runtime/Cooking/*`, `sim/Runtime/Core/*`). The economy only uses public
  `Item.Hash/Tags/Weight/Components`, `ItemRater.Rate`, `OperationResult`, `OperationCatalog.TryGet`,
  `CookingLimits.MaxDepth`, `IRandom`, `SeededRandom`.

## Out of scope
- Anything outside `/sim` (networking, UI, persistence/IO, backend, JSON or file loading).
- Players buying NPC dishes or commodities, grocer stock limits, player-to-player trade, taxes, other sinks.
- Item inventory (only bulk goods are held here), NPC agents/visuals, services, farm production,
  any change to cooking stats, rating, hashing or `OperationDef`.

## Design

### Units and determinism
- Money is `long` whole dollars ($). There are no sub-units (no cents, D23). No float in money, demand, prices or
  anything in `EconomyState`. All divisions are integer division on non-negative operands (= floor).
- `EconomyLimits`: `MaxBalance = 1e15`, `MaxPrice = 1e9`, `MaxGoodUnits = 1_000_000` (per account per good),
  `MaxLot = 10_000` (units per goods sale or grocer buy), `MaxPopulation = 100_000`, `MaxDemandPerTick = 1_000_000`,
  `DedupWindowTicks = 24`. Every product below stays far under `long.MaxValue` (worst `MaxPrice * 2 * MaxDemandPerTick`).
- Quality enters once, at the sale boundary: `QualityPermille(float q) = float.IsNaN(q) ? 0 :
  (int)(Math.Clamp(q, 0f, 1f) * 1000f)` (truncation). Only the int is used afterwards.
- `EconomyTotals` counters add saturating (cap `long.MaxValue`). No `DateTime`, no static mutable state;
  time = `EconomyState.Tick` (1 tick = 1 in-game hour, hour of day `Tick % 24`); randomness only via `TickInput.Random`.

### Goods and markets
Items have unbounded identities, so pricing is per **market**, never per item hash.
- `MarketKind { Item, Commodity }`. Item markets are consumer categories (`meal`, `snack`, `drink`, later more);
  an `Item` maps to one via tags. Commodity markets are fungible bulk goods without quality (`food_scraps`,
  `used_cooking_oil`, later `used_oil`, `scrap_parts`); the market id is the good id.
- `CategoryRule(string MarketId, IReadOnlyList<string> RequiredTags, IReadOnlyList<string> ForbiddenTags, int MinWeight)`.
  Mapping: the **first** rule in catalog order with all RequiredTags in `item.Tags`, none of ForbiddenTags, and
  `item.Weight >= MinWeight`; no match = `NoMarket`. Defaults, in order (plate is the serving unit, spec 005 Q3):
  `drink` [plated, drink] w>=1; `meal` [plated] not [drink] w>=2; `snack` [plated] not [drink] w>=1.
```csharp
public sealed record DemandProfile(int UnitsPerNpcPerDayCenti, IReadOnlyList<int> HourWeights, int JitterPercent);
public sealed record MarketDef(string Id, MarketKind Kind, long Floor, long Ceiling,
    int NpcQualityPermille, int MinQualityPermille, DemandProfile? Demand, int SmoothingPercent);
public enum ByproductTrigger { Operation, Discard, Sale }
// SourceId: operation id (Operation), market id (Sale), "" (Discard).
public sealed record ByproductRule(ByproductTrigger Trigger, string SourceId, string GoodId,
    int FixedUnits, int UnitsPerComponent);
public sealed record GrocerPrice(string BaseId, long Price);       // spec 005 BaseItemDef id, whole dollars
public sealed class EconomyCatalog
{
    public EconomyCatalog(IEnumerable<MarketDef> markets, IEnumerable<CategoryRule> rules,
        IEnumerable<ByproductRule> byproducts, IEnumerable<GrocerPrice> grocer,
        OperationCatalog operations);                               // copies inputs; ops only for validation
    public bool TryGetMarket(string? id, out MarketDef market);
    public bool TryGetGrocerPrice(string? baseId, out long price);
    public IReadOnlyList<MarketDef> Markets { get; }                // sorted by Id ordinal
    public IReadOnlyList<CategoryRule> Rules { get; }               // given order
    public IReadOnlyList<ByproductRule> Byproducts { get; }
    public IReadOnlyList<GrocerPrice> Grocer { get; }               // sorted by BaseId ordinal
    public long ByproductMaxPerComponent { get; }                   // B, see "Anti-faucet"
    public static EconomyCatalog CreateDefault();                   // provisional tables below
}
```
Constructor throws `ArgumentException` (data errors) when: an id or tag breaks `^[a-z0-9_]{1,32}$` (as spec 005);
duplicate market id; `Floor < 1`; `Ceiling > MaxPrice`; **margin rule** `2 * Ceiling < 3 * Floor`; `NpcQualityPermille`
not in 0..499; `MinQualityPermille` not in 0..1000; `SmoothingPercent` not in 1..100; HourWeights count != 24,
a weight outside 0..1000, or sum 0; `UnitsPerNpcPerDayCenti` not in 0..100_000; `JitterPercent` not in 0..50;
a CategoryRule to a missing or non-Item market; an Item market no rule reaches; **a ByproductRule whose GoodId
is not a Commodity market** (no dead-end goods); unit fields outside 0..1000 or both 0; an Operation rule whose
SourceId is unknown in `operations` or has `Kind == Mix` (flattening removes nodes, breaking the depth bound);
a Sale rule not on an Item market; a Discard rule with SourceId != ""; grocer: bad/duplicate BaseId, Price not in
1..MaxPrice, empty while byproduct rules exist; **anti-faucet rule** `B >= min(grocer Price)`.

Default catalog (all numbers PROVISIONAL, see Open questions; prices in whole dollars):
| market | kind | floor | ceiling | npc q | min q | units/NPC/day | hours (weight) |
|---|---|---|---|---|---|---|---|
| meal | Item | 300 | 1200 | 350 | 150 | 1.50 (150) | 6:1 7:3 8:2 9:1 10:1 11:4 12:6 13:3 14:1 15:1 16:1 17:2 18:5 19:6 20:3 21:1, else 0 |
| snack | Item | 100 | 400 | 350 | 150 | 0.80 (80) | 1 for hours 8..21, else 0 |
| drink | Item | 50 | 250 | 350 | 150 | 2.00 (200) | 1 for hours 7..22, else 0 |
| food_scraps | Commodity | 2 | 3 | 0 | 0 | none | - |
| used_cooking_oil | Commodity | 2 | 4 | 0 | 0 | none | - |
Jitter 10 %, smoothing 25 % for all. Byproducts: `(Operation, "cut", food_scraps, 0, 1)`,
`(Operation, "fry", used_cooking_oil, 1, 0)`, `(Discard, "", food_scraps, 0, 1)`. No Sale rules by default.
This gives `B = 7 * 4 + 3 = 31`. Grocer (spec 005 `TestItems` ids): salt 35, basil 40, oil 50, onion 50,
potato 60, tomato 70, apple 80, cheese 150 (all > 31).

### State (immutable records; lists sorted by key ordinal; methods return new state)
```csharp
public sealed record GoodAmount(string GoodId, int Units);
public sealed record Account(string Id, long Balance, IReadOnlyList<GoodAmount> Goods); // Goods sorted, Units > 0
public sealed record ConsumedInstance(long InstanceId, long Tick);
public sealed record MarketReport(int Demand, int PlayerFilled, int Wholesale, int NpcFilled, long DemandPaid); // closed hour
public sealed record MarketState(string MarketId, long Price, int OpenDemand, long DemandCarry,
    int DemandThisTick, int PlayerFilledThisTick, int WholesaleThisTick, long DemandPaidThisTick, MarketReport Last);
public sealed record EconomyTotals(long PaidToPlayers, long DebitedFromPlayers, long NpcBusinessRevenue, long GrocerRevenue);
public sealed record EconomyState(long Tick, IReadOnlyList<MarketState> Markets,
    IReadOnlyList<Account> Accounts, IReadOnlyList<ConsumedInstance> RecentConsumed, EconomyTotals Totals);
public sealed record TickInput(IRandom Random, int Population);
```
Record `==` compares list references, so tests compare `EconomySnapshot.Canonical(state)` (versioned `v1|...`
text of every field in fixed order, integers only) and `EconomySnapshot.Digest` (lowercase hex SHA-256 of it),
a test/debug form; persistence is a later /backend spec. NPC money is an unlimited source (consumers,
wholesaler) and sink (NPC businesses, grocer), tracked in `EconomyTotals`.

### Engine API (server-side; player input never throws)
```csharp
public enum EconomyError { None, InvalidAccountId, AccountExists, UnknownAccount, InvalidInstanceId,
    AlreadyConsumed, NoMarket, QualityTooLow, UnknownGood, InvalidQuantity, InsufficientGoods,
    InsufficientFunds, BalanceLimit, GoodsLimit, UnknownBaseItem, WholesaleRefused }
public enum SaleChannel { None, NpcDemand, Wholesaler, Mixed }
public sealed record SaleResult(EconomyState State, EconomyError Error, long Payout, string? MarketId,
    SaleChannel Channel, IReadOnlyList<GoodAmount> Byproducts) { public bool Ok => Error == EconomyError.None; }
public sealed record LedgerResult(EconomyState State, EconomyError Error) { public bool Ok => ...; }
public sealed record PurchaseResult(EconomyState State, EconomyError Error, long Cost) { public bool Ok => ...; }

public sealed class EconomyEngine          // holds only the immutable catalog
{
    public EconomyEngine(EconomyCatalog catalog);
    public EconomyState Initial();         // Tick 0, Price = Ceiling, all demand/carry/counters 0
    public LedgerResult OpenAccount(EconomyState s, string accountId);           // balance 0
    public LedgerResult TryDebit(EconomyState s, string accountId, long amount);
    public LedgerResult AddGoods(EconomyState s, string accountId, IReadOnlyList<GoodAmount> goods);
    public SaleResult TrySellItem(EconomyState s, string sellerId, long instanceId, Item item);
    public SaleResult TrySellGoods(EconomyState s, string sellerId, string goodId, int quantity);
    public LedgerResult Discard(EconomyState s, string accountId, long instanceId, Item item);
    public PurchaseResult TryBuyBase(EconomyState s, string buyerId, string baseId, int units);
    public EconomyState Tick(EconomyState s, TickInput input);
}
public static class PriceRules     // pure, used by engine and the arbitrage/faucet tests
{
    public static int QualityPermille(float quality);
    public static long DemandPayout(long price, int q);      // max(1, price * (500 + q) / 1000)
    public static long WholesalePayout(long floor, int q);   // max(1, floor * (500 + q / 2) / 1000)
    public static long NextPrice(MarketDef m, long price, int demand, int playerUnits);
    public static long? ItemCost(EconomyCatalog c, Item item);  // sum of grocer prices of Components; null if any unpriced
    public static long SurplusCap(EconomyCatalog c, Item item); // ItemCost - 1 - Weight * B; 0 if unpriced
    public static long ItemWholesalePayout(EconomyCatalog c, MarketDef m, Item item, int q); // min(WholesalePayout, SurplusCap)
}
public static class Byproducts {
    public static IReadOnlyList<GoodAmount> ForOperation(EconomyCatalog c, string operationId, IReadOnlyList<Item> inputs, OperationResult result);
    public static IReadOnlyList<GoodAmount> ForDiscard(EconomyCatalog c, Item item); }
public static class EconomyRng { public static IRandom ForTick(ulong worldSeed, long tick); }
```
Null arguments or `Population` outside 0..MaxPopulation throw (`ArgumentNullException`/`ArgumentOutOfRangeException`):
server code, not players. On any error `State` is the input instance and `Payout`/`Cost` are 0. A state not from
`Initial()` of the same catalog (missing `MarketState`) throws `InvalidOperationException`; tests may build edge
states with `with`. `amount`/`Units` <= 0 -> `InvalidQuantity`; `TryDebit` below 0 -> `InsufficientFunds`;
`AddGoods` unknown or non-Commodity good -> `UnknownGood`, over `MaxGoodUnits` -> `GoodsLimit` (all or nothing).

### Player sale flow
`TrySellItem` checks in order: account (`UnknownAccount`); `instanceId > 0` (`InvalidInstanceId`); not in
`RecentConsumed` (`AlreadyConsumed`); category rule (`NoMarket`); `q = QualityPermille(ItemRater.Rate(item).Quality)`
(computed here, never from the client) `>= MinQualityPermille` (`QualityTooLow`). Then:
- `OpenDemand > 0`: channel `NpcDemand`, payout `DemandPayout(Price, q)`, `OpenDemand -= 1`, `PlayerFilledThisTick += 1`,
  `DemandPaidThisTick += payout`. Else (saturated): channel `Wholesaler`, payout `ItemWholesalePayout(c, m, item, q)`;
  `< 1` fails with `WholesaleRefused` (item kept, can be discarded); else `WholesaleThisTick += 1`.
- Payout over `MaxBalance` -> `BalanceLimit`; Sale-trigger byproducts over `MaxGoodUnits` -> `GoodsLimit`. Else credit,
  record `(instanceId, Tick)` in `RecentConsumed`, add Sale byproducts, `PaidToPlayers += payout`.
`TrySellGoods`: account, Commodity market (`UnknownGood`), `1 <= quantity <= MaxLot` (`InvalidQuantity`), holdings
(`InsufficientGoods`). `a = min(quantity, OpenDemand)` units pay `Price` (`DemandPaidThisTick += a * Price`), the rest
`Floor` (flat, no quality); channel `NpcDemand`, `Wholesaler` or `Mixed`. `Discard`: account/instance checks, adds
`ForDiscard` goods, records the instance (no dead-end items).

### Byproducts (data-driven, spec 005 untouched)
`ForOperation` is empty unless `result.Ok`, the result is new (not the cut no-op: single input with equal `Hash`)
and every input component has a grocer price. Else per rule `(Operation, operationId, ...)`: `units = FixedUnits +
UnitsPerComponent * sum(input.Weight)`, merged per good, sorted by GoodId. Discard/Sale: same with `item.Weight`,
empty if unpriced. The server calls it after each Ok `ItemFactory.Apply` and credits via `AddGoods` (server-only,
never other amounts). Fries yield 1 food_scraps + 1 used_cooking_oil.

### NPC grocer
`TryBuyBase` checks: account (`UnknownAccount`), grocer price exists (`UnknownBaseItem`), `1 <= units <= MaxLot`
(`InvalidQuantity`), `cost = Price * units <= Balance` (`InsufficientFunds`); then debits, `GrocerRevenue += cost`.
The caller (later /game inventory spec) then calls `ItemFactory.CreateBase(baseId)` `units` times. Contract: base
items enter the world **only** after an Ok `TryBuyBase`. Fixed prices, no stock limits in v1 (hoarding cannot pay).

### Anti-faucet invariant (surplus path is always a net loss)
`B = ByproductMaxPerComponent = (CookingLimits.MaxDepth - 1) * V_op + max(V_discard, V_sale)`, where for each
trigger group `V = sum over its rules of (FixedUnits + UnitsPerComponent) * Ceiling(GoodId)`, `V_op` is the max
over operation ids, `V_discard`/`V_sale` the max over discard rules / sale markets. For an item X with weight W:
`Cost(X) = sum over Components of grocer price >= W * minPrice`.
- Byproducts: every component sits under at most `MaxDepth - 1 = 7` operation nodes (mix nodes, which flatten,
  carry no rules), each node yields at most `Fixed + Units * W <= W * (Fixed + Units)`, and an instance ends once
  (sale or discard). Any commodity unit pays at most its Ceiling. So lifetime byproduct value `BV(X) <= W * B`.
- Catalog rule `B < minPrice` gives `BV(X) < Cost(X)`. Wholesaler: `ItemWholesalePayout <= SurplusCap = Cost - 1 - W*B`.
  So `wholesale + BV <= Cost - 1 < Cost` for every q (the quality multiplier max 1.0x is irrelevant under the cap),
  and discard gives `BV <= W*B < Cost`. Dumping any item, cooked or raw, after buying its inputs always loses.
- Why a per-sale cap: a catalog-only rule needs `Floor_m < MinWeight_m * (minPrice - B)`, so one cheap spice
  (salt 35) would force every floor near 0. The cap uses the real components (plated cheese keeps its floor,
  plated salt gets 3). Byproducts stay a catalog rule: B is a per-component worst case, no tree walk needed.

### NPC demand, NPC supply and the tick
Aggregated per market, no agents: with 16..24 players and ~100 NPCs, agent AI adds cost and risk for no
economic gain; /game can animate NPCs from the pools. `Tick(s, input)` processes markets in Id ordinal order:
1. **NPC businesses fill** the remaining `OpenDemand` at the Ceiling with `NpcQualityPermille` (worse, pricier):
   `npcFilled = OpenDemand`; `NpcBusinessRevenue += npcFilled * Ceiling`. The town works with zero players.
2. `Last = (DemandThisTick, PlayerFilledThisTick, WholesaleThisTick, npcFilled, DemandPaidThisTick)`.
3. **Price** `NextPrice(m, Price, D = DemandThisTick, S = PlayerFilled + Wholesale)`:
   `target = D > 0 ? Ceiling - (Ceiling - Floor) * min(S, 2D) / (2D) : (S > 0 ? Floor : Price)`;
   `step = (target - Price) * SmoothingPercent / 100` (toward 0; if 0 while target != Price use `sign(target - Price)`); `Price' = clamp(Price + step, F, C)`.
4. **New demand** for hour `h = (Tick + 1) % 24` (markets with a profile only; exactly one RNG draw each):
   `W = sum(HourWeights)`, `total = DemandCarry + Population * UnitsPerNpcPerDayCenti * HourWeights[h]`,
   `units = total / (100 * W)`, `DemandCarry' = total % (100 * W)` (exact over a day, no float),
   `j = units * JitterPercent / 100`, `units = clamp(units + Random.NextInt(-j, j + 1), 0, MaxDemandPerTick)`.
   `OpenDemand = DemandThisTick = units`; `PlayerFilledThisTick = WholesaleThisTick = DemandPaidThisTick = 0`.
5. `Tick' = Tick + 1`; drop `RecentConsumed` entries with `Tick < Tick' - DedupWindowTicks`.
`EconomyRng.ForTick(seed, tick) = new SeededRandom(new SeededRandom(seed ^ (ulong)tick).NextUInt64())`: restore needs no RNG state.

### No-arbitrage proof (margin rule: Floor >= 1, 2*Ceiling >= 3*Floor, so Ceiling >= 2 and Ceiling > Floor)
Let F = Floor, C = Ceiling, P = Price, q in 0..1000, q_N = NpcQualityPermille <= 499.
- Price: step 3 moves P toward a target in [F, C] without overshoot and clamps, so F <= P <= C. NPC dish business
  -> wholesaler (not buyable in v1, proven for later): `WholesalePayout(F, q) <= max(1, F) = F < C`.
- NPC dish business -> NPC demand: NPC dishes keep q_N (plated items are forbidden inputs, spec 005), so
  `DemandPayout(P, q_N) <= max(1, floor(0.999 P)) <= max(1, P - 1) < C` (P = 1 gives 1 < 2 <= C). Commodities: `P <= C`.
- Grocer -> wholesaler, discard or byproduct sales: strict loss (anti-faucet invariant), any quality. Grocer -> cook
  -> open NPC demand: **profitable by design** (the job), the only profit source, at most `D_m(t)` sales per market and tick.
- **Money creation bound.** Per tick: `Last.DemandPaid <= Last.Demand * DemandPayout(C_m, 1000)` (Item markets,
  `= D * floor(1.5 C_m)`) and `<= Last.Demand * C_m` (Commodity). Over any run starting with no held items or goods:
  `PaidToPlayers - GrocerRevenue - DebitedFromPlayers <= M(T) = sum over ticks t, Item markets m of D_m(t) * floor(1.5 * C_m)`.
  Proof: attribute every payout to the item lineage it came from (bases only via the grocer, byproducts only from
  that lineage's operations/end). A lineage not ending in a demand sale nets `< 0`; one that does nets
  `<= floor(1.5 C_m) + W*B - Cost < floor(1.5 C_m)`. Held, unfinished lineages net `<= W*B - Cost < 0`.
  Defaults: about 180 meals, 96 snacks, 240 drinks per day, so `M(24) ~= $471_600` per town-day.

### Anti-exploit behaviour
- Same item twice: the server inventory removes it in the same step as an Ok sale (later spec); the economy also
  rejects a repeated `instanceId` within `DedupWindowTicks` (`AlreadyConsumed`).
- Flooding: sales beyond `OpenDemand` pay the capped wholesale payout (a net loss); prices fall toward Floor, never below.
- Worthless items: unplated -> `NoMarket`; below `MinQualityPermille` -> `QualityTooLow`; both can be `Discard`ed.
  Scrap/oil farming: cut no-op yields nothing, depth limit bounds yields, `B < minPrice` makes it a loss.
- Overflow: `BalanceLimit`/`GoodsLimit` errors, no partial effects; balances never negative.

### Server authority (for later netcode specs)
Engine calls run only on the server. Clients send intents ("sell item instance X", "buy 3 potato"); the server
resolves items from its own inventory and never accepts price, quality, market, cost or payout from clients.
Prices and `Last` may be broadcast read-only. The server drives `Tick` and persists state (later specs).

## Acceptance criteria
- [x] AC1: `bash scripts/verify.sh` passes incl. `PASS sim: dotnet test` and `PASS sim: no UnityEngine references`;
      no file under `sim/Runtime/Cooking/` or `sim/Runtime/Core/` changed (`git diff --stat main` shows none).
- [x] AC2: Arbitrage grid in `PriceRulesTests`: for F in {1,2,3,7,100,300,999}, C in {ceil(1.5F), 2F, 4F, MaxPrice},
      every P in a 9-step grid of [F, C] (incl. both ends), q in 0..1000 step 10 and q_N in 0..499 step 7:
      `WholesalePayout(F, q) < C` and `DemandPayout(P, q_N) < C`.
- [x] AC3: Default catalog, population 120, no player sales, 240 ticks: every Item market with demand has
      `Last.NpcFilled == Last.Demand` each tick, Price == Ceiling, and the meal demand summed over ticks 1..24 is
      within 10 % of 180 (jitter 0 run: exactly 180).
- [x] AC4: After 12 ticks (hour 12, snack demand open), `Plate(fries)` (quality ~0.70) earns more than
      `Plate(burnt potato)` (~0.30, passes `MinQuality`), both via `NpcDemand` in `snack`.
- [x] AC5: Flooding `snack`: selling 3x its open demand (at least 1) every tick for 48 ticks makes Price
      non-increasing, reach Floor, and never go below Floor; sales beyond demand report channel `Wholesaler`.
- [x] AC6: `ForOperation` for fry on cut potato and for cut on potato give 1 used_cooking_oil and 1 food_scraps;
      after `AddGoods`, `TrySellGoods` of each is Ok with payout >= its Floor; cut no-op gives no goods.
- [x] AC7: Same seed and same scripted sales twice -> identical `EconomySnapshot.Digest` after 100 ticks;
      a different seed gives a different digest.
- [x] AC8: Each `EconomyError` except `None` is produced by at least one test, with `State` unchanged
      (same reference) and `Payout == 0`. Plated raw potato -> `QualityTooLow`; unplated fries -> `NoMarket`.
- [x] AC9: Balance at `MaxBalance - 1` plus a sale -> `BalanceLimit`; `TryDebit` more than the balance ->
      `InsufficientFunds`; 1000 random ops (seeded) never leave a negative balance or goods count.
- [x] AC10: Catalog validation: margin violation (F=100, C=149), byproduct to a non-commodity good, 23 hour
      weights, `NpcQualityPermille = 500`, a rule on `mix`, a grocer price 31 with default byproducts (B = 31),
      and a duplicate grocer id each throw `ArgumentException`; `CreateDefault()` does not and has `B == 31`.
- [x] AC11: Every public method of `PriceRules`, `Byproducts`, `EconomyRng`, `EconomySnapshot`, `EconomyCatalog`
      and `EconomyEngine` has at least one test. Total test count >= 69 + 40.
- [x] AC12: Faucet grid: for Plate(fries), Plate(raw potato), Plate(burnt potato), Plate(Mix(tomato, cheese)),
      Plate(salt), Plate(apple) and q in 0..1000 step 10: `max(0, ItemWholesalePayout) + Weight * B < ItemCost`.
      End to end: buy a potato, cut, fry, plate, `AddGoods` the `ForOperation` goods, sell the plate to the saturated
      wholesaler and the goods with commodity `OpenDemand > 0` and `Price = Ceiling` (set via `with`): final
      balance < starting balance. Same for a plated raw potato via `Discard`.
- [x] AC13: `TryBuyBase` of 3 potatoes debits 180 and `GrocerRevenue == 180`; unknown base -> `UnknownBaseItem`,
      units 0 or `MaxLot + 1` -> `InvalidQuantity`, cost > balance -> `InsufficientFunds` (`Cost == 0`, state
      unchanged); a plate with an unpriced component -> `WholesaleRefused` when saturated, and no byproducts.
- [x] AC14: Money bound: 8 seeded bots (grant set via `with`) for 72 ticks buy, cook (fries, plated apple, mixes),
      sell into demand, flood, discard and sell all goods. Every tick and market `Last.DemandPaid <= Last.Demand *
      DemandPayout(C_m, 1000)` (Commodity `* C_m`); at the end `PaidToPlayers - GrocerRevenue - DebitedFromPlayers <= M(72)`.

## Test plan
- `TestEconomy.cs`: `EconomyEngine` on `CreateDefault()` plus a zero-jitter variant; spec 005 `TestItems` for
  all fixtures and `Plate`; an unpriced test base item (catalog without its grocer entry) for AC13; ticks use
  `EconomyRng.ForTick(42, tick)`; AC14 bots draw choices from `SeededRandom(7)`. One test class per Scope file;
  AC2 and AC12 are nested-loop `[Fact]`s. Command: `bash scripts/verify.sh` (fast loop `dotnet test sim/Sim.sln`).

## Human steps
None (pure /sim). The human answers the Open questions or approves the defaults.

## Open questions (answered 2026-11-05: human approved all recommended defaults; numbers stay provisional data)
1. Currency: decided 2026-11-05, dollar ($), whole dollars only, no sub-units (D23). Price levels stay
   in the hundreds because integer quality steps and the byproduct bound B need headroom; see Q2.
2. Floor/ceiling prices per market. [table above; commodities kept tiny (ceilings 3 and 4) so B stays < cheapest grocer price]
3. NPC population and needs per NPC per day. [120 NPCs; meal 1.5, snack 0.8, drink 2.0; hour weights above]
4. Margin rule. [Ceiling >= 1.5 x Floor, enforced by the catalog]
5. Quality effect. [demand payout 0.5x..1.5x of Price, wholesaler 0.5x..1.0x of Floor; NPC quality 350; min sell quality 150]
6. Category rules. [meal = plated weight >= 2, snack = plated weight 1, drink = plated + `drink` tag]
7. Price response. [linear in supply/demand up to 2x, 25 % smoothing per tick, 10 % demand jitter]
8. Taxes or sale fees. [none in v1]
9. DECIDED (human): players buy base items from an NPC grocer at fixed prices; NPC dishes/commodities not buyable.
10. Byproducts. [cut: 1 food_scraps per component; fry: 1 used_cooking_oil; discard: 1 food_scraps per component; no sale byproducts]
11. Money source/sink. [NPC money unlimited, totals tracked; the grocer is the v1 sink; rent etc. later]
12. Starting balance for new players. [0 in /sim; a starting grant (needed to buy first ingredients) is a /game decision]
13. Grocer prices (whole dollars). [salt 35, basil 40, oil 50, onion 50, potato 60, tomato 70, apple 80, cheese 150; all > B]
14. Grocer stock limits or dynamic prices. [none in v1; fixed prices, unlimited stock]

## Implementation notes
Built exactly to the spec; no formula, catalog default or AC was changed. 184 tests total (85 old + 99 new).
- Key numbers match the spec: B = 31, fries wholesale 28 (60 - 1 - 31), plated salt 3, tomato+cheese 157,
  zero-jitter money bound 324_000 + 57_600 + 90_000 = 471_600 per town-day (asserted by a test).
  Measured qualities: fries 696 permille, burnt 296, raw 125, tomato+cheese 746, salt 166, apple 396.
  AC14 bots (seed 7, 72 ticks): net 19_280 below zero, bound M(72) = 1_419_150 (bots never come close).
- String ids (account, good, base) that are null or invalid return error codes (`UnknownAccount`, `UnknownGood`,
  `UnknownBaseItem`, `InvalidAccountId`) instead of throwing; only state/item/list/TickInput nulls throw.
- Extra validation beyond the spec text: `Floor` in 1..MaxPrice and `Ceiling >= Floor` are checked before the margin
  rule so `2*Ceiling`/`3*Floor` cannot wrap; CategoryRule `MinWeight` must be 0..1000.
- `SurplusCap` is not clamped at 0 (may be negative); the engine treats `< 1` as `WholesaleRefused`.
- `Byproducts.ForSale` is `internal` (engine use only); Sale-trigger rules are covered through `TrySellItem`.
- Catalog `Demand.HourWeights` and rule tag lists are copied into read-only arrays; all state lists are read-only copies.
- Review hint: `EconomyCatalog.ComputeB` groups Operation rules per SourceId (V_op = max), all Discard rules as one group,
  Sale rules per market (V_sale = max).

### Follow-ups
- Per-sale cost is O(recent sales + accounts) due to immutable copies and a linear dedup scan; fine for 16-24 players, revisit in the persistence/netcode spec (sorted set or per-tick buckets).

## Blockers
(Builder fills this in after 3 failed attempts.)
