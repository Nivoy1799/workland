using System;
using Worktown.Sim.Cooking;
using Worktown.Sim.Core;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 032: a guest's order details from the injected seeded RNG, always in the same order of draws: the modifiers
    /// (<see cref="OrderModifiers.Draw"/>; none and no draw for a dish without a recipe template), the patience base, card or cash,
    /// the note size. Same seed and same dishes, same guests.
    /// </summary>
    public static class GuestOrders
    {
        public static GuestOrder Draw(IRandom random, RecipeTemplate? template)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var modifiers = OrderModifiers.Draw(random, template);
            int patience = GuestPatience.DrawBase(random);
            bool card = CashRules.PaysByCard(random);
            int note = CashRules.DrawNote(random);
            return new GuestOrder(modifiers, patience, card, note);
        }

        /// <summary>Spec 035 default to review: the share of guests (percent) that eat in; the rest take their order away.</summary>
        public const int EatInPercent = 50;

        /// <summary>
        /// Spec 035: one draw from the injected seeded RNG: does the guest eat in (its plate comes back dirty) or take it away? The game
        /// draws it from an RNG of its own, so the 022/032/033 draws keep their sequence.
        /// </summary>
        public static bool DrawEatIn(IRandom random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            return random.NextInt(0, 100) < EatInPercent;
        }

        /// <summary>The whole patience (s) of a guest for a dish of <paramref name="layers"/> layers.</summary>
        public static int PatienceSeconds(GuestOrder order, int layers) => GuestPatience.Seconds(order.PatienceBaseSeconds, layers);
    }
}
