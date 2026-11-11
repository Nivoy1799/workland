#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 032, development builds only: the cook tour's host (<c>-tour-orders</c>) scripts its first guests per market, so the
    /// visual check always takes the same orders: a snack guest wants fries (card); the meal guests want a falafel wrap with extra
    /// garlic and (spec 033) a lemonade (cash, a $20 note: change), grilled veggies (spec 037 AC3: cash with a $10 note, and it does not
    /// tip, so the $9.50 the tour sets on the menu board is what it owes: 50 cents of change in one coin) and a seitan wrap without
    /// lettuce (cash, a $20 note; its ticket shows the struck line; its patience runs out during the tour, so the sheets show the ring
    /// turn Terracotta and the guest leave). Spec 037: notes in cents ($5, $10, $20; there is no $50 note any more).
    /// Later guests draw as usual. Patience is scaled by <see cref="PatienceScale"/> (the tour is long), the seitan wrap guest's by
    /// <see cref="ShortPatienceScale"/>; the release build never compiles this.
    /// </summary>
    public sealed class TourOrders
    {
        public const string Arg = "-tour-orders";
        public const float PatienceScale = 8f;
        public const float ShortPatienceScale = 2.7f; // about 280 s: Sage at the first look, Terracotta at the second, gone before 14:00

        private readonly Dictionary<string, int> _next = new Dictionary<string, int>();

        private static readonly (string Market, byte Dish, string[] Mods, bool Card, int Note, float Patience, string Drink, bool EatIn, bool Tips)[] Script =
        {
            ("snack", Menu.Fries, new string[0], true, 1000, PatienceScale, null, true, true), // spec 035: eats in (its plate comes back dirty)
            ("meal", Menu.FalafelWrap, new[] { "extra garlic" }, false, 2000, PatienceScale, "lemonade", false, true), // spec 033: the tour's drink
            ("meal", Menu.GrilledVeggies, new string[0], false, 1000, PatienceScale, null, true, false), // spec 037 AC3: $9.50 with a $10 note
            ("meal", Menu.SeitanWrap, new[] { "no lettuce" }, false, 2000, ShortPatienceScale, null, false, true),
        };

        /// <summary>Spec 035: the last <see cref="TryNext(string, out MenuDish, out GuestOrder, out float, out string)"/> gave a scripted guest, and whether it eats in.</summary>
        public bool LastScripted { get; private set; }
        public bool LastEatIn { get; private set; }
        /// <summary>Spec 037: whether the last scripted guest tips (the AC3 guest does not, so it owes exactly the board's price).</summary>
        public bool LastTips { get; private set; } = true;

        public static bool Requested(string[] args) => args != null && Array.IndexOf(args, Arg) >= 0;

        /// <summary>The next scripted guest of <paramref name="market"/>, or false when its script is used up.</summary>
        public bool TryNext(string market, out MenuDish dish, out GuestOrder order, out float patienceScale) =>
            TryNext(market, out dish, out order, out patienceScale, out _);

        /// <summary>Spec 033: with the scripted guest's drink line (null: none).</summary>
        public bool TryNext(string market, out MenuDish dish, out GuestOrder order, out float patienceScale, out string drink)
        {
            dish = null;
            order = null;
            drink = null;
            patienceScale = PatienceScale;
            LastScripted = false;
            LastEatIn = false;
            LastTips = true;
            _next.TryGetValue(market, out int done);
            int seen = 0;
            foreach (var entry in Script)
            {
                if (entry.Market != market) continue;
                if (seen++ < done) continue;
                _next[market] = done + 1;
                dish = Menu.ById(entry.Dish);
                RecipeTemplate template = dish.Template != null ? RecipeTemplates.ById(dish.Template) : null;
                var mods = new List<OrderModifier>();
                foreach (OrderModifier m in OrderModifiers.Allowed(template))
                    if (Array.IndexOf(entry.Mods, m.Text) >= 0) mods.Add(m);
                order = new GuestOrder(mods, GuestPatience.BaseSeconds, entry.Card, entry.Note);
                patienceScale = entry.Patience;
                drink = entry.Drink;
                LastScripted = true;
                LastEatIn = entry.EatIn;
                LastTips = entry.Tips;
                return true;
            }
            return false;
        }
    }
}
#endif
