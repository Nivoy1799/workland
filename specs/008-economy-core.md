---
id: 008
title: Economy core (markets, NPC demand and supply, sales, byproducts, hourly tick) in /sim
status: draft
owner: architect
builder: sonnet
---

# 008: Economy core in /sim

## Goal
The deterministic circular economy (D9) run by the game server: aggregated NPC demand per market, NPC
businesses as price ceiling, an NPC wholesaler as floor, server-side player sales, byproducts that always
have a buyer, and a pure hourly tick. Cooking only now; later professions add markets and rules as data.

## Scope
Only `/sim`. Runtime in `sim/Runtime/Economy/`, namespace `Worktown.Sim.Economy`, one public type per file:
- New: `EconomyLimits.cs`, `MarketKind.cs`, `DemandProfile.cs`, `MarketDef.cs`, `CategoryRule.cs`,
  `ByproductTrigger.cs`, `ByproductRule.cs`, `EconomyCatalog.cs`, `GoodAmount.cs`, `Account.cs`,
  `ConsumedInstance.cs`, `MarketReport.cs`, `MarketState.cs`, `EconomyTotals.cs`, `EconomyState.cs`,
  `TickInput.cs`, `EconomyError.cs`, `SaleChannel.cs`, `SaleResult.cs`, `LedgerResult.cs`,
  `PriceRules.cs`, `Byproducts.cs`, `EconomyRng.cs`, `EconomyEngine.cs`, `EconomySnapshot.cs`.
- Tests in `sim/Sim.Tests~/Economy/`: `TestEconomy.cs` (fixtures), `PriceRulesTests.cs`, `EconomyCatalogTests.cs`,
  `SaleTests.cs`, `LedgerTests.cs`, `TickTests.cs`, `ByproductTests.cs`, `DeterminismTests.cs`. This spec file (notes).
- **No changes to spec 005 code** (`sim/Runtime/Cooking/*`, `sim/Runtime/Core/*`). The economy only uses
  public `Item.Hash/Tags/Weight`, `ItemRater.Rate`, `OperationResult`, `IRandom`, `SeededRandom`.

## Out of scope
- Anything outside `/sim` (networking, UI, persistence/IO, backend, JSON or file loading).
- Player purchases from NPC suppliers, ingredient shops, player-to-player trade, taxes, money sinks.
- Item inventory (only bulk goods are held here), NPC agents/visuals, services, farm production,
  any change to cooking stats, rating, hashing or `OperationDef`.

## Design

### Units and determinism
- Money is `long` minor units (placeholder "coin", 100 minor = 1 coin). No float in money, demand, prices or
  anything in `EconomyState`. All divisions are integer division on non-negative operands (= floor).
- `EconomyLimits`: `MaxBalance = 1_000_000_000_000_000`, `MaxPrice = 1_000_000_000`,
  `MaxGoodUnits = 1_000_000` (per account per good), `MaxLot = 10_000` (units per goods sale),
  `MaxPopulation = 100_000`, `MaxDemandPerTick = 1_000_000`, `DedupWindowTicks = 24`.
  These bound every product below far under `long.MaxValue` (worst: `MaxPrice * 2 * MaxDemandPerTick`).
- Quality enters once, at the sale boundary: `QualityPermille(float q) = float.IsNaN(q) ? 0 :
  (int)(Math.Clamp(q, 0f, 1f) * 1000f)` (truncation). Only the int is used afterwards.
- `EconomyTotals` counters add saturating (cap `long.MaxValue`). No `DateTime`, no static mutable state;
  time = `EconomyState.Tick` (1 tick = 1 in-game hour, hour of day `Tick % 24`); randomness only via `TickInput.Random`.

### Goods and markets
Items have unbounded identities, so pricing is per **market**, never per item hash.
- `MarketKind { Item, Commodity }`. Item markets are consumer categories (`meal`, `snack`, `drink`,
  later e.g. `bakery`); an `Item` maps to one via tags. Commodity markets are fungible bulk goods without
  quality (`food_scraps`, `used_cooking_oil`, later `used_oil`, `scrap_parts`); the market id is the good id.
- `CategoryRule(string MarketId, IReadOnlyList<string> RequiredTags, IReadOnlyList<string> ForbiddenTags, int MinWeight)`.
  Mapping: the **first** rule in catalog order with all RequiredTags in `item.Tags`, none of ForbiddenTags,
  and `item.Weight >= MinWeight`. No match = `NoMarket`. Default rules require `plated` (spec 005 Q3):
  1. `drink`: required [plated, drink], min weight 1
  2. `meal`: required [plated], forbidden [drink], min weight 2
  3. `snack`: required [plated], forbidden [drink], min weight 1
```csharp
public sealed record DemandProfile(int UnitsPerNpcPerDayCenti, IReadOnlyList<int> HourWeights, int JitterPercent);
public sealed record MarketDef(string Id, MarketKind Kind, long Floor, long Ceiling,
    int NpcQualityPermille, int MinQualityPermille, DemandProfile? Demand, int SmoothingPercent);
public enum ByproductTrigger { Operation, Discard, Sale }
// SourceId: operation id (Operation), market id (Sale), "" (Discard).
public sealed record ByproductRule(ByproductTrigger Trigger, string SourceId, string GoodId,
    int FixedUnits, int UnitsPerComponent);
public sealed class EconomyCatalog
{
    public EconomyCatalog(IEnumerable<MarketDef> markets, IEnumerable<CategoryRule> rules,
                          IEnumerable<ByproductRule> byproducts);   // copies inputs
    public bool TryGetMarket(string? id, out MarketDef market);
    public IReadOnlyList<MarketDef> Markets { get; }                // sorted by Id ordinal
    public IReadOnlyList<CategoryRule> Rules { get; }               // given order
    public IReadOnlyList<ByproductRule> Byproducts { get; }
    public static EconomyCatalog CreateDefault();                   // provisional table below
}
```
Constructor throws `ArgumentException` (programmer/data errors) when: an id or tag breaks
`^[a-z0-9_]{1,32}$` (same as spec 005); duplicate market id; `Floor < 1`; `Ceiling > MaxPrice`;
**margin rule** `2 * Ceiling < 3 * Floor` (i.e. Ceiling must be >= 1.5 x Floor); `NpcQualityPermille`
not in 0..499; `MinQualityPermille` not in 0..1000; `SmoothingPercent` not in 1..100; HourWeights count
!= 24, any weight < 0 or > 1000, or sum == 0; `UnitsPerNpcPerDayCenti` not in 0..100_000; `JitterPercent`
not in 0..50; a CategoryRule pointing to a missing or non-Item market; an Item market with no rule
reaching it; **a ByproductRule whose GoodId is not a Commodity market** (no dead-end goods); unit
fields < 0 or both 0.

Default catalog (all numbers PROVISIONAL, see Open questions; prices in minor units):
| market | kind | floor | ceiling | npc q | min q | units/NPC/day | hours (weight) |
|---|---|---|---|---|---|---|---|
| meal | Item | 300 | 1200 | 350 | 150 | 1.50 (150) | 6:1 7:3 8:2 9:1 10:1 11:4 12:6 13:3 14:1 15:1 16:1 17:2 18:5 19:6 20:3 21:1, else 0 |
| snack | Item | 100 | 400 | 350 | 150 | 0.80 (80) | 1 for hours 8..21, else 0 |
| drink | Item | 50 | 250 | 350 | 150 | 2.00 (200) | 1 for hours 7..22, else 0 |
| food_scraps | Commodity | 2 | 10 | 0 | 0 | none | - |
| used_cooking_oil | Commodity | 5 | 20 | 0 | 0 | none | - |
Jitter 10 %, smoothing 25 % for all. Byproducts: `(Operation, "cut", food_scraps, 0, 1)`,
`(Operation, "fry", used_cooking_oil, 1, 0)`, `(Discard, "", food_scraps, 0, 1)`. No Sale rules by default.

### State (immutable records; lists sorted by key ordinal; methods return new state)
```csharp
public sealed record GoodAmount(string GoodId, int Units);
public sealed record Account(string Id, long Balance, IReadOnlyList<GoodAmount> Goods); // Goods sorted, Units > 0
public sealed record ConsumedInstance(long InstanceId, long Tick);
public sealed record MarketReport(int Demand, int PlayerFilled, int Wholesale, int NpcFilled); // closed hour
public sealed record MarketState(string MarketId, long Price, int OpenDemand, long DemandCarry,
    int DemandThisTick, int PlayerFilledThisTick, int WholesaleThisTick, MarketReport Last);
public sealed record EconomyTotals(long PaidToPlayers, long DebitedFromPlayers, long NpcBusinessRevenue);
public sealed record EconomyState(long Tick, IReadOnlyList<MarketState> Markets,
    IReadOnlyList<Account> Accounts, IReadOnlyList<ConsumedInstance> RecentConsumed, EconomyTotals Totals);
public sealed record TickInput(IRandom Random, int Population);
```
Record `==` compares list references, so tests compare `EconomySnapshot.Canonical(state)` (a
versioned `v1|...` text of every field in fixed order, integers only) and `EconomySnapshot.Digest`
(lowercase hex SHA-256 of it). This is a test/debug canonical form; the persistence format belongs to a
later /backend spec. NPC money is an unlimited source (consumers, wholesaler) and sink (NPC business
revenue), tracked in `EconomyTotals` for monitoring.

### Engine API (server-side; player input never throws)
```csharp
public enum EconomyError { None, InvalidAccountId, AccountExists, UnknownAccount, InvalidInstanceId,
    AlreadyConsumed, NoMarket, QualityTooLow, UnknownGood, InvalidQuantity, InsufficientGoods,
    InsufficientFunds, BalanceLimit, GoodsLimit }
public enum SaleChannel { None, NpcDemand, Wholesaler, Mixed }
public sealed record SaleResult(EconomyState State, EconomyError Error, long Payout, string? MarketId,
    SaleChannel Channel, IReadOnlyList<GoodAmount> Byproducts) { public bool Ok => Error == EconomyError.None; }
public sealed record LedgerResult(EconomyState State, EconomyError Error) { public bool Ok => ...; }

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
    public EconomyState Tick(EconomyState s, TickInput input);
}
public static class PriceRules     // pure, used by engine and the arbitrage test
{
    public static int QualityPermille(float quality);
    public static long DemandPayout(long price, int q);      // max(1, price * (500 + q) / 1000)
    public static long WholesalePayout(long floor, int q);   // max(1, floor * (500 + q / 2) / 1000)
    public static long NextPrice(MarketDef m, long price, int demand, int playerUnits);
}
public static class Byproducts
{
    public static IReadOnlyList<GoodAmount> ForOperation(EconomyCatalog c, string operationId,
        IReadOnlyList<Item> inputs, OperationResult result);
    public static IReadOnlyList<GoodAmount> ForDiscard(EconomyCatalog c, Item item);
}
public static class EconomyRng { public static IRandom ForTick(ulong worldSeed, long tick); }
```
Null state/item/list/input, null `Random`, or `Population` outside 0..MaxPopulation throw
(`ArgumentNullException` / `ArgumentOutOfRangeException`): those come from server code, not players.
On any error the returned `State` is the input instance unchanged and `Payout == 0`. The engine assumes
`s` came from `Initial()` of the same catalog (a missing `MarketState` throws `InvalidOperationException`);
tests may still build edge states with `with` (e.g. a balance at `MaxBalance - 1`).
`amount`/`Units` <= 0 -> `InvalidQuantity`; `TryDebit` below 0 -> `InsufficientFunds`;
`AddGoods` unknown or non-Commodity good -> `UnknownGood`, over `MaxGoodUnits` -> `GoodsLimit` (all or nothing).

### Player sale flow
`TrySellItem` checks in order: seller exists (`UnknownAccount`); `instanceId > 0` (`InvalidInstanceId`);
not in `RecentConsumed` (`AlreadyConsumed`); category rule (`NoMarket`); `q = QualityPermille(ItemRater.Rate(item).Quality)`
(computed here, never taken from the client) `>= MinQualityPermille` (`QualityTooLow`); payout fits
`MaxBalance` (`BalanceLimit`). Then:
- `OpenDemand > 0`: channel `NpcDemand`, payout `DemandPayout(Price, q)`, `OpenDemand -= 1`, `PlayerFilledThisTick += 1`.
- else (market saturated, flooding): channel `Wholesaler`, payout `WholesalePayout(Floor, q)`, `WholesaleThisTick += 1`.
- Credit the seller, add `(instanceId, Tick)` to `RecentConsumed`, add Sale-trigger byproducts to the
  seller's goods (if that would exceed `GoodsLimit`, the whole sale fails), `PaidToPlayers += payout`.
`TrySellGoods`: account, good is a Commodity market (`UnknownGood`), `1 <= quantity <= MaxLot`
(`InvalidQuantity`), holdings >= quantity (`InsufficientGoods`). `a = min(quantity, OpenDemand)` units pay `Price` each,
the rest pay `Floor` each (commodities are flat: no quality). Channel `NpcDemand`, `Wholesaler` or `Mixed`.
`Discard`: same account/instance checks, adds `ForDiscard` goods, records the instance. This makes every
food item, even raw garbage below `MinQuality`, convertible to a good with a buyer.

### Byproducts (data-driven, spec 005 untouched)
`ForOperation` returns empty unless `result.Ok` and the result is a new item (not the cut no-op: a single
input whose `Hash` equals `result.Item.Hash`). Otherwise for every rule `(Operation, operationId, ...)`:
`units = FixedUnits + UnitsPerComponent * sum(input.Weight)`, merged per good, sorted by GoodId.
`ForDiscard`: Discard rules with `item.Weight`. The server calls `ForOperation` after each successful
`ItemFactory.Apply` and credits via `AddGoods`. Fries yield 1 food_scraps (cut) + 1 used_cooking_oil (fry).

### NPC demand, NPC supply and the tick
Aggregated per market, no agents: with 16..24 players and ~100 NPCs, agent AI adds cost and risk for no
economic gain; /game can animate NPCs from the pools. `Tick(s, input)` processes markets in Id ordinal order:
1. **NPC businesses fill** the remaining `OpenDemand` at the Ceiling with `NpcQualityPermille` (worse, pricier):
   `npcFilled = OpenDemand`; `NpcBusinessRevenue += npcFilled * Ceiling`. The town works with zero players.
2. `Last = (DemandThisTick, PlayerFilledThisTick, WholesaleThisTick, npcFilled)`.
3. **Price** `NextPrice(m, Price, D = DemandThisTick, S = PlayerFilled + Wholesale)`:
   `target = D > 0 ? Ceiling - (Ceiling - Floor) * min(S, 2D) / (2D) : (S > 0 ? Floor : Price)`;
   `diff = target - Price`; `step = diff * SmoothingPercent / 100` (truncates toward 0); if `step == 0 && diff != 0`
   then `step = sign(diff)`; `Price' = clamp(Price + step, Floor, Ceiling)`. No player supply -> Ceiling; supply of 2x demand or more -> Floor.
4. **New demand** for hour `h = (Tick + 1) % 24` (markets with a profile only; exactly one RNG draw each):
   `W = sum(HourWeights)`, `total = DemandCarry + Population * UnitsPerNpcPerDayCenti * HourWeights[h]`,
   `units = total / (100 * W)`, `DemandCarry' = total % (100 * W)` (exact over a day, no float),
   `j = units * JitterPercent / 100`, `units = clamp(units + Random.NextInt(-j, j + 1), 0, MaxDemandPerTick)`.
   `OpenDemand = DemandThisTick = units`; `PlayerFilledThisTick = WholesaleThisTick = 0`.
5. `Tick' = Tick + 1`; drop `RecentConsumed` entries with `Tick < Tick' - DedupWindowTicks`.
`EconomyRng.ForTick(seed, tick) = new SeededRandom(new SeededRandom(seed ^ (ulong)tick).NextUInt64())`, so a
server restored from a snapshot continues identically without persisting RNG state.

### No-arbitrage proof (validated margin rule: Floor >= 1, 2*Ceiling >= 3*Floor, so Ceiling >= 2 and Ceiling > Floor)
Let F = Floor, C = Ceiling, P = Price, q in 0..1000, q_N = NpcQualityPermille <= 499.
- Price bound: step 3 moves P toward a target in [F, C] without overshoot and clamps, so F <= P <= C every tick.
- NPC supplier -> NPC wholesaler: `WholesalePayout(F, q) = max(1, floor(F*(500 + q/2)/1000)) <= max(1, F) = F < C`
  for every q, including any quality a player could reach. Loss per unit >= C - F >= F/2 > 0.
- NPC supplier -> NPC demand: NPC goods keep q_N (plated items are forbidden inputs in spec 005, so they cannot
  be improved). `floor(P*(500+q_N)/1000) <= floor(0.999 P) <= P - 1`, hence `DemandPayout <= max(1, P - 1) < C`
  (P >= 2 gives P - 1 < P <= C; P = 1 gives 1 < 2 <= C). Commodities: payout `P <= C`, profit <= 0.
  So reselling ceiling-priced NPC goods to NPCs never gains, even if a later spec lets players buy them.

### Anti-exploit behaviour
- Same item twice: the server inventory removes the item in the same server step as an Ok sale (later spec);
  the economy also rejects a repeated `instanceId` within `DedupWindowTicks` with `AlreadyConsumed`.
- Flooding: sales beyond `OpenDemand` pay the quality-scaled floor; the next prices fall toward Floor, never below.
- Worthless items: unplated -> `NoMarket`; below `MinQualityPermille` -> `QualityTooLow`; both can be
  `Discard`ed for scraps. Scrap farming: the cut no-op yields nothing; each discard needs a new instance id.
- Overflow: `BalanceLimit`/`GoodsLimit` errors, no partial effects; balances never negative.

### Server authority (for later netcode specs)
Engine calls run only on the server. Clients send intents ("sell my item instance X", "sell 5 food_scraps");
the server resolves the `Item` from its own inventory. Clients never send price, quality, market or payout.
Prices and `Last` reports may be broadcast read-only. The server drives `Tick` from its game clock and
persists the state (later /backend spec).

## Acceptance criteria
- [ ] AC1: `bash scripts/verify.sh` passes incl. `PASS sim: dotnet test` and `PASS sim: no UnityEngine references`;
      no file under `sim/Runtime/Cooking/` or `sim/Runtime/Core/` changed (`git diff --stat main` shows none).
- [ ] AC2: Arbitrage grid in `PriceRulesTests`: for F in {1,2,3,7,100,300,999}, C in {ceil(1.5F), 2F, 4F, MaxPrice},
      every P in a 9-step grid of [F, C] (incl. both ends), q in 0..1000 step 10 and q_N in 0..499 step 7:
      `WholesalePayout(F, q) < C` and `DemandPayout(P, q_N) < C`.
- [ ] AC3: Default catalog, population 120, no player sales, 240 ticks: every Item market with demand has
      `Last.NpcFilled == Last.Demand` each tick, Price == Ceiling, and the meal demand summed over ticks 1..24 is
      within 10 % of 180 (jitter 0 run: exactly 180).
- [ ] AC4: After 12 ticks (hour 12, snack demand open), `Plate(fries)` (quality ~0.70) earns more than
      `Plate(burnt potato)` (~0.30, passes `MinQuality`), both via `NpcDemand` in `snack`; the same pair sold
      after demand is exhausted (channel `Wholesaler`) also keeps fries > burnt.
- [ ] AC5: Flooding `snack`: selling 3x its open demand (at least 1) every tick for 48 ticks makes Price
      non-increasing, reach Floor, and never go below Floor; sales beyond demand report channel `Wholesaler`.
- [ ] AC6: `ForOperation` for fry on cut potato and for cut on potato give 1 used_cooking_oil and 1 food_scraps;
      after `AddGoods`, `TrySellGoods` of each is Ok with payout >= its Floor; cut no-op gives no goods.
- [ ] AC7: Same seed and same scripted sales twice -> identical `EconomySnapshot.Digest` after 100 ticks;
      a different seed gives a different digest.
- [ ] AC8: Each `EconomyError` except `None` is produced by at least one test, with `State` unchanged
      (same reference) and `Payout == 0`. Plated raw potato -> `QualityTooLow`; unplated fries -> `NoMarket`.
- [ ] AC9: Balance at `MaxBalance - 1` plus a sale -> `BalanceLimit`; `TryDebit` more than the balance ->
      `InsufficientFunds`; 1000 random ops (seeded) never leave a negative balance or goods count.
- [ ] AC10: Catalog validation: margin violation (F=100, C=149), byproduct to a non-commodity good, 23 hour
      weights, and `NpcQualityPermille = 500` each throw `ArgumentException`; `CreateDefault()` does not.
- [ ] AC11: Every public method of `PriceRules`, `Byproducts`, `EconomyRng`, `EconomySnapshot`, `EconomyCatalog`
      and `EconomyEngine` has at least one test. Total test count >= 69 + 30.

## Test plan
- `TestEconomy.cs`: `EconomyEngine` on `CreateDefault()` plus a zero-jitter variant; spec 005 `TestItems` for
  fries, burnt/raw potato and `Plate`; ticks use `EconomyRng.ForTick(42, tick)`. One test class per Scope file;
  AC2 is a nested-loop `[Fact]`. Command: `bash scripts/verify.sh` (fast loop `dotnet test sim/Sim.sln`).

## Human steps
None (pure /sim). The human answers the Open questions or approves the defaults.

## Open questions (recommended default in brackets; all numbers provisional and data-only)
1. Currency name and minor units. [placeholder "coin", 100 minor = 1 coin]
2. Floor/ceiling prices per market. [table above]
3. NPC population and needs per NPC per day. [120 NPCs; meal 1.5, snack 0.8, drink 2.0; hour weights above]
4. Margin rule. [Ceiling >= 1.5 x Floor, enforced by the catalog]
5. Quality effect. [demand payout 0.5x..1.5x of Price, wholesaler 0.5x..1.0x of Floor; NPC quality 350; min sell quality 150]
6. Category rules. [meal = plated weight >= 2, snack = plated weight 1, drink = plated + `drink` tag]
7. Price response. [linear in supply/demand up to 2x, 25 % smoothing per tick, 10 % demand jitter]
8. Taxes or sale fees. [none in v1]
9. Can players buy from NPC suppliers? [not in v1; ingredient shops come in a later spec; proof already covers it]
10. Byproducts. [cut: 1 food_scraps per component; fry: 1 used_cooking_oil; discard: 1 food_scraps per component; no sale byproducts]
11. Money source/sink. [NPC money unlimited in v1, totals tracked; sinks (rent, ingredients) in later specs]
12. Starting balance for new players. [0 in /sim; any starting grant is a /game decision]

## Implementation notes
(Builder fills this in.)

## Blockers
(Builder fills this in after 3 failed attempts.)
