using System;
using System.Collections.Generic;
using Worktown.Sim.Core;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 032: the modifiers a guest may ask for on a recipe template, their seeded draw, their packing into one byte for the
    /// network, the order's own template and the check of a build against an order. Pure and deterministic.
    /// <para>Allowed list (<see cref="Allowed"/>), only from the recipe: "no X" for a required salad or sauce layer, "extra X" for a
    /// required sauce or filling layer; in layer order, "no" before "extra" on the same layer. The bread base never changes.</para>
    /// <para>Draw (defaults to review): 50 % none, 30 % one, 20 % two (<see cref="MaxPerOrder"/>); never "no X" and "extra X" on the
    /// same layer.</para>
    /// <para>Check: "no X" is kept when no layer of the build contains X; "extra" sauce when that sauce lies on twice or once as
    /// lots; "extra" filling when two layers of it are prepared right. The template part is scored against
    /// <see cref="ForOrder"/>: the removed layer is not required (not missing), an extra sauce layer accepts lots.</para>
    /// </summary>
    public static class OrderModifiers
    {
        /// <summary>At most this many modifiers per order at the start of the game (spec 032 default).</summary>
        public const int MaxPerOrder = 2;
        /// <summary>Draw shares in percent: none below <see cref="NoneBelow"/>, one below <see cref="OneBelow"/>, else two.</summary>
        public const int NoneBelow = 50;
        public const int OneBelow = 80;
        /// <summary>A packed byte holds two modifiers of 4 bits each (allowed index + 1; 0 = none), so at most 15 per recipe.</summary>
        public const int MaxAllowed = 15;

        /// <summary>The modifiers <paramref name="template"/> allows, in a fixed order (empty for null).</summary>
        public static IReadOnlyList<OrderModifier> Allowed(RecipeTemplate? template)
        {
            var list = new List<OrderModifier>();
            if (template == null) return list;
            for (int i = 0; i < template.Layers.Count && list.Count < MaxAllowed; i++)
            {
                TemplateLayer l = template.Layers[i];
                if (!l.Required) continue;
                string word = WordOf(l);
                if (l.Role == LayerRole.Salad || l.Role == LayerRole.Sauce) list.Add(new OrderModifier(ModifierKind.Remove, i, word));
                if ((l.Role == LayerRole.Sauce || l.Role == LayerRole.Filling) && list.Count < MaxAllowed)
                    list.Add(new OrderModifier(ModifierKind.Extra, i, word));
            }
            return list;
        }

        /// <summary>
        /// A guest's modifiers for <paramref name="template"/>, drawn with the injected seeded RNG: one draw for the count, then one per
        /// modifier among the allowed ones that do not clash with one already taken. Empty (and no draw) for a dish without a template.
        /// In allowed order.
        /// </summary>
        public static IReadOnlyList<OrderModifier> Draw(IRandom random, RecipeTemplate? template)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            IReadOnlyList<OrderModifier> allowed = Allowed(template);
            var taken = new List<int>();
            if (allowed.Count == 0) return Array.Empty<OrderModifier>();
            int roll = random.NextInt(0, 100);
            int count = roll < NoneBelow ? 0 : roll < OneBelow ? 1 : MaxPerOrder;
            var candidates = new List<int>();
            for (int k = 0; k < count; k++)
            {
                candidates.Clear();
                for (int i = 0; i < allowed.Count; i++)
                    if (!taken.Contains(i) && !Clashes(allowed, taken, i)) candidates.Add(i);
                if (candidates.Count == 0) break;
                taken.Add(candidates[random.NextInt(0, candidates.Count)]);
            }
            taken.Sort();
            var result = new OrderModifier[taken.Count];
            for (int i = 0; i < taken.Count; i++) result[i] = allowed[taken[i]];
            return result;
        }

        private static bool Clashes(IReadOnlyList<OrderModifier> allowed, List<int> taken, int candidate)
        {
            foreach (int t in taken)
                if (allowed[t].LayerIndex == allowed[candidate].LayerIndex) return true;
            return false;
        }

        /// <summary>The modifiers as one byte: two 4-bit allowed indices + 1 (0 = none), the first in the low bits. Unknown ones are dropped.</summary>
        public static byte Pack(RecipeTemplate? template, IReadOnlyList<OrderModifier>? modifiers)
        {
            if (template == null || modifiers == null) return 0;
            IReadOnlyList<OrderModifier> allowed = Allowed(template);
            int packed = 0, shift = 0;
            foreach (OrderModifier m in modifiers)
            {
                if (shift > 4) break;
                int index = IndexOf(allowed, m);
                if (index < 0) continue;
                packed |= (index + 1) << shift;
                shift += 4;
            }
            return (byte)packed;
        }

        /// <summary>The modifiers of a packed byte for <paramref name="template"/>; entries outside the allowed list are dropped.</summary>
        public static IReadOnlyList<OrderModifier> Unpack(RecipeTemplate? template, byte packed)
        {
            if (template == null || packed == 0) return Array.Empty<OrderModifier>();
            IReadOnlyList<OrderModifier> allowed = Allowed(template);
            var list = new List<OrderModifier>(MaxPerOrder);
            for (int shift = 0; shift <= 4; shift += 4)
            {
                int index = ((packed >> shift) & 0xF) - 1;
                if (index >= 0 && index < allowed.Count) list.Add(allowed[index]);
            }
            return list;
        }

        /// <summary>"extra garlic, no lettuce" (empty for none).</summary>
        public static string Describe(IReadOnlyList<OrderModifier> modifiers)
        {
            if (modifiers == null || modifiers.Count == 0) return string.Empty;
            var parts = new string[modifiers.Count];
            for (int i = 0; i < parts.Length; i++) parts[i] = modifiers[i].Text;
            return string.Join(", ", parts);
        }

        /// <summary>
        /// The template the order asks for: <paramref name="template"/> with each removed layer no longer required and each "extra"
        /// sauce layer accepting lots. Without modifiers the template itself (so a plain order scores exactly as in 031).
        /// </summary>
        public static RecipeTemplate ForOrder(RecipeTemplate template, IReadOnlyList<OrderModifier>? modifiers)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (modifiers == null || modifiers.Count == 0) return template;
            var layers = new List<TemplateLayer>(template.Layers.Count);
            for (int i = 0; i < template.Layers.Count; i++)
            {
                TemplateLayer l = template.Layers[i];
                if (Has(modifiers, ModifierKind.Remove, i)) continue; // not asked for: neither required nor claimed
                if (l.Role == LayerRole.Sauce && Has(modifiers, ModifierKind.Extra, i))
                    l = l with { SauceAmounts = Array.AsReadOnly(new[] { Assembly.Right, Assembly.Lots }) };
                layers.Add(l);
            }
            return template with { Layers = layers.AsReadOnly() };
        }

        /// <summary>The build against the order: the score on <see cref="ForOrder"/> and the modifiers it does not keep.</summary>
        public static ModifierCheck Check(Item build, RecipeTemplate template, IReadOnlyList<OrderModifier>? modifiers)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (template == null) throw new ArgumentNullException(nameof(template));
            TemplateScore score = RecipeTemplates.Score(build, ForOrder(template, modifiers));
            int mistakes = 0;
            if (modifiers != null)
                foreach (OrderModifier m in modifiers)
                    if (!Kept(build, template, m)) mistakes++;
            return new ModifierCheck(score, mistakes);
        }

        /// <summary>Whether the build keeps modifier <paramref name="m"/> of <paramref name="template"/>.</summary>
        public static bool Kept(Item build, RecipeTemplate template, OrderModifier m)
        {
            if (m.LayerIndex < 0 || m.LayerIndex >= template.Layers.Count) return true;
            TemplateLayer l = template.Layers[m.LayerIndex];
            IReadOnlyList<Item> layers = Assembly.LayersOf(build);
            if (m.Kind == ModifierKind.Remove)
            {
                foreach (Item layer in layers)
                    foreach (ItemComponent c in layer.Components)
                        if (Assembly.Contains(l.Bases, c.BaseId)) return false;
                return true;
            }
            int count = 0;
            foreach (Item layer in layers)
            {
                if (!Assembly.IsSingleChain(layer) || !Assembly.Contains(l.Bases, Assembly.BaseOf(layer))) continue;
                if (l.Role == LayerRole.Sauce)
                {
                    if (Assembly.SauceAmountOf(layer) == Assembly.Lots) return true;
                    count++;
                    continue;
                }
                if (l.NeedsCut && !Assembly.IsCut(layer)) continue;
                if (l.Ops != null && !Assembly.Contains(l.Ops, Assembly.HeatOpOf(layer))) continue;
                count++;
            }
            return count >= 2;
        }

        /// <summary>Spec 032 (patience): the layers the order needs: the template's required ones, less each "no", plus each "extra".</summary>
        public static int LayerCount(RecipeTemplate template, IReadOnlyList<OrderModifier>? modifiers)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            int n = 0;
            foreach (TemplateLayer l in template.Layers)
                if (l.Required) n++;
            if (modifiers != null)
                foreach (OrderModifier m in modifiers)
                    n += m.Kind == ModifierKind.Remove ? -1 : 1;
            return Math.Max(1, n);
        }

        /// <summary>The short word for a layer: its single ingredient ("lettuce", "garlic"), else its role ("salad", "sauce", "veggies").</summary>
        public static string WordOf(TemplateLayer layer)
        {
            if (layer.Bases.Count == 1) return WordOf(layer.Bases[0]);
            switch (layer.Role)
            {
                case LayerRole.Salad: return "salad";
                case LayerRole.Sauce: return "sauce";
                case LayerRole.Filling: return "veggies";
                default: return "bread";
            }
        }

        private static string WordOf(string baseId)
        {
            switch (baseId)
            {
                case "garlic_herb_sauce": return "garlic";
                case "chili_sauce": return "chili";
                case "falafel_mix": return "falafel";
                case "red_cabbage": return "cabbage";
                case "potato": return "fries";
                default: return baseId.Replace('_', ' ');
            }
        }

        private static bool Has(IReadOnlyList<OrderModifier> modifiers, ModifierKind kind, int layer)
        {
            foreach (OrderModifier m in modifiers)
                if (m.Kind == kind && m.LayerIndex == layer) return true;
            return false;
        }

        private static int IndexOf(IReadOnlyList<OrderModifier> allowed, OrderModifier m)
        {
            for (int i = 0; i < allowed.Count; i++)
                if (allowed[i].Kind == m.Kind && allowed[i].LayerIndex == m.LayerIndex) return i;
            return -1;
        }
    }
}
