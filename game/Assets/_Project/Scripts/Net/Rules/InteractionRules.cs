using Worktown.Game.Cooking;
using Worktown.Game.Economy;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 020, pure: the one rule for first person. From what you hold and what the dot points at it gives the
    /// click's action, the prompt under the dot and (through the action) the highlight colour, so they can never
    /// disagree. Client side only: "fits" uses what clients can see (a vessel's synced contents and the food's coarse
    /// look) with the server's own limits (<see cref="VesselRules"/>); the server checks everything again and decides.
    /// Spec 021: the board, pan, pot, plate and bowl are cookware you carry; rows of the spec 021 table.
    /// </summary>
    public static class InteractionRules
    {
        public const string ClickKey = "Click";
        public const string TooFarText = "Too far";

        /// <summary>
        /// The action of a click (spec 021 table, first match wins): knob or button = Press; knife + board with food =
        /// Cut; food that fits + cookware with room = PutInto; pan, pot or bowl with food + plate or bowl with room =
        /// Pour; food or a plate with food + counter = Sell; holding + free spot or burner = Place; empty hands + board,
        /// pan or bowl with food = Take; empty hands + pot (also with food), plate or empty cookware or any free item =
        /// PickUp; empty hands + crate = Buy, + plate shelf = Take; anything else None.
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
                case AimKind.Vessel: return VesselAction(held, target);
                case AimKind.Station: return StationAction(held, target);
                default: return InteractionAction.None;
            }
        }

        private static InteractionAction VesselAction(Held held, AimTarget target)
        {
            bool hasFood = target.ContentCount > 0;
            if (held.IsKnife)
                return target.Vessel == VesselKind.Board && hasFood ? InteractionAction.Cut : InteractionAction.None;
            if (held.IsFood) return Fits(held, target) ? InteractionAction.PutInto : InteractionAction.None;
            if (held.IsVessel) return CanPour(held, target) ? InteractionAction.Pour : InteractionAction.None;
            if (held.IsHolding) return InteractionAction.None; // a box goes into nothing
            return hasFood && VesselRules.TakesOutByHand(target.Vessel) ? InteractionAction.Take : InteractionAction.PickUp;
        }

        private static InteractionAction StationAction(Held held, AimTarget target)
        {
            switch (target.Station)
            {
                case StationKind.Crate: return held.IsHolding ? InteractionAction.None : InteractionAction.Buy;
                case StationKind.PlateShelf: return held.IsHolding ? InteractionAction.None : InteractionAction.Take;
                case StationKind.Counter: return SellsAtCounter(held) ? InteractionAction.Sell : InteractionAction.None;
                default: return InteractionAction.None; // the stove's body: its knobs, buttons and burners do the work
            }
        }

        /// <summary>
        /// The counter sells loose food (it plates it, spec 009) and a plate with food (spec 021); not an empty plate
        /// and not a pan, pot or bowl (pour it onto a plate first).
        /// </summary>
        public static bool SellsAtCounter(Held held)
        {
            if (held.IsFood) return true;
            return held.IsVessel && held.Vessel == VesselKind.Plate && held.ContentCount > 0;
        }

        /// <summary>
        /// Whether the held food may go into the cookware, as far as a client can tell: a finished plate goes nowhere
        /// but the counter, and each kind holds <see cref="VesselRules.Capacity"/> things. Sim refusals (food the sim
        /// would not cut or heat) are server side, so such food shows green and the server refuses with a toast.
        /// </summary>
        public static bool Fits(Held held, AimTarget target)
        {
            if (!held.IsFood) return false;
            if (target.Kind == AimKind.Station) return target.Station == StationKind.Counter;
            if (target.Kind != AimKind.Vessel) return false;
            return VesselRules.CanPutIn(target.Vessel, target.ContentCount, held.IsPlated) == CookOutcome.Ok;
        }

        /// <summary>Holding a pan, pot or bowl with food over a plate or bowl that has room.</summary>
        public static bool CanPour(Held held, AimTarget target)
        {
            return held.HasContents && target.Kind == AimKind.Vessel && VesselRules.CanPour(held.Vessel, target.Vessel)
                && target.ContentCount < VesselRules.Capacity(target.Vessel);
        }

        /// <summary>
        /// Why a click would do nothing, shown grey under the dot; null when there is nothing to say (or the click does
        /// something). Spec 020: a target beyond reach says "Too far" when a step closer it would show anything.
        /// </summary>
        public static string Reason(Held held, AimTarget target)
        {
            if (target.IsTooFar)
            {
                AimTarget near = target.InReach();
                return ActionFor(held, near) != InteractionAction.None || Reason(held, near) != null ? TooFarText : null;
            }
            if (ActionFor(held, target) != InteractionAction.None) return null;
            switch (target.Kind)
            {
                case AimKind.Spot: return held.IsHolding ? "No room here" : null;
                case AimKind.Vessel: return VesselReason(held, target);
                case AimKind.Station:
                    if (target.Station != StationKind.Counter || !held.IsVessel) return null;
                    if (held.Vessel == VesselKind.Plate) return "Put food on the plate first";
                    return held.HasContents ? "Pour it onto a plate first" : null;
                default: return null;
            }
        }

        private static string VesselReason(Held held, AimTarget target)
        {
            if (held.IsKnife) return target.Vessel == VesselKind.Board ? "Put food on the board to cut" : null;
            if (held.IsFood)
            {
                CookOutcome fits = VesselRules.CanPutIn(target.Vessel, target.ContentCount, held.IsPlated);
                return fits == CookOutcome.VesselFull ? Full(target.Vessel) : held.Name + " can't go in there";
            }
            if (held.HasContents && VesselRules.CanPour(held.Vessel, target.Vessel)) return Full(target.Vessel);
            return null;
        }

        /// <summary>The text under the dot: what a click does, else the grey reason, else null (no prompt).</summary>
        public static string PromptFor(Held held, AimTarget target)
        {
            switch (ActionFor(held, target))
            {
                case InteractionAction.Press: return ButtonPrompt(target.Option, target.StoveFlags);
                case InteractionAction.Cut: return "Drag down · cut   Right click · turn";
                case InteractionAction.PutInto: return Click("put " + held.Name + " " + IntoWhat(target.Vessel));
                case InteractionAction.Pour: return Click("pour into the " + VesselRules.Name(target.Vessel));
                case InteractionAction.Sell: return Click("sell " + (held.IsVessel ? held.ContentName ?? "the plate" : held.Name));
                case InteractionAction.Place:
                    return Click("put " + held.Name + (target.IsBurner ? " on the burner" : " here"));
                case InteractionAction.PickUp: return Click("pick up " + (string.IsNullOrEmpty(target.Name) ? "it" : target.Name));
                case InteractionAction.Buy:
                    string what = string.IsNullOrEmpty(target.Name) ? "one" : target.Name;
                    return Click("take " + what + (target.Price >= 0 ? " (" + EconomyRules.Money(target.Price) + ")" : string.Empty));
                case InteractionAction.Take: return Click("take " + TakeWhat(target));
                default: return Reason(held, target);
            }
        }

        /// <summary>"Click · burner on", "Click · fry → sauté", ... for a stove knob or button (any burner).</summary>
        public static string ButtonPrompt(int option, byte stoveFlags)
        {
            if (!StationRules.TryParseStoveOption(option, out int burner, out int button)) return Click("press");
            if (button == StationRules.StoveSwitch)
                return Click(StationRules.IsBurnerOn(stoveFlags, burner) ? "burner off" : "burner on");
            return Click(StationRules.IsBurnerSaute(stoveFlags, burner) ? "sauté → fry" : "fry → sauté");
        }

        /// <summary>
        /// Spec 020 AC7, key map: E and the digit keys act only in third person (spec 007); first person uses the mouse
        /// (and Q to drop).
        /// </summary>
        public static bool KeysActInView(bool firstPerson) => !firstPerson;

        /// <summary>Spec 020: Q (gamepad X) drops what you hold, first person only; third person keeps E.</summary>
        public static bool DropKeyActs(bool firstPerson) => firstPerson;

        private static string Full(VesselKind kind)
        {
            string name = VesselRules.Name(kind);
            return char.ToUpperInvariant(name[0]) + name.Substring(1) + " is full";
        }

        private static string IntoWhat(VesselKind kind)
        {
            switch (kind)
            {
                case VesselKind.Board: return "on the board";
                case VesselKind.Plate: return "on the plate";
                default: return "into the " + VesselRules.Name(kind);
            }
        }

        private static string TakeWhat(AimTarget target)
        {
            if (target.Kind == AimKind.Station) return "a plate"; // the plate shelf
            if (target.Vessel == VesselKind.Bowl && target.ContentCount > 1) return "the mix";
            return string.IsNullOrEmpty(target.ContentName) ? "it" : target.ContentName;
        }

        private static string Click(string what) => ClickKey + " · " + what;
    }
}
