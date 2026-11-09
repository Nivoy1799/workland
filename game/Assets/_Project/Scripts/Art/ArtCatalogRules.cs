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
                case StationKind.Fridge: return ArtKey.StationFridge; // spec 028b
                case StationKind.Shelf: return ArtKey.StationShelf;
                case StationKind.Fryer: return ArtKey.StationFryer; // spec 029
                case StationKind.Griddle: return ArtKey.StationGriddle; // spec 030
                case StationKind.Assembly: return ArtKey.StationAssembly; // spec 031
                case StationKind.Register: return ArtKey.StationRegister; // spec 032
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
                case VesselKind.Basket: return ArtKey.ToolFryerBasket; // spec 029
                default: throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "no art key for cookware");
            }
        }

        /// <summary>Spec 024: the player characters, in variant order (index = <see cref="CharacterVariant"/> of the owner id).</summary>
        public static ArtKey[] PlayerCharacterKeys() => new[] { ArtKey.CharacterPlayerMale, ArtKey.CharacterPlayerFemale };

        /// <summary>Spec 024: the customer characters, in variant order (index = <see cref="CharacterVariant"/> of the object id).</summary>
        public static ArtKey[] CustomerCharacterKeys() =>
            new[]
            {
                ArtKey.CharacterCustomerMale, ArtKey.CharacterCustomerFemale,
                ArtKey.CharacterCustomerMale2, ArtKey.CharacterCustomerFemale2,
                ArtKey.CharacterCustomerMale3, ArtKey.CharacterCustomerFemale3
            };

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

        /// <summary>Spec 024: the character of the customer with this network object id (second review: through <see cref="CustomerVariant"/>).</summary>
        public static ArtKey CustomerCharacterKey(int objectId)
        {
            ArtKey[] keys = CustomerCharacterKeys();
            return keys[CustomerVariant(objectId, keys.Length)];
        }

        /// <summary>
        /// Spec 027d, second review: which of <paramref name="count"/> looks a customer shows. Object ids do not come one after
        /// another for customers (other objects spawn in between, so a plain modulo kept giving the same few looks), so the
        /// id goes through an integer hash first. Spec 027d keeps neighbouring ids different: an even id takes its hashed
        /// look, an odd id its hashed look unless one of its two even neighbours has it, then the next free one (so every
        /// pair of neighbours differs without looking further than one id). Pure and the same on every client.
        /// </summary>
        public static int CustomerVariant(int objectId, int count)
        {
            if (count <= 0) return 0;
            int raw = HashedVariant(objectId, count);
            if ((objectId & 1) == 0 || count < 3) return raw;
            int before, after;
            unchecked
            {
                before = HashedVariant(objectId - 1, count);
                after = HashedVariant(objectId + 1, count);
            }
            int v = raw;
            for (int i = 0; i < count; i++)
            {
                if (v != before && v != after) return v;
                v = (v + 1) % count;
            }
            return raw;
        }

        private static int HashedVariant(int id, int count) => (int)(MixId(id) % (uint)count);

        /// <summary>A fixed integer hash (the lowbias32 finalizer): every bit of the id moves every bit of the result.</summary>
        public static uint MixId(int id)
        {
            unchecked
            {
                uint x = (uint)id;
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return x;
            }
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
