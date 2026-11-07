using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Swap layer (spec 015): maps a visual slot to a wrapper prefab. Gameplay objects keep their
    /// colliders and components; only a visual child comes from here.
    /// </summary>
    [CreateAssetMenu(menuName = "Worktown/Art Catalog")]
    public sealed class ArtCatalog : ScriptableObject
    {
        [SerializeField] private List<ArtCatalogEntry> _entries = new List<ArtCatalogEntry>();

        public IReadOnlyList<ArtCatalogEntry> Entries => _entries;

        public bool TryGet(ArtKey key, out GameObject prefab) =>
            ArtCatalogRules.TryGet(ToPairs(), key, out prefab);

        /// <summary>Tuple view for <see cref="ArtCatalogRules"/>.</summary>
        public List<(ArtKey Key, GameObject Prefab)> ToPairs()
        {
            var pairs = new List<(ArtKey, GameObject)>(_entries.Count);
            foreach (ArtCatalogEntry e in _entries) pairs.Add((e.Key, e.Prefab));
            return pairs;
        }

        /// <summary>Editor tooling: replaces all entries.</summary>
        public void SetEntries(IEnumerable<ArtCatalogEntry> entries)
        {
            _entries = new List<ArtCatalogEntry>(entries);
        }
    }
}
