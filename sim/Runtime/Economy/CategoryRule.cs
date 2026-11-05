using System.Collections.Generic;

namespace Worktown.Sim.Economy
{
    public sealed record CategoryRule(string MarketId, IReadOnlyList<string> RequiredTags,
        IReadOnlyList<string> ForbiddenTags, int MinWeight);
}
