using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Worktown.Sim.Cooking
{
    /// <summary>Identity rules (hash version v1). Any change to the preimages must bump the prefix.</summary>
    internal static class ItemCanonical
    {
        private const string HexDigits = "0123456789abcdef";

        internal static bool IsValidId(string? id)
        {
            if (id == null || id.Length < 1 || id.Length > 32) return false;
            foreach (char c in id)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }

        internal static string Sha256Hex(string preimage)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(preimage);
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                sb.Append(HexDigits[b >> 4]);
                sb.Append(HexDigits[b & 0xF]);
            }
            return sb.ToString();
        }

        internal static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        internal static string BaseHash(string baseId) => Sha256Hex("v1|b|" + baseId);

        /// <summary>buckets must be sorted by param name (ordinal); inputs in canonical order.</summary>
        internal static string OpHash(string opId, IReadOnlyList<KeyValuePair<string, string>> buckets, IReadOnlyList<Item> inputs)
        {
            var sb = new StringBuilder("v1|o|");
            sb.Append(opId).Append('|');
            for (int i = 0; i < buckets.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(buckets[i].Key).Append('=').Append(buckets[i].Value);
            }
            sb.Append('|');
            for (int i = 0; i < inputs.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(inputs[i].Hash);
            }
            return Sha256Hex(sb.ToString());
        }

        internal static string OpTree(string opId, IReadOnlyList<KeyValuePair<string, string>> buckets, IReadOnlyList<Item> inputs)
        {
            var sb = new StringBuilder(opId);
            sb.Append('(');
            for (int i = 0; i < buckets.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(buckets[i].Key).Append('=').Append(buckets[i].Value);
            }
            sb.Append(';');
            for (int i = 0; i < inputs.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(inputs[i].CanonicalTree);
            }
            sb.Append(')');
            return sb.ToString();
        }

        /// <summary>Hash ordinal, then Stats fields in declaration order, then Surface.</summary>
        internal static int CompareItems(Item a, Item b)
        {
            int c = StringComparer.Ordinal.Compare(a.Hash, b.Hash);
            if (c != 0) return c;
            var x = a.Stats;
            var y = b.Stats;
            if ((c = x.Sweet.CompareTo(y.Sweet)) != 0) return c;
            if ((c = x.Salty.CompareTo(y.Salty)) != 0) return c;
            if ((c = x.Sour.CompareTo(y.Sour)) != 0) return c;
            if ((c = x.Bitter.CompareTo(y.Bitter)) != 0) return c;
            if ((c = x.Umami.CompareTo(y.Umami)) != 0) return c;
            if ((c = x.Fat.CompareTo(y.Fat)) != 0) return c;
            if ((c = x.Crunch.CompareTo(y.Crunch)) != 0) return c;
            if ((c = x.HeatTolerance.CompareTo(y.HeatTolerance)) != 0) return c;
            if ((c = x.Doneness.CompareTo(y.Doneness)) != 0) return c;
            if ((c = x.Moisture.CompareTo(y.Moisture)) != 0) return c;
            return a.Surface.CompareTo(b.Surface);
        }

        internal static int CompareComponents(ItemComponent a, ItemComponent b)
        {
            int c = StringComparer.Ordinal.Compare(a.BaseId, b.BaseId);
            return c != 0 ? c : a.Doneness.CompareTo(b.Doneness);
        }
    }
}
