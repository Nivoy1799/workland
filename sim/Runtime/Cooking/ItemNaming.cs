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
            // Spec 031: a build is named by its dish ("falafel wrap") or its layers, and by how it is closed and packed.
            if (item.Kind == OperationKind.Assemble || item.Kind == OperationKind.Close || item.Kind == OperationKind.Pack)
                return Assembly.NameOf(item, catalog);

            var names = new List<string>(item.Inputs.Count);
            foreach (var input in item.Inputs)
                names.Add(catalog.TryGetName(input.Hash, out var n) ? n : FallbackName(input, catalog));

            string text = Expand(item.NameTemplate, Join(names), item.BucketLabels);

            while (text.Contains("  ")) text = text.Replace("  ", " ");
            return text.Trim();
        }

        /// <summary>Single pass: braces inside inserted text are never expanded again.</summary>
        private static string Expand(string template, string inputs, IReadOnlyDictionary<string, string> labels)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < template.Length)
            {
                if (template[i] == '{')
                {
                    int end = template.IndexOf('}', i + 1);
                    if (end > 0)
                    {
                        string key = template.Substring(i + 1, end - i - 1);
                        if (key == "in") { sb.Append(inputs); i = end + 1; continue; }
                        if (labels.TryGetValue(key, out var label)) { sb.Append(label); i = end + 1; continue; }
                    }
                }
                sb.Append(template[i]);
                i++;
            }
            return sb.ToString();
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
