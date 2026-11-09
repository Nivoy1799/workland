using System.Collections.Generic;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 038, every build (not only development builds): the first guests a host spawns while its own tutorial runs order what the
    /// tutorial teaches, so its steps match: a snack guest wants fries and eats in (its plate comes back dirty for the wash step), a meal
    /// guest wants a falafel wrap with a lemonade and takes it to go; both pay cash with a note bigger than the bill (the change step).
    /// Later guests draw as usual. Only <see cref="CustomerService"/> on a host uses it (Tutorial.TutorialRules.HostCalms); a dedicated
    /// server never does. Mirrors the dev-only TourOrders (spec 032), which the cook tour keeps using.
    /// </summary>
    public sealed class TutorialOrders
    {
        private readonly Dictionary<string, int> _next = new Dictionary<string, int>();

        private static readonly (string Market, byte Dish, int Note, string Drink, bool EatIn)[] Script =
        {
            ("snack", Menu.Fries, 20, null, true),
            ("meal", Menu.FalafelWrap, 50, "lemonade", false),
        };

        /// <summary>The last <see cref="TryNext"/> gave a scripted guest, and whether it eats in.</summary>
        public bool LastScripted { get; private set; }
        public bool LastEatIn { get; private set; }

        /// <summary>The script's guests (tests).</summary>
        public static int Count => Script.Length;

        /// <summary>The next scripted guest of <paramref name="market"/> (no modifiers, cash), or false when its script is used up.</summary>
        public bool TryNext(string market, out MenuDish dish, out GuestOrder order, out string drink)
        {
            dish = null;
            order = null;
            drink = null;
            LastScripted = false;
            LastEatIn = false;
            _next.TryGetValue(market ?? string.Empty, out int done);
            int seen = 0;
            foreach (var entry in Script)
            {
                if (entry.Market != market) continue;
                if (seen++ < done) continue;
                _next[market] = done + 1;
                dish = Menu.ById(entry.Dish);
                order = new GuestOrder(new List<OrderModifier>(), GuestPatience.BaseSeconds, false, entry.Note);
                drink = entry.Drink;
                LastScripted = true;
                LastEatIn = entry.EatIn;
                return true;
            }
            return false;
        }
    }
}
