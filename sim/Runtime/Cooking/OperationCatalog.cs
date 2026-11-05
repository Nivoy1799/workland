using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>Validated, immutable set of operation definitions.</summary>
    public sealed class OperationCatalog
    {
        private readonly Dictionary<string, OperationDef> _ops = new Dictionary<string, OperationDef>(StringComparer.Ordinal);

        public OperationCatalog(IEnumerable<OperationDef> ops)
        {
            if (ops == null) throw new ArgumentNullException(nameof(ops));
            foreach (var op in ops)
            {
                Validate(op);
                if (_ops.ContainsKey(op.Id)) throw new ArgumentException($"Duplicate operation id '{op.Id}'.", nameof(ops));
                _ops.Add(op.Id, op);
            }
        }

        public int Count => _ops.Count;

        public bool TryGet(string? id, out OperationDef op)
        {
            if (id != null && _ops.TryGetValue(id, out var o)) { op = o; return true; }
            op = null!;
            return false;
        }

        /// <summary>The built-in operation set from spec 005. Bucket boundaries are provisional.</summary>
        public static OperationCatalog CreateDefault()
        {
            var plated = new[] { "plated" };
            var none = Array.Empty<string>();

            var shape = new ParamDef("shape", ParamKind.Choice, 0f, 1f, new[]
            {
                new BucketDef("halves", 0.2f, "halves"),
                new BucketDef("slices", 0.5f, "slices"),
                new BucketDef("sticks", 0.6f, "sticks"),
                new BucketDef("dice", 0.8f, "cubes"),
            });
            var panTemp = new ParamDef("temp", ParamKind.Number, 120f, 220f, new[]
            {
                new BucketDef("medium", 120f, ""),
                new BucketDef("hot", 170f, "crispy"),
            });
            var panTime = new ParamDef("time", ParamKind.Number, 10f, 600f, new[]
            {
                new BucketDef("short", 10f, "lightly"),
                new BucketDef("medium", 60f, ""),
                new BucketDef("long", 180f, "dark"),
            });
            var boilTime = new ParamDef("time", ParamKind.Number, 30f, 1800f, new[]
            {
                new BucketDef("short", 30f, "lightly"),
                new BucketDef("medium", 300f, ""),
                new BucketDef("long", 900f, "soft"),
            });

            return new OperationCatalog(new[]
            {
                new OperationDef("cut", "board", OperationKind.Cut, false, 1, 1, new[] { shape }, null,
                    plated, none, "{in} {shape}"),
                new OperationDef("fry", "pan", OperationKind.Heat, false, 1, 1, new[] { panTemp, panTime },
                    new HeatProfile(null, 0.35f, -0.6f, 0.5f, 0.15f, 0f), plated, none, "{temp} {time} fried {in}"),
                new OperationDef("saute", "pan", OperationKind.Heat, false, 1, 1, new[] { panTemp, panTime },
                    new HeatProfile(null, 0.15f, -0.4f, 0.25f, 0.2f, 0f), plated, none, "{temp} {time} sauteed {in}"),
                new OperationDef("boil", "pot", OperationKind.Heat, false, 1, 1, new[] { boilTime },
                    new HeatProfile(100f, 0f, 0.5f, -0.8f, 0f, 0.1f), plated, none, "{time} boiled {in}"),
                new OperationDef("mix", "counter", OperationKind.Mix, true, 2, CookingLimits.MaxMixInputs,
                    Array.Empty<ParamDef>(), null, plated, none, "{in} mix"),
                new OperationDef("plate", "counter", OperationKind.Plate, true, 1, CookingLimits.MaxPlateInputs,
                    Array.Empty<ParamDef>(), null, plated, plated, "plate of {in}"),
            });
        }

        private static void Validate(OperationDef op)
        {
            const string p = "ops";
            if (op == null) throw new ArgumentException("Operation must not be null.", p);
            if (!ItemCanonical.IsValidId(op.Id)) throw new ArgumentException($"Invalid operation id '{op.Id}'.", p);
            if (string.IsNullOrWhiteSpace(op.Station)) throw new ArgumentException($"Operation '{op.Id}' needs a station.", p);
            if (string.IsNullOrWhiteSpace(op.NameTemplate)) throw new ArgumentException($"Operation '{op.Id}' needs a name template.", p);
            if (op.MinInputs < 1 || op.MaxInputs < op.MinInputs)
                throw new ArgumentException($"Operation '{op.Id}' has invalid input arity.", p);
            if (op.Params == null || op.ForbiddenInputTags == null || op.AddedTags == null)
                throw new ArgumentException($"Operation '{op.Id}' has null lists.", p);
            foreach (var t in op.ForbiddenInputTags)
                if (!ItemCanonical.IsValidId(t)) throw new ArgumentException($"Operation '{op.Id}' has invalid tag '{t}'.", p);
            foreach (var t in op.AddedTags)
                if (!ItemCanonical.IsValidId(t)) throw new ArgumentException($"Operation '{op.Id}' has invalid tag '{t}'.", p);

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var param in op.Params)
            {
                if (param == null || !ItemCanonical.IsValidId(param.Name))
                    throw new ArgumentException($"Operation '{op.Id}' has an invalid param name.", p);
                if (!names.Add(param.Name)) throw new ArgumentException($"Operation '{op.Id}' has duplicate param '{param.Name}'.", p);
                if (!(param.Min <= param.Max) || float.IsInfinity(param.Min) || float.IsInfinity(param.Max))
                    throw new ArgumentException($"Param '{param.Name}' of '{op.Id}' has an invalid range.", p);
                if (param.Buckets == null || param.Buckets.Count == 0)
                    throw new ArgumentException($"Param '{param.Name}' of '{op.Id}' needs buckets.", p);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                float prev = float.NegativeInfinity;
                foreach (var b in param.Buckets)
                {
                    if (b == null || !ItemCanonical.IsValidId(b.Id) || b.Label == null || float.IsNaN(b.Value) || float.IsInfinity(b.Value))
                        throw new ArgumentException($"Param '{param.Name}' of '{op.Id}' has an invalid bucket.", p);
                    if (!ids.Add(b.Id)) throw new ArgumentException($"Param '{param.Name}' of '{op.Id}' has duplicate bucket '{b.Id}'.", p);
                    if (param.Kind == ParamKind.Number)
                    {
                        if (!(b.Value > prev)) throw new ArgumentException($"Buckets of '{param.Name}' must ascend.", p);
                        prev = b.Value;
                    }
                }
            }

            ParamDef? Find(string name) { foreach (var x in op.Params) if (x.Name == name) return x; return null; }
            if (op.Kind == OperationKind.Cut)
            {
                var shape = Find("shape");
                if (shape == null || shape.Kind != ParamKind.Choice)
                    throw new ArgumentException($"Cut operation '{op.Id}' needs a Choice param 'shape'.", p);
            }
            else if (op.Kind == OperationKind.Heat)
            {
                if (op.Heat == null) throw new ArgumentException($"Heat operation '{op.Id}' needs a HeatProfile.", p);
                var time = Find("time");
                if (time == null || time.Kind != ParamKind.Number)
                    throw new ArgumentException($"Heat operation '{op.Id}' needs a Number param 'time'.", p);
                var temp = Find("temp");
                bool hasTemp = temp != null && temp.Kind == ParamKind.Number;
                if (hasTemp == op.Heat.FixedTemperatureC.HasValue)
                    throw new ArgumentException($"Heat operation '{op.Id}' needs exactly one of a 'temp' param or a fixed temperature.", p);
            }
        }
    }
}
