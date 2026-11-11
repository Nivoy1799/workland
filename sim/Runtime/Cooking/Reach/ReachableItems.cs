using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 056a: every item the menu routes can produce, for the offline name list (D38). Pure and deterministic; items are
    /// made only through the <see cref="ItemFactory"/>. The rules:
    /// <list type="number">
    /// <item>Chains: every prefix of every chain (the base item too), each step in every reachable bucket: Choice params take
    /// each bucket, Number params sweep <see cref="ReachLimits.SweepSteps"/> + 1 evenly spaced values from Min to Max plus each
    /// bucket threshold, across all param combinations of the op, over every item of the previous step; deduplicated by hash
    /// (the first item of a hash goes on). Wrong prep included: a burnt potato gets a name too.</item>
    /// <item>Combined dishes take each chain only in its target form (the first swept item whose buckets equal the step's
    /// target; none throws: the route is wrong). Single: each chain's target and its plate. Mix: one target per chain (each
    /// combination of distinct bases), mixed, the mix on a plate, and the parts on one plate.</item>
    /// <item>Builds: the template's layers in template order, each required layer once per fitting chain target, an optional
    /// layer none or each fitting target; every prefix (an open build) plus the template's close and pack; the finished build
    /// must score with nothing missing, wrong or short (else it throws). Order modifiers are not enumerated.</item>
    /// <item>Drinks: the empty cup, the drink, every fill bucket and a lid on each fill; the lidded right fill is the target.</item>
    /// </list>
    /// The result is sorted by hash; an item reached by several routes lists them all (ordinal). More than
    /// <see cref="ReachLimits.MaxItems"/> items throws.
    /// </summary>
    public static class ReachableItems
    {
        private static readonly IReadOnlyDictionary<string, ParamValue> NoParams = new Dictionary<string, ParamValue>();

        /// <summary>
        /// The reachable items of <paramref name="routes"/>; the operation params come from
        /// <see cref="OperationCatalog.CreateDefault"/> (the catalog the shipped factory uses).
        /// </summary>
        public static IReadOnlyList<ReachableItem> Enumerate(ItemFactory factory, IReadOnlyList<RouteDef> routes, ReachLimits limits) =>
            Enumerate(factory, OperationCatalog.CreateDefault(), routes, limits);

        /// <summary>The reachable items of <paramref name="routes"/>, the op params from <paramref name="operations"/> (the factory's).</summary>
        /// <exception cref="InvalidOperationException">A route is wrong (an unknown base or op, a target no sweep reaches, a
        /// finished build that does not fit its template) or the set exceeds <see cref="ReachLimits.MaxItems"/>.</exception>
        public static IReadOnlyList<ReachableItem> Enumerate(ItemFactory factory, OperationCatalog operations, IReadOnlyList<RouteDef> routes,
            ReachLimits limits)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (operations == null) throw new ArgumentNullException(nameof(operations));
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            if (limits == null) throw new ArgumentNullException(nameof(limits));
            if (limits.SweepSteps < 1) throw new ArgumentException("SweepSteps must be at least 1.", nameof(limits));
            if (limits.MaxItems < 1) throw new ArgumentException("MaxItems must be at least 1.", nameof(limits));

            var run = new Run(factory, operations, limits);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (RouteDef route in routes)
            {
                if (route == null || string.IsNullOrEmpty(route.Id) || route.Chains == null)
                    throw new ArgumentException("Routes need an id and chains.", nameof(routes));
                if (!ids.Add(route.Id)) throw new ArgumentException($"Duplicate route id '{route.Id}'.", nameof(routes));
                run.AddRoute(route);
            }
            return run.Result();
        }

        private sealed class Entry
        {
            public Entry(Item item) { Item = item; }
            public Item Item { get; }
            public SortedSet<string> Routes { get; } = new SortedSet<string>(StringComparer.Ordinal);
            public bool IsTarget { get; set; }
        }

        private sealed class Run
        {
            private readonly ItemFactory _factory;
            private readonly OperationCatalog _ops;
            private readonly ReachLimits _limits;
            private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            private readonly Dictionary<string, List<Item>> _variants = new Dictionary<string, List<Item>>(StringComparer.Ordinal);

            public Run(ItemFactory factory, OperationCatalog ops, ReachLimits limits)
            {
                _factory = factory;
                _ops = ops;
                _limits = limits;
            }

            public void AddRoute(RouteDef route)
            {
                // Rule 1: the chains, every prefix in every bucket; and each chain's target forms (one per base).
                var targets = new List<List<Item>>(route.Chains.Count);
                foreach (RouteChain chain in route.Chains)
                {
                    if (chain == null || chain.BaseIds == null || chain.BaseIds.Count == 0 || chain.Steps == null)
                        throw Wrong(route, "a chain needs bases and steps");
                    var chainTargets = new List<Item>(chain.BaseIds.Count);
                    foreach (string baseId in chain.BaseIds) chainTargets.Add(SweepChain(route, baseId, chain.Steps));
                    targets.Add(chainTargets);
                }

                switch (route.Kind)
                {
                    case RouteKind.Single:
                        foreach (List<Item> chainTargets in targets)
                            foreach (Item t in chainTargets)
                            {
                                Add(t, route, true);
                                Add(Apply(route, "plate", new[] { t }), route, true);
                            }
                        break;
                    case RouteKind.Drink:
                        Add(Base(route, Drinks.CupId), route, false);
                        foreach (List<Item> chainTargets in targets)
                            foreach (Item t in chainTargets) Add(t, route, true);
                        break;
                    case RouteKind.Mix:
                        Combine(route, targets, 0, new List<Item>(), new List<string>());
                        break;
                    case RouteKind.Build:
                        AddBuilds(route, targets);
                        break;
                    default:
                        throw Wrong(route, "unknown kind");
                }
            }

            public IReadOnlyList<ReachableItem> Result()
            {
                var hashes = new List<string>(_entries.Keys);
                hashes.Sort(StringComparer.Ordinal);
                var items = new List<ReachableItem>(hashes.Count);
                foreach (string hash in hashes)
                {
                    Entry e = _entries[hash];
                    Item item = e.Item;
                    string fallback = ItemNaming.FallbackName(item, NameCatalog.Empty);
                    items.Add(new ReachableItem(item.Hash, item.CanonicalTree, fallback, item.Tags,
                        Array.AsReadOnly(new List<string>(e.Routes).ToArray()), e.IsTarget, LabelWords(item, fallback)));
                }
                return items.AsReadOnly();
            }

            private Item SweepChain(RouteDef route, string baseId, IReadOnlyList<RouteStep> steps)
            {
                Item start = Base(route, baseId);
                Add(start, route, false);
                var frontier = new List<Item> { start };
                Item target = start;
                foreach (RouteStep step in steps)
                {
                    if (step == null || step.Target == null) throw Wrong(route, "a step needs a target");
                    OperationDef op = Op(route, step.OperationId);
                    var next = new List<Item>();
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (Item item in frontier)
                        foreach (Item v in Variants(op, item))
                            if (seen.Add(v.Hash)) next.Add(v);
                    foreach (Item v in next) Add(v, route, false);
                    frontier = next;
                    target = TargetOf(route, op, step.Target, target);
                }
                return target;
            }

            /// <summary>Rule 2: one target per chain, each combination of distinct bases: the mix, the mix plated, the parts plated.</summary>
            private void Combine(RouteDef route, List<List<Item>> targets, int index, List<Item> parts, List<string> bases)
            {
                if (index == targets.Count)
                {
                    if (parts.Count < 2) throw Wrong(route, "a mix needs two chains");
                    Item mixed = Apply(route, "mix", parts);
                    Add(mixed, route, true);
                    Add(Apply(route, "plate", new[] { mixed }), route, true);
                    Add(Apply(route, "plate", parts), route, true);
                    return;
                }
                foreach (Item t in targets[index])
                {
                    string baseId = BaseOf(t);
                    if (bases.Contains(baseId)) continue;
                    parts.Add(t);
                    bases.Add(baseId);
                    Combine(route, targets, index + 1, parts, bases);
                    parts.RemoveAt(parts.Count - 1);
                    bases.RemoveAt(bases.Count - 1);
                }
            }

            /// <summary>Rule 3: the template's layers in order (optional ones none or each fit), every prefix, the close and the pack.</summary>
            private void AddBuilds(RouteDef route, List<List<Item>> targets)
            {
                RecipeTemplate template = RecipeTemplates.ById(route.TemplateId) ?? throw Wrong(route, "unknown template");
                var pool = new List<Item>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (List<Item> chainTargets in targets)
                    foreach (Item t in chainTargets)
                        if (seen.Add(t.Hash)) pool.Add(t);

                var candidates = new List<List<Item>>(template.Layers.Count);
                foreach (TemplateLayer layer in template.Layers)
                {
                    var fits = new List<Item>();
                    foreach (string b in layer.Bases)
                        foreach (Item t in pool)
                            if (BaseOf(t) == b && FitsPrepared(layer, t)) fits.Add(t);
                    if (fits.Count == 0 && layer.Required) throw Wrong(route, "no chain fills a required layer");
                    candidates.Add(fits);
                }
                Layer(route, template, candidates, 0, new List<Item>());
            }

            private void Layer(RouteDef route, RecipeTemplate template, List<List<Item>> candidates, int index, List<Item> layers)
            {
                if (index == candidates.Count)
                {
                    Finish(route, template, layers);
                    return;
                }
                if (!template.Layers[index].Required) Layer(route, template, candidates, index + 1, layers);
                foreach (Item layer in candidates[index])
                {
                    layers.Add(layer);
                    Add(Apply(route, Assembly.AssembleOperationId, layers), route, false);
                    Layer(route, template, candidates, index + 1, layers);
                    layers.RemoveAt(layers.Count - 1);
                }
            }

            private void Finish(RouteDef route, RecipeTemplate template, List<Item> layers)
            {
                Item done = Apply(route, Assembly.AssembleOperationId, layers);
                if (template.Close != null)
                {
                    done = Apply(route, Assembly.CloseOperationId, new[] { done }, Assembly.CloseParams(Assembly.Folded));
                    if (template.Close == Assembly.Wrapped)
                    {
                        Add(done, route, false);
                        done = Apply(route, Assembly.CloseOperationId, new[] { done }, Assembly.CloseParams(Assembly.Wrapped));
                    }
                }
                if (template.Pack != null)
                {
                    if (template.Close != null) Add(done, route, false);
                    done = Apply(route, Assembly.PackOperationId, new[] { done }, Assembly.PackParams(template.Pack));
                }
                TemplateScore score = RecipeTemplates.Score(done, template);
                if (score.Missing != 0 || score.WrongPrep != 0 || score.CloseShort != 0 || score.PackShort != 0)
                    throw Wrong(route, "the finished build does not fit its template");
                Add(done, route, true);
            }

            private static bool FitsPrepared(TemplateLayer layer, Item item)
            {
                if (!Assembly.IsSingleChain(item)) return false;
                if (layer.NeedsCut && !Assembly.IsCut(item)) return false;
                if (layer.Ops != null && !Assembly.Contains(layer.Ops, Assembly.HeatOpOf(item))) return false;
                return layer.Ops == null || !DonenessZones.IsUndercooked(item);
            }

            /// <summary>Every item of <paramref name="op"/> on <paramref name="input"/> over the param sweep, first of each hash.</summary>
            private List<Item> Variants(OperationDef op, Item input)
            {
                string key = op.Id + "|" + input.Hash;
                if (_variants.TryGetValue(key, out List<Item>? known)) return known;
                var values = new List<List<ParamValue>>(op.Params.Count);
                foreach (ParamDef p in op.Params) values.Add(ValuesOf(p, _limits.SweepSteps));
                var found = new List<Item>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var chosen = new Dictionary<string, ParamValue>(StringComparer.Ordinal);
                Sweep(op, input, values, 0, chosen, found, seen);
                _variants[key] = found;
                return found;
            }

            private void Sweep(OperationDef op, Item input, List<List<ParamValue>> values, int index, Dictionary<string, ParamValue> chosen,
                List<Item> found, HashSet<string> seen)
            {
                if (index == values.Count)
                {
                    OperationResult r = _factory.Apply(op.Id, new[] { input }, chosen);
                    if (r.Ok && r.Item != null && seen.Add(r.Item.Hash)) found.Add(r.Item);
                    return;
                }
                string name = op.Params[index].Name;
                foreach (ParamValue v in values[index])
                {
                    chosen[name] = v;
                    Sweep(op, input, values, index + 1, chosen, found, seen);
                }
                chosen.Remove(name);
            }

            private static List<ParamValue> ValuesOf(ParamDef p, int steps)
            {
                var list = new List<ParamValue>();
                if (p.Kind == ParamKind.Choice)
                {
                    foreach (BucketDef b in p.Buckets) list.Add(ParamValue.Choice(b.Id));
                    return list;
                }
                var numbers = new SortedSet<float>();
                for (int i = 0; i <= steps; i++)
                    numbers.Add(i == steps ? p.Max : (float)(p.Min + (p.Max - (double)p.Min) * i / steps));
                foreach (BucketDef b in p.Buckets) numbers.Add(b.Value);
                foreach (float x in numbers) list.Add(ParamValue.Number(x));
                return list;
            }

            private Item TargetOf(RouteDef route, OperationDef op, IReadOnlyDictionary<string, string> target, Item input)
            {
                foreach (var kv in target)
                {
                    bool known = false;
                    foreach (ParamDef p in op.Params) if (p.Name == kv.Key) known = true;
                    if (!known) throw Wrong(route, $"op '{op.Id}' has no param '{kv.Key}'");
                }
                foreach (Item v in Variants(op, input))
                {
                    bool match = true;
                    foreach (var kv in target)
                        if (!v.Buckets.TryGetValue(kv.Key, out string? bucket) || bucket != kv.Value) { match = false; break; }
                    if (match) return v;
                }
                throw Wrong(route, $"op '{op.Id}' never reaches its target on '{input.CanonicalTree}'");
            }

            private Item Base(RouteDef route, string baseId)
            {
                OperationResult r = _factory.CreateBase(baseId);
                if (!r.Ok || r.Item == null) throw Wrong(route, $"unknown base item '{baseId}'");
                return r.Item;
            }

            private OperationDef Op(RouteDef route, string opId)
            {
                if (!_ops.TryGet(opId, out OperationDef op)) throw Wrong(route, $"unknown op '{opId}'");
                return op;
            }

            private Item Apply(RouteDef route, string opId, IReadOnlyList<Item> inputs, IReadOnlyDictionary<string, ParamValue>? parameters = null)
            {
                OperationResult r = _factory.Apply(opId, inputs, parameters ?? NoParams);
                if (!r.Ok || r.Item == null) throw Wrong(route, $"op '{opId}' failed with {r.Error}");
                return r.Item;
            }

            private void Add(Item item, RouteDef route, bool target)
            {
                if (!_entries.TryGetValue(item.Hash, out Entry? e))
                {
                    if (_entries.Count >= _limits.MaxItems)
                        throw new InvalidOperationException($"More than {_limits.MaxItems} reachable items (spec 056: raise ReachLimits.MaxItems or trim the routes).");
                    e = new Entry(item);
                    _entries.Add(item.Hash, e);
                }
                e.Routes.Add(route.Id);
                if (target) e.IsTarget = true;
            }

            private static string BaseOf(Item item) => Assembly.BaseOf(item) ?? string.Empty;

            private static InvalidOperationException Wrong(RouteDef route, string why) =>
                new InvalidOperationException($"Route '{route.Id}' is wrong: {why} (spec 056).");
        }

        /// <summary>The words of the item's own non-empty bucket labels that its plain name shows, by param name; words under three letters left out.</summary>
        private static IReadOnlyList<string> LabelWords(Item item, string fallbackName)
        {
            var names = new List<string>(item.BucketLabels.Keys);
            names.Sort(StringComparer.Ordinal);
            var nameWords = new HashSet<string>(fallbackName.ToLowerInvariant().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
            var words = new List<string>();
            foreach (string param in names)
                foreach (string word in item.BucketLabels[param].Split(' '))
                    if (word.Length >= 3 && nameWords.Contains(word.ToLowerInvariant()) && !words.Contains(word)) words.Add(word);
            return words.AsReadOnly();
        }
    }
}
