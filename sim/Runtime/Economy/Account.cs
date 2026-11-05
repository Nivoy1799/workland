using System.Collections.Generic;

namespace Worktown.Sim.Economy
{
    /// <summary>Goods are sorted by GoodId (ordinal), Units &gt; 0.</summary>
    public sealed record Account(string Id, long Balance, IReadOnlyList<GoodAmount> Goods);
}
