using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 031: one layer of a recipe template: its role, the base ingredients that fill it, the heat ops it needs (null: no
    /// heat needed, raw or heated both fit and a raw one rates as done), whether it must be cut, and whether the dish needs it.
    /// Spec 032: a sauce layer's accepted amount buckets (null: only <see cref="Assembly.Right"/>, as in 031); an order with
    /// "extra" sauce accepts lots too (<see cref="OrderModifiers.ForOrder"/>). The default templates never set it, so every
    /// old score is unchanged.
    /// </summary>
    public sealed record TemplateLayer(LayerRole Role, IReadOnlyList<string> Bases, IReadOnlyList<string>? Ops, bool NeedsCut,
        bool Required, IReadOnlyList<string>? SauceAmounts = null);
}
