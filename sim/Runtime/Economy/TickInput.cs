using System.Collections.Generic;
using Worktown.Sim.Core;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// One economy tick's input. Spec 037: <paramref name="DemandPermille"/> scales each market's new NPC demand (market id to
    /// permille, 1000 = unchanged; a missing market or a null map is 1000): the menu board's prices (<see cref="MenuPricing"/>). It
    /// scales the hour's new demand before the hour's division, so a fraction of a unit carries over to the next hour (the permille's
    /// own rounding drops less than one carry step, 1 / (100 x the hour weights' sum) of a unit, per market and hour), and the draws
    /// stay the same.
    /// </summary>
    public sealed record TickInput(IRandom Random, int Population, IReadOnlyDictionary<string, int>? DemandPermille = null);
}
