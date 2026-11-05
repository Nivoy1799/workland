using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Worktown.Sim.Cooking
{
    public static class DishScorer
    {
        /// <summary>
        /// Profile = component-wise average of ingredient stats.
        ///
        /// Quality (0..1) = 0.5 * balance + 0.25 * umamiScore + 0.25 * fatScore, where
        ///   tastes   = Sweet, Salty, Sour, Bitter, Umami of the profile
        ///   dominance = max(tastes) / sum(tastes)        (1 if sum is 0); in 0.2..1 for valid input (stats are
        ///                validated to 0..1; an all-zero taste sum is defined as 1)
        ///   balance  = 1 - (dominance - 0.2) / 0.8, clamped 0..1  (even mix = 1, one taste = 0)
        ///   umamiScore = 1 - |Umami - 0.4| / 0.6, clamped 0..1    (rewards moderate umami)
        ///   fatScore   = 1 - |Fat - 0.4| / 0.6, clamped 0..1      (rewards moderate fat)
        ///
        /// ComboHash = SHA-256 (lowercase hex) of the ordinally sorted ingredient ids joined
        /// with '\n'. Duplicates are kept: two tomatoes differ from one tomato.
        ///
        /// Ingredients are sorted by Id (ordinal) first, so sums and hash are order independent.
        /// Throws ArgumentException if an ingredient is null, has null Stats, has an Id that is
        /// null, empty or contains '\n', or has a stat that is NaN, infinite or outside 0..1.
        /// </summary>
        public static DishResult Score(IReadOnlyList<Ingredient> ingredients)
        {
            if (ingredients == null) throw new ArgumentNullException(nameof(ingredients));
            if (ingredients.Count == 0) throw new ArgumentException("At least one ingredient is required.", nameof(ingredients));

            var sorted = new List<Ingredient>(ingredients.Count);
            foreach (var ingredient in ingredients)
            {
                Validate(ingredient);
                sorted.Add(ingredient);
            }
            sorted.Sort((x, y) => StringComparer.Ordinal.Compare(x.Id, y.Id));

            float sweet = 0, salty = 0, sour = 0, bitter = 0, umami = 0, fat = 0, crunch = 0, heat = 0;
            var ids = new List<string>(sorted.Count);
            foreach (var ingredient in sorted)
            {
                var s = ingredient.Stats;
                sweet += s.Sweet; salty += s.Salty; sour += s.Sour; bitter += s.Bitter; umami += s.Umami;
                fat += s.Fat; crunch += s.Crunch; heat += s.HeatTolerance;
                ids.Add(ingredient.Id);
            }

            float n = sorted.Count;
            var profile = new IngredientStats(
                sweet / n, salty / n, sour / n, bitter / n, umami / n,
                fat / n, crunch / n, heat / n);

            return new DishResult(ComputeQuality(profile), profile, ComputeHash(ids));
        }

        private static void Validate(Ingredient ingredient)
        {
            if (ingredient == null) throw new ArgumentException("Ingredient must not be null.", "ingredients");
            if (string.IsNullOrEmpty(ingredient.Id) || ingredient.Id.Contains('\n'))
                throw new ArgumentException("Ingredient Id must be non-empty and must not contain '\\n'.", "ingredients");
            var s = ingredient.Stats;
            if (s == null) throw new ArgumentException($"Ingredient '{ingredient.Id}' has null Stats.", "ingredients");
            CheckStat(ingredient.Id, nameof(s.Sweet), s.Sweet);
            CheckStat(ingredient.Id, nameof(s.Salty), s.Salty);
            CheckStat(ingredient.Id, nameof(s.Sour), s.Sour);
            CheckStat(ingredient.Id, nameof(s.Bitter), s.Bitter);
            CheckStat(ingredient.Id, nameof(s.Umami), s.Umami);
            CheckStat(ingredient.Id, nameof(s.Fat), s.Fat);
            CheckStat(ingredient.Id, nameof(s.Crunch), s.Crunch);
            CheckStat(ingredient.Id, nameof(s.HeatTolerance), s.HeatTolerance);
        }

        private static void CheckStat(string id, string name, float value)
        {
            // NaN fails both comparisons, so test the valid range positively.
            if (!(value >= 0f && value <= 1f))
                throw new ArgumentException($"Ingredient '{id}' stat {name} must be a finite value in 0..1.", "ingredients");
        }

        private static float ComputeQuality(IngredientStats p)
        {
            float sum = p.Sweet + p.Salty + p.Sour + p.Bitter + p.Umami;
            float max = Math.Max(Math.Max(Math.Max(p.Sweet, p.Salty), Math.Max(p.Sour, p.Bitter)), p.Umami);
            float dominance = sum <= 0f ? 1f : max / sum;
            float balance = Clamp01(1f - (dominance - 0.2f) / 0.8f);
            float umamiScore = Clamp01(1f - Math.Abs(p.Umami - 0.4f) / 0.6f);
            float fatScore = Clamp01(1f - Math.Abs(p.Fat - 0.4f) / 0.6f);
            return 0.5f * balance + 0.25f * umamiScore + 0.25f * fatScore;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static string ComputeHash(List<string> sortedIds)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(string.Join("\n", sortedIds));
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
