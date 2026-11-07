using Worktown.Game.Cooking;
using Worktown.Game.Economy;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 020, pure: the one rule for first person. From what you hold and what the dot points at it gives the
    /// click's action, the prompt under the dot and (through the action) the highlight colour, so they can never
    /// disagree. Client side only: "fits" uses what clients can see (the station's synced contents and the food's
    /// coarse look) with the server's own limits (AssemblyRules.MaxItems, one food per pan, pot or board); the
    /// server checks everything again and decides.
    /// </summary>
    public static class InteractionRules
    {
        public const string ClickKey = "Click";
        public const string TooFarText = "Too far";

        /// <summary>
        /// The action of a click. Rows (spec 020 table, first match wins): button = Press; knife + board with
        /// food = Cut; food + empty board = PutOnBoard; fitting food + pan, pot, plating table or bowl = PutInto;
        /// food + counter = Sell; holding + free spot = Place; empty hands + free item = PickUp; empty hands +
        /// crate = Buy; empty hands + pan, pot, board, plate or bowl with food = Take; anything else None.
        /// </summary>
        public static InteractionAction ActionFor(Held held, AimTarget target)
        {
            if (target.IsTooFar) return InteractionAction.None; // spec 020: the server would refuse it (TooFar)
            switch (target.Kind)
            {
                case AimKind.Button: return InteractionAction.Press;
                case AimKind.Spot:
                    return held.IsHolding && target.SpotIndex >= 0 ? InteractionAction.Place : InteractionAction.None;
                case AimKind.Pickup: return held.IsHolding ? InteractionAction.None : InteractionAction.PickUp;
                case AimKind.Station: return StationAction(held, target);
                default: return InteractionAction.None;
            }
        }

        private static InteractionAction StationAction(Held held, AimTarget target)
        {
            bool hasFood = target.ContentCount > 0;
            if (held.IsKnife)
                return target.Station == StationKind.Board && hasFood ? InteractionAction.Cut : InteractionAction.None;
            if (held.IsFood)
            {
                if (!Fits(held, target)) return InteractionAction.None;
                switch (target.Station)
                {
                    case StationKind.Board: return InteractionAction.PutOnBoard;
                    case StationKind.Counter: return InteractionAction.Sell;
                    default: return InteractionAction.PutInto;
                }
            }
            if (held.IsHolding) return InteractionAction.None; // a box: no station takes it
            switch (target.Station)
            {
                case StationKind.Crate: return InteractionAction.Buy;
                case StationKind.Counter: return InteractionAction.None;
                default: return hasFood ? InteractionAction.Take : InteractionAction.None;
            }
        }

        /// <summary>
        /// Whether the held food may go onto or into the station, as far as a client can tell. A finished plate
        /// goes nowhere but the counter; the board, pan and pot take one food; the plating table and the bowl
        /// take up to <see cref="AssemblyRules.MaxItems"/>; the counter sells any food (it plates it, spec 009).
        /// </summary>
        public static bool Fits(Held held, AimTarget target)
        {
            if (!held.IsFood || target.Kind != AimKind.Station) return false;
            switch (target.Station)
            {
                case StationKind.Counter: return true;
                case StationKind.Board:
                case StationKind.Pan:
                case StationKind.Pot:
                    return !held.IsPlated && target.ContentCount == 0;
                case StationKind.PlatingTable:
                case StationKind.Bowl:
                    return !held.IsPlated && target.ContentCount < AssemblyRules.MaxItems(target.Station);
                default:
                    return false; // a crate: the hands are full
            }
        }

        /// <summary>
        /// Why a click would do nothing, shown grey under the dot; null when there is nothing to say (or the
        /// click does something). Food that does not fit, the knife on an empty board, a full surface. Spec 020:
        /// a target beyond reach says "Too far" when a step closer it would show anything (an action or a reason).
        /// </summary>
        public static string Reason(Held held, AimTarget target)
        {
            if (target.IsTooFar)
            {
                AimTarget near = target.InReach();
                return ActionFor(held, near) != InteractionAction.None || Reason(held, near) != null ? TooFarText : null;
            }
            if (ActionFor(held, target) != InteractionAction.None) return null;
            if (target.Kind == AimKind.Spot) return held.IsHolding ? "No room here" : null;
            if (target.Kind != AimKind.Station) return null;
            if (held.IsKnife && target.Station == StationKind.Board) return "Put food on the board to cut";
            if (held.IsFood && TakesFood(target.Station)) return held.Name + " can't go in there";
            return null;
        }

        /// <summary>The text under the dot: what a click does, else the grey reason, else null (no prompt).</summary>
        public static string PromptFor(Held held, AimTarget target)
        {
            switch (ActionFor(held, target))
            {
                case InteractionAction.Press: return ButtonPrompt(target.Option, target.StoveFlags);
                case InteractionAction.Cut: return "Drag down · cut   Right click · turn";
                case InteractionAction.PutOnBoard: return Click("put " + held.Name + " on the board");
                case InteractionAction.PutInto: return Click("put " + held.Name + " " + IntoWhat(target.Station));
                case InteractionAction.Sell: return Click("sell " + held.Name);
                case InteractionAction.Place: return Click("put " + held.Name + " here");
                case InteractionAction.PickUp: return Click("pick up " + (string.IsNullOrEmpty(target.Name) ? "it" : target.Name));
                case InteractionAction.Buy:
                    string what = string.IsNullOrEmpty(target.Name) ? "one" : target.Name;
                    return Click("take " + what + (target.Price >= 0 ? " (" + EconomyRules.Money(target.Price) + ")" : string.Empty));
                case InteractionAction.Take: return Click("take " + TakeWhat(target));
                default: return Reason(held, target);
            }
        }

        /// <summary>"Click · switch on", "Click · fry → sauté", ... for a stove button.</summary>
        public static string ButtonPrompt(int option, byte stoveFlags)
        {
            if (option == StationRules.StoveSwitch)
                return Click(StationRules.IsStoveOn(stoveFlags) ? "switch off" : "switch on");
            if (option == StationRules.PanMode)
                return Click(StationRules.IsSaute(stoveFlags) ? "sauté → fry" : "fry → sauté");
            return Click("press");
        }

        /// <summary>
        /// Spec 020 AC7, key map: E and the digit keys act only in third person (spec 007/016); first person uses
        /// the mouse (and Q to drop).
        /// </summary>
        public static bool KeysActInView(bool firstPerson) => !firstPerson;

        /// <summary>Spec 020: Q (gamepad X) drops what you hold, first person only; third person keeps E.</summary>
        public static bool DropKeyActs(bool firstPerson) => firstPerson;

        private static bool TakesFood(StationKind kind)
        {
            return kind == StationKind.Board || kind == StationKind.Pan || kind == StationKind.Pot
                || kind == StationKind.PlatingTable || kind == StationKind.Bowl;
        }

        private static string IntoWhat(StationKind kind)
        {
            switch (kind)
            {
                case StationKind.Pan: return "into the pan";
                case StationKind.Pot: return "into the pot";
                case StationKind.PlatingTable: return "on the plate";
                case StationKind.Bowl: return "into the bowl";
                default: return "in";
            }
        }

        private static string TakeWhat(AimTarget target)
        {
            if (target.Station == StationKind.PlatingTable) return "the plate";
            if (target.Station == StationKind.Bowl && target.ContentCount > 1) return "the mix";
            return string.IsNullOrEmpty(target.ContentName) ? "it" : target.ContentName;
        }

        private static string Click(string what) => ClickKey + " · " + what;
    }
}
