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

        /// <summary>The catalog slot of a kitchen station. Explicit, so enum order never matters.</summary>
        public static ArtKey StationKey(StationKind kind)
        {
            switch (kind)
            {
                case StationKind.Crate: return ArtKey.StationCrate;
                case StationKind.Board: return ArtKey.StationBoard;
                case StationKind.Pan: return ArtKey.StationPan;
                case StationKind.Pot: return ArtKey.StationPot;
                case StationKind.Counter: return ArtKey.StationCounter;
                case StationKind.PlatingTable: return ArtKey.StationPlatingTable;
                case StationKind.Bowl: return ArtKey.StationBowl;
                default: throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "no art key for station kind");
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
