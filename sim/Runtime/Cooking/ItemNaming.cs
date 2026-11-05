using System;
using System.Collections.Generic;
using System.Text;

namespace Worktown.Sim.Cooking
{
    /// <summary>Display names only (D5): never affects stats or rating.</summary>
    public static class ItemNaming
    {
        /// <summary>Catalog name, else source name, else the deterministic fallback from the tree.</summary>
        public static string Resolve(Item item, NameCatalog catalog, IItemNameSource? source)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (catalog.TryGetName(item.Hash, out var fixedName)) return fixedName;
            if (source != null && source.TryGetName(item.Hash, out var sourced) && !string.IsNullOrWhiteSpace(sourced)) return sourced;
            return FallbackName(item, catalog);
        }

        /// <summary>
        /// Base: DisplayName. Operation: its template with {in} = input names (catalog name, else their fallback,
        /// joined ", " and " and " before the last) and {param} = bucket label. Built only from the tree.
        /// </summary>
        public static string FallbackName(Item item, NameCatalog catalog)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (item.BaseId != null) return item.DisplayName;

            var names = new List<string>(item.Inputs.Count);
            foreach (var input in item.Inputs)
                names.Add(catalog.TryGetName(input.Hash, out var n) ? n : FallbackName(input, catalog));

            string text = item.NameTemplate.Replace("{in}", Join(names));
            foreach (var kv in item.BucketLabels)
                text = text.Replace("{" + kv.Key + "}", kv.Value);

            while (text.Contains("  ")) text = text.Replace("  ", " ");
            return text.Trim();
        }

        private static string Join(List<string> names)
        {
            if (names.Count == 1) return names[0];
            var sb = new StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(i == names.Count - 1 ? " and " : ", ");
                sb.Append(names[i]);
            }
            return sb.ToString();
        }
    }
}
