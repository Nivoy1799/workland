using System.Collections.Generic;

namespace Worktown.Game.Net.Rules
{
    /// <summary>
    /// Spec 047, pure: the build hash client and server compare (JoinRules.Decide). It covers the game version and the network
    /// surface (ProtocolSurface: every RPC, synced field and broadcast with its types), so two builds of the same code match on any
    /// PC and a build whose messages differ is refused cleanly instead of failing later. FNV-1a, 64 bit, over the version and the
    /// lines sorted ordinally: the same inputs give the same hash in every process.
    /// </summary>
    public static class BuildHash
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        /// <summary>16 lower-case hex digits.</summary>
        public static string Compute(string version, IEnumerable<string> surface)
        {
            var lines = new List<string>();
            if (surface != null)
                foreach (string line in surface)
                    if (!string.IsNullOrEmpty(line)) lines.Add(line);
            lines.Sort(System.StringComparer.Ordinal);
            ulong hash = Offset;
            hash = Mix(hash, version ?? string.Empty);
            foreach (string line in lines)
            {
                hash = Mix(hash, "\n");
                hash = Mix(hash, line);
            }
            return hash.ToString("x16");
        }

        /// <summary>The first 8 digits, for the menu corner and the error text.</summary>
        public static string Short(string hash) =>
            string.IsNullOrEmpty(hash) ? "unknown" : hash.Length <= 8 ? hash : hash.Substring(0, 8);

        private static ulong Mix(ulong hash, string text)
        {
            foreach (char c in text)
            {
                hash ^= (byte)(c & 0xFF);
                hash *= Prime;
                hash ^= (byte)(c >> 8);
                hash *= Prime;
            }
            return hash;
        }
    }
}
