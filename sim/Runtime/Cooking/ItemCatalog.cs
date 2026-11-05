using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>Validated, immutable set of base item definitions.</summary>
    public sealed class ItemCatalog
    {
        private readonly Dictionary<string, BaseItemDef> _defs = new Dictionary<string, BaseItemDef>(StringComparer.Ordinal);

        /// <exception cref="ArgumentException">Null element, invalid or duplicate id, empty display name,
        /// null or out-of-range stats (NaN, infinite, outside 0..1), invalid window or tag.</exception>
        public ItemCatalog(IEnumerable<BaseItemDef> defs)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            foreach (var def in defs)
            {
                Validate(def);
                if (_defs.ContainsKey(def.Id)) throw new ArgumentException($"Duplicate base item id '{def.Id}'.", nameof(defs));
                _defs.Add(def.Id, def with { Tags = Array.AsReadOnly(new List<string>(def.Tags).ToArray()) });
            }
        }

        public int Count => _defs.Count;

        public bool TryGet(string? id, out BaseItemDef def)
        {
            if (id != null && _defs.TryGetValue(id, out var d)) { def = d; return true; }
            def = null!;
            return false;
        }

        private static void Validate(BaseItemDef def)
        {
            const string p = "defs";
            if (def == null) throw new ArgumentException("Base item must not be null.", p);
            if (!ItemCanonical.IsValidId(def.Id)) throw new ArgumentException($"Invalid base item id '{def.Id}'. Ids match ^[a-z0-9_]{{1,32}}$.", p);
            if (string.IsNullOrWhiteSpace(def.DisplayName)) throw new ArgumentException($"Base item '{def.Id}' needs a display name.", p);
            var s = def.Stats;
            if (s == null) throw new ArgumentException($"Base item '{def.Id}' has null Stats.", p);
            Stat(def.Id, nameof(s.Sweet), s.Sweet);
            Stat(def.Id, nameof(s.Salty), s.Salty);
            Stat(def.Id, nameof(s.Sour), s.Sour);
            Stat(def.Id, nameof(s.Bitter), s.Bitter);
            Stat(def.Id, nameof(s.Umami), s.Umami);
            Stat(def.Id, nameof(s.Fat), s.Fat);
            Stat(def.Id, nameof(s.Crunch), s.Crunch);
            Stat(def.Id, nameof(s.HeatTolerance), s.HeatTolerance);
            Stat(def.Id, nameof(s.Doneness), s.Doneness);
            Stat(def.Id, nameof(s.Moisture), s.Moisture);
            var w = def.IdealDoneness;
            if (w == null || !(w.Min >= 0f && w.Min <= w.Max && w.Max <= 1f))
                throw new ArgumentException($"Base item '{def.Id}' needs a doneness window with 0 <= Min <= Max <= 1.", p);
            if (def.Tags == null) throw new ArgumentException($"Base item '{def.Id}' has null Tags.", p);
            foreach (var tag in def.Tags)
                if (!ItemCanonical.IsValidId(tag)) throw new ArgumentException($"Base item '{def.Id}' has invalid tag '{tag}'.", p);
        }

        private static void Stat(string id, string name, float value)
        {
            // NaN fails both comparisons, so test the valid range positively.
            if (!(value >= 0f && value <= 1f))
                throw new ArgumentException($"Base item '{id}' stat {name} must be a finite value in 0..1.", "defs");
        }
    }
}
