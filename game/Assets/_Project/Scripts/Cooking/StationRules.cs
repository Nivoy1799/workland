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

        /// <summary>Spec 016: pan option keys.</summary>
        public const int PanHeatDown = 1;
        public const int PanHeatUp = 2;
        public const int PanToggleMode = 3;
        public const int PanOptionCount = 3;

        /// <summary>
        /// Number of option keys a station accepts (0 for stations that only use E). Spec 016: the pan
        /// has three heat controls, the pot none; both cook over time with E.
        /// </summary>
        public static int OptionCount(StationKind kind)
        {
            if (kind == StationKind.Pan) return PanOptionCount;
            StationAction[] actions = ActionsOf(kind);
            return actions == null ? 0 : actions.Length;
        }

        /// <summary>Spec 016: pan and pot cook food over time.</summary>
        public static bool IsHeat(StationKind kind)
        {
            return kind == StationKind.Pan || kind == StationKind.Pot;
        }

        /// <summary>Spec 016: keys on the station's world label, e.g. "E · 1-3".</summary>
        public static string KeysHint(StationKind kind)
        {
            int options = OptionCount(kind);
            if (options == 0) return "E";
            return IsHeat(kind) ? "E · 1-" + options : "1-" + options;
        }

        /// <summary>The instant action for option 1..n (board only), or false for other stations and options.</summary>
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

        /// <summary>Prompt for the overlay, e.g. "Board: 1 halves, 2 slices, 3 sticks, 4 cubes".</summary>
        public static string Prompt(StationKind kind, string stationName)
        {
            switch (kind)
            {
                case StationKind.Crate: return stationName + ": E take one";
                case StationKind.Counter: return stationName + ": E sell";
                case StationKind.PlatingTable: return stationName + ": E put food on / E (empty hands) take the plate";
                case StationKind.Bowl: return stationName + ": E put food in / E (empty hands) take the mix";
                case StationKind.Pan: return stationName + ": E put food in / E (empty hands) take it out, 1 heat down, 2 heat up, 3 fry/saute";
                case StationKind.Pot: return stationName + ": E put food in / E (empty hands) take it out";
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
                default: return null;
            }
        }

        private static StationAction Cut(string shape)
        {
            string label = shape == "dice" ? "cubes" : shape;
            return new StationAction("cut",
                new Dictionary<string, ParamValue> { ["shape"] = ParamValue.Choice(shape) }, label);
        }
    }
}
