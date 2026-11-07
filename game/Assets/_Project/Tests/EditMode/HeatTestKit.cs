using System;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 016 test helper, spec 021: cooks a registered food in a pan or pot vessel standing on a burner through
    /// KitchenState, the way the game does (pan on the burner, switch on, put in at one tick, take out later), and
    /// registers the result under the same id. Also cuts food on a board vessel like the old board digit keys.
    /// </summary>
    internal static class HeatTestKit
    {
        public const double TickRate = 30d;
        /// <summary>Vessel object ids of the test pan, pot and board, and the burner ids they stand on.</summary>
        public const int PanIndex = 50;
        public const int PotIndex = 51;
        public const int BoardIndex = 52;
        public const int PanBurner = 900;
        public const int PotBurner = 901;
        public const uint StartTick = 1000;

        private const int S = VesselRules.ActionStroke;
        private const int T = VesselRules.ActionTurn;

        /// <summary>Ticks for a sim duration at <see cref="TickRate"/> (exact for multiples of 0.2 s).</summary>
        public static uint TicksFor(double simSeconds)
        {
            return (uint)Math.Round(simSeconds / CookingTime.TimeScale * TickRate);
        }

        /// <summary>Puts the vessel on the burner and switches the burner on if it is off.</summary>
        public static void SwitchOn(KitchenState kitchen, int vesselId, int burnerId, uint tick = StartTick)
        {
            if (kitchen.BurnerUnder(vesselId) != burnerId) kitchen.SetVesselBurner(vesselId, burnerId, tick);
            if (kitchen.BurnerOf(burnerId).IsOn) return;
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseBurner(burnerId, StationRules.StoveSwitch, tick));
            Assert.IsTrue(kitchen.IsCooking(vesselId));
        }

        public static Item Fry(KitchenState kitchen, int foodId, double simSeconds)
        {
            return Cook(kitchen, foodId, VesselKind.Pan, PanIndex, PanBurner, simSeconds);
        }

        public static Item Boil(KitchenState kitchen, int foodId, double simSeconds)
        {
            return Cook(kitchen, foodId, VesselKind.Pot, PotIndex, PotBurner, simSeconds);
        }

        /// <summary>
        /// The old spec 007 pan presets by time at the fixed pan heat (190 C): 1 light (40 s), 2 golden
        /// (150 s, the same item as before), 3 dark (400 s).
        /// </summary>
        public static Item PanPreset(KitchenState kitchen, int foodId, int preset)
        {
            switch (preset)
            {
                case 1: return Fry(kitchen, foodId, 40);
                case 2: return Fry(kitchen, foodId, 150);
                case 3: return Fry(kitchen, foodId, 400);
                default: throw new ArgumentOutOfRangeException(nameof(preset));
            }
        }

        /// <summary>
        /// Spec 019/021: cuts the registered food on a board vessel the way the old board digit keys did (1 halves,
        /// 2 slices, 3 sticks, 4 dice): put on the board, strokes and turns, take. Registers the result under the
        /// same id and returns it. Taking applies one sim cut with the reached shape, the same item the keys made.
        /// </summary>
        public static Item Cut(KitchenState kitchen, int foodId, int option)
        {
            int[] steps;
            switch (option)
            {
                case 1: steps = new[] { S }; break;
                case 2: steps = new[] { S, S }; break;
                case 3: steps = new[] { S, S, T, S }; break;
                case 4: steps = new[] { S, S, T, S, T, S }; break;
                default: throw new ArgumentOutOfRangeException(nameof(option));
            }
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnBoard(foodId, BoardIndex));
            foreach (int step in steps)
                Assert.AreEqual(CookOutcome.Ok, step == S ? kitchen.TryBoardStroke(BoardIndex) : kitchen.TryBoardTurn(BoardIndex));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewBoardTake(BoardIndex, out Item cut));
            kitchen.ClearBoard(BoardIndex);
            kitchen.Register(foodId, cut);
            return cut;
        }

        private static Item Cook(KitchenState kitchen, int foodId, VesselKind kind, int vesselId, int burnerId, double simSeconds)
        {
            SwitchOn(kitchen, vesselId, burnerId);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnHeat(foodId, vesselId, kind, StartTick));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(vesselId, StartTick + TicksFor(simSeconds), TickRate,
                out Item cooked));
            kitchen.ClearHeat(vesselId);
            kitchen.Register(foodId, cooked);
            return cooked;
        }
    }
}
