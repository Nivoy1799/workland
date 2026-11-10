using System;
using System.Collections.Generic;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;
using Worktown.Sim.Core;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// The spec 022 menu: six dishes the spec 021 kitchen can cook from the crate ingredients (spec 029: fries from the fryer, and
    /// "Falafel (4)"), and the seeded draw of a
    /// customer's order. Pure and deterministic (no RNG of its own, no mutable state).
    /// </summary>
    public static class Menu
    {
        /// <summary>Dish id of "no order".</summary>
        public const byte None = 0;

        public const byte Fries = 1;
        public const byte PotatoSoup = 2;
        public const byte OnionSoup = 3;
        public const byte TomatoSalad = 4;
        public const byte FriedApple = 5;
        public const byte VeggieMix = 6;
        /// <summary>Spec 029: four falafel balls from the fryer.</summary>
        public const byte Falafel = 7;
        /// <summary>Spec 030: grilled paprika and one more grilled vegetable on a plate (the griddle, both sides done; whole or cut).</summary>
        public const byte GrilledVeggies = 8;
        /// <summary>Spec 030: a flatbread toasted on both sides on the griddle.</summary>
        public const byte ToastedFlatbread = 9;
        /// <summary>Spec 031: built on the assembly board (recipe templates; order matching compares the layers).</summary>
        public const byte SeitanWrap = 10;
        public const byte FalafelWrap = 11;
        public const byte SeitanFlatbread = 12;
        public const byte VeggieBox = 13;

        private static readonly IReadOnlyList<string> Sticks = new[] { "sticks" };
        private static readonly IReadOnlyList<string> Slices = new[] { "slices" };
        private static readonly IReadOnlyList<string> SlicesOrHalves = new[] { "halves", "slices" };
        /// <summary>Spec 029: fries come from the fryer now (the pan still makes them, as before).</summary>
        private static readonly IReadOnlyList<string> FriesOps = new[] { Worktown.Sim.Cooking.DeepFry.OperationId, "fry" };
        private static readonly IReadOnlyList<string> DeepFryOp = new[] { Worktown.Sim.Cooking.DeepFry.OperationId };
        private static readonly IReadOnlyList<string> SauteOp = new[] { "saute" };
        private static readonly IReadOnlyList<string> BoilOp = new[] { "boil" };
        private static readonly IReadOnlyList<string> GriddleOp = new[] { Worktown.Sim.Cooking.Griddle.OperationId }; // spec 030
        private static readonly IReadOnlyList<string> ToastOp = new[] { Worktown.Sim.Cooking.Griddle.ToastOperationId };
        /// <summary>030 review: "Grilled veggies" takes only the grill vegetables (seitan is no vegetable).</summary>
        private static readonly IReadOnlyList<string> GrillVegetables = new[] { "paprika", "eggplant", "mushroom" };

        /// <summary>
        /// The menu, in id order. Fries, potato soup and fried apple are one ingredient (sim market snack); onion soup,
        /// tomato salad and veggie mix are two or more (meal). "Any cut" means cut, in any shape.
        /// </summary>
        public static readonly IReadOnlyList<MenuDish> All = new[]
        {
            new MenuDish(Fries, "Fries", "snack", "potato",
                new[] { new DishNeed("potato", Sticks, FriesOps, "medium") }, false, false, 1),
            new MenuDish(PotatoSoup, "Potato soup", "snack", "potato",
                new[] { new DishNeed("potato", DishNeed.AnyCut, BoilOp, "medium") }, false, false, 1),
            new MenuDish(OnionSoup, "Onion soup", "meal", "onion",
                new[] { new DishNeed("onion", null, BoilOp, "medium"), new DishNeed(null, null, BoilOp, "medium") }, true, false, 2),
            new MenuDish(TomatoSalad, "Tomato salad", "meal", "tomato",
                new[] { new DishNeed("tomato", Slices, null, null), new DishNeed("onion", DishNeed.AnyCut, null, null) }, true, true, 2),
            new MenuDish(FriedApple, "Fried apple", "snack", "apple",
                new[] { new DishNeed("apple", SlicesOrHalves, SauteOp, "short") }, false, false, 1),
            new MenuDish(VeggieMix, "Veggie mix", "meal", null,
                new[]
                {
                    new DishNeed(null, DishNeed.AnyCut, null, null), new DishNeed(null, DishNeed.AnyCut, null, null),
                    new DishNeed(null, DishNeed.AnyCut, null, null),
                }, false, false, 4),
            new MenuDish(Falafel, "Falafel (4)", "snack", "falafel_mix",
                new[] { new DishNeed("falafel_mix", null, DeepFryOp, "medium") }, false, false, 1),
            // Spec 030: the griddle's "time" bucket is its shorter side, so "medium" means both sides grilled. Spec 043: the buckets
            // follow the zones, so "medium" is both sides at least Good (12 s seitan, 10 s vegetables); undercooked is Close.
            new MenuDish(GrilledVeggies, "Grilled veggies", "meal", "paprika",
                new[] { new DishNeed("paprika", null, GriddleOp, "medium"), new DishNeed(null, null, GriddleOp, "medium", GrillVegetables) },
                false, false, 3),
            new MenuDish(ToastedFlatbread, "Toasted flatbread", "snack", "flatbread",
                new[] { new DishNeed("flatbread", null, ToastOp, "medium") }, false, false, 1),
            // Spec 031: the assembly board. Needs list the required layers (for the stock check); the match is by the template.
            new MenuDish(SeitanWrap, "Seitan wrap", "meal", "seitan", BuildNeeds("wrap", "seitan", "lettuce", "chili_sauce"), false, false,
                CookingLimits.MaxBuildLayers, RecipeTemplates.SeitanWrapId),
            new MenuDish(FalafelWrap, "Falafel wrap", "meal", "falafel_mix", BuildNeeds("wrap", "falafel_mix", "lettuce", "garlic_herb_sauce"),
                false, false, CookingLimits.MaxBuildLayers, RecipeTemplates.FalafelWrapId),
            new MenuDish(SeitanFlatbread, "Seitan flatbread", "meal", "seitan", BuildNeeds("flatbread", "seitan", "red_cabbage", "garlic_herb_sauce"),
                false, false, CookingLimits.MaxBuildLayers, RecipeTemplates.SeitanFlatbreadId),
            new MenuDish(VeggieBox, "Veggie box", "meal", "potato", BuildNeeds("potato", "paprika", "garlic_herb_sauce"), false, false,
                CookingLimits.MaxBuildLayers, RecipeTemplates.VeggieBoxId),
        };

        private static IReadOnlyList<DishNeed> BuildNeeds(params string[] bases)
        {
            var needs = new DishNeed[bases.Length];
            for (int i = 0; i < bases.Length; i++) needs[i] = new DishNeed(bases[i], null, null, null);
            return needs;
        }

        /// <summary>What the crates sell (spec 007 demo ingredients): potato, apple, tomato, onion.</summary>
        public static readonly IReadOnlyList<string> CrateIngredients = CrateIds();

        /// <summary>The dish with this id, or null.</summary>
        public static MenuDish ById(byte id)
        {
            foreach (MenuDish dish in All)
                if (dish.Id == id) return dish;
            return null;
        }

        /// <summary>Display name of a dish id; "?" when unknown.</summary>
        public static string NameOf(byte id)
        {
            MenuDish dish = ById(id);
            return dish != null ? dish.Name : "?";
        }

        /// <summary>
        /// Whether the dish can be made from <paramref name="forSale"/>: every named ingredient is for sale and enough
        /// other ingredients are for sale for its "any other" needs.
        /// </summary>
        public static bool CanMake(MenuDish dish, IReadOnlyCollection<string> forSale)
        {
            if (dish == null || forSale == null) return false;
            var named = new List<string>();
            int any = 0;
            foreach (DishNeed need in dish.Needs)
            {
                if (need.BaseId == null) any++;
                else if (!named.Contains(need.BaseId)) named.Add(need.BaseId);
            }
            foreach (string baseId in named)
                if (!Contains(forSale, baseId)) return false;
            if (dish.Main != null && !Contains(forSale, dish.Main)) return false;
            int others = 0;
            var seen = new List<string>();
            foreach (string baseId in forSale)
            {
                if (baseId == null || named.Contains(baseId) || seen.Contains(baseId)) continue;
                seen.Add(baseId);
                others++;
            }
            return others >= any;
        }

        /// <summary>The dishes of <paramref name="market"/> that can be made from <paramref name="forSale"/>, in id order.</summary>
        public static IReadOnlyList<MenuDish> Available(string market, IReadOnlyCollection<string> forSale)
        {
            var dishes = new List<MenuDish>();
            foreach (MenuDish dish in All)
                if (dish.Market == market && CanMake(dish, forSale)) dishes.Add(dish);
            return dishes;
        }

        public static bool HasDishes(string market, IReadOnlyCollection<string> forSale)
        {
            return Available(market, forSale).Count > 0;
        }

        /// <summary>
        /// A customer's order: one of the available dishes of the market, drawn with the injected seeded RNG (one draw).
        /// Null (and no draw) when the market has no dish, e.g. drink.
        /// </summary>
        public static MenuDish Draw(IRandom random, string market, IReadOnlyCollection<string> forSale)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            IReadOnlyList<MenuDish> dishes = Available(market, forSale);
            if (dishes.Count == 0) return null;
            return dishes[random.NextInt(0, dishes.Count)];
        }

        private static bool Contains(IReadOnlyCollection<string> list, string value)
        {
            foreach (string item in list)
                if (item == value) return true;
            return false;
        }

        private static IReadOnlyList<string> CrateIds()
        {
            var ids = new string[DemoIngredients.All.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = DemoIngredients.All[i].Id;
            return Array.AsReadOnly(ids);
        }
    }
}
