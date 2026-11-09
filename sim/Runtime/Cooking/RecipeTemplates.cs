using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 031: the recipe templates of the assembly board and the score of a build against one. Pure and deterministic.
    /// <para>Claiming: the template's layers in order each take the first layer of the build that is not taken yet and fits
    /// (its base ingredient, a needed heat op, a needed cut); a required layer that only fits by ingredient is taken as prepared
    /// wrong. Layers no template layer took are extras: neutral, they neither count in the rating nor cost anything, so piling
    /// more layers on (or squeezing a sauce twice) can never raise the score.</para>
    /// <para>Penalties (defaults to review): a missing required layer <see cref="MissingPenalty"/>, a layer prepared wrong
    /// <see cref="WrongPrepPenalty"/>, each pair of claimed layers out of role order <see cref="OrderPenalty"/> (at most
    /// <see cref="MaxOrderPenalty"/>: forgiving), each sauce layer with too little or too much <see cref="SaucePenalty"/>, the close
    /// step missing <see cref="CloseMissingPenalty"/> (folded where it should be wrapped <see cref="ClosePartialPenalty"/>), the pack
    /// missing <see cref="PackMissingPenalty"/> (the other container <see cref="PackWrongPenalty"/>). Factor = 1 minus them, at least
    /// <see cref="MinFactor"/>; it is never above 1 (no step gives a bonus).</para>
    /// <para>Rating: Taste of the claimed layers' combined stats (weight-weighted, a balanced wrap tastes better than its parts) ×
    /// (0.2 + 0.8 × their doneness factor; a layer the template takes raw, such as lettuce or a wrap, counts as done when it
    /// was not heated) × their quality factor (the fryer's shake bonus) × Factor, clamped 0..1.</para>
    /// </summary>
    public static class RecipeTemplates
    {
        public const float MissingPenalty = 0.25f;
        public const float WrongPrepPenalty = 0.1f;
        public const float OrderPenalty = 0.06f;
        public const float MaxOrderPenalty = 0.18f;
        public const float SaucePenalty = 0.05f;
        public const float CloseMissingPenalty = 0.15f;
        public const float ClosePartialPenalty = 0.08f;
        public const float PackMissingPenalty = 0.12f;
        public const float PackWrongPenalty = 0.06f;
        public const float MinFactor = 0.2f;

        public const string SeitanWrapId = "seitan_wrap";
        public const string FalafelWrapId = "falafel_wrap";
        public const string SeitanFlatbreadId = "seitan_flatbread";
        public const string VeggieBoxId = "veggie_box";

        private static readonly string[] Wrap = { "wrap" };
        private static readonly string[] Flatbread = { "flatbread" };
        private static readonly string[] Seitan = { "seitan" };
        private static readonly string[] Falafel = { "falafel_mix" };
        private static readonly string[] Potato = { "potato" };
        private static readonly string[] Lettuce = { "lettuce" };
        private static readonly string[] CabbageOrLettuce = { "red_cabbage", "lettuce" };
        private static readonly string[] Salads = { "cucumber", "tomato", "red_cabbage", "onion", "lettuce" };
        private static readonly string[] GrillVegetables = { "paprika", "eggplant", "mushroom" };
        private static readonly string[] GarlicHerb = { "garlic_herb_sauce" };
        private static readonly string[] Chili = { "chili_sauce" };
        private static readonly string[] AnySauce = { "garlic_herb_sauce", "chili_sauce" };
        private static readonly string[] Griddled = { Griddle.OperationId };
        private static readonly string[] DeepFried = { DeepFry.OperationId };
        private static readonly string[] FriedPotato = { DeepFry.OperationId, "fry" };

        private static TemplateLayer Layer(LayerRole role, string[] bases, string[]? ops = null, bool cut = false, bool required = true) =>
            new TemplateLayer(role, Array.AsReadOnly(bases), ops != null ? Array.AsReadOnly(ops) : null, cut, required);

        /// <summary>The four dishes of spec 031 (defaults to review), in menu order.</summary>
        public static readonly IReadOnlyList<RecipeTemplate> Default = Array.AsReadOnly(new[]
        {
            new RecipeTemplate(SeitanWrapId, "seitan wrap", Array.AsReadOnly(new[]
            {
                Layer(LayerRole.Base, Wrap),
                Layer(LayerRole.Filling, Seitan, Griddled),
                Layer(LayerRole.Salad, Lettuce, cut: true),
                Layer(LayerRole.Salad, Salads, cut: true, required: false),
                Layer(LayerRole.Sauce, Chili),
            }), Assembly.Wrapped, null),
            new RecipeTemplate(FalafelWrapId, "falafel wrap", Array.AsReadOnly(new[]
            {
                Layer(LayerRole.Base, Wrap),
                Layer(LayerRole.Filling, Falafel, DeepFried),
                Layer(LayerRole.Salad, Lettuce, cut: true),
                Layer(LayerRole.Salad, Salads, cut: true, required: false),
                Layer(LayerRole.Sauce, GarlicHerb),
            }), Assembly.Wrapped, null),
            new RecipeTemplate(SeitanFlatbreadId, "seitan flatbread", Array.AsReadOnly(new[]
            {
                Layer(LayerRole.Base, Flatbread),
                Layer(LayerRole.Filling, Seitan, Griddled),
                Layer(LayerRole.Salad, CabbageOrLettuce, cut: true),
                Layer(LayerRole.Salad, Salads, cut: true, required: false),
                Layer(LayerRole.Sauce, AnySauce),
            }), Assembly.Folded, Assembly.Bag),
            new RecipeTemplate(VeggieBoxId, "veggie box", Array.AsReadOnly(new[]
            {
                Layer(LayerRole.Filling, Potato, FriedPotato, cut: true),
                Layer(LayerRole.Filling, GrillVegetables, Griddled),
                Layer(LayerRole.Filling, GrillVegetables, Griddled, required: false),
                Layer(LayerRole.Sauce, AnySauce),
            }), null, Assembly.Box),
        });

        /// <summary>The template with this id, or null.</summary>
        public static RecipeTemplate? ById(string? id)
        {
            foreach (RecipeTemplate t in Default)
                if (string.Equals(t.Id, id, StringComparison.Ordinal)) return t;
            return null;
        }

        /// <summary>The score of <paramref name="build"/> (any build item: open, closed, packed, on a plate) against <paramref name="template"/>.</summary>
        public static TemplateScore Score(Item build, RecipeTemplate template)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (template == null) throw new ArgumentNullException(nameof(template));
            IReadOnlyList<Item> layers = Assembly.LayersOf(build);
            int n = layers.Count;
            var taken = new bool[n];
            var claimedAt = new List<int>();
            var claimedRole = new List<LayerRole>();
            var claimedRaw = new List<bool>();
            int missing = 0, wrong = 0, sauceOff = 0;
            foreach (TemplateLayer tl in template.Layers)
            {
                int pick = -1;
                bool ok = false;
                for (int i = 0; i < n && pick < 0; i++)
                    if (!taken[i] && Fits(tl, layers[i], true)) { pick = i; ok = true; }
                if (pick < 0 && tl.Required)
                    for (int i = 0; i < n && pick < 0; i++)
                        if (!taken[i] && Fits(tl, layers[i], false)) pick = i;
                if (pick < 0)
                {
                    if (tl.Required) missing++;
                    continue;
                }
                taken[pick] = true;
                if (!ok) wrong++;
                if (tl.Role == LayerRole.Sauce && Assembly.SauceAmountOf(layers[pick]) != Assembly.Right) sauceOff++;
                claimedAt.Add(pick);
                claimedRole.Add(tl.Role);
                claimedRaw.Add(tl.Ops == null);
            }

            int inversions = 0;
            for (int a = 0; a < claimedAt.Count; a++)
                for (int b = 0; b < claimedAt.Count; b++)
                    if (claimedAt[a] < claimedAt[b] && claimedRole[a] > claimedRole[b]) inversions++;

            string? close = Assembly.CloseOf(build);
            float closePenalty = 0f;
            int closeShort = 0;
            if (template.Close == Assembly.Wrapped && close != Assembly.Wrapped)
            {
                closePenalty = close == Assembly.Folded ? ClosePartialPenalty : CloseMissingPenalty;
                closeShort = 1;
            }
            else if (template.Close == Assembly.Folded && close == null)
            {
                closePenalty = CloseMissingPenalty;
                closeShort = 1;
            }
            string? pack = Assembly.PackOf(build);
            float packPenalty = 0f;
            int packShort = 0;
            if (template.Pack != null && pack != template.Pack)
            {
                packPenalty = pack == null ? PackMissingPenalty : PackWrongPenalty;
                packShort = 1;
            }

            float penalty = missing * MissingPenalty + wrong * WrongPrepPenalty + Math.Min(MaxOrderPenalty, inversions * OrderPenalty)
                + sauceOff * SaucePenalty + closePenalty + packPenalty;
            float factor = Math.Max(MinFactor, Math.Min(1f, 1f - penalty));

            float taste = 0f, done = 0f, quality = 0f;
            if (claimedAt.Count > 0)
            {
                float total = 0f, qf = 0f, doneSum = 0f;
                float sweet = 0, salty = 0, sour = 0, bitter = 0, umami = 0, fat = 0, crunch = 0, heat = 0, doneness = 0, moist = 0;
                for (int k = 0; k < claimedAt.Count; k++)
                {
                    Item layer = layers[claimedAt[k]];
                    float w = layer.Weight;
                    ItemStats s = layer.Stats;
                    total += w;
                    sweet += s.Sweet * w; salty += s.Salty * w; sour += s.Sour * w; bitter += s.Bitter * w; umami += s.Umami * w;
                    fat += s.Fat * w; crunch += s.Crunch * w; heat += s.HeatTolerance * w; doneness += s.Doneness * w; moist += s.Moisture * w;
                    qf += layer.QualityFactor * w;
                    bool rawFits = claimedRaw[k] && Assembly.HeatOpOf(layer) == null;
                    doneSum += (rawFits ? 1f : ItemRater.Rate(layer).DonenessFactor) * w;
                }
                var combined = new ItemStats(sweet / total, salty / total, sour / total, bitter / total, umami / total, fat / total,
                    crunch / total, heat / total, doneness / total, moist / total);
                taste = ItemRater.Taste(combined);
                done = doneSum / total;
                quality = Clamp01(taste * (0.2f + 0.8f * done) * (qf / total) * factor);
            }
            return new TemplateScore(template, quality, taste, done, factor, missing, wrong, inversions, sauceOff, closeShort, packShort,
                claimedAt.Count);
        }

        /// <summary>The template the build rates best against (the first on a tie); null for no build.</summary>
        public static TemplateScore? Best(Item build)
        {
            if (!Assembly.IsBuild(build)) return null;
            TemplateScore? best = null;
            foreach (RecipeTemplate t in Default)
            {
                TemplateScore s = Score(build, t);
                if (best == null || s.Quality > best.Quality) best = s;
            }
            return best;
        }

        /// <summary>
        /// The dish a build is named after: the best rated template that has every required layer on it (prepared right or not),
        /// or null. Used for names only.
        /// </summary>
        public static RecipeTemplate? Named(Item build)
        {
            TemplateScore? best = null;
            foreach (RecipeTemplate t in Default)
            {
                TemplateScore s = Score(build, t);
                if (!s.HasAllLayers) continue;
                if (best == null || s.Quality > best.Quality) best = s;
            }
            return best?.Template;
        }

        /// <summary>Whether <paramref name="layer"/> fits the template layer: its ingredient and, with <paramref name="prepared"/>, its prep.</summary>
        private static bool Fits(TemplateLayer tl, Item layer, bool prepared)
        {
            if (!Assembly.Contains(tl.Bases, Assembly.BaseOf(layer))) return false;
            if (!prepared) return true;
            if (tl.NeedsCut && !Assembly.IsCut(layer)) return false;
            if (tl.Ops != null && !Assembly.Contains(tl.Ops, Assembly.HeatOpOf(layer))) return false;
            return true;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
