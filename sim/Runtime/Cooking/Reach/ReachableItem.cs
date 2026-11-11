using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>
    /// Spec 056a: one item the menus can produce. <see cref="FallbackName"/> is today's built-in name
    /// (<see cref="ItemNaming.FallbackName"/> with an empty catalog); <see cref="Routes"/> the route ids that reach it (ordinal);
    /// <see cref="IsTarget"/> true when it is a finished dish as a route asks for it; <see cref="LabelWords"/> the words of the
    /// item's own non-empty bucket labels ("crispy", "dark"; words under three letters such as "a" and "of" left out).
    /// </summary>
    public sealed record ReachableItem(string Hash, string CanonicalTree, string FallbackName, IReadOnlyList<string> Tags,
        IReadOnlyList<string> Routes, bool IsTarget, IReadOnlyList<string> LabelWords);
}
