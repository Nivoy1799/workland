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
        /// DonenessFactor = mean over components of clamp01(1 - dist / 0.2), dist = distance from the ideal window.
        /// Quality = Taste * (0.2 + 0.8 * DonenessFactor) * QualityFactor (spec 029: 1 unless deep-fried), clamped 0..1. A plate
        /// is the Weight-weighted mean of its inputs' ratings.
        /// </summary>
        public static ItemRating Rate(Item item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

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
                float dist = Math.Max(0f, Math.Max(c.Ideal.Min - c.Doneness, c.Doneness - c.Ideal.Max));
                sum += Clamp01(1f - dist / 0.2f);
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
