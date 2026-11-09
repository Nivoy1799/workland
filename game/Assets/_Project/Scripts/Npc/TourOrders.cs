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
    /// garlic (cash, a $50 note: change), grilled veggies (card) and a seitan wrap without lettuce (cash, a $20 note; its ticket shows
    /// the struck line; its patience runs out during the tour, so the sheets show the ring turn Terracotta and the guest leave).
    /// Later guests draw as usual. Patience is scaled by <see cref="PatienceScale"/> (the tour is long), the seitan wrap guest's by
    /// <see cref="ShortPatienceScale"/>; the release build never compiles this.
    /// </summary>
    public sealed class TourOrders
    {
        public const string Arg = "-tour-orders";
        public const float PatienceScale = 8f;
        public const float ShortPatienceScale = 2.7f; // about 280 s: Sage at the first look, Terracotta at the second, gone before 14:00

        private readonly Dictionary<string, int> _next = new Dictionary<string, int>();

        private static readonly (string Market, byte Dish, string[] Mods, bool Card, int Note, float Patience)[] Script =
        {
            ("snack", Menu.Fries, new string[0], true, 10, PatienceScale),
            ("meal", Menu.FalafelWrap, new[] { "extra garlic" }, false, 50, PatienceScale),
            ("meal", Menu.GrilledVeggies, new string[0], true, 10, PatienceScale),
            ("meal", Menu.SeitanWrap, new[] { "no lettuce" }, false, 20, ShortPatienceScale),
        };

        public static bool Requested(string[] args) => args != null && Array.IndexOf(args, Arg) >= 0;

        /// <summary>The next scripted guest of <paramref name="market"/>, or false when its script is used up.</summary>
        public bool TryNext(string market, out MenuDish dish, out GuestOrder order, out float patienceScale)
        {
            dish = null;
            order = null;
            patienceScale = PatienceScale;
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
                return true;
            }
            return false;
        }
    }
}
#endif
