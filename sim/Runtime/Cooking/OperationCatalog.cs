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
                _ops.Add(op.Id, Copy(op));
            }
        }

        private static IReadOnlyList<T> ReadOnlyCopy<T>(IReadOnlyList<T> list) => Array.AsReadOnly(new List<T>(list).ToArray());

        private static bool Fin(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        private static OperationDef Copy(OperationDef op)
        {
            var pars = new List<ParamDef>();
            foreach (var pd in op.Params) pars.Add(pd with { Buckets = ReadOnlyCopy(pd.Buckets) });
            return op with
            {
                Params = ReadOnlyCopy(pars),
                ForbiddenInputTags = ReadOnlyCopy(op.ForbiddenInputTags),
                AddedTags = ReadOnlyCopy(op.AddedTags),
            };
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
                DeepFryOperation(plated, none),
            });
        }

        /// <summary>
        /// Spec 029: the fryer's <c>deep_fry</c> heat op: oil temperature and time (sim seconds) like the pan, plus the server's
        /// "shake" (one shake in the good window: <see cref="DeepFry.ShakeBonus"/>) and "drain" (taken out before the basket
        /// drained: <see cref="DeepFry.GreasyPenalty"/>) choices. Doneness follows <see cref="DeepFry.DefaultProfile"/>.
        /// </summary>
        private static OperationDef DeepFryOperation(string[] plated, string[] none)
        {
            var oilTemp = new ParamDef("temp", ParamKind.Number, 150f, 190f, new[]
            {
                new BucketDef("medium", 150f, ""),
                new BucketDef("hot", 175f, ""),
            });
            var fryTime = new ParamDef("time", ParamKind.Number, 6f, 600f, new[]
            {
                new BucketDef("short", 6f, "pale"),
                new BucketDef("medium", 120f, "golden"),
                new BucketDef("long", 240f, "dark"),
            });
            var shake = new ParamDef(DeepFry.ShakeParam, ParamKind.Choice, 0f, 1f, new[]
            {
                new BucketDef(DeepFry.NotShaken, 0f, ""),
                new BucketDef(DeepFry.Shaken, 1f, ""),
            });
            var drain = new ParamDef(DeepFry.DrainParam, ParamKind.Choice, 0f, 1f, new[]
            {
                new BucketDef(DeepFry.Drained, 0f, ""),
                new BucketDef(DeepFry.Greasy, 1f, "greasy"),
            });
            return new OperationDef(DeepFry.OperationId, "fryer", OperationKind.Heat, false, 1, 1, new[] { oilTemp, fryTime, shake, drain },
                new HeatProfile(null, 0.45f, -0.6f, 0.7f, 0.15f, 0f, DeepFry.DefaultProfile), plated, none,
                "{" + DeepFry.DrainParam + "} {time} deep-fried {in}");
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
                if (param.Kind == ParamKind.Number && param.Buckets[0] != null && param.Buckets[0].Value != param.Min)
                    throw new ArgumentException($"First bucket of '{param.Name}' of '{op.Id}' must start at Min.", p);
                foreach (var b in param.Buckets)
                {
                    if (b == null || !ItemCanonical.IsValidId(b.Id) || b.Label == null || float.IsNaN(b.Value) || float.IsInfinity(b.Value))
                        throw new ArgumentException($"Param '{param.Name}' of '{op.Id}' has an invalid bucket.", p);
                    if (!ids.Add(b.Id)) throw new ArgumentException($"Param '{param.Name}' of '{op.Id}' has duplicate bucket '{b.Id}'.", p);
                    if (param.Kind == ParamKind.Number)
                    {
                        if (!(b.Value > prev)) throw new ArgumentException($"Buckets of '{param.Name}' must ascend.", p);
                        prev = b.Value;
                        if (!(b.Value <= param.Max)) throw new ArgumentException($"Bucket '{b.Id}' of '{param.Name}' exceeds Max.", p);
                    }
                    if (op.Kind == OperationKind.Cut && param.Name == "shape" && !(b.Value >= 0f && b.Value <= 1f))
                        throw new ArgumentException($"Cut shape bucket '{b.Id}' of '{op.Id}' must have a value in 0..1.", p);
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
                var h = op.Heat;
                if (!Fin(h.FatGain) || !Fin(h.MoistureDelta) || !Fin(h.CrunchGain)
                    || !Fin(h.UmamiGain) || !Fin(h.Dilution)
                    || (h.FixedTemperatureC.HasValue && !Fin(h.FixedTemperatureC.Value)))
                    throw new ArgumentException($"Heat profile of '{op.Id}' must be finite.", p);
                var time = Find("time");
                if (time == null || time.Kind != ParamKind.Number)
                    throw new ArgumentException($"Heat operation '{op.Id}' needs a Number param 'time'.", p);
                var temp = Find("temp");
                bool hasTemp = temp != null && temp.Kind == ParamKind.Number;
                if (hasTemp == op.Heat.FixedTemperatureC.HasValue)
                    throw new ArgumentException($"Heat operation '{op.Id}' needs exactly one of a 'temp' param or a fixed temperature.", p);
                if (h.Fry != null)
                {
                    // Spec 029: a deep fry op has the server's shake and drain choices and finite, ordered curves.
                    bool Choice(string name, string a, string b)
                    {
                        var pd = Find(name);
                        if (pd == null || pd.Kind != ParamKind.Choice || pd.Buckets.Count != 2) return false;
                        return pd.Buckets[0].Id == a && pd.Buckets[1].Id == b;
                    }
                    if (!Choice(DeepFry.ShakeParam, DeepFry.NotShaken, DeepFry.Shaken) || !Choice(DeepFry.DrainParam, DeepFry.Drained, DeepFry.Greasy))
                        throw new ArgumentException($"Deep fry operation '{op.Id}' needs the 'shake' and 'drain' choices.", p);
                    var curves = new List<FryCurve>(h.Fry.Curves) { h.Fry.Fallback };
                    foreach (var c in curves)
                        if (c == null || !(c.WindowMinAt > 0f && c.WindowMaxAt > c.WindowMinAt && c.BurntAt > c.WindowMaxAt)
                            || !Fin(c.BurntAt))
                            throw new ArgumentException($"Deep fry operation '{op.Id}' has an invalid fry curve.", p);
                }
            }
        }
    }
}
