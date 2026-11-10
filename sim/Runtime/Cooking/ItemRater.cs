using System;

namespace Worktown.Sim.Cooking
{
    public static class ItemRater
    {
        /// <summary>
        /// Taste (0..1) = 0.5 * balance + 0.25 * umamiScore + 0.25 * fatScore, where
        ///   dominance = max(tastes) / sum(tastes) over Sweet, Salty, Sour, Bitter, Umami (1 if the sum is 0)
        ///   balance = 1 - (dominance - 0.2) / 0.8, clamped 0..1
        ///   umamiScore = 1 - |Umami - 0.4| / 0.6, clamped 0..1
        ///   fatScore = 1 - |Fat - 0.4| / 0.6, clamped 0..1
        /// DonenessFactor = mean over components of DonenessZones.Factor (spec 043: 1 in the window's Perfect half, 0.9 in its Good
        /// half, falling off over 0.2 outside it).
        /// Quality = Taste * (0.2 + 0.8 * DonenessFactor) * QualityFactor (spec 029: 1 unless deep-fried), clamped 0..1. A plate
        /// is the Weight-weighted mean of its inputs' ratings.
        /// </summary>
        public static ItemRating Rate(Item item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            // Spec 031: a build (open, closed or packed) rates against its best recipe template (RecipeTemplates). A plate of a
            // build goes through the plate rule below (its one input: the build's own rating).
            if (item.Kind == OperationKind.Assemble || item.Kind == OperationKind.Close || item.Kind == OperationKind.Pack)
            {
                TemplateScore? best = RecipeTemplates.Best(item);
                if (best != null) return new ItemRating(best.Quality, best.Taste, best.DonenessFactor);
            }

            // Spec 033: a cup of a drink rates by the drink's own taste (its balance) and the fill level (the quality factor).
            if (item.Kind == OperationKind.Fill || item.Kind == OperationKind.Lid)
            {
                float drink = Drinks.Taste(item.Stats);
                return new ItemRating(Clamp01(drink * item.QualityFactor), drink, 1f);
            }

            if (item.Kind == OperationKind.Plate)
            {
                float quality = 0f, taste = 0f, factor = 0f, total = 0f;
                foreach (var input in item.Inputs)
                {
                    var r = Rate(input);
                    float w = input.Weight;
                    quality += r.Quality * w; taste += r.Taste * w; factor += r.DonenessFactor * w;
                    total += w;
                }
                return new ItemRating(quality / total, taste / total, factor / total);
            }

            float t = Taste(item.Stats);
            float sum = 0f;
            foreach (var c in item.Components)
            {
                sum += DonenessZones.Factor(c.Doneness, c.Ideal);
            }
            float f = sum / item.Components.Count;
            return new ItemRating(Clamp01(t * (0.2f + 0.8f * f) * item.QualityFactor), t, f);
        }

        public static float Taste(ItemStats p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            float sum = p.Sweet + p.Salty + p.Sour + p.Bitter + p.Umami;
            float max = Math.Max(Math.Max(Math.Max(p.Sweet, p.Salty), Math.Max(p.Sour, p.Bitter)), p.Umami);
            float dominance = sum <= 0f ? 1f : max / sum;
            float balance = Clamp01(1f - (dominance - 0.2f) / 0.8f);
            float umamiScore = Clamp01(1f - Math.Abs(p.Umami - 0.4f) / 0.6f);
            float fatScore = Clamp01(1f - Math.Abs(p.Fat - 0.4f) / 0.6f);
            return 0.5f * balance + 0.25f * umamiScore + 0.25f * fatScore;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
