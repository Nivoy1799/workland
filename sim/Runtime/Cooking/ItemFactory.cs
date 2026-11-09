using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Worktown.Sim.Cooking
{
    /// <summary>Creates items. Invalid player intents return an error code; only programmer errors throw.</summary>
    public sealed class ItemFactory
    {
        private static readonly IReadOnlyDictionary<string, string> NoStrings =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

        private readonly ItemCatalog _items;
        private readonly OperationCatalog _ops;

        public ItemFactory(ItemCatalog items, OperationCatalog operations)
        {
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _ops = operations ?? throw new ArgumentNullException(nameof(operations));
        }

        public OperationResult CreateBase(string baseId)
        {
            if (!_items.TryGet(baseId, out var def)) return Fail(OperationError.UnknownBaseItem);
            var tags = new List<string>(def.Tags);
            tags.Sort(StringComparer.Ordinal);
            var distinct = DistinctSorted(tags);
            var comp = new ItemComponent(def.Id, def.Stats.Doneness, def.IdealDoneness);
            var item = new Item(
                ItemCanonical.BaseHash(def.Id), def.Id, def.Id, null, NoStrings, Array.AsReadOnly(Array.Empty<Item>()),
                def.Stats, 0f, Array.AsReadOnly(new[] { comp }), distinct, 1,
                null, def.DisplayName, def.DisplayName, NoStrings);
            return new OperationResult(item, OperationError.None);
        }

        public OperationResult Apply(string operationId, IReadOnlyList<Item> inputs,
                                     IReadOnlyDictionary<string, ParamValue> parameters)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            foreach (var input in inputs)
                if (input == null) throw new ArgumentException("Input items must not be null.", nameof(inputs));

            if (!_ops.TryGet(operationId, out var op)) return Fail(OperationError.UnknownOperation);

            // 1. arity (after mix flattening)
            var flat = new List<Item>();
            foreach (var input in inputs)
            {
                if (op.Kind == OperationKind.Mix && input.OperationId == op.Id) flat.AddRange(input.Inputs);
                else flat.Add(input);
            }
            if (flat.Count < op.MinInputs || flat.Count > op.MaxInputs) return Fail(OperationError.ArityViolation);

            // 2. forbidden tags
            foreach (var input in flat)
                foreach (var tag in input.Tags)
                    if (Contains(op.ForbiddenInputTags, tag)) return Fail(OperationError.ForbiddenInput);

            // 3. params
            if (!TryResolve(op, parameters, out var resolved)) return Fail(OperationError.InvalidParam);
            // Spec 030: a griddle op's "time" is the shorter side (Griddle.Params), so one item has one hash whichever side went first.
            if (op.Heat?.Grill != null && NumberOf(resolved, "time") > NumberOf(resolved, Griddle.LongSideParam))
                return Fail(OperationError.InvalidParam);

            // Spec 031: close and pack steps go in order (fold before paper or a bag; only bread folds; a box needs no close).
            string? stepChoice = null;
            if (op.Kind == OperationKind.Close || op.Kind == OperationKind.Pack)
            {
                foreach (var r in resolved) stepChoice = r.Bucket.Id;
                OperationError step = Assembly.CheckStep(op.Id, stepChoice, flat[0]);
                if (step != OperationError.None) return Fail(step);
            }

            // 4. canonical order
            if (op.Commutative) flat.Sort(ItemCanonical.CompareItems);

            // 5. transform (Cut may collapse to a no-op)
            ItemStats stats;
            float surface;
            List<ItemComponent> components;
            float factor;
            string template = op.NameTemplate;
            switch (op.Kind)
            {
                case OperationKind.Cut:
                {
                    float s = 0f;
                    foreach (var r in resolved) if (r.Name == "shape") s = r.Bucket.Value;
                    var src = flat[0];
                    if (s <= src.Surface) return new OperationResult(src, OperationError.None);
                    stats = src.Stats;
                    surface = s;
                    components = new List<ItemComponent>(src.Components);
                    factor = src.QualityFactor;
                    break;
                }
                case OperationKind.Portion:
                case OperationKind.Close:
                case OperationKind.Pack:
                {
                    // Spec 031: a squeeze, a close or a pack keeps what it is made of (only its buckets and tags change).
                    var src = flat[0];
                    stats = src.Stats;
                    surface = src.Surface;
                    components = new List<ItemComponent>(src.Components);
                    factor = src.QualityFactor;
                    break;
                }
                case OperationKind.Heat:
                    Heat(op, flat[0], resolved, out stats, out surface, out components, out factor);
                    // Spec 029: the basket shapes the input ("{drain} {time} fries", "... falafel balls").
                    if (op.Heat!.Fry != null && DeepFry.TryGetShape(flat[0], out DeepFryShape shaped))
                        template = "{" + DeepFry.DrainParam + "} {time} " + shaped.Name;
                    // Spec 030: the griddle names the item from its two sides' buckets ("grilled seitan strips").
                    if (op.Heat.Grill != null)
                        template = Griddle.NameTemplate(BucketOf(resolved, "time"), BucketOf(resolved, Griddle.LongSideParam),
                            op.Heat.Grill.Toasts, Griddle.TryGetShape(flat[0], out DeepFryShape cut) ? cut.Name : null);
                    break;
                default:
                    Combine(flat, out stats, out surface, out components, out factor);
                    break;
            }

            // 6. build and check limits
            int depth = 1;
            foreach (var input in flat) depth = Math.Max(depth, input.Depth + 1);
            if (depth > CookingLimits.MaxDepth) return Fail(OperationError.DepthLimit);
            if (components.Count > CookingLimits.MaxComponents) return Fail(OperationError.ComponentLimit);

            var buckets = new List<KeyValuePair<string, string>>();
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in resolved)
            {
                buckets.Add(new KeyValuePair<string, string>(r.Name, r.Bucket.Id));
                labels[r.Name] = r.Bucket.Label;
            }
            buckets.Sort((x, y) => StringComparer.Ordinal.Compare(x.Key, y.Key));
            var bucketMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in buckets) bucketMap[kv.Key] = kv.Value;

            var tags = new List<string>();
            foreach (var input in flat) tags.AddRange(input.Tags);
            tags.AddRange(op.AddedTags);
            if (op.Kind == OperationKind.Close && stepChoice == Assembly.Wrapped) tags.Add(Assembly.PlatedTag); // spec 031: finished
            tags.Sort(StringComparer.Ordinal);

            var item = new Item(
                ItemCanonical.OpHash(op.Id, buckets, flat), ItemCanonical.OpTree(op.Id, buckets, flat),
                null, op.Id, new ReadOnlyDictionary<string, string>(bucketMap), Array.AsReadOnly(flat.ToArray()),
                stats, surface, Array.AsReadOnly(components.ToArray()), DistinctSorted(tags), depth,
                op.Kind, string.Empty, template, new ReadOnlyDictionary<string, string>(labels), factor);
            return new OperationResult(item, OperationError.None);
        }

        private static float NumberOf(List<Resolved> resolved, string name)
        {
            foreach (var r in resolved) if (r.Name == name) return r.Number;
            return 0f;
        }

        private static string BucketOf(List<Resolved> resolved, string name)
        {
            foreach (var r in resolved) if (r.Name == name) return r.Bucket.Id;
            return string.Empty;
        }

        private sealed class Resolved
        {
            public Resolved(string name, BucketDef bucket, float number) { Name = name; Bucket = bucket; Number = number; }
            public string Name { get; }
            public BucketDef Bucket { get; }
            public float Number { get; }
        }

        private static bool TryResolve(OperationDef op, IReadOnlyDictionary<string, ParamValue> given, out List<Resolved> resolved)
        {
            resolved = new List<Resolved>();
            if (given.Count != op.Params.Count) return false;
            foreach (var def in op.Params)
            {
                if (!given.TryGetValue(def.Name, out var v) || v.Kind != def.Kind) return false;
                if (def.Kind == ParamKind.Number)
                {
                    float x = v.NumberValue;
                    if (!(x >= def.Min && x <= def.Max)) return false; // also rejects NaN; infinities fall outside
                    BucketDef chosen = def.Buckets[0];
                    foreach (var b in def.Buckets) if (b.Value <= x) chosen = b;
                    resolved.Add(new Resolved(def.Name, chosen, x));
                }
                else
                {
                    BucketDef? chosen = null;
                    foreach (var b in def.Buckets)
                        if (v.ChoiceId != null && string.Equals(b.Id, v.ChoiceId, StringComparison.Ordinal)) chosen = b;
                    if (chosen == null) return false;
                    resolved.Add(new Resolved(def.Name, chosen, 0f));
                }
            }
            return true;
        }

        private static void Heat(OperationDef op, Item src, List<Resolved> resolved,
                                 out ItemStats stats, out float surface, out List<ItemComponent> components, out float factor)
        {
            var profile = op.Heat!;
            float temp = profile.FixedTemperatureC ?? 0f;
            float time = 0f, longSide = 0f;
            bool shaken = false, greasy = false;
            foreach (var r in resolved)
            {
                if (r.Name == "temp") temp = r.Number;
                else if (r.Name == "time") time = r.Number;
                else if (r.Name == Griddle.LongSideParam) longSide = r.Number;
                else if (r.Name == DeepFry.ShakeParam) shaken = r.Bucket.Id == DeepFry.Shaken;
                else if (r.Name == DeepFry.DrainParam) greasy = r.Bucket.Id == DeepFry.Greasy;
            }

            var s = src.Stats;
            float g;
            components = new List<ItemComponent>(src.Components.Count);
            float doneSum = 0f;
            if (profile.Grill != null)
            {
                // Spec 030: each side along its curve (time = the shorter side, longSide the longer), combined per component; the
                // stat changes follow the mean of the two sides, saturating at the end of the window.
                g = 0f;
                foreach (var c in src.Components)
                {
                    FryCurve curve = profile.Grill.CurveFor(c.BaseId);
                    float a = Griddle.SideDoneness(curve, c.Ideal, c.Doneness, time);
                    float b = Griddle.SideDoneness(curve, c.Ideal, c.Doneness, longSide);
                    float d = Griddle.CombineSides(a, b, c.Ideal);
                    doneSum += d;
                    components.Add(new ItemComponent(c.BaseId, d, c.Ideal));
                    g += 0.5f * (Math.Min(1f, time / curve.WindowMaxAt) + Math.Min(1f, longSide / curve.WindowMaxAt));
                }
                g /= Math.Max(1, src.Components.Count);
                factor = src.QualityFactor;
            }
            else if (profile.Fry != null)
            {
                // Spec 029: each component along its fry curve; the stat changes saturate at the end of its window.
                float seconds = DeepFry.EffectiveSeconds(time, temp);
                g = 0f;
                foreach (var c in src.Components)
                {
                    FryCurve curve = profile.Fry.CurveFor(c.BaseId);
                    float d = DeepFry.Doneness(curve, c.Ideal, c.Doneness, seconds);
                    doneSum += d;
                    components.Add(new ItemComponent(c.BaseId, d, c.Ideal));
                    g += Math.Min(1f, seconds / curve.WindowMaxAt);
                }
                g /= Math.Max(1, src.Components.Count);
                factor = DeepFry.FactorAfter(src.QualityFactor, shaken, greasy); // capped: re-frying never stacks the bonus (029b)
            }
            else
            {
                float dose = Math.Max(0f, (temp - 60f) / 120f) * (time / 180f) * (0.5f + src.Surface);
                g = Math.Min(1f, dose);
                foreach (var c in src.Components)
                {
                    float d = ItemCanonical.Clamp01(c.Doneness + dose * (1f - 0.5f * s.HeatTolerance));
                    doneSum += d;
                    components.Add(new ItemComponent(c.BaseId, d, c.Ideal));
                }
                factor = src.QualityFactor;
            }
            components.Sort(ItemCanonical.CompareComponents);
            float doneness = ItemCanonical.Clamp01(doneSum / components.Count);

            float fat = ItemCanonical.Clamp01(s.Fat + profile.FatGain * g * (1f - s.Fat));
            float umami = ItemCanonical.Clamp01(s.Umami + profile.UmamiGain * g * (1f - s.Umami));
            float moisture = profile.MoistureDelta < 0f
                ? s.Moisture * (1f + profile.MoistureDelta * g)
                : s.Moisture + profile.MoistureDelta * g * (1f - s.Moisture);
            moisture = ItemCanonical.Clamp01(moisture);
            float crunch = profile.CrunchGain >= 0f
                ? s.Crunch + profile.CrunchGain * g * (1f - moisture) * (1f - s.Crunch)
                : s.Crunch * (1f + profile.CrunchGain * g);
            crunch = ItemCanonical.Clamp01(crunch);
            float keep = 1f - profile.Dilution * g;
            float sweet = ItemCanonical.Clamp01(s.Sweet * keep);
            float salty = ItemCanonical.Clamp01(s.Salty * keep);
            umami = ItemCanonical.Clamp01(umami * keep);
            float bitter = ItemCanonical.Clamp01(s.Bitter + 2f * Math.Max(0f, doneness - 0.9f));

            stats = new ItemStats(sweet, salty, s.Sour, bitter, umami, fat, crunch, s.HeatTolerance, doneness, moisture);
            surface = src.Surface;
        }

        private static void Combine(List<Item> ordered, out ItemStats stats, out float surface, out List<ItemComponent> components,
                                    out float factor)
        {
            float total = 0f;
            float sweet = 0, salty = 0, sour = 0, bitter = 0, umami = 0, fat = 0, crunch = 0, heat = 0, moist = 0, surf = 0;
            float quality = 0f;
            components = new List<ItemComponent>();
            foreach (var item in ordered)
            {
                float w = item.Weight;
                var s = item.Stats;
                total += w;
                quality += item.QualityFactor * w;
                sweet += s.Sweet * w; salty += s.Salty * w; sour += s.Sour * w; bitter += s.Bitter * w;
                umami += s.Umami * w; fat += s.Fat * w; crunch += s.Crunch * w; heat += s.HeatTolerance * w;
                moist += s.Moisture * w; surf += item.Surface * w;
                components.AddRange(item.Components);
            }
            components.Sort(ItemCanonical.CompareComponents);
            float doneSum = 0f;
            foreach (var c in components) doneSum += c.Doneness;

            stats = new ItemStats(
                ItemCanonical.Clamp01(sweet / total), ItemCanonical.Clamp01(salty / total),
                ItemCanonical.Clamp01(sour / total), ItemCanonical.Clamp01(bitter / total),
                ItemCanonical.Clamp01(umami / total), ItemCanonical.Clamp01(fat / total),
                ItemCanonical.Clamp01(crunch / total), ItemCanonical.Clamp01(heat / total),
                ItemCanonical.Clamp01(doneSum / components.Count), ItemCanonical.Clamp01(moist / total));
            surface = ItemCanonical.Clamp01(surf / total);
            factor = quality / total; // spec 029: exactly 1 when every input's is 1
        }

        private static IReadOnlyList<string> DistinctSorted(List<string> sorted)
        {
            var result = new List<string>();
            foreach (var t in sorted)
                if (result.Count == 0 || result[result.Count - 1] != t) result.Add(t);
            return Array.AsReadOnly(result.ToArray());
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            foreach (var x in list) if (x == value) return true;
            return false;
        }

        private static OperationResult Fail(OperationError error) => new OperationResult(null, error);
    }
}
