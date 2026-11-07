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

        /// <summary>Spec 016: option 1 on the pan and pot is the on/off switch, option 2 the pan's fry/saute.</summary>
        public const int StoveSwitch = 1;
        public const int PanMode = 2;

        /// <summary>
        /// Spec 019: board options above the digit range, sent while holding a knife: one cut stroke, and
        /// turning the food 90 degrees. Option 0 (E) puts food on the board or takes it.
        /// </summary>
        public const int KnifeStroke = 10;
        public const int KnifeTurn = 11;

        /// <summary>Spec 019: bits of <c>StationContents.StoveFlags</c>, so the button lights need no text parsing.</summary>
        public const byte StoveFlagOn = 1;
        public const byte StoveFlagSaute = 2;

        /// <summary>Spec 019: the synced stove byte: bit 0 on, bit 1 saute.</summary>
        public static byte StoveFlags(bool on, bool saute)
        {
            return (byte)((on ? StoveFlagOn : 0) | (saute ? StoveFlagSaute : 0));
        }

        public static bool IsStoveOn(byte flags) => (flags & StoveFlagOn) != 0;
        public static bool IsSaute(byte flags) => (flags & StoveFlagSaute) != 0;

        /// <summary>
        /// Spec 019 (AC7): the station option a digit key 1-4 sends, 0 for none. First person has no digit
        /// keys at all (the stoves have buttons, the board a knife); third person keeps them on the board, pan
        /// and pot. A digit above the station's options is passed on so the caller can say so. This is a
        /// client input rule; the server does not know the view mode and still accepts digits.
        /// </summary>
        public static int DigitOption(StationKind kind, int digit, bool firstPerson)
        {
            if (firstPerson || digit < 1 || digit > 4) return 0;
            return OptionCount(kind) > 0 ? digit : 0;
        }

        /// <summary>Spec 019: keys on the station's world label, without digits (first person is the default view).</summary>
        public static string FirstPersonKeysHint(StationKind kind)
        {
            switch (kind)
            {
                case StationKind.Board: return "E · knife";
                case StationKind.Pan: return "E · buttons";
                case StationKind.Pot: return "E · switch";
                default: return "E";
            }
        }

        /// <summary>Spec 019: prompt under the dot for a stove button: "E · switch on", "E · fry → sauté", ...</summary>
        public static string ButtonPrompt(int option, byte stoveFlags)
        {
            if (option == StoveSwitch) return IsStoveOn(stoveFlags) ? "E · switch off" : "E · switch on";
            if (option == PanMode) return IsSaute(stoveFlags) ? "E · sauté → fry" : "E · fry → sauté";
            return null;
        }

        /// <summary>
        /// Spec 019: prompt under the dot when looking at a station in first person, without digits.
        /// <paramref name="contentName"/> is what lies on the board or in the pan or pot (null when empty).
        /// Crates, the counter, the plating table and the bowl keep their spec 007/013 prompts.
        /// </summary>
        public static string FirstPersonPrompt(StationKind kind, string stationName, bool holding, bool holdingKnife, string contentName)
        {
            switch (kind)
            {
                case StationKind.Board:
                    if (holdingKnife)
                        return contentName != null ? "drag down · cut   right click · turn" : stationName + ": put food on it to cut";
                    if (holding)
                        return contentName == null ? "E · put on board" : stationName + ": take the " + contentName + " first";
                    return contentName != null ? "E · take " + contentName : stationName + ": put food on it, then take the knife";
                case StationKind.Pan:
                case StationKind.Pot:
                    if (holdingKnife) return stationName + ": put the knife down first";
                    if (holding) return "E · put in the " + stationName.ToLowerInvariant();
                    return contentName != null ? "E · take " + contentName : stationName + ": switch it on with the button";
                default:
                    return Prompt(kind, stationName);
            }
        }

        /// <summary>
        /// Number of option keys a station accepts (0 for stations that only use E). Spec 016: the pan
        /// has on/off and fry/saute, the pot on/off; both cook over time with E.
        /// </summary>
        public static int OptionCount(StationKind kind)
        {
            if (kind == StationKind.Pan) return PanMode;
            if (kind == StationKind.Pot) return StoveSwitch;
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
            if (!IsHeat(kind)) return "1-" + options;
            return options == 1 ? "E · 1" : "E · 1-" + options;
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
                case StationKind.Pan: return stationName + ": E put food in / E (empty hands) take it out, 1 on/off, 2 fry/saute";
                case StationKind.Pot: return stationName + ": E put food in / E (empty hands) take it out, 1 on/off";
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
