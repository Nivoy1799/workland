using System.Collections.Generic;
using UnityEngine;
using Worktown.Game.Net.Rules;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Maps a station and an option key (1-based) to a sim operation with preset parameters
    /// (spec 007). Pure, no networking.
    /// </summary>
    public static class StationRules
    {
        private static readonly StationAction[] BoardActions =
        {
            Cut("halves"), Cut("slices"), Cut("sticks"), Cut("dice")
        };

        private static readonly StationAction[] PanActions =
        {
            Fry(180f, 40f, "light"), Fry(190f, 150f, "golden"), Fry(200f, 400f, "dark")
        };

        private static readonly StationAction[] PotActions =
        {
            Boil(60f, "short"), Boil(300f, "medium"), Boil(900f, "long")
        };

        /// <summary>Number of option keys a station accepts (0 for crate and counter, which use E).</summary>
        public static int OptionCount(StationKind kind)
        {
            StationAction[] actions = ActionsOf(kind);
            return actions == null ? 0 : actions.Length;
        }

        /// <summary>The action for option 1..n, or false for unknown stations and options.</summary>
        public static bool TryGetAction(StationKind kind, int option, out StationAction action)
        {
            action = null;
            StationAction[] actions = ActionsOf(kind);
            if (actions == null || option < 1 || option > actions.Length) return false;
            action = actions[option - 1];
            return true;
        }

        /// <summary>Same reach as pickups (2.5 m).</summary>
        public static bool IsInReach(Vector3 playerPosition, Vector3 stationPosition)
        {
            return PickupRules.IsInReach(playerPosition, stationPosition);
        }

        /// <summary>Prompt for the overlay, e.g. "Pan: 1 light, 2 golden, 3 dark".</summary>
        public static string Prompt(StationKind kind, string stationName)
        {
            switch (kind)
            {
                case StationKind.Crate: return stationName + ": E take one";
                case StationKind.Counter: return stationName + ": E sell";
                case StationKind.PlatingTable: return stationName + ": E put food on / E (empty hands) take the plate";
                case StationKind.Bowl: return stationName + ": E put food in / E (empty hands) take the mix";
            }
            StationAction[] actions = ActionsOf(kind);
            var parts = new List<string>(actions.Length);
            for (int i = 0; i < actions.Length; i++)
                parts.Add((i + 1) + " " + actions[i].Label);
            return stationName + ": " + string.Join(", ", parts);
        }

        private static StationAction[] ActionsOf(StationKind kind)
        {
            switch (kind)
            {
                case StationKind.Board: return BoardActions;
                case StationKind.Pan: return PanActions;
                case StationKind.Pot: return PotActions;
                default: return null;
            }
        }

        private static StationAction Cut(string shape)
        {
            string label = shape == "dice" ? "cubes" : shape;
            return new StationAction("cut",
                new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice(shape) }, label);
        }

        private static StationAction Fry(float temp, float time, string label)
        {
            return new StationAction("fry", new Dictionary<string, ParamValue>
            {
                ["temp"] = ParamValue.Number(temp),
                ["time"] = ParamValue.Number(time)
            }, label);
        }

        private static StationAction Boil(float time, string label)
        {
            return new StationAction("boil",
                new Dictionary<string, ParamValue> { ["time"] = ParamValue.Number(time) }, label);
        }
    }
}
