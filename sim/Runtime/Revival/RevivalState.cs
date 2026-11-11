using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065: the revival of every district, in catalog order. Immutable; <see cref="RevivalEngine"/> returns new states.
    /// <see cref="Canonical"/> is its invariant text and <see cref="Digest"/> the SHA-256 of it (lowercase hex). Two states are
    /// equal when their canonical texts are.
    /// </summary>
    public sealed class RevivalState : IEquatable<RevivalState>
    {
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>A state from one entry per district (any order is accepted; the list is kept as given).</summary>
        public RevivalState(IEnumerable<DistrictRevival> districts)
        {
            if (districts == null) throw new ArgumentException("districts are required.", nameof(districts));
            Districts = districts.ToList().AsReadOnly();
            for (int i = 0; i < Districts.Count; i++)
            {
                DistrictRevival d = Districts[i] ?? throw new ArgumentException("districts contain null.", nameof(districts));
                if (d.DistrictId == null || _index.ContainsKey(d.DistrictId)) throw new ArgumentException("Duplicate or missing district id '" + d.DistrictId + "'.");
                if (d.ProgressPpm < 0 || d.ProgressPpm > RevivalCatalog.Ppm) throw new ArgumentException(d.DistrictId + ": progress must be 0..1,000,000 ppm.");
                if (!Enum.IsDefined(typeof(RevivalLevel), d.Level)) throw new ArgumentException(d.DistrictId + ": unknown level.");
                _index[d.DistrictId] = i;
            }
            Canonical = BuildCanonical();
            Digest = Sha256(Canonical);
        }

        public IReadOnlyList<DistrictRevival> Districts { get; }
        public string Canonical { get; }
        public string Digest { get; }

        /// <summary>The entry of a district (throws for an unknown id).</summary>
        public DistrictRevival Of(string districtId) =>
            districtId != null && _index.TryGetValue(districtId, out int i) ? Districts[i] : throw new ArgumentException("Unknown district '" + districtId + "'.");

        /// <summary>The level of a district (throws for an unknown id).</summary>
        public RevivalLevel LevelOf(string districtId) => Of(districtId).Level;

        /// <summary>The progress of a district towards its next level, ppm (throws for an unknown id).</summary>
        public int ProgressPpm(string districtId) => Of(districtId).ProgressPpm;

        /// <summary>The saved form: the districts in order with their level and progress.</summary>
        public SavedRevival ToSaved() => new SavedRevival(
            Districts.Select(d => d.DistrictId).ToList().AsReadOnly(),
            Districts.Select(d => (int)d.Level).ToList().AsReadOnly(),
            Districts.Select(d => d.ProgressPpm).ToList().AsReadOnly());

        /// <summary>
        /// Reads a saved revival against the catalog: levels clamp to 0..3 and progress to 0..1,000,000 (Thriving is always full),
        /// unknown ids are ignored, the first entry of a repeated id wins, entries beyond the shortest list are ignored, and missing
        /// districts get their start values. Loading never rebuilds levels from points. A null save gives the start state.
        /// </summary>
        public static RevivalState FromSaved(RevivalCatalog catalog, SavedRevival? saved)
        {
            if (catalog == null) throw new ArgumentException("A catalog is required.", nameof(catalog));
            var found = new Dictionary<string, DistrictRevival>(StringComparer.Ordinal);
            if (saved?.DistrictIds != null && saved.Levels != null && saved.ProgressPpm != null)
            {
                int n = Math.Min(saved.DistrictIds.Count, Math.Min(saved.Levels.Count, saved.ProgressPpm.Count));
                for (int i = 0; i < n; i++)
                {
                    string id = saved.DistrictIds[i];
                    if (id == null || !catalog.HasDistrict(id) || found.ContainsKey(id)) continue;
                    var level = (RevivalLevel)Math.Max(0, Math.Min(3, saved.Levels[i]));
                    int ppm = level == RevivalLevel.Thriving ? RevivalCatalog.Ppm : Math.Max(0, Math.Min(RevivalCatalog.Ppm, saved.ProgressPpm[i]));
                    found[id] = new DistrictRevival(id, level, ppm);
                }
            }
            return new RevivalState(catalog.Districts.Select(d =>
                found.TryGetValue(d.DistrictId, out DistrictRevival? r) ? r : new DistrictRevival(d.DistrictId, RevivalLevel.Grey, d.StartProgressPpm)));
        }

        public bool Equals(RevivalState? other) => other != null && string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is RevivalState s && Equals(s);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Canonical);

        private string BuildCanonical()
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(32 + Districts.Count * 40);
            sb.Append("revival065\n");
            foreach (DistrictRevival d in Districts)
                sb.Append(d.DistrictId).Append(' ').Append(((int)d.Level).ToString(c)).Append(' ').Append(d.ProgressPpm.ToString(c)).Append('\n');
            return sb.ToString();
        }

        private static string Sha256(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
