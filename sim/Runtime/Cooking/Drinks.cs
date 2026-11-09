using System;
using System.Collections.Generic;
using Worktown.Sim.Core;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 033: the drinks station's sim rules, pure and deterministic. Two ops:
    /// <list type="bullet">
    /// <item><c>fill</c> (Fill): a cup of a drink, <see cref="MlParam"/> = the millilitres poured (the server measures the hold:
    /// <see cref="PourRate"/> ml per second), in the buckets <see cref="Low"/> (under <see cref="RightFrom"/>), <see cref="Right"/>
    /// (<see cref="RightFrom"/> to <see cref="FullFrom"/>), <see cref="Full"/> (to the brim, <see cref="CupMl"/>) and
    /// <see cref="Spilled"/> (over the brim; the server stops at <see cref="MaxMl"/>). Its one input is the drink itself (a base
    /// item tagged <see cref="DrinkTag"/>); the cup is implied (an empty cup is the base item <see cref="CupId"/>).</item>
    /// <item><c>lid</c> (Lid): a lid on a filled cup: finished (tagged "plated"), sold as it is in the drink market.</item>
    /// </list>
    /// The fill level only changes the quality (<see cref="FillFactor"/>), never the taste; a drink rates by <see cref="Taste"/>.
    /// No animal products anywhere (D27).
    /// </summary>
    public static class Drinks
    {
        public const string FillOperationId = "fill";
        public const string LidOperationId = "lid";
        public const string Station = "drinks";
        public const string MlParam = "ml";

        /// <summary>Fill buckets (ids).</summary>
        public const string Low = "low";
        public const string Right = "right";
        public const string Full = "full";
        public const string Spilled = "spilled";

        /// <summary>A cup holds this much (ml).</summary>
        public const float CupMl = 400f;
        /// <summary>The fill line: from here (ml) the cup is right (spec 033 default to review: 340–380 ml).</summary>
        public const float RightFrom = 340f;
        /// <summary>From here (ml) the cup is full to the brim (no longer right).</summary>
        public const float FullFrom = 380f;
        /// <summary>From here (ml) it ran over: more than a millilitre over the brim is a spill.</summary>
        public const float SpillFrom = CupMl + 1f;
        /// <summary>The server stops pouring here (ml): a spill, the cup is full.</summary>
        public const float MaxMl = 500f;
        /// <summary>ml per second a tap pours (spec 033 default to review: a good hold is about 1.7–1.9 s).</summary>
        public const float PourRate = 200f;

        public const string DrinkTag = "drink";
        public const string CupTag = "cup";
        public const string PlatedTag = "plated";
        public const string CupId = "cup";

        // Stats: sweet, salty, sour, bitter, umami, fat, crunch, heat tolerance, doneness, moisture. Drinks are not cooked (window 0..1).
        public static readonly BaseItemDef Cup = new BaseItemDef(CupId, "empty cup",
            new ItemStats(0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f), new DonenessWindow(0f, 1f), Array.AsReadOnly(new[] { CupTag }));
        public static readonly BaseItemDef Lemonade = Drink("lemonade", "lemonade", 0.45f, 0f, 0.40f, 0.05f, 0f);
        public static readonly BaseItemDef IcedTea = Drink("iced_tea", "iced tea", 0.35f, 0f, 0.15f, 0.25f, 0.05f);
        public static readonly BaseItemDef Soda = Drink("soda", "soda", 0.60f, 0.05f, 0.15f, 0.05f, 0f);

        /// <summary>The drinks in tap order: lemonade, iced tea, soda.</summary>
        public static readonly IReadOnlyList<BaseItemDef> All = Array.AsReadOnly(new[] { Lemonade, IcedTea, Soda });

        /// <summary>The empty cup and the three drinks (the base items the station needs), in that order.</summary>
        public static readonly IReadOnlyList<BaseItemDef> Items = Array.AsReadOnly(new[] { Cup, Lemonade, IcedTea, Soda });

        /// <summary>The fill parameters for <paramref name="ml"/> poured (clamped to 0..<see cref="MaxMl"/>; NaN is 0).</summary>
        public static IReadOnlyDictionary<string, ParamValue> FillParams(float ml)
        {
            return new Dictionary<string, ParamValue> { [MlParam] = ParamValue.Number(ClampMl(ml)) };
        }

        /// <summary><paramref name="ml"/> clamped to 0..<see cref="MaxMl"/>; NaN and negatives are 0.</summary>
        public static float ClampMl(float ml) => float.IsNaN(ml) || ml < 0f ? 0f : Math.Min(ml, MaxMl);

        /// <summary>The fill bucket of <paramref name="ml"/> (the catalog's buckets): low, right, full or spilled.</summary>
        public static string BucketOf(float ml)
        {
            float x = ClampMl(ml);
            if (x >= SpillFrom) return Spilled;
            if (x >= FullFrom) return Full;
            return x >= RightFrom ? Right : Low;
        }

        /// <summary>
        /// The quality factor of a cup with <paramref name="ml"/> in it: 1 at the fill line (right), 0.9 full to the brim (hard to
        /// carry), 0.6 spilled over; a low cup from 0.3 (a splash) rising to 1 at the line. Exact values only change quality (D20).
        /// </summary>
        public static float FillFactor(float ml)
        {
            float x = ClampMl(ml);
            if (x >= SpillFrom) return 0.6f;
            if (x >= FullFrom) return 0.9f;
            if (x >= RightFrom) return 1f;
            return 0.3f + 0.7f * (x / RightFrom);
        }

        /// <summary>
        /// A drink's taste (0..1): drinks are about the balance of their five tastes, not umami or fat: 0.4 + 0.6 × balance, the
        /// balance as in <see cref="ItemRater.Taste"/> (1 − (dominance − 0.2) / 0.8, clamped).
        /// </summary>
        public static float Taste(ItemStats p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            float sum = p.Sweet + p.Salty + p.Sour + p.Bitter + p.Umami;
            float max = Math.Max(Math.Max(Math.Max(p.Sweet, p.Salty), Math.Max(p.Sour, p.Bitter)), p.Umami);
            float dominance = sum <= 0f ? 1f : max / sum;
            float balance = 1f - (dominance - 0.2f) / 0.8f;
            balance = balance < 0f ? 0f : balance > 1f ? 1f : balance;
            return 0.4f + 0.6f * balance;
        }

        /// <summary>A drink base item (tagged <see cref="DrinkTag"/>, no op).</summary>
        public static bool IsDrinkBase(Item? item) => item != null && item.BaseId != null && HasTag(item, DrinkTag);

        /// <summary>The empty cup (the base item <see cref="CupId"/>).</summary>
        public static bool IsEmptyCup(Item? item) => item != null && item.BaseId == CupId;

        /// <summary>A filled cup without a lid (a fill item).</summary>
        public static bool IsFill(Item? item) => item != null && item.OperationId == FillOperationId;

        /// <summary>A filled cup with a lid.</summary>
        public static bool IsLidded(Item? item) => item != null && item.OperationId == LidOperationId;

        /// <summary>A cup: empty, filled or lidded.</summary>
        public static bool IsCup(Item? item) => IsEmptyCup(item) || IsFill(item) || IsLidded(item);

        /// <summary>The fill item of a filled or lidded cup, or null.</summary>
        public static Item? FillNode(Item? item)
        {
            if (IsFill(item)) return item;
            if (IsLidded(item) && item!.Inputs.Count == 1 && IsFill(item.Inputs[0])) return item.Inputs[0];
            return null;
        }

        /// <summary>The drink's base id in a filled or lidded cup ("lemonade"), or null (empty cup, anything else).</summary>
        public static string? DrinkOf(Item? item)
        {
            Item? fill = FillNode(item);
            return fill != null && fill.Inputs.Count == 1 ? fill.Inputs[0].BaseId : null;
        }

        /// <summary>The fill bucket of a filled or lidded cup, or null.</summary>
        public static string? LevelOf(Item? item)
        {
            Item? fill = FillNode(item);
            return fill != null && fill.Buckets.TryGetValue(MlParam, out string? bucket) ? bucket : null;
        }

        /// <summary>The index of <paramref name="baseId"/> in <see cref="All"/> (the tap), or -1.</summary>
        public static int IndexOf(string? baseId)
        {
            for (int i = 0; i < All.Count; i++)
                if (string.Equals(All[i].Id, baseId, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>
        /// Spec 033: the drink line of a guest's order, drawn with the injected seeded RNG (one draw): half the guests order no
        /// drink (null), the rest lemonade, iced tea or soda alike.
        /// </summary>
        public static string? DrawOrderDrink(IRandom random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            int roll = random.NextInt(0, 2 * All.Count);
            return roll < All.Count ? null : All[roll - All.Count].Id;
        }

        private static BaseItemDef Drink(string id, string name, float sweet, float salty, float sour, float bitter, float umami)
            => new BaseItemDef(id, name, new ItemStats(sweet, salty, sour, bitter, umami, 0f, 0f, 1f, 0f, 1f), new DonenessWindow(0f, 1f),
                Array.AsReadOnly(new[] { DrinkTag }));

        private static bool HasTag(Item item, string tag)
        {
            foreach (string t in item.Tags) if (string.Equals(t, tag, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
