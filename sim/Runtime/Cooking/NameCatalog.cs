using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>Fixed names for specific item hashes (e.g. "fries").</summary>
    public sealed class NameCatalog
    {
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.Ordinal);

        public NameCatalog(IEnumerable<NamedItem> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            foreach (var item in items)
            {
                if (item == null) throw new ArgumentException("NamedItem must not be null.", nameof(items));
                if (string.IsNullOrWhiteSpace(item.Hash)) throw new ArgumentException("Hash must not be empty.", nameof(items));
                if (string.IsNullOrWhiteSpace(item.Name)) throw new ArgumentException("Name must not be empty.", nameof(items));
                if (_names.ContainsKey(item.Hash)) throw new ArgumentException($"Duplicate hash '{item.Hash}'.", nameof(items));
                _names.Add(item.Hash, item.Name);
            }
        }

        public static NameCatalog Empty => new NameCatalog(Array.Empty<NamedItem>());

        public bool TryGetName(string hash, out string name)
        {
            if (hash != null && _names.TryGetValue(hash, out var n)) { name = n; return true; }
            name = string.Empty;
            return false;
        }
    }
}
