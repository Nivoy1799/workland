using System;
using System.Collections.Generic;
using Worktown.Game.Cooking;
using Worktown.Game.Net.Rules;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tutorial
{
    /// <summary>
    /// Spec 038: the default first shift, pure. Fourteen steps in order, each with a short instruction that names the real action (the
    /// prompt line stays the game's own), a done-predicate and a target selector over a <see cref="TutorialSnapshot"/>. A step's
    /// predicate also holds when a later step of its chain shows (sticks in the basket mean the potato was taken and cut), so a player
    /// who is ahead is never asked to go back; <see cref="TutorialProgress"/> remembers every predicate that ever held.
    /// The names checked are the sim's own words ("potato sticks", "fries", "falafel balls", "lettuce slices", "... sauce").
    /// </summary>
    public static class TutorialSteps
    {
        /// <summary>Step 1: walked at least this far and turned at least this much.</summary>
        public const float MoveMetres = 3f;
        public const float TurnDegrees = 90f;

        /// <summary>The default first shift (one shared, immutable list).</summary>
        public static readonly IReadOnlyList<TutorialStep> Default = Array.AsReadOnly(new[]
        {
            new TutorialStep(TutorialStepId.MoveAndLook, "Walk with WASD and look around with the mouse.", false, Moved, NoTarget),
            new TutorialStep(TutorialStepId.TakePotato, "Take a potato from the dry shelf: aim at it and left click.", false, HasPotato,
                PotatoTarget),
            new TutorialStep(TutorialStepId.CutSticks,
                "Put the potato on the cutting board, pick up the knife and drag down to cut it into sticks (the mouse wheel turns it).",
                false, HasSticks, CutTarget),
            new TutorialStep(TutorialStepId.FrySticks,
                "Take the sticks to the fryer: switch it on, put them in the basket and lower it. Shake it once, raise it at perfect.",
                false, HasFries, FryTarget, FryText),
            new TutorialStep(TutorialStepId.PlateFries, "Pick up a plate and left click the raised basket: the fries tip onto it.", false,
                HasPlatedFries, PlateTarget),
            new TutorialStep(TutorialStepId.BuildWrap,
                "Build a falafel wrap on the assembly board: a wrap, falafel from the fryer, sliced lettuce, then hold a sauce for a second.",
                false, HasBuild, BuildTarget),
            new TutorialStep(TutorialStepId.FoldAndWrap, "Left click the board with empty hands to fold the wrap, then left click the wrap paper on the rack.",
                false, HasWrapped, FoldTarget),
            new TutorialStep(TutorialStepId.ServeGuest, "Serve a guest: carry the fries or the wrap to the serving counter and left click it.", false,
                HasServed, ServeTarget),
            new TutorialStep(TutorialStepId.TakePayment,
                "The guest pays at the register. Cash: left click coins and notes until the change is right, then hand it over. Card: click the terminal.",
                false, HasPaid, PayTarget),
            new TutorialStep(TutorialStepId.FillLemonade,
                "Take a cup, hold left click on the lemonade tap until it is full, then left click the lids. Serve it with the wrap.",
                false, HasLemonade, LemonadeTarget),
            new TutorialStep(TutorialStepId.WashPlate,
                "Guests who eat in bring their plate back. Take it from the dish return, put it in the sink and hold left click to scrub.",
                false, HasWashed, WashTarget),
            new TutorialStep(TutorialStepId.OrderStock,
                "Stock runs out. Order more at the terminal on the back wall: left click a row (the box comes to the back door).",
                false, HasOrdered, OrderTarget),
            new TutorialStep(TutorialStepId.ShaveSpit,
                "Optional: load the spit, turn its knob to high, and when the cone browns hold left click on it with the knife to shave.",
                true, HasShaved, SpitTarget),
            new TutorialStep(TutorialStepId.Done, "That is the whole kitchen. Have a good first shift! F1 replays this tutorial.", false, Never,
                NoTarget),
        });

        // ---- Predicates (each public, so the tests pin them one by one) ----

        public static bool Moved(in TutorialSnapshot s) => s.Moved >= MoveMetres && s.Turned >= TurnDegrees;

        public static bool HasPotato(in TutorialSnapshot s) =>
            HeldHas(in s, "potato") || Has(s.BoardTitle, "potato") || HasSticks(in s);

        public static bool HasSticks(in TutorialSnapshot s) =>
            HeldHas(in s, "potato stick") || Has(s.BoardTitle, "potato stick") || Has(s.BasketTitle, "potato stick") || HasFries(in s);

        /// <summary>Fried: fries in a basket out of the oil, or in the hands (on a plate too).</summary>
        public static bool HasFries(in TutorialSnapshot s) =>
            (Has(s.BasketTitle, "fries") && FryerRules.StateOf(s.BasketMode) != BasketState.Lowered) || HeldHas(in s, "fries");

        public static bool HasPlatedFries(in TutorialSnapshot s) =>
            s.Held.IsVessel && s.Held.Vessel == VesselKind.Plate && Has(s.Held.ContentName, "fries");

        /// <summary>Layers of the tutorial's wrap: the wrap, falafel, lettuce, a sauce.</summary>
        public const int WrapLayers = 4;

        /// <summary>
        /// The open build has falafel and at least <see cref="WrapLayers"/> layers (wrap, falafel, lettuce, sauce), or it is finished. The
        /// board's synced name is only the take name ("Falafel wrap" once it matches the dish), so the layers are counted, not named.
        /// </summary>
        public static bool HasBuild(in TutorialSnapshot s) =>
            (BuildHas(in s, "falafel") && s.BuildCount >= WrapLayers) || HasWrapped(in s);

        /// <summary>Finished (wrapped, boxed or bagged) with falafel in it: on the board, or taken.</summary>
        public static bool HasWrapped(in TutorialSnapshot s) =>
            (BuildRules.IsFinished(s.BuildMode) && BuildHas(in s, "falafel")) || (s.Held.IsBuild && Has(s.Held.Name, "falafel"));

        public static bool HasServed(in TutorialSnapshot s) => (s.Events & TutorialEvents.Served) != 0;

        public static bool HasPaid(in TutorialSnapshot s) => (s.Events & TutorialEvents.Paid) != 0;

        /// <summary>A filled cup with its lid in the hands.</summary>
        public static bool HasLemonade(in TutorialSnapshot s) =>
            s.Held.IsCup && s.Held.CupLidded && s.Held.CupLevel != DrinkRules.LevelEmpty;

        public static bool HasWashed(in TutorialSnapshot s) => (s.Events & TutorialEvents.Scrubbed) != 0;

        public static bool HasOrdered(in TutorialSnapshot s) => (s.Events & TutorialEvents.Ordered) != 0;

        public static bool HasShaved(in TutorialSnapshot s) =>
            (s.Events & TutorialEvents.Shaved) != 0 || HeldHas(in s, "shaved") || BuildHas(in s, "shaved");

        private static bool Never(in TutorialSnapshot s) => false;

        // ---- Target selectors (the next thing to touch, from what the player has now) ----

        private static TutorialTarget NoTarget(in TutorialSnapshot s) => TutorialTarget.None;

        private static TutorialTarget PotatoTarget(in TutorialSnapshot s) => TutorialTarget.PotatoSlot;

        public static TutorialTarget CutTarget(in TutorialSnapshot s)
        {
            if (s.Held.IsKnife || HeldFood(in s, "potato")) return TutorialTarget.CuttingBoard;
            if (Has(s.BoardTitle, "potato")) return TutorialTarget.Knife;
            return s.Held.IsHolding ? TutorialTarget.CuttingBoard : TutorialTarget.PotatoSlot;
        }

        public static TutorialTarget FryTarget(in TutorialSnapshot s)
        {
            if (Has(s.BasketTitle, "potato stick") || Has(s.BasketTitle, "fries")) return BasketStep(in s);
            if (HeldFood(in s, "stick")) return s.FryerOn ? TutorialTarget.FryerBasket : TutorialTarget.FryerPower;
            return TutorialTarget.CuttingBoard; // the knife back down, the sticks taken
        }

        /// <summary>
        /// 038b: step 4 while the knife is still in the hands (the target is the board with the sticks): put the knife down first. Spec 041:
        /// the right click puts down what is held (the HUD: "Right click · drop" or "put down here").
        /// </summary>
        public const string FryKnifeText =
            "Put the knife down first (right click). Then take the sticks from the board to the fryer: switch it on, basket in, lower it.";

        /// <summary>038b: <see cref="FryKnifeText"/> while the knife is held and the sticks are not in the basket yet, else null.</summary>
        public static string FryText(in TutorialSnapshot s) =>
            s.Held.IsKnife && FryTarget(in s) == TutorialTarget.CuttingBoard ? FryKnifeText : null;

        public static TutorialTarget PlateTarget(in TutorialSnapshot s) =>
            s.Held.IsVessel && s.Held.Vessel == VesselKind.Plate ? TutorialTarget.FryerBasket : TutorialTarget.Plate;

        public static TutorialTarget BuildTarget(in TutorialSnapshot s)
        {
            bool any = s.BuildCount > 0;
            if (!any) return HeldFood(in s, "wrap") || HeldFood(in s, "flatbread") ? TutorialTarget.AssemblyBoard : TutorialTarget.WrapSlot;
            if (!BuildHas(in s, "falafel"))
            {
                if (HeldFood(in s, "falafel"))
                    return Has(s.Held.Name, "mix") ? (s.FryerOn ? TutorialTarget.FryerBasket : TutorialTarget.FryerPower) : TutorialTarget.AssemblyBoard;
                if (Has(s.BasketTitle, "falafel")) return BasketStep(in s);
                return TutorialTarget.FalafelSlot;
            }
            if (s.BuildCount < WrapLayers - 1) // the lettuce next
            {
                if (HeldFood(in s, "lettuce")) return Has(s.Held.Name, "slice") ? TutorialTarget.AssemblyBoard : TutorialTarget.CuttingBoard;
                if (Has(s.BoardTitle, "lettuce"))
                    return s.Held.IsKnife || Has(s.BoardTitle, "slice") ? TutorialTarget.CuttingBoard : TutorialTarget.Knife;
                return TutorialTarget.LettuceSlot;
            }
            return s.BuildCount < WrapLayers ? TutorialTarget.Sauce : TutorialTarget.AssemblyBoard;
        }

        public static TutorialTarget FoldTarget(in TutorialSnapshot s) =>
            BuildRules.IsOpen(s.BuildMode) ? TutorialTarget.AssemblyBoard : TutorialTarget.WrapPaper;

        private static TutorialTarget ServeTarget(in TutorialSnapshot s) => TutorialTarget.Counter;

        private static TutorialTarget PayTarget(in TutorialSnapshot s) => TutorialTarget.Register;

        public static TutorialTarget LemonadeTarget(in TutorialSnapshot s)
        {
            if (!s.Held.IsCup) return TutorialTarget.CupStack;
            return s.Held.CupLevel == DrinkRules.LevelEmpty ? TutorialTarget.LemonadeTap : TutorialTarget.LidStack;
        }

        public static TutorialTarget WashTarget(in TutorialSnapshot s) =>
            s.Held.IsDirtyDish || s.SinkBasin > 0 ? TutorialTarget.Sink : TutorialTarget.DishReturn;

        private static TutorialTarget OrderTarget(in TutorialSnapshot s) => TutorialTarget.Terminal;

        public static TutorialTarget SpitTarget(in TutorialSnapshot s)
        {
            if (!SpitRules.HasCone(s.SpitFlags)) return TutorialTarget.SpitCone; // empty hands load a cone
            if (SpitRules.LevelOf(s.SpitFlags) != Spit.High) return TutorialTarget.SpitKnob;
            return s.Held.IsKnife ? TutorialTarget.SpitCone : TutorialTarget.Knife;
        }

        /// <summary>A basket with food in it: on (power), down (button), shake while frying, up at perfect (button), take it out.</summary>
        private static TutorialTarget BasketStep(in TutorialSnapshot s)
        {
            BasketState state = FryerRules.StateOf(s.BasketMode);
            if (state == BasketState.Lowered)
                return s.BasketZone >= HeatZone.Perfect ? TutorialTarget.FryerButton : TutorialTarget.FryerBasket;
            if (Has(s.BasketTitle, "fries") || Has(s.BasketTitle, "falafel ball")) return TutorialTarget.FryerBasket;
            return s.FryerOn ? TutorialTarget.FryerButton : TutorialTarget.FryerPower;
        }

        // ---- Helpers (no allocation: ordinal, case-insensitive searches over the synced strings) ----

        private static bool Has(string text, string word) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool HeldFood(in TutorialSnapshot s, string word) => s.Held.IsFood && Has(s.Held.Name, word);

        /// <summary>The hands' food, or what the held cookware holds.</summary>
        private static bool HeldHas(in TutorialSnapshot s, string word) =>
            HeldFood(in s, word) || (s.Held.IsVessel && Has(s.Held.ContentName, word));

        private static bool BuildHas(in TutorialSnapshot s, string word)
        {
            IReadOnlyList<string> layers = s.BuildLayers;
            if (layers == null) return false;
            for (int i = 0; i < layers.Count; i++)
                if (Has(layers[i], word)) return true;
            return false;
        }
    }
}
