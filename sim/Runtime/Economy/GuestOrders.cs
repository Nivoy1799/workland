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

        /// <summary>The whole patience (s) of a guest for a dish of <paramref name="layers"/> layers.</summary>
        public static int PatienceSeconds(GuestOrder order, int layers) => GuestPatience.Seconds(order.PatienceBaseSeconds, layers);
    }
}
