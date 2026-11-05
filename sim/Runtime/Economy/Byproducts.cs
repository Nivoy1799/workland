using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Sim.Economy
{
    /// <summary>Data-driven byproduct goods. The server calls these and credits via EconomyEngine.AddGoods.</summary>
    public static class Byproducts
    {
        private static readonly IReadOnlyList<GoodAmount> Empty = Array.AsReadOnly(Array.Empty<GoodAmount>());

        /// <summary>Goods for one Ok ItemFactory.Apply. Empty on failure, on the cut no-op or with unpriced inputs.</summary>
        public static IReadOnlyList<GoodAmount> ForOperation(EconomyCatalog c, string operationId,
            IReadOnlyList<Item> inputs, OperationResult result)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (!result.Ok || result.Item == null) return Empty;
            if (inputs.Count == 1 && inputs[0] != null && inputs[0].Hash == result.Item.Hash) return Empty;
            long weight = 0;
            foreach (var input in inputs)
            {
                if (input == null) throw new ArgumentException("Input items must not be null.", nameof(inputs));
                if (!AllPriced(c, input)) return Empty;
                weight += input.Weight;
            }
            return Collect(c, ByproductTrigger.Operation, operationId, weight);
        }

        public static IReadOnlyList<GoodAmount> ForDiscard(EconomyCatalog c, Item item)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (!AllPriced(c, item)) return Empty;
            return Collect(c, ByproductTrigger.Discard, "", item.Weight);
        }

        /// <summary>Goods for selling an item on a market (Sale rules).</summary>
        internal static IReadOnlyList<GoodAmount> ForSale(EconomyCatalog c, string marketId, Item item)
        {
            if (!AllPriced(c, item)) return Empty;
            return Collect(c, ByproductTrigger.Sale, marketId, item.Weight);
        }

        private static bool AllPriced(EconomyCatalog c, Item item)
        {
            foreach (var comp in item.Components)
                if (!c.TryGetGrocerPrice(comp.BaseId, out _)) return false;
            return true;
        }

        private static IReadOnlyList<GoodAmount> Collect(EconomyCatalog c, ByproductTrigger trigger, string sourceId, long weight)
        {
            var sums = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var r in c.Byproducts)
            {
                if (r.Trigger != trigger || !string.Equals(r.SourceId, sourceId, StringComparison.Ordinal)) continue;
                long units = r.FixedUnits + (long)r.UnitsPerComponent * weight;
                sums.TryGetValue(r.GoodId, out var cur);
                sums[r.GoodId] = cur + units;
            }
            var list = new List<GoodAmount>();
            foreach (var kv in sums)
            {
                if (kv.Value <= 0) continue;
                // cannot overflow: validated rules bound FixedUnits/UnitsPerComponent and CookingLimits.MaxComponents bounds weight
                list.Add(new GoodAmount(kv.Key, checked((int)kv.Value)));
            }
            return list.Count == 0 ? Empty : Array.AsReadOnly(list.ToArray());
        }
    }
}
