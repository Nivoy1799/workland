using System;
using System.Collections.Generic;
using Worktown.Game.Climb;
using Worktown.Game.Cooking;
using Worktown.Game.Npc;
using Worktown.Sim.Climb;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Office
{
    /// <summary>
    /// Spec 059, pure: the special of the day: none (<see cref="Menu.None"/>) or one dish on the menu (a game dish id). It changes only
    /// what guests who join after it order. Without a special every order is exactly as before (the same seeds, the same draws). With one,
    /// a guest whose drawn dish is not on the menu takes the special (instead of 057's <c>drawn dish id % count</c>), and every
    /// <see cref="EveryNth"/>th guest that joins the service (its join ordinal, no RNG) switches to it. The food corner (one dish) never
    /// changes. A special that is no longer on the menu does nothing until it is on again.
    /// </summary>
    public static class SpecialRules
    {
        /// <summary>Every 4th joining guest of a service takes the special.</summary>
        public const int EveryNth = 4;
        public const string Tag = "special";
        public const string NotOnMenuText = "Put it on the menu first";
        public const string NoSpecialText = "No special today";

        private static readonly string[] Markets = MarketsOf();

        private static string[] MarketsOf()
        {
            var list = new List<string>();
            foreach (MenuDish d in Menu.All) if (!list.Contains(d.Market)) list.Add(d.Market);
            return list.ToArray();
        }

        /// <summary>
        /// The game dishes on the menu at <paramref name="stage"/>: every dish in the open kitchen, the fries at the food corner, and at the
        /// restaurant the dishes of <paramref name="menu"/> (a 057 recipe mask) for every market.
        /// </summary>
        public static IReadOnlyList<byte> MenuDishes(byte stage, byte menu, ClimbCatalog climb)
        {
            var dishes = new List<byte>();
            if (stage == ShopStageRules.OpenKitchen)
            {
                foreach (MenuDish d in Menu.All) dishes.Add(d.Id);
                return dishes;
            }
            if (stage < ShopStageRules.FirstRestaurant)
            {
                dishes.Add(FoodCornerRules.GameDish);
                return dishes;
            }
            foreach (string market in Markets)
                foreach (byte d in RestaurantMenu.OnDishes(climb ?? FoodCornerRules.DefaultClimb, menu, market))
                    if (!dishes.Contains(d)) dishes.Add(d);
            dishes.Sort();
            return dishes;
        }

        /// <summary>Whether <paramref name="special"/> is set and on <paramref name="menuDishes"/>.</summary>
        public static bool IsOn(byte special, IReadOnlyList<byte> menuDishes) => special != Menu.None && Contains(menuDishes, special);

        /// <summary>
        /// Whether the guest with join ordinal <paramref name="joinOrdinal"/> (1 for the service's first) takes the special:
        /// never at the food corner or without a special on the menu; always when its drawn dish was not on the menu; else every
        /// <see cref="EveryNth"/>th guest.
        /// </summary>
        public static bool TakesSpecial(byte stage, byte special, bool specialOnMenu, bool drawnOnMenu, int joinOrdinal)
        {
            if (stage == ShopStageRules.FoodCorner) return false;
            if (special == Menu.None || !specialOnMenu) return false;
            if (!drawnOnMenu) return true;
            return joinOrdinal > 0 && joinOrdinal % EveryNth == 0;
        }

        /// <summary>
        /// A joining guest's order after the special: <paramref name="dish"/> (already mapped to the menu by 057) becomes the special when
        /// <see cref="TakesSpecial"/> says so (its modifiers cleared, as 057's mapping clears them); else the order is returned unchanged.
        /// <paramref name="drawnDish"/> is the dish the guest drew, before 057's mapping.
        /// </summary>
        public static GuestOrder Apply(byte stage, byte special, IReadOnlyList<byte> menuDishes, byte drawnDish, int joinOrdinal, GuestOrder order,
            ref MenuDish dish)
        {
            if (order == null || dish == null) return order;
            if (!TakesSpecial(stage, special, IsOn(special, menuDishes), Contains(menuDishes, drawnDish), joinOrdinal)) return order;
            if (dish.Id == special) return order;
            MenuDish chosen = Menu.ById(special);
            if (chosen == null) return order;
            dish = chosen;
            return order with { Modifiers = Array.Empty<OrderModifier>() };
        }

        /// <summary>
        /// A press of the special button on dish <paramref name="dish"/> (<paramref name="dishOnMenu"/>: it is on the menu now or from the
        /// next service): the special again clears it; another dish on the menu becomes it; a dish off the menu is refused (NotOnMenu); the food
        /// corner refuses (one dish); a drink or no dish is no option.
        /// </summary>
        public static CookOutcome Decide(byte stage, byte current, byte dish, bool dishOnMenu, out byte next)
        {
            next = current;
            if (stage == ShopStageRules.FoodCorner) return CookOutcome.NotOpenYet;
            if (dish == Menu.None || Menu.ById(dish) == null) return CookOutcome.InvalidOption;
            if (current == dish)
            {
                next = Menu.None;
                return CookOutcome.Ok;
            }
            if (!dishOnMenu) return CookOutcome.NotOnMenu;
            next = dish;
            return CookOutcome.Ok;
        }

        /// <summary>A saved special: its MenuPrices line id to a dish id (<see cref="Menu.None"/> for none, a drink or an unknown id).</summary>
        public static byte FromLineId(string lineId)
        {
            int line = MenuPrices.LineOfId(lineId);
            if (line < 0 || MenuPrices.Lines[line].Drink != null) return Menu.None;
            return MenuPrices.Lines[line].Dish;
        }

        /// <summary>The save's line id of a special (empty for none).</summary>
        public static string ToLineId(byte special)
        {
            int line = MenuPrices.LineOfDish(special);
            return line >= 0 ? MenuPrices.Lines[line].Id : string.Empty;
        }

        private static bool Contains(IReadOnlyList<byte> list, byte value)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return true;
            return false;
        }
    }
}
