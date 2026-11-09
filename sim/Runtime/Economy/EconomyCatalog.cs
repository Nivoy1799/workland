using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Sim.Economy
{
    /// <summary>Validated, immutable economy data. All numbers in the default catalog are provisional.</summary>
    public sealed class EconomyCatalog
    {
        private readonly Dictionary<string, MarketDef> _markets = new Dictionary<string, MarketDef>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _grocer = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <exception cref="ArgumentException">Any data error, see spec 008.</exception>
        public EconomyCatalog(IEnumerable<MarketDef> markets, IEnumerable<CategoryRule> rules,
            IEnumerable<ByproductRule> byproducts, IEnumerable<GrocerPrice> grocer, OperationCatalog operations)
        {
            if (markets == null) throw new ArgumentNullException(nameof(markets));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (byproducts == null) throw new ArgumentNullException(nameof(byproducts));
            if (grocer == null) throw new ArgumentNullException(nameof(grocer));
            if (operations == null) throw new ArgumentNullException(nameof(operations));

            var marketList = new List<MarketDef>();
            foreach (var m in markets)
            {
                ValidateMarket(m);
                if (_markets.ContainsKey(m.Id)) throw Bad($"Duplicate market id '{m.Id}'.");
                var copy = m with { Demand = m.Demand == null ? null : m.Demand with { HourWeights = Copy(m.Demand.HourWeights) } };
                _markets.Add(m.Id, copy);
                marketList.Add(copy);
            }
            marketList.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            Markets = Array.AsReadOnly(marketList.ToArray());

            var ruleList = new List<CategoryRule>();
            var reached = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rules)
            {
                if (r == null) throw Bad("Category rule must not be null.");
                if (!ItemCanonical.IsValidId(r.MarketId)) throw Bad($"Invalid market id '{r.MarketId}' in category rule.");
                if (!_markets.TryGetValue(r.MarketId, out var mk) || mk.Kind != MarketKind.Item)
                    throw Bad($"Category rule targets missing or non-Item market '{r.MarketId}'.");
                if (r.RequiredTags == null || r.ForbiddenTags == null) throw Bad("Category rule has null tag lists.");
                foreach (var t in r.RequiredTags) if (!ItemCanonical.IsValidId(t)) throw Bad($"Invalid tag '{t}'.");
                foreach (var t in r.ForbiddenTags) if (!ItemCanonical.IsValidId(t)) throw Bad($"Invalid tag '{t}'.");
                if (r.MinWeight < 0 || r.MinWeight > 1000) throw Bad("Category rule MinWeight must be in 0..1000.");
                reached.Add(r.MarketId);
                ruleList.Add(r with { RequiredTags = Copy(r.RequiredTags), ForbiddenTags = Copy(r.ForbiddenTags) });
            }
            foreach (var m in marketList)
                if (m.Kind == MarketKind.Item && !reached.Contains(m.Id)) throw Bad($"Item market '{m.Id}' is not reached by any category rule.");
            Rules = Array.AsReadOnly(ruleList.ToArray());

            var grocerList = new List<GrocerPrice>();
            foreach (var g in grocer)
            {
                if (g == null) throw Bad("Grocer price must not be null.");
                if (!ItemCanonical.IsValidId(g.BaseId)) throw Bad($"Invalid grocer base id '{g.BaseId}'.");
                if (g.Price < 1 || g.Price > EconomyLimits.MaxPrice) throw Bad($"Grocer price of '{g.BaseId}' must be in 1..MaxPrice.");
                if (_grocer.ContainsKey(g.BaseId)) throw Bad($"Duplicate grocer base id '{g.BaseId}'.");
                _grocer.Add(g.BaseId, g.Price);
                grocerList.Add(g);
            }
            grocerList.Sort((a, b) => string.CompareOrdinal(a.BaseId, b.BaseId));
            Grocer = Array.AsReadOnly(grocerList.ToArray());

            var ruleOut = new List<ByproductRule>();
            foreach (var b in byproducts)
            {
                ValidateByproduct(b, operations);
                ruleOut.Add(b);
            }
            Byproducts = Array.AsReadOnly(ruleOut.ToArray());

            long minPrice = long.MaxValue;
            foreach (var g in grocerList) minPrice = Math.Min(minPrice, g.Price);
            if (ruleOut.Count > 0 && grocerList.Count == 0) throw Bad("Byproduct rules need a non-empty grocer list.");
            try
            {
                ByproductMaxPerComponent = ComputeB(ruleOut);
            }
            catch (OverflowException)
            {
                throw Bad("Byproduct value overflows.");
            }
            if (grocerList.Count > 0 && ByproductMaxPerComponent >= minPrice)
                throw Bad($"Anti-faucet rule violated: B={ByproductMaxPerComponent} must be below the cheapest grocer price {minPrice}.");
        }

        public IReadOnlyList<MarketDef> Markets { get; }
        public IReadOnlyList<CategoryRule> Rules { get; }
        public IReadOnlyList<ByproductRule> Byproducts { get; }
        public IReadOnlyList<GrocerPrice> Grocer { get; }

        /// <summary>B: worst-case lifetime byproduct value per item component (whole dollars).</summary>
        public long ByproductMaxPerComponent { get; }

        public bool TryGetMarket(string? id, out MarketDef market)
        {
            if (id != null && _markets.TryGetValue(id, out var m)) { market = m; return true; }
            market = null!;
            return false;
        }

        public bool TryGetGrocerPrice(string? baseId, out long price)
        {
            if (baseId != null && _grocer.TryGetValue(baseId, out price)) return true;
            price = 0;
            return false;
        }

        public static EconomyCatalog CreateDefault()
        {
            const int jitterSmooth = 10;
            var meal = new[] { 0, 0, 0, 0, 0, 0, 1, 3, 2, 1, 1, 4, 6, 3, 1, 1, 1, 2, 5, 6, 3, 1, 0, 0 };
            var snack = new int[24];
            var drink = new int[24];
            for (int h = 8; h <= 21; h++) snack[h] = 1;
            for (int h = 7; h <= 22; h++) drink[h] = 1;
            var markets = new[]
            {
                new MarketDef("meal", MarketKind.Item, 300, 1200, 350, 150, new DemandProfile(150, meal, jitterSmooth), 25),
                new MarketDef("snack", MarketKind.Item, 100, 400, 350, 150, new DemandProfile(80, snack, jitterSmooth), 25),
                new MarketDef("drink", MarketKind.Item, 50, 250, 350, 150, new DemandProfile(200, drink, jitterSmooth), 25),
                new MarketDef("food_scraps", MarketKind.Commodity, 2, 3, 0, 0, null, 25),
                new MarketDef("used_cooking_oil", MarketKind.Commodity, 2, 4, 0, 0, null, 25),
            };
            var none = Array.Empty<string>();
            var rules = new[]
            {
                new CategoryRule("drink", new[] { "plated", "drink" }, none, 1),
                new CategoryRule("meal", new[] { "plated" }, new[] { "drink" }, 2),
                new CategoryRule("snack", new[] { "plated" }, new[] { "drink" }, 1),
            };
            var byproducts = new[]
            {
                new ByproductRule(ByproductTrigger.Operation, "cut", "food_scraps", 0, 1),
                new ByproductRule(ByproductTrigger.Operation, "fry", "used_cooking_oil", 1, 0),
                new ByproductRule(ByproductTrigger.Discard, "", "food_scraps", 0, 1),
            };
            var grocer = new[]
            {
                new GrocerPrice("salt", 35), new GrocerPrice("basil", 40), new GrocerPrice("oil", 50),
                new GrocerPrice("onion", 50), new GrocerPrice("potato", 60), new GrocerPrice("tomato", 70),
                new GrocerPrice("apple", 80), new GrocerPrice("cashew", 150),
                // Spec 028: the street-food stock (D27: plant-based only).
                new GrocerPrice("lettuce", 45), new GrocerPrice("cucumber", 50), new GrocerPrice("red_cabbage", 55),
                new GrocerPrice("flatbread", 60), new GrocerPrice("lemon", 60), new GrocerPrice("wrap", 70),
                new GrocerPrice("garlic_herb_sauce", 80), new GrocerPrice("chili_sauce", 80),
                new GrocerPrice("falafel_mix", 90), new GrocerPrice("seitan", 110),
                // Spec 030: the grill vegetables.
                new GrocerPrice("paprika", 60), new GrocerPrice("eggplant", 70), new GrocerPrice("mushroom", 80),
                // Spec 031: packaging at the assembly board (defaults to review; no cheaper than the cheapest food, anti-faucet rule).
                new GrocerPrice("wrap_paper", 35), new GrocerPrice("takeaway_box", 40), new GrocerPrice("paper_bag", 35),
            };
            return new EconomyCatalog(markets, rules, byproducts, grocer, OperationCatalog.CreateDefault());
        }

        private static ArgumentException Bad(string message) => new ArgumentException(message);

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> list) => Array.AsReadOnly(new List<T>(list).ToArray());

        private static void ValidateMarket(MarketDef m)
        {
            if (m == null) throw Bad("Market must not be null.");
            if (!ItemCanonical.IsValidId(m.Id)) throw Bad($"Invalid market id '{m.Id}'.");
            if (m.Floor < 1 || m.Floor > EconomyLimits.MaxPrice) throw Bad($"Market '{m.Id}': Floor must be in 1..MaxPrice.");
            if (m.Ceiling < m.Floor || m.Ceiling > EconomyLimits.MaxPrice) throw Bad($"Market '{m.Id}': Ceiling must be in Floor..MaxPrice.");
            if (2 * m.Ceiling < 3 * m.Floor) throw Bad($"Market '{m.Id}': margin rule 2*Ceiling >= 3*Floor violated.");
            if (m.NpcQualityPermille < 0 || m.NpcQualityPermille > 499) throw Bad($"Market '{m.Id}': NpcQualityPermille must be in 0..499.");
            if (m.MinQualityPermille < 0 || m.MinQualityPermille > 1000) throw Bad($"Market '{m.Id}': MinQualityPermille must be in 0..1000.");
            if (m.SmoothingPercent < 1 || m.SmoothingPercent > 100) throw Bad($"Market '{m.Id}': SmoothingPercent must be in 1..100.");
            if (m.Kind != MarketKind.Item && m.Kind != MarketKind.Commodity) throw Bad($"Market '{m.Id}': unknown kind.");
            var d = m.Demand;
            if (d == null) return;
            if (d.HourWeights == null || d.HourWeights.Count != 24) throw Bad($"Market '{m.Id}': HourWeights must have 24 entries.");
            long sum = 0;
            foreach (var w in d.HourWeights)
            {
                if (w < 0 || w > 1000) throw Bad($"Market '{m.Id}': hour weight outside 0..1000.");
                sum += w;
            }
            if (sum == 0) throw Bad($"Market '{m.Id}': hour weights sum to 0.");
            if (d.UnitsPerNpcPerDayCenti < 0 || d.UnitsPerNpcPerDayCenti > 100_000) throw Bad($"Market '{m.Id}': UnitsPerNpcPerDayCenti must be in 0..100000.");
            if (d.JitterPercent < 0 || d.JitterPercent > 50) throw Bad($"Market '{m.Id}': JitterPercent must be in 0..50.");
        }

        private void ValidateByproduct(ByproductRule b, OperationCatalog operations)
        {
            if (b == null) throw Bad("Byproduct rule must not be null.");
            if (!ItemCanonical.IsValidId(b.GoodId)) throw Bad($"Invalid good id '{b.GoodId}'.");
            if (!_markets.TryGetValue(b.GoodId, out var good) || good.Kind != MarketKind.Commodity)
                throw Bad($"Byproduct good '{b.GoodId}' is not a Commodity market.");
            if (b.FixedUnits < 0 || b.FixedUnits > 1000 || b.UnitsPerComponent < 0 || b.UnitsPerComponent > 1000
                || (b.FixedUnits == 0 && b.UnitsPerComponent == 0))
                throw Bad("Byproduct unit fields must be in 0..1000 and not both 0.");
            switch (b.Trigger)
            {
                case ByproductTrigger.Operation:
                    if (!operations.TryGet(b.SourceId, out var op)) throw Bad($"Byproduct rule for unknown operation '{b.SourceId}'.");
                    if (op.Kind == OperationKind.Mix) throw Bad("Byproduct rules on mix operations are not allowed.");
                    break;
                case ByproductTrigger.Sale:
                    if (!_markets.TryGetValue(b.SourceId ?? "", out var mk) || mk.Kind != MarketKind.Item)
                        throw Bad("Sale byproduct rule must name an Item market.");
                    break;
                case ByproductTrigger.Discard:
                    if (b.SourceId != "") throw Bad("Discard byproduct rule must have an empty SourceId.");
                    break;
                default:
                    throw Bad("Unknown byproduct trigger.");
            }
        }

        private long ComputeB(List<ByproductRule> rules)
        {
            checked
            {
                var perOp = new Dictionary<string, long>(StringComparer.Ordinal);
                var perSale = new Dictionary<string, long>(StringComparer.Ordinal);
                long discard = 0;
                foreach (var r in rules)
                {
                    long v = (r.FixedUnits + r.UnitsPerComponent) * _markets[r.GoodId].Ceiling;
                    if (r.Trigger == ByproductTrigger.Discard) { discard += v; continue; }
                    var map = r.Trigger == ByproductTrigger.Operation ? perOp : perSale;
                    map.TryGetValue(r.SourceId, out var cur);
                    map[r.SourceId] = cur + v;
                }
                long vOp = 0, vSale = 0;
                foreach (var kv in perOp) vOp = Math.Max(vOp, kv.Value);
                foreach (var kv in perSale) vSale = Math.Max(vSale, kv.Value);
                return (CookingLimits.MaxDepth - 1) * vOp + Math.Max(discard, vSale);
            }
        }
    }
}
