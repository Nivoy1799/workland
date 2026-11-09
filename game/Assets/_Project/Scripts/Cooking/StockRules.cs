using UnityEngine;
using Worktown.Sim.Economy;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 028b, pure server rules for taking one ingredient from stock: a slot of the fridge or dry shelf, or a crate
    /// (spec 007, one implicit slot: option 0 at the station's centre). Both go through the same checks, in this order,
    /// before anything changes: the slot exists on that station (the option), the player reaches the slot's anchor (not the
    /// station's centre), the hands are free, the ingredient is known; then KitchenService creates the sim item and the
    /// grocer is paid (<see cref="PaidOutcome"/>: no money, no potato). The request is RequestStation(station, option):
    /// two ints, no new RPC.
    /// </summary>
    public static class StockRules
    {
        /// <summary>
        /// What option <paramref name="option"/> of <paramref name="station"/> takes: the ingredient and where reach is
        /// measured to. False for a station that holds no stock, a crate's option other than 0, or a slot number the
        /// station does not have.
        /// </summary>
        public static bool TryResolve(Station station, int option, out int ingredientIndex, out Vector3 anchor)
        {
            ingredientIndex = -1;
            anchor = default;
            if (station == null) return false;
            if (station.Kind == StationKind.Crate)
            {
                if (option != 0) return false;
                ingredientIndex = station.IngredientIndex;
                anchor = station.transform.position;
                return true;
            }
            if (!StockLayout.IsStock(station.Kind) || !station.TryGetSlot(option, out StockSlot slot)) return false;
            ingredientIndex = slot.IngredientIndex;
            anchor = slot.Anchor;
            return true;
        }

        /// <summary>
        /// The checks before anything changes: <see cref="CookOutcome.InvalidOption"/> for no such slot,
        /// <see cref="CookOutcome.TooFar"/> beyond reach of the anchor, <see cref="CookOutcome.HandsFull"/>,
        /// <see cref="CookOutcome.UnknownStation"/> for an ingredient outside <see cref="DemoIngredients.All"/>, else Ok.
        /// </summary>
        public static CookOutcome CheckTake(bool slotExists, int ingredientIndex, Vector3 player, Vector3 anchor, bool handsFull)
        {
            if (!slotExists) return CookOutcome.InvalidOption;
            if (!StationRules.IsInReach(player, anchor)) return CookOutcome.TooFar;
            if (handsFull) return CookOutcome.HandsFull;
            if (ingredientIndex < 0 || ingredientIndex >= DemoIngredients.All.Count) return CookOutcome.UnknownStation;
            return CookOutcome.Ok;
        }

        /// <summary>
        /// <see cref="TryResolve"/> then <see cref="CheckTake"/>: the whole check of one take request; the ingredient's id
        /// when it is Ok, else null.
        /// </summary>
        public static CookOutcome CheckTake(Station station, int option, Vector3 player, bool handsFull, out string baseId)
        {
            baseId = null;
            bool exists = TryResolve(station, option, out int ingredient, out Vector3 anchor);
            CookOutcome outcome = CheckTake(exists, ingredient, player, anchor, handsFull);
            if (outcome == CookOutcome.Ok) baseId = DemoIngredients.All[ingredient].Id;
            return outcome;
        }

        /// <summary>Spec 009: the grocer's answer as the kitchen's: no money is NotEnoughMoney, any other error SimRefused.</summary>
        public static CookOutcome PaidOutcome(EconomyError paid)
        {
            if (paid == EconomyError.None) return CookOutcome.Ok;
            return paid == EconomyError.InsufficientFunds ? CookOutcome.NotEnoughMoney : CookOutcome.SimRefused;
        }
    }
}
