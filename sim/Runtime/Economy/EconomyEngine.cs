using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Server-side economy. Holds only the immutable catalog; every method returns a new state.
    /// Player-input problems return an error code (State = input instance, Payout/Cost = 0).
    /// Null state/item/list arguments and bad TickInput are programmer errors and throw.
    /// </summary>
    public sealed class EconomyEngine
    {
        private static readonly IReadOnlyList<GoodAmount> NoGoods = Array.AsReadOnly(Array.Empty<GoodAmount>());
        private static readonly MarketReport EmptyReport = new MarketReport(0, 0, 0, 0, 0);

        private readonly EconomyCatalog _catalog;

        public EconomyEngine(EconomyCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public EconomyState Initial()
        {
            var list = new List<MarketState>();
            foreach (var m in _catalog.Markets)
                list.Add(new MarketState(m.Id, m.Ceiling, 0, 0, 0, 0, 0, 0, EmptyReport));
            return new EconomyState(0, ReadOnly(list), ReadOnly(new List<Account>()),
                ReadOnly(new List<ConsumedInstance>()), new EconomyTotals(0, 0, 0, 0));
        }

        public LedgerResult OpenAccount(EconomyState s, string accountId)
        {
            Need(s);
            if (!ItemCanonical.IsValidId(accountId)) return Led(s, EconomyError.InvalidAccountId);
            if (FindAccount(s, accountId) >= 0) return Led(s, EconomyError.AccountExists);
            var list = new List<Account>(s.Accounts) { new Account(accountId, 0, NoGoods) };
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return new LedgerResult(s with { Accounts = ReadOnly(list) }, EconomyError.None);
        }

        public LedgerResult TryDebit(EconomyState s, string accountId, long amount)
        {
            Need(s);
            int ai = FindAccount(s, accountId);
            if (ai < 0) return Led(s, EconomyError.UnknownAccount);
            if (amount <= 0) return Led(s, EconomyError.InvalidQuantity);
            var acc = s.Accounts[ai];
            if (amount > acc.Balance) return Led(s, EconomyError.InsufficientFunds);
            var totals = s.Totals with { DebitedFromPlayers = Sat(s.Totals.DebitedFromPlayers, amount) };
            var next = s with { Accounts = ReplaceAccount(s, ai, acc with { Balance = acc.Balance - amount }), Totals = totals };
            return new LedgerResult(next, EconomyError.None);
        }

        public LedgerResult AddGoods(EconomyState s, string accountId, IReadOnlyList<GoodAmount> goods)
        {
            Need(s);
            if (goods == null) throw new ArgumentNullException(nameof(goods));
            foreach (var g in goods)
                if (g == null) throw new ArgumentException("Goods must not contain null.", nameof(goods));
            int ai = FindAccount(s, accountId);
            if (ai < 0) return Led(s, EconomyError.UnknownAccount);
            foreach (var g in goods)
            {
                if (!_catalog.TryGetMarket(g.GoodId, out var m) || m.Kind != MarketKind.Commodity)
                    return Led(s, EconomyError.UnknownGood);
                if (g.Units <= 0) return Led(s, EconomyError.InvalidQuantity);
            }
            var goodsOut = MergeGoods(s.Accounts[ai].Goods, goods);
            if (goodsOut == null) return Led(s, EconomyError.GoodsLimit);
            var acc = s.Accounts[ai] with { Goods = goodsOut };
            return new LedgerResult(s with { Accounts = ReplaceAccount(s, ai, acc) }, EconomyError.None);
        }

        public SaleResult TrySellItem(EconomyState s, string sellerId, long instanceId, Item item)
        {
            Need(s);
            if (item == null) throw new ArgumentNullException(nameof(item));
            int ai = FindAccount(s, sellerId);
            if (ai < 0) return Fail(s, EconomyError.UnknownAccount);
            if (instanceId <= 0) return Fail(s, EconomyError.InvalidInstanceId);
            if (IsConsumed(s, instanceId)) return Fail(s, EconomyError.AlreadyConsumed);
            string? marketId = CategoryOf(item);
            if (marketId == null) return Fail(s, EconomyError.NoMarket);
            _catalog.TryGetMarket(marketId, out var market);
            int q = PriceRules.QualityPermille(ItemRater.Rate(item).Quality);
            if (q < market.MinQualityPermille) return Fail(s, EconomyError.QualityTooLow);

            int mi = FindMarket(s, marketId);
            var ms = s.Markets[mi];
            long payout;
            SaleChannel channel;
            MarketState msOut;
            if (ms.OpenDemand > 0)
            {
                channel = SaleChannel.NpcDemand;
                payout = PriceRules.DemandPayout(ms.Price, q);
                msOut = ms with
                {
                    OpenDemand = ms.OpenDemand - 1,
                    PlayerFilledThisTick = SatInt(ms.PlayerFilledThisTick, 1),
                    DemandPaidThisTick = ms.DemandPaidThisTick + payout,
                };
            }
            else
            {
                channel = SaleChannel.Wholesaler;
                payout = PriceRules.ItemWholesalePayout(_catalog, market, item, q);
                if (payout < 1) return Fail(s, EconomyError.WholesaleRefused);
                msOut = ms with { WholesaleThisTick = SatInt(ms.WholesaleThisTick, 1) };
            }

            var acc = s.Accounts[ai];
            if (payout > EconomyLimits.MaxBalance - acc.Balance) return Fail(s, EconomyError.BalanceLimit);
            var byproducts = Byproducts.ForSale(_catalog, marketId, item);
            var goodsOut = acc.Goods;
            if (byproducts.Count > 0)
            {
                var merged = MergeGoods(acc.Goods, byproducts);
                if (merged == null) return Fail(s, EconomyError.GoodsLimit);
                goodsOut = merged;
            }

            var markets = new List<MarketState>(s.Markets);
            markets[mi] = msOut;
            var consumed = new List<ConsumedInstance>(s.RecentConsumed) { new ConsumedInstance(instanceId, s.Tick) };
            var next = s with
            {
                Markets = ReadOnly(markets),
                Accounts = ReplaceAccount(s, ai, acc with { Balance = acc.Balance + payout, Goods = goodsOut }),
                RecentConsumed = ReadOnly(consumed),
                Totals = s.Totals with { PaidToPlayers = Sat(s.Totals.PaidToPlayers, payout) },
            };
            return new SaleResult(next, EconomyError.None, payout, marketId, channel, byproducts);
        }

        public SaleResult TrySellGoods(EconomyState s, string sellerId, string goodId, int quantity)
        {
            Need(s);
            int ai = FindAccount(s, sellerId);
            if (ai < 0) return Fail(s, EconomyError.UnknownAccount);
            if (!_catalog.TryGetMarket(goodId, out var market) || market.Kind != MarketKind.Commodity)
                return Fail(s, EconomyError.UnknownGood);
            if (quantity < 1 || quantity > EconomyLimits.MaxLot) return Fail(s, EconomyError.InvalidQuantity);
            var acc = s.Accounts[ai];
            int held = 0;
            foreach (var g in acc.Goods) if (string.Equals(g.GoodId, goodId, StringComparison.Ordinal)) held = g.Units;
            if (held < quantity) return Fail(s, EconomyError.InsufficientGoods);

            int mi = FindMarket(s, market.Id);
            var ms = s.Markets[mi];
            int atDemand = Math.Min(quantity, ms.OpenDemand);
            int atFloor = quantity - atDemand;
            long payout = atDemand * ms.Price + atFloor * market.Floor;
            if (payout > EconomyLimits.MaxBalance - acc.Balance) return Fail(s, EconomyError.BalanceLimit);
            var channel = atFloor == 0 ? SaleChannel.NpcDemand : (atDemand == 0 ? SaleChannel.Wholesaler : SaleChannel.Mixed);

            var goodsOut = new List<GoodAmount>();
            foreach (var g in acc.Goods)
            {
                if (!string.Equals(g.GoodId, goodId, StringComparison.Ordinal)) goodsOut.Add(g);
                else if (g.Units > quantity) goodsOut.Add(g with { Units = g.Units - quantity });
            }
            var markets = new List<MarketState>(s.Markets);
            markets[mi] = ms with
            {
                OpenDemand = ms.OpenDemand - atDemand,
                PlayerFilledThisTick = SatInt(ms.PlayerFilledThisTick, atDemand),
                WholesaleThisTick = SatInt(ms.WholesaleThisTick, atFloor),
                DemandPaidThisTick = ms.DemandPaidThisTick + atDemand * ms.Price,
            };
            var next = s with
            {
                Markets = ReadOnly(markets),
                Accounts = ReplaceAccount(s, ai, acc with { Balance = acc.Balance + payout, Goods = ReadOnly(goodsOut) }),
                Totals = s.Totals with { PaidToPlayers = Sat(s.Totals.PaidToPlayers, payout) },
            };
            return new SaleResult(next, EconomyError.None, payout, market.Id, channel, NoGoods);
        }

        public LedgerResult Discard(EconomyState s, string accountId, long instanceId, Item item)
        {
            Need(s);
            if (item == null) throw new ArgumentNullException(nameof(item));
            int ai = FindAccount(s, accountId);
            if (ai < 0) return Led(s, EconomyError.UnknownAccount);
            if (instanceId <= 0) return Led(s, EconomyError.InvalidInstanceId);
            if (IsConsumed(s, instanceId)) return Led(s, EconomyError.AlreadyConsumed);
            var acc = s.Accounts[ai];
            var byproducts = Byproducts.ForDiscard(_catalog, item);
            var goodsOut = acc.Goods;
            if (byproducts.Count > 0)
            {
                var merged = MergeGoods(acc.Goods, byproducts);
                if (merged == null) return Led(s, EconomyError.GoodsLimit);
                goodsOut = merged;
            }
            var consumed = new List<ConsumedInstance>(s.RecentConsumed) { new ConsumedInstance(instanceId, s.Tick) };
            var next = s with
            {
                Accounts = ReplaceAccount(s, ai, acc with { Goods = goodsOut }),
                RecentConsumed = ReadOnly(consumed),
            };
            return new LedgerResult(next, EconomyError.None);
        }

        public PurchaseResult TryBuyBase(EconomyState s, string buyerId, string baseId, int units)
        {
            Need(s);
            int ai = FindAccount(s, buyerId);
            if (ai < 0) return new PurchaseResult(s, EconomyError.UnknownAccount, 0);
            if (!_catalog.TryGetGrocerPrice(baseId, out var price)) return new PurchaseResult(s, EconomyError.UnknownBaseItem, 0);
            if (units < 1 || units > EconomyLimits.MaxLot) return new PurchaseResult(s, EconomyError.InvalidQuantity, 0);
            return Debit(s, ai, price * units);
        }

        /// <summary>
        /// Spec 031b: buys <paramref name="percent"/> % of one base item from the NPC grocer (a squeezed portion of sauce):
        /// <see cref="PriceRules.PortionCost"/> of its grocer price. Percent outside 1..100 is InvalidQuantity. On failure nothing
        /// changes.
        /// </summary>
        public PurchaseResult TryBuyPortion(EconomyState s, string buyerId, string baseId, int percent)
        {
            Need(s);
            int ai = FindAccount(s, buyerId);
            if (ai < 0) return new PurchaseResult(s, EconomyError.UnknownAccount, 0);
            if (!_catalog.TryGetGrocerPrice(baseId, out var price)) return new PurchaseResult(s, EconomyError.UnknownBaseItem, 0);
            if (percent < 1 || percent > 100) return new PurchaseResult(s, EconomyError.InvalidQuantity, 0);
            return Debit(s, ai, PriceRules.PortionCost(price, percent));
        }

        /// <summary>
        /// Spec 033: one pour at the drinks station, charged once: the cup's grocer price when <paramref name="withCup"/> (it was
        /// empty) plus <paramref name="percent"/> % of the drink's (<see cref="PriceRules.FillCost"/>). Percent outside 1..100 is
        /// InvalidQuantity; on any failure nothing changes.
        /// </summary>
        public PurchaseResult TryBuyFill(EconomyState s, string buyerId, string drinkId, int percent, bool withCup)
        {
            Need(s);
            int ai = FindAccount(s, buyerId);
            if (ai < 0) return new PurchaseResult(s, EconomyError.UnknownAccount, 0);
            if (!_catalog.TryGetGrocerPrice(drinkId, out var drink)) return new PurchaseResult(s, EconomyError.UnknownBaseItem, 0);
            long cup = 0;
            if (withCup && !_catalog.TryGetGrocerPrice(Drinks.CupId, out cup)) return new PurchaseResult(s, EconomyError.UnknownBaseItem, 0);
            if (percent < 1 || percent > 100) return new PurchaseResult(s, EconomyError.InvalidQuantity, 0);
            return Debit(s, ai, PriceRules.FillCost(cup, drink, percent, withCup));
        }

        /// <summary>
        /// Spec 034: an order of <paramref name="packs"/> packs from the supplier, paid once: <see cref="Supplier.OrderCost"/> of the
        /// good's grocer price (the bulk price). UnknownBaseItem for a good the catalog does not price or an offer at another price (038b:
        /// or a price good other than the good itself while the good has a grocer price of its own),
        /// InvalidQuantity outside 1..<see cref="Supplier.MaxPacksPerOrder"/>, InsufficientFunds; on any failure nothing changes.
        /// </summary>
        public PurchaseResult TryBuySupply(EconomyState s, string buyerId, SupplierOffer offer, int packs)
        {
            Need(s);
            if (offer == null) throw new ArgumentNullException(nameof(offer));
            int ai = FindAccount(s, buyerId);
            if (ai < 0) return new PurchaseResult(s, EconomyError.UnknownAccount, 0);
            if (!_catalog.TryGetGrocerPrice(offer.PriceGoodId, out var price) || price != offer.GrocerPrice) // 036 review: the price good
                return new PurchaseResult(s, EconomyError.UnknownBaseItem, 0);
            // 038b: a good the grocer prices is bought at its own price; another price good only for a good it has none for (a cone).
            if (offer.PriceGoodId != offer.GoodId && _catalog.TryGetGrocerPrice(offer.GoodId, out _))
                return new PurchaseResult(s, EconomyError.UnknownBaseItem, 0);
            if (packs < 1 || packs > Supplier.MaxPacksPerOrder || offer.PackSize < 1 || offer.GrocerItemsPerUnit < 1)
                return new PurchaseResult(s, EconomyError.InvalidQuantity, 0);
            return Debit(s, ai, Supplier.OrderCost(_catalog, offer, packs));
        }

        /// <summary>
        /// Spec 034, the refund backstop: <paramref name="amount"/> paid to the grocer or supplier goes back to the account (a server
        /// path charged but could not hand over). Money only moves: the grocer's revenue falls by the same amount. UnknownAccount,
        /// InvalidQuantity for 0 or less, more than the grocer ever took or (034 review) more than this account paid it (Account.GrocerPaid),
        /// BalanceLimit; nothing changes then.
        /// </summary>
        public LedgerResult Refund(EconomyState s, string accountId, long amount)
        {
            Need(s);
            int ai = FindAccount(s, accountId);
            if (ai < 0) return Led(s, EconomyError.UnknownAccount);
            var acc = s.Accounts[ai];
            // 034 review: never more than this account paid the grocer and supplier (less what it got back already).
            if (amount <= 0 || amount > s.Totals.GrocerRevenue || amount > acc.GrocerPaid) return Led(s, EconomyError.InvalidQuantity);
            if (acc.Balance > EconomyLimits.MaxBalance - amount) return Led(s, EconomyError.BalanceLimit);
            var next = s with
            {
                Accounts = ReplaceAccount(s, ai, acc with { Balance = acc.Balance + amount, GrocerPaid = acc.GrocerPaid - amount }),
                Totals = s.Totals with { GrocerRevenue = s.Totals.GrocerRevenue - amount },
            };
            return new LedgerResult(next, EconomyError.None);
        }

        /// <summary>031b review: the grocer debit shared by <see cref="TryBuyBase"/> and <see cref="TryBuyPortion"/>: account
        /// <paramref name="ai"/> pays <paramref name="cost"/> to the grocer, or InsufficientFunds and nothing changes.</summary>
        private static PurchaseResult Debit(EconomyState s, int ai, long cost)
        {
            var acc = s.Accounts[ai];
            if (cost > acc.Balance) return new PurchaseResult(s, EconomyError.InsufficientFunds, 0);
            var next = s with
            {
                Accounts = ReplaceAccount(s, ai, acc with { Balance = acc.Balance - cost, GrocerPaid = Sat(acc.GrocerPaid, cost) }),
                Totals = s.Totals with { GrocerRevenue = Sat(s.Totals.GrocerRevenue, cost) },
            };
            return new PurchaseResult(next, EconomyError.None, cost);
        }

        public EconomyState Tick(EconomyState s, TickInput input)
        {
            Need(s);
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Random == null) throw new ArgumentNullException(nameof(input), "Random must not be null.");
            if (input.Population < 0 || input.Population > EconomyLimits.MaxPopulation)
                throw new ArgumentOutOfRangeException(nameof(input), "Population must be in 0..MaxPopulation.");

            long newTick = s.Tick + 1;
            int hour = (int)(newTick % 24);
            long revenue = s.Totals.NpcBusinessRevenue;
            var list = new List<MarketState>();
            foreach (var m in _catalog.Markets)
            {
                var ms = s.Markets[FindMarket(s, m.Id)];

                // 1. NPC businesses fill what is left at the ceiling.
                int npcFilled = ms.OpenDemand;
                revenue = Sat(revenue, npcFilled * m.Ceiling);
                // 2. close the hour
                var last = new MarketReport(ms.DemandThisTick, ms.PlayerFilledThisTick, ms.WholesaleThisTick,
                    npcFilled, ms.DemandPaidThisTick);
                // 3. price
                long price = PriceRules.NextPrice(m, ms.Price, ms.DemandThisTick, SatInt(ms.PlayerFilledThisTick, ms.WholesaleThisTick));
                // 4. new demand
                int units = 0;
                long carry = ms.DemandCarry;
                if (m.Demand != null)
                {
                    long w = 0;
                    foreach (var x in m.Demand.HourWeights) w += x;
                    long total = ms.DemandCarry + (long)input.Population * m.Demand.UnitsPerNpcPerDayCenti * m.Demand.HourWeights[hour];
                    long baseUnits = total / (100 * w);
                    carry = total % (100 * w);
                    long j = baseUnits * m.Demand.JitterPercent / 100;
                    long drawn = baseUnits + input.Random.NextInt((int)-j, (int)j + 1);
                    units = (int)Math.Max(0L, Math.Min(drawn, EconomyLimits.MaxDemandPerTick));
                }
                list.Add(new MarketState(m.Id, price, units, carry, units, 0, 0, 0, last));
            }

            var consumed = new List<ConsumedInstance>();
            foreach (var c in s.RecentConsumed)
                if (c.Tick >= newTick - EconomyLimits.DedupWindowTicks) consumed.Add(c);

            return s with
            {
                Tick = newTick,
                Markets = ReadOnly(list),
                RecentConsumed = ReadOnly(consumed),
                Totals = s.Totals with { NpcBusinessRevenue = revenue },
            };
        }

        // ---- helpers ----

        private static void Need(EconomyState s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
        }

        private static IReadOnlyList<T> ReadOnly<T>(List<T> list) => Array.AsReadOnly(list.ToArray());

        private static LedgerResult Led(EconomyState s, EconomyError e) => new LedgerResult(s, e);

        private static SaleResult Fail(EconomyState s, EconomyError e)
            => new SaleResult(s, e, 0, null, SaleChannel.None, NoGoods);

        private static long Sat(long a, long b)
        {
            long r = unchecked(a + b);
            return (b > 0 && r < a) ? long.MaxValue : r;
        }

        private static int FindAccount(EconomyState s, string? id)
        {
            if (id == null) return -1;
            for (int i = 0; i < s.Accounts.Count; i++)
                if (string.Equals(s.Accounts[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static int FindMarket(EconomyState s, string id)
        {
            for (int i = 0; i < s.Markets.Count; i++)
                if (string.Equals(s.Markets[i].MarketId, id, StringComparison.Ordinal)) return i;
            throw new InvalidOperationException($"State has no market '{id}'. Use EconomyEngine.Initial() of the same catalog.");
        }

        private static int SatInt(int a, int b) => (int)Math.Min((long)a + b, int.MaxValue);

        private static bool IsConsumed(EconomyState s, long instanceId)
        {
            foreach (var c in s.RecentConsumed) if (c.InstanceId == instanceId) return true;
            return false;
        }

        private static IReadOnlyList<Account> ReplaceAccount(EconomyState s, int index, Account acc)
        {
            var list = new List<Account>(s.Accounts);
            list[index] = acc;
            return ReadOnly(list);
        }

        /// <summary>Merged, sorted goods, or null if any good would exceed MaxGoodUnits.</summary>
        private static IReadOnlyList<GoodAmount>? MergeGoods(IReadOnlyList<GoodAmount> held, IReadOnlyList<GoodAmount> add)
        {
            var sums = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var g in held) sums[g.GoodId] = g.Units;
            foreach (var g in add)
            {
                sums.TryGetValue(g.GoodId, out var cur);
                sums[g.GoodId] = cur + g.Units;
            }
            var list = new List<GoodAmount>();
            foreach (var kv in sums)
            {
                if (kv.Value > EconomyLimits.MaxGoodUnits) return null;
                if (kv.Value > 0) list.Add(new GoodAmount(kv.Key, (int)kv.Value));
            }
            return ReadOnly(list);
        }

        private string? CategoryOf(Item item)
        {
            foreach (var r in _catalog.Rules)
            {
                if (item.Weight < r.MinWeight) continue;
                if (!HasAll(item.Tags, r.RequiredTags) || HasAny(item.Tags, r.ForbiddenTags)) continue;
                return r.MarketId;
            }
            return null;
        }

        private static bool HasAll(IReadOnlyList<string> tags, IReadOnlyList<string> required)
        {
            foreach (var t in required) if (!Has(tags, t)) return false;
            return true;
        }

        private static bool HasAny(IReadOnlyList<string> tags, IReadOnlyList<string> forbidden)
        {
            foreach (var t in forbidden) if (Has(tags, t)) return true;
            return false;
        }

        private static bool Has(IReadOnlyList<string> tags, string t)
        {
            foreach (var x in tags) if (string.Equals(x, t, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
