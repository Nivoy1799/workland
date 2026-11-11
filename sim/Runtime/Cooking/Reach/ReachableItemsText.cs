using System;
using System.Collections.Generic;
using System.Text;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 056a: the reachable-items file (<c>ai-gateway/offline/reachable.v1.json</c>), the input of the offline name bake.
    /// Pure and deterministic: items sorted by hash, LF line ends, one item per line, ASCII only (anything else throws), no
    /// timestamps. <c>setHash</c> is <see cref="SetHash"/>.
    /// </summary>
    public static class ReachableItemsText
    {
        public const string FormatName = "worktown-reachable-items";
        public const int FormatVersion = 1;
        public const string HashVersion = "v1";

        /// <summary>The first 16 hex chars of the SHA-256 of the items' hashes, sorted ordinally and joined by "\n".</summary>
        public static string SetHash(IReadOnlyList<ReachableItem> items)
        {
            List<ReachableItem> sorted = Sorted(items);
            var hashes = new string[sorted.Count];
            for (int i = 0; i < hashes.Length; i++) hashes[i] = sorted[i].Hash;
            return ItemCanonical.Sha256Hex(string.Join("\n", hashes)).Substring(0, 16);
        }

        /// <summary>The file text of <paramref name="items"/>.</summary>
        /// <exception cref="ArgumentException">A hash that is not 64 lowercase hex chars, a duplicate hash, or a string with a
        /// char outside printable ASCII.</exception>
        public static string Format(IReadOnlyList<ReachableItem> items)
        {
            List<ReachableItem> sorted = Sorted(items);
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"format\": ").Append(Quote(FormatName)).Append(",\n");
            sb.Append("  \"formatVersion\": ").Append(FormatVersion).Append(",\n");
            sb.Append("  \"hashVersion\": ").Append(Quote(HashVersion)).Append(",\n");
            sb.Append("  \"setHash\": ").Append(Quote(SetHash(sorted))).Append(",\n");
            sb.Append("  \"items\": [");
            for (int i = 0; i < sorted.Count; i++)
            {
                ReachableItem item = sorted[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    { \"hash\": ").Append(Quote(item.Hash));
                sb.Append(", \"canonicalTree\": ").Append(Quote(item.CanonicalTree));
                sb.Append(", \"fallbackName\": ").Append(Quote(item.FallbackName));
                sb.Append(", \"tags\": ").Append(List(item.Tags));
                sb.Append(", \"routes\": ").Append(List(item.Routes));
                sb.Append(", \"target\": ").Append(item.IsTarget ? "true" : "false");
                sb.Append(", \"labelWords\": ").Append(List(item.LabelWords));
                sb.Append(" }");
            }
            sb.Append(sorted.Count == 0 ? "]\n" : "\n  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        private static List<ReachableItem> Sorted(IReadOnlyList<ReachableItem> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var sorted = new List<ReachableItem>(items.Count);
            foreach (ReachableItem item in items)
            {
                if (item == null) throw new ArgumentException("Items must not be null.", nameof(items));
                if (!IsHash(item.Hash)) throw new ArgumentException($"Invalid item hash '{item.Hash}'.", nameof(items));
                if (item.CanonicalTree == null || item.FallbackName == null || item.Tags == null || item.Routes == null
                    || item.LabelWords == null)
                    throw new ArgumentException("Item fields must not be null.", nameof(items));
                sorted.Add(item);
            }
            sorted.Sort((a, b) => StringComparer.Ordinal.Compare(a.Hash, b.Hash));
            for (int i = 1; i < sorted.Count; i++)
                if (sorted[i].Hash == sorted[i - 1].Hash) throw new ArgumentException($"Duplicate item hash '{sorted[i].Hash}'.", nameof(items));
            return sorted;
        }

        private static bool IsHash(string? hash)
        {
            if (hash == null || hash.Length != 64) return false;
            foreach (char c in hash)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        private static string List(IReadOnlyList<string> values)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Quote(values[i]));
            }
            return sb.Append(']').ToString();
        }

        private static string Quote(string? value)
        {
            if (value == null) throw new ArgumentException("Strings must not be null.");
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                if (c < 0x20 || c > 0x7E) throw new ArgumentException($"Non-ASCII or control char in '{value}'.");
                if (c == '"' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
