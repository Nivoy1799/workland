using System;
using System.Collections.Generic;
using Worktown.Sim.Climb;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 037, pure: the menu board's lines (every dish of the <see cref="Menu"/>, then every drink) and their reference prices in
    /// cents: the market reference guests compare with, which is also the board's start price. A dish that spec 052's climb knows takes
    /// the climb's reference price (fries $4.00, the falafel wrap $9.00, the seitan wrap $10.00, grilled veggies $8.00, lemonade $3.00),
    /// so the board and the climb never drift; the others are street-food defaults to review. Immutable.
    /// </summary>
    public static class MenuPrices
    {
        /// <summary>One line of the board: a dish (its menu id) or a drink (its id), its name, market and reference price.</summary>
        public sealed class Line
        {
            public Line(string id, string name, string market, byte dish, string drink, long reference)
            {
                Id = id;
                Name = name;
                Market = market;
                Dish = dish;
                Drink = drink;
                Reference = reference;
            }

            /// <summary>A stable id for the save ("fries", "falafel_wrap", "lemonade").</summary>
            public string Id { get; }
            public string Name { get; }
            /// <summary>The sim market the line's guests come from (meal, snack, drink).</summary>
            public string Market { get; }
            /// <summary>The menu dish (Menu.None for a drink).</summary>
            public byte Dish { get; }
            /// <summary>The drink id (null for a dish).</summary>
            public string Drink { get; }
            /// <summary>The reference and start price (cents).</summary>
            public long Reference { get; }
        }

        private static readonly ClimbCatalog Climb = ClimbCatalog.CreateDefault();

        /// <summary>The board's lines in a fixed order: the menu's dishes in id order, then the drinks.</summary>
        public static readonly IReadOnlyList<Line> Lines = Build();

        /// <summary>The line of menu dish <paramref name="dish"/>, or -1.</summary>
        public static int LineOfDish(byte dish)
        {
            if (dish == Menu.None) return -1;
            for (int i = 0; i < Lines.Count; i++)
                if (Lines[i].Drink == null && Lines[i].Dish == dish) return i;
            return -1;
        }

        /// <summary>The line of drink <paramref name="drinkId"/>, or -1.</summary>
        public static int LineOfDrink(string drinkId)
        {
            if (drinkId == null) return -1;
            for (int i = 0; i < Lines.Count; i++)
                if (Lines[i].Drink == drinkId) return i;
            return -1;
        }

        /// <summary>The line with save id <paramref name="id"/>, or -1.</summary>
        public static int LineOfId(string id)
        {
            if (id == null) return -1;
            for (int i = 0; i < Lines.Count; i++)
                if (Lines[i].Id == id) return i;
            return -1;
        }

        /// <summary>The reference price (cents) of line <paramref name="line"/>, or 0 for no line.</summary>
        public static long Reference(int line) => line >= 0 && line < Lines.Count ? Lines[line].Reference : 0;

        /// <summary>The climb's reference price of a spec 052 dish (cents), or <paramref name="fallback"/> when the climb has no such dish.</summary>
        private static long ClimbReference(string climbDish, long fallback)
        {
            foreach (DishDef d in Climb.Dishes)
                if (d.Id == climbDish) return d.ReferencePrice;
            return fallback;
        }

        private static IReadOnlyList<Line> Build()
        {
            var lines = new List<Line>();
            foreach (MenuDish dish in Menu.All)
            {
                string id = IdOf(dish.Name);
                long reference = ClimbReference(id, DefaultCents(dish.Id));
                lines.Add(new Line(id, dish.Name, dish.Market, dish.Id, null, reference));
            }
            foreach (BaseItemDef drink in Drinks.All)
                lines.Add(new Line(drink.Id, drink.DisplayName, "drink", Menu.None, drink.Id, ClimbReference(drink.Id, DefaultDrinkCents(drink.Id))));
            return Array.AsReadOnly(lines.ToArray());
        }

        /// <summary>"Falafel (4)" to "falafel_4", "Seitan wrap" to "seitan_wrap".</summary>
        public static string IdOf(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in name.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            return sb.ToString().TrimEnd('_');
        }

        /// <summary>Street-food defaults to review (cents) for the dishes spec 052's climb does not price.</summary>
        private static long DefaultCents(byte dish)
        {
            switch (dish)
            {
                case Menu.PotatoSoup: return 450;
                case Menu.OnionSoup: return 550;
                case Menu.TomatoSalad: return 600;
                case Menu.FriedApple: return 400;
                case Menu.VeggieMix: return 650;
                case Menu.Falafel: return 450;
                case Menu.ToastedFlatbread: return 350;
                case Menu.SeitanFlatbread: return 950;
                case Menu.VeggieBox: return 850;
                default: return 500;
            }
        }

        private static long DefaultDrinkCents(string drink) => drink == "soda" ? 250 : 300;
    }
}
