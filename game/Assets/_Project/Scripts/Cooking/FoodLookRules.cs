using System.Collections.Generic;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 017: reads the coarse <see cref="FoodLook"/> off a sim item, server side. Only the
    /// ingredient index and bucket ids leave the server; stats, quality and times never do (D21). Pure.
    /// </summary>
    public static class FoodLookRules
    {
        private const string PlatedTag = "plated";
        private const string MixOp = "mix";
        private const string BoilOp = "boil";
        private const string DeepFryOp = DeepFry.OperationId;
        private const string ShapeParam = "shape";
        private const string TimeParam = "time";

        /// <summary>
        /// The look of <paramref name="item"/>. Walks down the chain of single-input ops: the last cut gives
        /// the shape, the last heat op's time bucket the doneness; a "plated" tag gives Kind plated, a mix op
        /// Kind mixed (their inputs are the parts, see <see cref="PartsOf"/>). A mix that was not cut as a
        /// whole takes the shape of its first part, so it never draws as a whole ingredient. Null gives an
        /// unknown look.
        /// </summary>
        public static FoodLook From(Item item)
        {
            FoodLook look = Walk(item, out Item node, out bool hasShape);
            if (node != null && look.Kind == FoodLook.KindMixed && !hasShape && node.Inputs.Count > 0)
                look.Shape = PartLook(node.Inputs[0], look).Shape;
            return look;
        }

        /// <summary>
        /// The looks of a plate's or mix's parts in the item's input order (canonical for these ops), at most
        /// <see cref="FoodLook.MaxParts"/>. Heat applied to the whole mix is carried onto each part when it is
        /// more done than the part itself, so a fried mix shows fried parts. Empty for a single food.
        /// </summary>
        public static IReadOnlyList<FoodLook> PartsOf(Item item)
        {
            FoodLook outer = Walk(item, out Item node, out _);
            if (node == null) return System.Array.Empty<FoodLook>();
            int count = System.Math.Min(node.Inputs.Count, FoodLook.MaxParts);
            var parts = new FoodLook[count];
            for (int i = 0; i < count; i++) parts[i] = PartLook(node.Inputs[i], outer);
            return parts;
        }

        /// <summary>Index into <see cref="DemoIngredients.All"/>, or 255 when the id is not a demo ingredient.</summary>
        public static byte IngredientIndex(string baseId)
        {
            if (baseId == null) return FoodLook.UnknownIngredient;
            for (int i = 0; i < DemoIngredients.All.Count && i < FoodLook.UnknownIngredient; i++)
                if (DemoIngredients.All[i].Id == baseId) return (byte)i;
            return FoodLook.UnknownIngredient;
        }

        /// <summary>Cut bucket id to shape: "halves" 1, "slices" 2, "sticks" 3, "dice" 4, else whole.</summary>
        public static byte ShapeOf(string bucketId)
        {
            switch (bucketId)
            {
                case "halves": return FoodLook.ShapeHalves;
                case "slices": return FoodLook.ShapeSlices;
                case "sticks": return FoodLook.ShapeSticks;
                case "dice": return FoodLook.ShapeCubes;
                default: return FoodLook.ShapeWhole;
            }
        }

        /// <summary>Heat time bucket id to doneness: "short" 1, "medium" 2, "long" 3, else raw.</summary>
        public static byte DonenessOf(string bucketId)
        {
            switch (bucketId)
            {
                case "short": return FoodLook.DonenessShort;
                case "medium": return FoodLook.DonenessMedium;
                case "long": return FoodLook.DonenessLong;
                default: return FoodLook.DonenessRaw;
            }
        }

        /// <summary>
        /// Spec 016/017: the look of food in a pan or pot while it cooks, with the doneness bucket reached so
        /// far (the same bucket the label shows). <paramref name="bucketId"/> empty means still raw.
        /// </summary>
        public static FoodLook Cooking(Item food, string operationId, string bucketId)
        {
            FoodLook look = From(food);
            byte doneness = DonenessOf(bucketId);
            // Spec 029: food in the fryer's basket is shaped at once (falafel mix shows as balls), raw until it browns.
            if (operationId == DeepFryOp) look.Heat = FoodLook.HeatDeepFried;
            if (doneness == FoodLook.DonenessRaw) return look;
            look.Doneness = doneness;
            look.Heat = HeatOf(operationId);
            return look;
        }

        /// <summary>The heat byte of a heat op: boiled, deep-fried (spec 029), else fried.</summary>
        public static byte HeatOf(string operationId)
        {
            if (operationId == BoilOp) return FoodLook.HeatBoiled;
            return operationId == DeepFryOp ? FoodLook.HeatDeepFried : FoodLook.HeatFried;
        }

        /// <summary>
        /// Spec 019: the look of food on a cutting board while it is being cut: the same look with the cut
        /// shape reached so far. Only the shape byte changes.
        /// </summary>
        public static FoodLook WithShape(FoodLook look, byte shape)
        {
            look.Shape = shape;
            return look;
        }

        private static byte IngredientOf(Item item)
        {
            string baseId = item.Components.Count > 0 ? item.Components[0].BaseId : item.BaseId;
            return IngredientIndex(baseId);
        }

        private static bool IsAssembly(Item item)
        {
            return item.OperationId == MixOp || IsPlateNode(item);
        }

        // The plate op is the one that adds the "plated" tag; its inputs never carry it (forbidden).
        private static bool IsPlateNode(Item item)
        {
            if (item.Inputs.Count == 0 || !HasTag(item, PlatedTag)) return false;
            foreach (Item input in item.Inputs)
                if (HasTag(input, PlatedTag)) return false;
            return true;
        }

        /// <summary>
        /// Walks the single-input chain from the top. Stops at the plate or mix node (returned in
        /// <paramref name="assemblyNode"/>, else null) and reports whether a cut was found above it.
        /// </summary>
        private static FoodLook Walk(Item item, out Item assemblyNode, out bool hasShape)
        {
            assemblyNode = null;
            hasShape = false;
            if (item == null) return FoodLook.Unknown;
            var look = new FoodLook { Ingredient = IngredientOf(item) };
            bool hasHeat = false;
            Item current = item;
            while (current != null && current.OperationId != null)
            {
                if (!hasShape && current.Buckets.TryGetValue(ShapeParam, out string shape))
                {
                    look.Shape = ShapeOf(shape);
                    hasShape = true;
                }
                if (!hasHeat && current.Buckets.TryGetValue(TimeParam, out string time))
                {
                    look.Doneness = DonenessOf(time);
                    look.Heat = HeatOf(current.OperationId);
                    hasHeat = true;
                }
                if (IsAssembly(current))
                {
                    look.Kind = IsPlateNode(current) ? FoodLook.KindPlated : FoodLook.KindMixed;
                    look.PartCount = (byte)System.Math.Min(current.Inputs.Count, FoodLook.MaxParts);
                    assemblyNode = current;
                    break;
                }
                current = current.Inputs.Count > 0 ? current.Inputs[0] : null;
            }
            return look;
        }

        /// <summary>A part's own look, with the outer heat when that is more done than the part.</summary>
        private static FoodLook PartLook(Item input, FoodLook outer)
        {
            FoodLook part = From(input);
            if (outer.Doneness > part.Doneness)
            {
                part.Doneness = outer.Doneness;
                part.Heat = outer.Heat;
            }
            return part;
        }

        private static bool HasTag(Item item, string tag)
        {
            foreach (string t in item.Tags)
                if (t == tag) return true;
            return false;
        }
    }
}
