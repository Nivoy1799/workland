using Worktown.Game.Cooking;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 020: what the dot points at, as plain data for <see cref="InteractionRules"/>: the kind of target and
    /// what clients can see of it (the station's synced contents, the stove flags, the item's name, the chosen
    /// spot). <c>default</c> is "nothing".
    /// </summary>
    public readonly struct AimTarget
    {
        /// <summary><see cref="AimKind.Other"/> = nothing to interact with.</summary>
        public readonly AimKind Kind;
        public readonly StationKind Station;
        /// <summary>Stations: how many things lie on or in it (from its synced contents).</summary>
        public readonly int ContentCount;
        /// <summary>Stations: the first content name ("potato slices"), null when empty.</summary>
        public readonly string ContentName;
        /// <summary>Buttons: the station option (StationRules.StoveSwitch or PanMode).</summary>
        public readonly int Option;
        /// <summary>Buttons: the stove's synced flags (on, saute).</summary>
        public readonly byte StoveFlags;
        /// <summary>Pickups: the item's name ("potato", "knife", "box"). Crates: the ingredient ("potato").</summary>
        public readonly string Name;
        /// <summary>Crates: the grocer price in dollars, -1 when unknown.</summary>
        public readonly long Price;
        /// <summary>Spots: the chosen free spot, <see cref="PlaceSpots.None"/> when the surface is full.</summary>
        public readonly int SpotIndex;
        /// <summary>Spec 020: the target is beyond reach (the ray sees it, the server would refuse): no action, "Too far".</summary>
        public readonly bool IsTooFar;

        private AimTarget(AimKind kind, StationKind station, int contentCount, string contentName, int option,
            byte stoveFlags, string name, long price, int spotIndex, bool tooFar = false)
        {
            IsTooFar = tooFar;
            Kind = kind;
            Station = station;
            ContentCount = contentCount;
            ContentName = contentName;
            Option = option;
            StoveFlags = stoveFlags;
            Name = name;
            Price = price;
            SpotIndex = spotIndex;
        }

        public static AimTarget Nothing => default;

        /// <summary>The same target, but beyond reach.</summary>
        public AimTarget TooFar()
        {
            return new AimTarget(Kind, Station, ContentCount, ContentName, Option, StoveFlags, Name, Price, SpotIndex, true);
        }

        /// <summary>The same target within reach (what a step closer would give).</summary>
        public AimTarget InReach()
        {
            return new AimTarget(Kind, Station, ContentCount, ContentName, Option, StoveFlags, Name, Price, SpotIndex, false);
        }

        public static AimTarget Button(int option, byte stoveFlags)
        {
            return new AimTarget(AimKind.Button, default, 0, null, option, stoveFlags, null, -1, PlaceSpots.None);
        }

        /// <summary>Any station except a crate (use <see cref="Crate"/>).</summary>
        public static AimTarget OnStation(StationKind kind, int contentCount, string contentName)
        {
            int count = contentCount < 0 ? 0 : contentCount;
            return new AimTarget(AimKind.Station, kind, count, count > 0 ? contentName : null, 0, 0, null, -1, PlaceSpots.None);
        }

        public static AimTarget Crate(string ingredient, long price)
        {
            return new AimTarget(AimKind.Station, StationKind.Crate, 0, null, 0, 0, ingredient, price, PlaceSpots.None);
        }

        public static AimTarget Item(string name)
        {
            return new AimTarget(AimKind.Pickup, default, 0, null, 0, 0, name, -1, PlaceSpots.None);
        }

        /// <summary>A place surface; <paramref name="spotIndex"/> is the chosen free spot or PlaceSpots.None (full).</summary>
        public static AimTarget Spot(int spotIndex)
        {
            return new AimTarget(AimKind.Spot, default, 0, null, 0, 0, null, -1, spotIndex < 0 ? PlaceSpots.None : spotIndex);
        }
    }
}
