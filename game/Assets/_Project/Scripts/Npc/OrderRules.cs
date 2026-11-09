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
            if (ordered.Template != null) return MatchBuild(served, ordered); // spec 031
            var parts = new List<Part>();
            bool mixed = false;
            Collect(served, null, null, null, parts, ref mixed);
            if (parts.Count == 0) return OrderMatch.Wrong;
            if (ordered.Main != null && !HasBase(parts, ordered.Main)) return OrderMatch.Wrong;

            int misses = Misses(ordered, parts, mixed);
            if (misses == 0) return OrderMatch.Exact;
            return misses == 1 ? OrderMatch.Close : OrderMatch.Wrong;
        }

        /// <summary>
        /// Spec 031: a dish built on the assembly board matches by its recipe template, layer by layer: no build, or the main
        /// ingredient missing, is Wrong; then one step short (a required layer missing or prepared wrong, the close or the pack
        /// short of the template) is Close, none is Exact, more is Wrong. The layer order and the sauce amount only cost score
        /// (forgiving), never the match; extra layers are neutral.
        /// </summary>
        public static OrderMatch MatchBuild(Item served, MenuDish ordered)
        {
            RecipeTemplate template = RecipeTemplates.ById(ordered.Template);
            if (served == null || template == null || !Assembly.IsBuild(served)) return OrderMatch.Wrong;
            if (ordered.Main != null)
            {
                bool main = false;
                foreach (Item layer in Assembly.LayersOf(served))
                    if (Assembly.BaseOf(layer) == ordered.Main) main = true;
                if (!main) return OrderMatch.Wrong;
            }
            TemplateScore s = RecipeTemplates.Score(served, template);
            int misses = s.Missing + s.WrongPrep + s.CloseShort + s.PackShort;
            if (misses == 0) return OrderMatch.Exact;
            return misses == 1 ? OrderMatch.Close : OrderMatch.Wrong;
        }

        /// <summary>
        /// Spec 032: the served item against a guest's order: the dish and its packed modifiers (<see cref="OrderModifiers.Pack"/>).
        /// A dish built on the board is checked per layer with the modifiers (<see cref="OrderModifiers.Check"/>, the 031 template
        /// match extended): Exact with no step short, Close with one, Wrong with more, no build or no main ingredient; its quality is
        /// the order's (scored against the order's template). Other dishes match as in 022 (no modifiers exist for them) with the
        /// item's own rating. Misses: 0 Exact, 1 Close, at least 2 Wrong.
        /// </summary>
        public static OrderFit Fit(Item served, MenuDish ordered, byte modifiers)
        {
            if (served == null || ordered == null) return new OrderFit(OrderMatch.Wrong, 2, 0f, 1);
            float itemQuality = Worktown.Sim.Cooking.ItemRater.Rate(served).Quality;
            RecipeTemplate template = ordered.Template != null ? RecipeTemplates.ById(ordered.Template) : null;
            if (template == null)
            {
                OrderMatch plain = Match(served, ordered);
                int m = plain == OrderMatch.Exact ? 0 : plain == OrderMatch.Close ? 1 : 2;
                return new OrderFit(plain, m, itemQuality, Worktown.Game.Cooking.StarRating.Stars(itemQuality));
            }
            if (!Assembly.IsBuild(served) || !HasMainLayer(served, ordered))
                return new OrderFit(OrderMatch.Wrong, 2, itemQuality, Worktown.Game.Cooking.StarRating.Stars(itemQuality));
            ModifierCheck check = OrderModifiers.Check(served, template, OrderModifiers.Unpack(template, modifiers));
            int misses = check.Misses;
            OrderMatch match = misses == 0 ? OrderMatch.Exact : misses == 1 ? OrderMatch.Close : OrderMatch.Wrong;
            float quality = check.Score.Quality;
            return new OrderFit(match, misses, quality, Worktown.Game.Cooking.StarRating.Stars(quality));
        }

        /// <summary>Spec 032: <see cref="Fit"/>'s match only.</summary>
        public static OrderMatch Match(Item served, MenuDish ordered, byte modifiers) => Fit(served, ordered, modifiers).Match;

        private static bool HasMainLayer(Item served, MenuDish ordered)
        {
            if (ordered.Main == null) return true;
            foreach (Item layer in Assembly.LayersOf(served))
                if (Assembly.BaseOf(layer) == ordered.Main) return true;
            return false;
        }

        /// <summary>Spec 032: the guest's share of the sale price in permille by the match (no star tip any more: the tip is
        /// <see cref="Worktown.Sim.Economy.TipRules"/>): exact 100 %, close 60 %, wrong 25 %.</summary>
        public static int GuestSharePermille(OrderMatch match)
        {
            switch (match)
            {
                case OrderMatch.Exact: return ExactPermille;
                case OrderMatch.Close: return ClosePermille;
                default: return WrongPermille;
            }
        }

        /// <summary>
        /// Spec 032: the bill's share of the sim's sale payout in permille: the match's share, times the order's quality over the
        /// item's in the sim's demand formula (price × (500 + q) / 1000), so a build the guest asked to be without a layer is paid
        /// as the order, not as an incomplete recipe. Deterministic; at most 1.5 × the share (q 0 to 1000).
        /// </summary>
        public static int BillPermille(OrderMatch match, float itemQuality, float orderQuality)
        {
            long share = GuestSharePermille(match);
            long qi = 500 + Worktown.Sim.Economy.PriceRules.QualityPermille(itemQuality);
            long qo = 500 + Worktown.Sim.Economy.PriceRules.QualityPermille(orderQuality);
            return (int)(share * qo / qi);
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
                if (need.AnyOf != null && !Contains(need.AnyOf, part.BaseId)) continue; // 030 review
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
