using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 031: one layer of a recipe template: its role, the base ingredients that fill it, the heat ops it needs (null: no
    /// heat needed, raw or heated both fit and a raw one rates as done), whether it must be cut, and whether the dish needs it.
    /// </summary>
    public sealed record TemplateLayer(LayerRole Role, IReadOnlyList<string> Bases, IReadOnlyList<string>? Ops, bool NeedsCut,
        bool Required);
}
