using System.Collections.Generic;
using Worktown.Game.Cooking;

namespace Worktown.Game.Art
{
    /// <summary>Pure lookup rules for the catalog, so they are testable without assets.</summary>
    public static class ArtCatalogRules
    {
        /// <summary>First entry with a prefab for the key; false when missing or unassigned.</summary>
        public static bool TryGet<T>(IReadOnlyList<(ArtKey Key, T Prefab)> entries, ArtKey key, out T prefab)
            where T : class
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Key == key && entries[i].Prefab != null)
                {
                    prefab = entries[i].Prefab;
                    return true;
                }
            }
            prefab = null;
            return false;
        }

        /// <summary>
        /// The catalog slot of a kitchen station. Explicit, so enum order never matters. Spec 021: the stove uses the
        /// spec 015 stove model (old pan station slot), the plate shelf the plain cabinet (old plating table slot).
        /// </summary>
        public static ArtKey StationKey(StationKind kind)
        {
            switch (kind)
            {
                case StationKind.Crate: return ArtKey.StationCrate;
                case StationKind.Counter: return ArtKey.StationCounter;
                case StationKind.Stove: return ArtKey.StationPan;
                case StationKind.PlateShelf: return ArtKey.StationPlatingTable;
                default: throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "no art key for station kind");
            }
        }

        /// <summary>Spec 021: the spec 017/019 model of each kind of cookware.</summary>
        public static ArtKey VesselKey(VesselKind kind)
        {
            switch (kind)
            {
                case VesselKind.Board: return ArtKey.ToolCuttingBoard;
                case VesselKind.Pan: return ArtKey.FoodPan;
                case VesselKind.Pot: return ArtKey.FoodPot;
                case VesselKind.Plate: return ArtKey.FoodPlate;
                case VesselKind.Bowl: return ArtKey.FoodBowl;
                default: throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "no art key for cookware");
            }
        }

        /// <summary>Spec 024: the player characters, in variant order (index = <see cref="CharacterVariant"/> of the owner id).</summary>
        public static ArtKey[] PlayerCharacterKeys() => new[] { ArtKey.CharacterPlayerMale, ArtKey.CharacterPlayerFemale };

        /// <summary>Spec 024: the customer characters, in variant order (index = <see cref="CharacterVariant"/> of the object id).</summary>
        public static ArtKey[] CustomerCharacterKeys() =>
            new[] { ArtKey.CharacterCustomerMale, ArtKey.CharacterCustomerFemale };

        /// <summary>
        /// Spec 024: which of <paramref name="count"/> character variants an id shows (owner client id for players, object
        /// id for customers). Every client computes the same index from the same id; negative ids wrap too.
        /// </summary>
        public static int CharacterVariant(int id, int count)
        {
            if (count <= 0) return 0;
            int index = id % count;
            return index < 0 ? index + count : index;
        }

        /// <summary>Spec 024: the character of the player with this owner client id (even = male, odd = female).</summary>
        public static ArtKey PlayerCharacterKey(int ownerClientId)
        {
            ArtKey[] keys = PlayerCharacterKeys();
            return keys[CharacterVariant(ownerClientId, keys.Length)];
        }

        /// <summary>Spec 024: the character of the customer with this network object id.</summary>
        public static ArtKey CustomerCharacterKey(int objectId)
        {
            ArtKey[] keys = CustomerCharacterKeys();
            return keys[CharacterVariant(objectId, keys.Length)];
        }

        /// <summary>Keys from <paramref name="required"/> that have no assigned prefab.</summary>
        public static List<ArtKey> MissingKeys<T>(IReadOnlyList<(ArtKey Key, T Prefab)> entries,
            IEnumerable<ArtKey> required) where T : class
        {
            var missing = new List<ArtKey>();
            foreach (ArtKey key in required)
                if (!TryGet(entries, key, out _)) missing.Add(key);
            return missing;
        }

        /// <summary>Keys that appear more than once, each reported once.</summary>
        public static List<ArtKey> DuplicateKeys<T>(IReadOnlyList<(ArtKey Key, T Prefab)> entries) where T : class
        {
            var seen = new HashSet<ArtKey>();
            var reported = new HashSet<ArtKey>();
            var result = new List<ArtKey>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (!seen.Add(entries[i].Key) && reported.Add(entries[i].Key))
                    result.Add(entries[i].Key);
            }
            return result;
        }
    }
}
