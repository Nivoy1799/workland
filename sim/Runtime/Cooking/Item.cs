using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>Immutable result of the cooking process. Only ItemFactory creates items.</summary>
    public sealed class Item
    {
        internal Item(
            string hash, string canonicalTree, string? baseId, string? operationId,
            IReadOnlyDictionary<string, string> buckets, IReadOnlyList<Item> inputs,
            ItemStats stats, float surface, IReadOnlyList<ItemComponent> components,
            IReadOnlyList<string> tags, int depth,
            OperationKind? kind, string displayName, string nameTemplate,
            IReadOnlyDictionary<string, string> bucketLabels)
        {
            Hash = hash;
            CanonicalTree = canonicalTree;
            BaseId = baseId;
            OperationId = operationId;
            Buckets = buckets;
            Inputs = inputs;
            Stats = stats;
            Surface = surface;
            Components = components;
            Tags = tags;
            Depth = depth;
            Kind = kind;
            DisplayName = displayName;
            NameTemplate = nameTemplate;
            BucketLabels = bucketLabels;
        }

        /// <summary>Identity: 64 lowercase hex chars (SHA-256 of the canonical preimage).</summary>
        public string Hash { get; }
        public string CanonicalTree { get; }
        public string? BaseId { get; }
        public string? OperationId { get; }
        /// <summary>Param name to bucket id.</summary>
        public IReadOnlyDictionary<string, string> Buckets { get; }
        /// <summary>Inputs in canonical order.</summary>
        public IReadOnlyList<Item> Inputs { get; }
        /// <summary>Exact values, server-only.</summary>
        public ItemStats Stats { get; }
        /// <summary>0 whole .. 1 fine; set by Cut.</summary>
        public float Surface { get; }
        /// <summary>Sorted by (BaseId ordinal, Doneness).</summary>
        public IReadOnlyList<ItemComponent> Components { get; }
        /// <summary>Sorted distinct union of input tags and added tags.</summary>
        public IReadOnlyList<string> Tags { get; }
        /// <summary>Base = 1, operation = 1 + max input depth.</summary>
        public int Depth { get; }
        public int Weight => Components.Count;

        // Internal data so naming and rating need only the item itself.
        internal OperationKind? Kind { get; }
        internal string DisplayName { get; }
        internal string NameTemplate { get; }
        /// <summary>Param name to bucket label.</summary>
        internal IReadOnlyDictionary<string, string> BucketLabels { get; }
    }
}
