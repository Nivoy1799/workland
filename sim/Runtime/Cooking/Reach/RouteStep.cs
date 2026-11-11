using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 056a: one step of a route chain: the operation and the bucket the menu asks for, per param name (as the game's
    /// <c>DishNeed</c>: fries are <c>cut shape=sticks</c>, then <c>deep_fry</c> with <c>time=medium</c>). Params left out of
    /// <see cref="Target"/> take the first value of the sweep that reaches the named buckets.
    /// </summary>
    public sealed record RouteStep(string OperationId, IReadOnlyDictionary<string, string> Target);
}
