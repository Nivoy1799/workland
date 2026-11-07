using System;
using NUnit.Framework;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 016 test helper: cooks a registered food in a pan or pot through KitchenState, the way the
    /// game does (switch on, put in at one tick, take out later), and registers the result under the
    /// same id.
    /// </summary>
    internal static class HeatTestKit
    {
        public const double TickRate = 30d;
        public const int PanIndex = 50;
        public const int PotIndex = 51;
        public const uint StartTick = 1000;

        /// <summary>Ticks for a sim duration at <see cref="TickRate"/> (exact for multiples of 0.2 s).</summary>
        public static uint TicksFor(double simSeconds)
        {
            return (uint)Math.Round(simSeconds / CookingTime.TimeScale * TickRate);
        }

        /// <summary>Switches the stove on if it is off.</summary>
        public static void SwitchOn(KitchenState kitchen, int stationIndex, StationKind kind, uint tick = StartTick)
        {
            if (kitchen.StoveOf(stationIndex).IsOn) return;
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryUseStove(stationIndex, kind, StationRules.StoveSwitch, tick));
            Assert.IsTrue(kitchen.StoveOf(stationIndex).IsOn);
        }

        public static Item Fry(KitchenState kitchen, int foodId, double simSeconds)
        {
            return Cook(kitchen, foodId, StationKind.Pan, PanIndex, simSeconds);
        }

        public static Item Boil(KitchenState kitchen, int foodId, double simSeconds)
        {
            return Cook(kitchen, foodId, StationKind.Pot, PotIndex, simSeconds);
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

        private static Item Cook(KitchenState kitchen, int foodId, StationKind kind, int stationIndex, double simSeconds)
        {
            SwitchOn(kitchen, stationIndex, kind);
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPutOnHeat(foodId, stationIndex, kind, 0, StartTick));
            Assert.AreEqual(CookOutcome.Ok, kitchen.TryPreviewHeatTake(stationIndex, StartTick + TicksFor(simSeconds), TickRate,
                out Item cooked, out _));
            kitchen.ClearHeat(stationIndex);
            kitchen.Register(foodId, cooked);
            return cooked;
        }
    }
}
