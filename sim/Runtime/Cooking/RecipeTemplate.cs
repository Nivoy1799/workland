using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 031: a dish built on the assembly board: its layers in build order, how it is closed (<see cref="Assembly.Folded"/>,
    /// <see cref="Assembly.Wrapped"/> or null) and packed (<see cref="Assembly.Box"/>, <see cref="Assembly.Bag"/> or null).
    /// </summary>
    public sealed record RecipeTemplate(string Id, string Name, IReadOnlyList<TemplateLayer> Layers, string? Close, string? Pack);
}
