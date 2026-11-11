using UnityEngine;
using Worktown.Game.Net.Rules;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Pure rules for the kitchen furniture (spec 007). Spec 021: the stations are crates, the counter, the stoves
    /// and the plate shelf; cooking happens in cookware (VesselRules). A stove has two burners; each burner has an
    /// on/off knob and a fry/sauté button, sent as one station option (<see cref="StoveOption"/>).
    /// </summary>
    public static class StationRules
    {
        /// <summary>Spec 016: the on/off switch and the pan's fry/saute button (per burner since spec 021).</summary>
        public const int StoveSwitch = 1;
        public const int PanMode = 2;

        /// <summary>Spec 021: burners on one stove.</summary>
        public const int BurnersPerStove = 2;

        /// <summary>
        /// The station option a stove button sends: burner 0 is 1 (switch) and 2 (mode), burner 1 is 3 and 4.
        /// Burner 0 keeps the spec 016 numbers.
        /// </summary>
        public static int StoveOption(int burner, int button)
        {
            return burner * 2 + button;
        }

        /// <summary>Reads a stove option back; false for anything that is no burner button.</summary>
        public static bool TryParseStoveOption(int option, out int burner, out int button)
        {
            burner = -1;
            button = 0;
            if (option < 1 || option > BurnersPerStove * 2) return false;
            burner = (option - 1) / 2;
            button = (option - 1) % 2 + 1;
            return true;
        }

        /// <summary>Spec 019 bits of <c>StationContents.StoveFlags</c>, per burner: bit 2b on, bit 2b+1 saute.</summary>
        public const byte StoveFlagOn = 1;
        public const byte StoveFlagSaute = 2;

        /// <summary>The flag bits of one burner, to be combined with | for a stove.</summary>
        public static byte BurnerFlags(int burner, bool on, bool saute)
        {
            if (burner < 0 || burner >= BurnersPerStove) return 0;
            int bits = (on ? StoveFlagOn : 0) | (saute ? StoveFlagSaute : 0);
            return (byte)(bits << (burner * 2));
        }

        public static bool IsBurnerOn(byte flags, int burner)
        {
            return burner >= 0 && burner < BurnersPerStove && ((flags >> (burner * 2)) & StoveFlagOn) != 0;
        }

        public static bool IsBurnerSaute(byte flags, int burner)
        {
            return burner >= 0 && burner < BurnersPerStove && ((flags >> (burner * 2)) & StoveFlagSaute) != 0;
        }

        /// <summary>Same reach as pickups (2.5 m).</summary>
        public static bool IsInReach(Vector3 playerPosition, Vector3 stationPosition)
        {
            return PickupRules.IsInReach(playerPosition, stationPosition);
        }

        /// <summary>
        /// Third-person prompt at the bottom (spec 007): the Use key for crates, the counter and the shelf. Spec 048 (review): the keys come
        /// in by name (<paramref name="useKey"/>, <paramref name="viewKey"/>: Settings.KeyPrompts.KeyName of the bindings), so the prompt follows
        /// a rebind.
        /// </summary>
        public static string Prompt(StationKind kind, string stationName, string useKey, string viewKey)
        {
            switch (kind)
            {
                case StationKind.Crate: return stationName + ": " + useKey + " take one";
                case StationKind.Counter: return stationName + ": " + useKey + " sell";
                case StationKind.PlateShelf: return stationName + ": " + useKey + " take a plate";
                case StationKind.Stove: return stationName + ": cook in first person (" + viewKey + ")";
                // Spec 028b: a slot is picked by aiming, so the fridge and the shelf are used in first person.
                case StationKind.Fridge:
                case StationKind.Shelf: return stationName + ": pick an item in first person (" + viewKey + ")";
                // Spec 029: the fryer's knob, basket buttons and baskets are aimed at.
                case StationKind.Fryer: return stationName + ": fry in first person (" + viewKey + ")";
                // Spec 030: the griddle's knob and spots are aimed at.
                case StationKind.Griddle: return stationName + ": grill in first person (" + viewKey + ")";
                // Spec 033: E takes a cup or lids one, holding E pours from the nearest tap.
                case StationKind.DrinkDispenser: return stationName + ": " + useKey + " take a cup or a lid, hold " + useKey + " to pour";
                // Spec 034: E orders the nearest row; the page and the pack count are picked in first person.
                case StationKind.Terminal: return stationName + ": " + useKey + " order the nearest row, page in first person (" + viewKey + ")";
                // Spec 035: the cleaning stations.
                case StationKind.DishReturn: return stationName + ": " + useKey + " take a dirty dish";
                case StationKind.Sink: return stationName + ": " + useKey + " put a dish in, hold " + useKey + " to scrub";
                case StationKind.TrashCan: return stationName + ": " + useKey + " throw away, " + useKey + " with empty hands takes the bag";
                case StationKind.Bin: return stationName + ": " + useKey + " throw the bag in";
                // Spec 037: the menu board's buttons are aimed at.
                case StationKind.MenuBoard: return stationName + ": set prices in first person (" + viewKey + ")";
                default: return stationName;
            }
        }
    }
}
