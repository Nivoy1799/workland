using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 022: does a served sim item match a menu dish, and what share of the sale price does the customer pay.
    /// Pure and deterministic; runs on the server only (it reads the item tree, which never leaves the server).
    /// </summary>
    public static class OrderRules
    {
        public const int ExactPermille = 1000;
        /// <summary>Tip per star above <see cref="TipFromStars"/> on an exact match (10 %).</summary>
        public const int TipPermillePerStar = 100;
        public const int TipFromStars = 2;
        public const int ClosePermille = 600;
        public const int WrongPermille = 250;

        private const string MixOp = "mix";
        private const string ShapeParam = "shape";
        private const string TimeParam = "time";

        /// <summary>One base ingredient in the served item, with the last cut and last heat above it.</summary>
        private struct Part
        {
            public string BaseId;
            public string Shape;
            public string HeatOp;
            public string HeatTime;
        }

        /// <summary>
        /// Exact: everything the dish needs is there. Close: the main ingredient is there and exactly one step is wrong
        /// or missing (a cut, a heat op or its doneness, a missing ingredient, not mixed, heated though it must not be,
        /// one ingredient too many). Wrong: anything else, also a missing main ingredient or a null item.
        /// The plate op is ignored; mixes and plates are looked through to their parts.
        /// </summary>
        public static OrderMatch Match(Item served, MenuDish ordered)
        {
            if (served == null || ordered == null) return OrderMatch.Wrong;
            var parts = new List<Part>();
            bool mixed = false;
            Collect(served, null, null, null, parts, ref mixed);
            if (parts.Count == 0) return OrderMatch.Wrong;
            if (ordered.Main != null && !HasBase(parts, ordered.Main)) return OrderMatch.Wrong;

            int misses = Misses(ordered, parts, mixed);
            if (misses == 0) return OrderMatch.Exact;
            return misses == 1 ? OrderMatch.Close : OrderMatch.Wrong;
        }

        /// <summary>Share of the sale price in permille: exact 100 % + 10 % per star above 2, close 60 %, wrong 25 %.</summary>
        public static int PayPermille(OrderMatch match, int stars)
        {
            switch (match)
            {
                case OrderMatch.Exact: return ExactPermille + TipPermillePerStar * Math.Max(0, stars - TipFromStars);
                case OrderMatch.Close: return ClosePermille;
                default: return WrongPermille;
            }
        }

        /// <summary>Short text for logs and the serve feed.</summary>
        public static string Describe(OrderMatch match)
        {
            switch (match)
            {
                case OrderMatch.Exact: return "exact order";
                case OrderMatch.Close: return "close";
                default: return "wrong dish";
            }
        }

        private static int Misses(MenuDish dish, List<Part> parts, bool mixed)
        {
            int misses = 0;
            var claimed = new List<string>();
            // Named needs first (they take their own ingredient), then the "any other" ones.
            foreach (DishNeed need in dish.Needs)
                if (need.BaseId != null) misses += Claim(need, parts, claimed);
            foreach (DishNeed need in dish.Needs)
                if (need.BaseId == null) misses += Claim(need, parts, claimed);

            if (dish.NeedsMix && parts.Count >= 2 && !mixed) misses++;
            if (dish.NoHeat && AnyHeated(parts)) misses++;
            if (DistinctBases(parts) > dish.MaxIngredients) misses++;
            return misses;
        }

        /// <summary>
        /// The best fitting part for the need (fewest misses, first on ties) among the ingredients no other need took;
        /// its ingredient is then taken. A need nothing fits is one missing step.
        /// </summary>
        private static int Claim(DishNeed need, List<Part> parts, List<string> claimed)
        {
            int best = -1;
            int bestMisses = int.MaxValue;
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (part.BaseId == null || claimed.Contains(part.BaseId)) continue;
                if (need.BaseId != null && part.BaseId != need.BaseId) continue;
                int m = PartMisses(need, part);
                if (m >= bestMisses) continue;
                best = i;
                bestMisses = m;
            }
            if (best < 0) return 1;
            claimed.Add(parts[best].BaseId);
            return bestMisses;
        }

        private static int PartMisses(DishNeed need, Part part)
        {
            int misses = 0;
            if (need.NeedsCut && (part.Shape == null || (need.Shapes.Count > 0 && !Contains(need.Shapes, part.Shape))))
                misses++;
            if (need.NeedsHeat && (part.HeatOp == null || !Contains(need.HeatOps, part.HeatOp)
                || TimeRank(part.HeatTime) < TimeRank(need.MinTime)))
                misses++;
            return misses;
        }

        /// <summary>
        /// Walks the item tree from the top: the first cut met (the last applied, the finest) gives a part its shape, the
        /// first heat op met its heat and time bucket; a mix anywhere marks the dish mixed. Base items are the parts.
        /// </summary>
        private static void Collect(Item node, string shape, string heatOp, string heatTime, List<Part> parts, ref bool mixed)
        {
            if (node.OperationId == null)
            {
                parts.Add(new Part { BaseId = node.BaseId, Shape = shape, HeatOp = heatOp, HeatTime = heatTime });
                return;
            }
            if (node.OperationId == MixOp) mixed = true;
            if (shape == null && node.Buckets.TryGetValue(ShapeParam, out string s)) shape = s;
            if (heatOp == null && node.Buckets.TryGetValue(TimeParam, out string t))
            {
                heatOp = node.OperationId;
                heatTime = t;
            }
            foreach (Item input in node.Inputs) Collect(input, shape, heatOp, heatTime, parts, ref mixed);
        }

        /// <summary>Time buckets in order; empty or unknown is 0 (any).</summary>
        private static int TimeRank(string bucket)
        {
            switch (bucket)
            {
                case "short": return 1;
                case "medium": return 2;
                case "long": return 3;
                default: return 0;
            }
        }

        private static bool HasBase(List<Part> parts, string baseId)
        {
            foreach (Part part in parts)
                if (part.BaseId == baseId) return true;
            return false;
        }

        private static bool AnyHeated(List<Part> parts)
        {
            foreach (Part part in parts)
                if (part.HeatOp != null) return true;
            return false;
        }

        private static int DistinctBases(List<Part> parts)
        {
            var seen = new List<string>();
            foreach (Part part in parts)
                if (!seen.Contains(part.BaseId)) seen.Add(part.BaseId);
            return seen.Count;
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            foreach (string item in list)
                if (item == value) return true;
            return false;
        }
    }
}
