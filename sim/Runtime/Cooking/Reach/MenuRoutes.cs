using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 056a: the routes of the shipped menu: one per dish of the game's <c>Menu.All</c> (13 dishes, the same names, in menu
    /// order) and one per drink (lemonade, iced tea, soda). Targets follow the menu's <c>DishNeed</c>s: the pan and the fryer at
    /// the kitchen's one heat ("hot"), the time bucket the dish asks for ("medium": done), an unshaken, drained basket, a griddle
    /// item done on both sides, the right squeeze of sauce, a cup filled to the line. "Any other ingredient" needs list a few
    /// plausible bases (other combinations keep their built-in name). The game's drift test (spec 056c) checks every target
    /// against the menu's order rules.
    /// </summary>
    public static class MenuRoutes
    {
        public const string Fries = "fries";
        public const string PotatoSoup = "potato_soup";
        public const string OnionSoup = "onion_soup";
        public const string TomatoSalad = "tomato_salad";
        public const string FriedApple = "fried_apple";
        public const string VeggieMix = "veggie_mix";
        public const string Falafel = "falafel";
        public const string GrilledVeggies = "grilled_veggies";
        public const string ToastedFlatbread = "toasted_flatbread";
        public const string SeitanWrap = "seitan_wrap";
        public const string FalafelWrap = "falafel_wrap";
        public const string SeitanFlatbread = "seitan_flatbread";
        public const string VeggieBox = "veggie_box";
        public const string Lemonade = "lemonade";
        public const string IcedTea = "iced_tea";
        public const string Soda = "soda";

        private static readonly IReadOnlyDictionary<string, string> NoTarget = Target();

        private static IReadOnlyDictionary<string, string> Target(params string[] pairs)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < pairs.Length; i += 2) map[pairs[i]] = pairs[i + 1];
            return map;
        }

        private static RouteStep CutTo(string shape) => new RouteStep("cut", Target("shape", shape));
        private static RouteStep PanFry() => new RouteStep("fry", Target("temp", "hot", "time", "medium"));
        private static RouteStep Saute() => new RouteStep("saute", Target("temp", "hot", "time", "medium"));
        private static RouteStep Boil() => new RouteStep("boil", Target("time", "medium"));
        private static RouteStep DeepFried() => new RouteStep(DeepFry.OperationId, Target("temp", "hot", "time", "medium",
            DeepFry.ShakeParam, DeepFry.NotShaken, DeepFry.DrainParam, DeepFry.Drained));
        private static RouteStep Grilled() => new RouteStep(Griddle.OperationId, Target("time", "medium", Griddle.LongSideParam, "medium"));
        private static RouteStep Toasted() => new RouteStep(Griddle.ToastOperationId, Target("time", "medium", Griddle.LongSideParam, "medium"));
        private static RouteStep Shaved() => new RouteStep(Spit.OperationId, Target("time", "medium"));
        private static RouteStep Squeezed() => new RouteStep(Assembly.SqueezeOperationId, Target(Assembly.AmountParam, Assembly.Right));
        private static RouteStep Filled() => new RouteStep(Drinks.FillOperationId, Target(Drinks.MlParam, Drinks.Right));
        private static RouteStep Lidded() => new RouteStep(Drinks.LidOperationId, NoTarget);

        private static RouteChain Chain(string[] bases, params RouteStep[] steps) =>
            new RouteChain(Array.AsReadOnly(bases), Array.AsReadOnly(steps));

        private static RouteChain Chain(string baseId, params RouteStep[] steps) => Chain(new[] { baseId }, steps);

        private static RouteDef Route(string id, string name, RouteKind kind, string? template, params RouteChain[] chains) =>
            new RouteDef(id, name, kind, Array.AsReadOnly(chains), template);

        private static readonly string[] SaladBases = { "cucumber", "tomato", "red_cabbage", "onion", "lettuce" };
        private static readonly string[] Sauces = { "garlic_herb_sauce", "chili_sauce" };
        private static readonly string[] GrillVegetables = { "paprika", "eggplant", "mushroom" };

        /// <summary>The default routes, menu order then the drinks in tap order.</summary>
        public static readonly IReadOnlyList<RouteDef> Default = Array.AsReadOnly(new[]
        {
            Route(Fries, "Fries", RouteKind.Single, null,
                Chain("potato", CutTo("sticks"), DeepFried()),
                Chain("potato", CutTo("sticks"), PanFry())),
            Route(PotatoSoup, "Potato soup", RouteKind.Single, null,
                Chain("potato", CutTo("dice"), Boil())),
            Route(OnionSoup, "Onion soup", RouteKind.Mix, null,
                Chain("onion", Boil()),
                Chain(new[] { "potato", "tomato" }, Boil())),
            Route(TomatoSalad, "Tomato salad", RouteKind.Mix, null,
                Chain("tomato", CutTo("slices")),
                Chain("onion", CutTo("slices"))),
            Route(FriedApple, "Fried apple", RouteKind.Single, null,
                Chain("apple", CutTo("slices"), Saute()),
                Chain("apple", CutTo("halves"), Saute())),
            Route(VeggieMix, "Veggie mix", RouteKind.Mix, null,
                Chain("tomato", CutTo("dice")),
                Chain("onion", CutTo("dice")),
                Chain(new[] { "cucumber", "paprika", "red_cabbage" }, CutTo("dice"))),
            Route(Falafel, "Falafel (4)", RouteKind.Single, null,
                Chain("falafel_mix", DeepFried())),
            Route(GrilledVeggies, "Grilled veggies", RouteKind.Mix, null,
                Chain("paprika", Grilled()),
                Chain(new[] { "eggplant", "mushroom" }, Grilled())),
            Route(ToastedFlatbread, "Toasted flatbread", RouteKind.Single, null,
                Chain("flatbread", Toasted())),
            Route(SeitanWrap, "Seitan wrap", RouteKind.Build, RecipeTemplates.SeitanWrapId,
                Chain("wrap"),
                Chain("seitan", CutTo("sticks"), Grilled()),
                Chain("seitan", Shaved()),
                Chain(SaladBases, CutTo("slices")),
                Chain("chili_sauce", Squeezed())),
            Route(FalafelWrap, "Falafel wrap", RouteKind.Build, RecipeTemplates.FalafelWrapId,
                Chain("wrap"),
                Chain("falafel_mix", DeepFried()),
                Chain(SaladBases, CutTo("slices")),
                Chain("garlic_herb_sauce", Squeezed())),
            Route(SeitanFlatbread, "Seitan flatbread", RouteKind.Build, RecipeTemplates.SeitanFlatbreadId,
                Chain("flatbread"),
                Chain("seitan", CutTo("sticks"), Grilled()),
                Chain("seitan", Shaved()),
                Chain(SaladBases, CutTo("slices")),
                Chain(Sauces, Squeezed())),
            Route(VeggieBox, "Veggie box", RouteKind.Build, RecipeTemplates.VeggieBoxId,
                Chain("potato", CutTo("sticks"), DeepFried()),
                Chain("potato", CutTo("sticks"), PanFry()),
                Chain(GrillVegetables, Grilled()),
                Chain(Sauces, Squeezed())),
            Route(Lemonade, "Lemonade", RouteKind.Drink, null, Chain("lemonade", Filled(), Lidded())),
            Route(IcedTea, "Iced tea", RouteKind.Drink, null, Chain("iced_tea", Filled(), Lidded())),
            Route(Soda, "Soda", RouteKind.Drink, null, Chain("soda", Filled(), Lidded())),
        });
    }
}
