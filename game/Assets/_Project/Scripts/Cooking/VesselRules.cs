using UnityEngine;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 021, pure: what each kind of cookware holds, what fits, where it can be poured and what empty hands do
    /// with it. Shared by the server (KitchenState) and the client's InteractionRules, so the highlight and the
    /// server agree on capacity. The sim dry runs (food the sim would refuse) stay server side.
    /// </summary>
    public static class VesselRules
    {
        /// <summary>Open question 6: a pot takes up to four ingredients, each boiled for its own time.</summary>
        public const int PotCapacity = 4;
        /// <summary>Open question 4: plates come from the shelf, at most this many in the world.</summary>
        public const int MaxPlatesInWorld = 8;

        /// <summary>RequestUse actions: use (put into, pour, take), one knife stroke, turn the food on a board.</summary>
        public const int ActionUse = 0;
        public const int ActionStroke = 1;
        public const int ActionTurn = 2;
        /// <summary>Spec 029: shake a lowered fryer basket (empty hands).</summary>
        public const int ActionShake = 3;

        /// <summary>How many things it holds: board and pan one (open question 2), pot four, plate and bowl the sim limits.</summary>
        public static int Capacity(VesselKind kind)
        {
            switch (kind)
            {
                case VesselKind.Board: return 1;
                case VesselKind.Pan: return 1;
                case VesselKind.Pot: return PotCapacity;
                case VesselKind.Plate: return CookingLimits.MaxPlateInputs;
                case VesselKind.Bowl: return CookingLimits.MaxMixInputs;
                case VesselKind.Basket: return 1; // spec 029: one portion
                default: return 0;
            }
        }

        /// <summary>Pan and pot cook over time while they stand on a burner that is on; spec 029: a basket while it is in the oil.</summary>
        public static bool IsHeat(VesselKind kind) => kind == VesselKind.Pan || kind == VesselKind.Pot || kind == VesselKind.Basket;

        /// <summary>Spec 029: the cookware that cooks on a burner (pan, pot); a basket hangs in its fryer.</summary>
        public static bool StandsOnBurner(VesselKind kind) => kind == VesselKind.Pan || kind == VesselKind.Pot;

        /// <summary>Plate and bowl combine what is put in (sim plate / mix).</summary>
        public static bool IsAssembly(VesselKind kind) => kind == VesselKind.Plate || kind == VesselKind.Bowl;

        /// <summary>
        /// Whether food may go in, before any sim check: plated food never fits (it is finished), a full vessel
        /// says VesselFull. Ok means the server goes on with its sim dry run.
        /// </summary>
        public static CookOutcome CanPutIn(VesselKind kind, int count, bool plated)
        {
            if (plated) return CookOutcome.AlreadyPlated;
            if (count < 0) count = 0;
            if (count >= Capacity(kind)) return CookOutcome.VesselFull;
            return CookOutcome.Ok;
        }

        /// <summary>Holding <paramref name="source"/>: a pan or pot pours onto a plate or into a bowl, a bowl onto a plate.</summary>
        public static bool CanPour(VesselKind source, VesselKind target)
        {
            if (IsHeat(source)) return target == VesselKind.Plate || target == VesselKind.Bowl;
            return source == VesselKind.Bowl && target == VesselKind.Plate;
        }

        /// <summary>
        /// Empty hands on a vessel with food: board, pan and bowl hand the food out (open question 3); a pot and a
        /// plate are picked up whole (a pot is poured, a plate is sold).
        /// </summary>
        public static bool TakesOutByHand(VesselKind kind)
        {
            return kind == VesselKind.Board || kind == VesselKind.Pan || kind == VesselKind.Bowl || kind == VesselKind.Basket;
        }

        /// <summary>Plates do not come back when lost (the shelf gives new ones); all other cookware has a home spot.</summary>
        public static bool RespawnsAtHome(VesselKind kind) => kind != VesselKind.Plate && kind != VesselKind.Basket; // spec 029: fixed

        /// <summary>
        /// Spec 029b: the target checks of a RequestUse before anything changes (KitchenService.TryUseVessel). Cookware must
        /// lie free (a Pickup nobody holds); a fryer basket has no Pickup and is never carried. Reach is measured to the
        /// target's pivot (a basket's networked root, which never moves). NotAVessel or TooFar, else Ok.
        /// </summary>
        public static CookOutcome CheckUseTarget(VesselKind kind, bool hasPickup, bool isHeld, Vector3 player, Vector3 pivot)
        {
            if (kind != VesselKind.Basket && (!hasPickup || isHeld)) return CookOutcome.NotAVessel;
            if (!StationRules.IsInReach(player, pivot)) return CookOutcome.TooFar;
            return CookOutcome.Ok;
        }

        /// <summary>Spec 029b: a shake is for a basket (InvalidOption otherwise), with empty hands (HandsFull), else Ok.</summary>
        public static CookOutcome CheckShake(VesselKind kind, bool handsFull)
        {
            if (kind != VesselKind.Basket) return CookOutcome.InvalidOption;
            return handsFull ? CookOutcome.HandsFull : CookOutcome.Ok;
        }

        /// <summary>
        /// Spec 029b: holding <paramref name="held"/> over a fryer basket tips the basket's food onto it: a plate or a bowl.
        /// </summary>
        public static bool TipsFromBasket(VesselKind held) => IsAssembly(held);

        /// <summary>Lower-case name for prompts: "board", "pan", "pot", "plate", "bowl".</summary>
        public static string Name(VesselKind kind)
        {
            switch (kind)
            {
                case VesselKind.Board: return "board";
                case VesselKind.Pan: return "pan";
                case VesselKind.Pot: return "pot";
                case VesselKind.Plate: return "plate";
                case VesselKind.Bowl: return "bowl";
                case VesselKind.Basket: return "basket";
                default: return "it";
            }
        }

        /// <summary>Lost like the knife (spec 019): gone, or fallen below the world.</summary>
        public static bool IsLost(bool exists, float height) => CutRules.IsKnifeLost(exists, height);

        /// <summary>After <see cref="CutRules.KnifeRespawnSeconds"/> lost, a new one appears at its home spot.</summary>
        public static bool ShouldRespawn(float lostSince, float now) => CutRules.ShouldRespawnKnife(lostSince, now);
    }
}
