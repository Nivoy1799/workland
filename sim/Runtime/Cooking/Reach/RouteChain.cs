using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 056a: one ingredient's chain of steps. <see cref="BaseIds"/> are alternatives (each base runs the same steps: "any
    /// other ingredient" of a dish, or the salad candidates of a build). No steps: the base item itself (a raw wrap).
    /// </summary>
    public sealed record RouteChain(IReadOnlyList<string> BaseIds, IReadOnlyList<RouteStep> Steps);
}
