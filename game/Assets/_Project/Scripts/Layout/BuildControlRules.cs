using Worktown.Game.Climb;
using Worktown.Sim.Layout;

namespace Worktown.Game.Layout
{
    /// <summary>
    /// Spec 062b, pure: spec 041's mapping in build mode, with the same meanings as when cooking. Left click picks the aimed piece up (on
    /// the storage pallet: takes the next stored piece); right click places the held piece at the ghost (on the pallet: stores it) and never
    /// picks anything up; the wheel turns the held piece a quarter turn (on the pallet with empty hands: picks the next stored piece);
    /// Ctrl+Z undoes; B or Esc leaves. Also who may enter (<see cref="EntryRefusal"/>), the prompts and the refusal texts.
    /// </summary>
    public static class BuildControlRules
    {
        public const string CloseFirstText = "Close the shop first";
        public const string HandsFullText = "Put down what you hold";
        public const string StageText = "Build mode opens with your restaurant";
        public const string TightAisleText = "Tight aisle";
        /// <summary>feel.md 6: an aisle under 1.2 m works but feels tight (062a question 2: the hard rule is 0.9 m).</summary>
        public const int ComfortAisleMm = 1200;

        public static BuildAction ActionFor(BuildInput input, bool holding, bool aimsAtPiece, bool aimsAtPallet)
        {
            switch (input)
            {
                case BuildInput.LeftClick:
                    if (holding) return BuildAction.None;
                    if (aimsAtPallet) return BuildAction.TakeOut;
                    return aimsAtPiece ? BuildAction.PickUp : BuildAction.None;
                case BuildInput.RightClick:
                    if (!holding) return BuildAction.None; // the right click never picks up
                    return aimsAtPallet ? BuildAction.Store : BuildAction.Place;
                case BuildInput.Wheel:
                    if (holding) return BuildAction.Turn;
                    return aimsAtPallet ? BuildAction.NextStored : BuildAction.None;
                case BuildInput.Undo: return BuildAction.Undo;
                case BuildInput.Leave: return BuildAction.Leave;
                default: return BuildAction.None;
            }
        }

        /// <summary>Why build mode cannot be entered now (null: it can): the shop must be closed, the stage allow it, the hands be empty.</summary>
        public static string EntryRefusal(ShopDoorState door, byte stage, bool handsEmpty)
        {
            if (!StageAllows(stage)) return StageText;
            if (!ShopDoorRules.AllowsBuild(door)) return CloseFirstText;
            if (!handsEmpty) return HandsFullText;
            return null;
        }

        /// <summary>Question 1: build mode from stage 2 (the rented restaurant); stage 0, the open kitchen, too (dev and the tours).</summary>
        public static bool StageAllows(byte stage) => stage == ShopStageRules.OpenKitchen || stage >= ShopStageRules.FirstRestaurant;

        /// <summary>The prompt for the aim: it names the button ("Left click · pick up fryer", "Right click · place here   Wheel · turn").</summary>
        public static string Prompt(bool holding, string aimedName, bool aimsAtPallet, bool placeValid, string problem)
        {
            if (holding)
            {
                if (aimsAtPallet) return "Right click · store   Wheel · turn";
                return placeValid ? "Right click · place here   Wheel · turn" : (problem ?? "Not here") + "   Wheel · turn";
            }
            if (aimsAtPallet) return "Left click · take out a stored piece   Wheel · next";
            if (!string.IsNullOrEmpty(aimedName)) return "Left click · pick up " + aimedName;
            return "Aim at a piece · Ctrl+Z undo · B leave";
        }

        /// <summary>A piece's name in prompts: "fryer", "dish return", "worktop".</summary>
        public static string NameOf(string typeId) => LayoutRules.NameOf(typeId);

        /// <summary>The text a refused intent shows (a refused move: its problem's kind, as the ghost had it).</summary>
        public static string RefusalText(BuildOutcome outcome, LayoutProblemKind? kind)
        {
            switch (outcome)
            {
                case BuildOutcome.Ok: return null;
                case BuildOutcome.NotClosed: return CloseFirstText;
                case BuildOutcome.NotBuilding: return HandsFullText;
                case BuildOutcome.Claimed: return "Someone else holds it";
                case BuildOutcome.Invalid: return kind.HasValue ? ProblemText(kind.Value) : "It does not fit there";
                case BuildOutcome.Busy: return null; // too fast: nothing to say
                case BuildOutcome.NothingToUndo: return "Nothing to undo";
                case BuildOutcome.NotMovable: return "This one stays where it is";
                case BuildOutcome.StationOn: return "Turn it off first";
                case BuildOutcome.Required: return "The shop needs it: place it somewhere else";
                case BuildOutcome.NothingStored: return "Nothing is stored";
                case BuildOutcome.Blocked: return "Something is in the way now";
                case BuildOutcome.NotHeld: return "Pick a piece up first";
                default: return "That did not work";
            }
        }

        /// <summary>A short text per problem kind (the client knows only the kind of the server's refusal).</summary>
        public static string ProblemText(LayoutProblemKind kind)
        {
            switch (kind)
            {
                case LayoutProblemKind.Outside: return "It must stand inside the shop";
                case LayoutProblemKind.OffWall: return "It must hang on a wall";
                case LayoutProblemKind.Overlap: return "Something is in the way";
                case LayoutProblemKind.KeepOut: return "That blocks a door";
                case LayoutProblemKind.Aisle: return "Aisles need 0.9 m";
                case LayoutProblemKind.Unreachable: return "Nobody could reach it there";
                case LayoutProblemKind.NoGuestCounter: return "Guests could not reach the counter";
                case LayoutProblemKind.NoGuestSide: return "Guests on one side, the kitchen on the other";
                case LayoutProblemKind.Missing: return "The shop needs it";
                default: return "It does not fit there";
            }
        }
    }
}
