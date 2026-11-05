using System.Collections.Generic;

namespace Worktown.Sim.Cooking
{
    /// <summary>Definition of a raw base item. Stats.Doneness is the starting doneness.</summary>
    public sealed record BaseItemDef(
        string Id, string DisplayName, ItemStats Stats, DonenessWindow IdealDoneness, IReadOnlyList<string> Tags);
}
