using System;
using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 031: the assembly board's sim rules, pure and deterministic. Four ops:
    /// <list type="bullet">
    /// <item><c>squeeze</c> (Portion): a measured portion of sauce, <see cref="AmountParam"/> = the seconds the dispenser was held
    /// (buckets little / right / lots, the right amount from <see cref="RightFrom"/> to <see cref="LotsFrom"/>);</item>
    /// <item><c>assemble</c> (Assemble): the build, its inputs the layers in the order they went on (not commutative);</item>
    /// <item><c>close</c> (Close): <see cref="Folded"/> (a bread base folded over the fillings) or <see cref="Wrapped"/> (a folded
    /// build in wrap paper; finished, sold as it is);</item>
    /// <item><c>pack</c> (Pack): into a <see cref="Box"/> or a <see cref="Bag"/> (finished). A build on bread is folded first; a box
    /// needs no close.</item>
    /// </list>
    /// The score of a build comes from <see cref="RecipeTemplates"/>.
    /// </summary>
    public static class Assembly
    {
        public const string SqueezeOperationId = "squeeze";
        public const string AssembleOperationId = "assemble";
        public const string CloseOperationId = "close";
        public const string PackOperationId = "pack";
        public const string Station = "assembly";

        public const string AmountParam = "amount";
        public const string StyleParam = "style";
        public const string ContainerParam = "container";

        /// <summary>Squeeze buckets (ids).</summary>
        public const string Little = "little";
        public const string Right = "right";
        public const string Lots = "lots";
        /// <summary>The sauce window (spec 031 default to review: hold 0.5–1.5 s for the right amount).</summary>
        public const float RightFrom = 0.5f;
        public const float LotsFrom = 1.5f;
        /// <summary>The longest squeeze (s); the server stops a squeeze there.</summary>
        public const float MaxSqueezeSeconds = 6f;

        public const string Folded = "folded";
        public const string Wrapped = "wrapped";
        public const string Box = "box";
        public const string Bag = "bag";

        /// <summary>Tags: a build (assemble), closed (close), packed (pack). Wrapped and packed builds are also "plated".</summary>
        public const string BuildTag = "build";
        public const string ClosedTag = "closed";
        public const string PackedTag = "packed";
        public const string PlatedTag = "plated";

        /// <summary>The bread a build can be folded on.</summary>
        public static readonly IReadOnlyList<string> Breads = Array.AsReadOnly(new[] { "flatbread", "wrap" });
        /// <summary>The sauces of the dispensers.</summary>
        public static readonly IReadOnlyList<string> Sauces = Array.AsReadOnly(new[] { "garlic_herb_sauce", "chili_sauce" });

        /// <summary>The squeeze parameters for <paramref name="seconds"/> held (clamped to 0..<see cref="MaxSqueezeSeconds"/>).</summary>
        public static IReadOnlyDictionary<string, ParamValue> SqueezeParams(float seconds)
        {
            float s = float.IsNaN(seconds) || seconds < 0f ? 0f : Math.Min(seconds, MaxSqueezeSeconds);
            return new Dictionary<string, ParamValue> { [AmountParam] = ParamValue.Number(s) };
        }

        public static IReadOnlyDictionary<string, ParamValue> CloseParams(string style) =>
            new Dictionary<string, ParamValue> { [StyleParam] = ParamValue.Choice(style) };

        public static IReadOnlyDictionary<string, ParamValue> PackParams(string container) =>
            new Dictionary<string, ParamValue> { [ContainerParam] = ParamValue.Choice(container) };

        /// <summary>A build: an assemble, close or pack item (also on a single-item plate: the counter plates loose food).</summary>
        public static bool IsBuild(Item? item) => BuildNode(item) != null;

        /// <summary>The assemble node of a build (looking through close, pack and a one-item plate), or null.</summary>
        public static Item? BuildNode(Item? item)
        {
            Item? current = item;
            while (current != null)
            {
                switch (current.Kind)
                {
                    case OperationKind.Assemble: return current;
                    case OperationKind.Close:
                    case OperationKind.Pack:
                        current = current.Inputs.Count == 1 ? current.Inputs[0] : null;
                        break;
                    case OperationKind.Plate:
                        current = current.Inputs.Count == 1 ? current.Inputs[0] : null;
                        break;
                    default: return null;
                }
            }
            return null;
        }

        /// <summary>The layers of a build in the order they went on; empty for anything else.</summary>
        public static IReadOnlyList<Item> LayersOf(Item? item)
        {
            Item? node = BuildNode(item);
            return node != null ? node.Inputs : Array.Empty<Item>();
        }

        /// <summary>How the build is closed: <see cref="Wrapped"/>, <see cref="Folded"/> or null (open, or no build).</summary>
        public static string? CloseOf(Item? item)
        {
            string? found = null;
            for (Item? c = item; c != null && c.Kind != OperationKind.Assemble; c = c.Inputs.Count == 1 ? c.Inputs[0] : null)
            {
                if (c.Kind != OperationKind.Close || !c.Buckets.TryGetValue(StyleParam, out string? style)) continue;
                if (style == Wrapped) return Wrapped;
                found ??= style;
            }
            return found;
        }

        /// <summary>What the build is packed in: <see cref="Box"/>, <see cref="Bag"/> or null.</summary>
        public static string? PackOf(Item? item)
        {
            for (Item? c = item; c != null && c.Kind != OperationKind.Assemble; c = c.Inputs.Count == 1 ? c.Inputs[0] : null)
                if (c.Kind == OperationKind.Pack && c.Buckets.TryGetValue(ContainerParam, out string? container)) return container;
            return null;
        }

        /// <summary>The base ingredient a layer is made of (the bottom of its chain; a mix's first part).</summary>
        public static string? BaseOf(Item layer)
        {
            if (layer == null) return null;
            if (layer.BaseId != null) return layer.BaseId;
            return layer.Components.Count > 0 ? layer.Components[0].BaseId : null;
        }

        /// <summary>True when the build's first layer is bread (it can be folded).</summary>
        public static bool HasBreadBase(Item? item)
        {
            IReadOnlyList<Item> layers = LayersOf(item);
            return layers.Count > 0 && Contains(Breads, BaseOf(layers[0]));
        }

        /// <summary>True for a sauce layer (a squeeze, or a sauce put on whole).</summary>
        public static bool IsSauce(Item layer) => Contains(Sauces, BaseOf(layer));

        /// <summary>
        /// The amount bucket of a sauce layer: a squeeze's bucket; a sauce put on whole (a bottle) is <see cref="Lots"/>.
        /// </summary>
        public static string SauceAmountOf(Item layer)
        {
            if (layer.Kind == OperationKind.Portion && layer.Buckets.TryGetValue(AmountParam, out string? amount)) return amount;
            return Lots;
        }

        /// <summary>A heat op somewhere in the layer's chain (the first met from the top), or null.</summary>
        public static string? HeatOpOf(Item layer)
        {
            for (Item? c = layer; c != null; c = c.Inputs.Count > 0 ? c.Inputs[0] : null)
                if (c.Kind == OperationKind.Heat) return c.OperationId;
            return null;
        }

        /// <summary>True when the layer (or what it was made from) was cut.</summary>
        public static bool IsCut(Item layer)
        {
            for (Item? c = layer; c != null; c = c.Inputs.Count > 0 ? c.Inputs[0] : null)
                if (c.Kind == OperationKind.Cut) return true;
            return false;
        }

        /// <summary>
        /// The close or pack step <paramref name="opId"/> with <paramref name="choice"/> on <paramref name="input"/> is in order:
        /// fold an open build on bread; wrap in paper a folded one; pack a folded build on bread, or an open build without bread.
        /// <see cref="OperationError.StepOrder"/> otherwise (also when the input is no build).
        /// </summary>
        public static OperationError CheckStep(string opId, string? choice, Item input)
        {
            if (input == null || !IsBuild(input) || input.Kind == OperationKind.Plate) return OperationError.StepOrder;
            bool open = input.Kind == OperationKind.Assemble;
            bool folded = input.Kind == OperationKind.Close && input.Buckets.TryGetValue(StyleParam, out string? s) && s == Folded;
            bool bread = HasBreadBase(input);
            if (opId == CloseOperationId)
            {
                if (choice == Folded) return open && bread ? OperationError.None : OperationError.StepOrder;
                if (choice == Wrapped) return folded ? OperationError.None : OperationError.StepOrder;
                return OperationError.StepOrder;
            }
            if (opId == PackOperationId) return (bread ? folded : open) ? OperationError.None : OperationError.StepOrder;
            return OperationError.None;
        }

        /// <summary>
        /// The display name of a build: an open build that has every layer of a recipe template is that dish ("falafel wrap"),
        /// else its first layer "with" the rest; "folded …", "… in paper", "… in a box" ("veggie box" stays "veggie box"),
        /// "… in a bag". From the tree only.
        /// </summary>
        public static string NameOf(Item item, NameCatalog catalog)
        {
            switch (item.Kind)
            {
                case OperationKind.Assemble: return OpenName(item, catalog);
                case OperationKind.Close:
                    string inner = ItemNaming.FallbackName(item.Inputs[0], catalog);
                    if (item.Buckets.TryGetValue(StyleParam, out string? style) && style == Wrapped)
                        return OpenName(BuildNode(item)!, catalog) + " in paper";
                    return "folded " + inner;
                case OperationKind.Pack:
                    string dish = OpenName(BuildNode(item)!, catalog);
                    if (item.Buckets.TryGetValue(ContainerParam, out string? container) && container == Box)
                        return dish.EndsWith(" box", StringComparison.Ordinal) ? dish : dish + " in a box";
                    return dish + " in a bag";
                default: return ItemNaming.FallbackName(item, catalog);
            }
        }

        private static string OpenName(Item build, NameCatalog catalog)
        {
            RecipeTemplate? dish = RecipeTemplates.Named(build);
            if (dish != null) return dish.Name;
            IReadOnlyList<Item> layers = build.Inputs;
            string first = Name(layers[0], catalog);
            if (layers.Count == 1) return first;
            var rest = new List<string>(layers.Count - 1);
            for (int i = 1; i < layers.Count; i++) rest.Add(Name(layers[i], catalog));
            string joined = rest.Count == 1 ? rest[0] : string.Join(", ", rest.GetRange(0, rest.Count - 1)) + " and " + rest[rest.Count - 1];
            return first + " with " + joined;
        }

        private static string Name(Item item, NameCatalog catalog) =>
            catalog.TryGetName(item.Hash, out string fixedName) ? fixedName : ItemNaming.FallbackName(item, catalog);

        internal static bool Contains(IReadOnlyList<string> list, string? value)
        {
            if (value == null) return false;
            foreach (string s in list) if (string.Equals(s, value, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
