using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: what one service did. <see cref="BigJobId"/> is set when a big job replaced the service. Per channel (id
    /// order) <see cref="YourGuests"/> and <see cref="RivalGuests"/> (-1 when the channel has no rival). <see cref="Revenue"/>
    /// includes a big job's pay or prize, <see cref="JobCost"/> is its deposit. <see cref="GoalCosts"/> are the costs
    /// (ascending) of what money could be saved for at the end of the service: unowned purchases of the stage and ready big jobs.
    /// </summary>
    public sealed record ServiceResult(
        int Index,
        ClimbStage Stage,
        string? BigJobId,
        long StartSeconds,
        int Seconds,
        int Chosen,
        int Served,
        int Missed,
        int Mistakes,
        long Revenue,
        long IngredientCost,
        long Upkeep,
        long JobCost,
        long Spent,
        long BalanceAfter,
        int AvgQuality,
        int WaitSeconds,
        int StarsTenths,
        int Reputation,
        IReadOnlyList<string> ChannelIds,
        IReadOnlyList<int> YourGuests,
        IReadOnlyList<int> RivalGuests,
        IReadOnlyList<string> Bought,
        IReadOnlyList<long> GoalCosts)
    {
        /// <summary>Net income of the service: revenue minus ingredients, upkeep and a big job deposit (purchases not counted).</summary>
        public long Net => Revenue - IngredientCost - Upkeep - JobCost;

        /// <summary>One invariant text line with every field, the unit of the run digest.</summary>
        public string Canonical()
        {
            var sb = new StringBuilder(160);
            CultureInfo c = CultureInfo.InvariantCulture;
            sb.Append(Index.ToString(c)).Append(' ').Append(((int)Stage).ToString(c)).Append(' ').Append(BigJobId ?? "-")
                .Append(' ').Append(StartSeconds.ToString(c)).Append(' ').Append(Seconds.ToString(c))
                .Append(" g=").Append(Chosen.ToString(c)).Append('/').Append(Served.ToString(c)).Append('/')
                .Append(Missed.ToString(c)).Append('/').Append(Mistakes.ToString(c))
                .Append(" $=").Append(Revenue.ToString(c)).Append('/').Append(IngredientCost.ToString(c)).Append('/')
                .Append(Upkeep.ToString(c)).Append('/').Append(JobCost.ToString(c)).Append('/').Append(Spent.ToString(c))
                .Append('/').Append(BalanceAfter.ToString(c))
                .Append(" q=").Append(AvgQuality.ToString(c)).Append(" w=").Append(WaitSeconds.ToString(c))
                .Append(" s=").Append(StarsTenths.ToString(c)).Append(" r=").Append(Reputation.ToString(c)).Append(" ch=");
            for (int i = 0; i < ChannelIds.Count; i++)
                sb.Append(i == 0 ? "" : ",").Append(ChannelIds[i]).Append(':').Append(YourGuests[i].ToString(c)).Append(':')
                    .Append(RivalGuests[i].ToString(c));
            sb.Append(" b=").Append(string.Join(",", Bought)).Append(" goals=");
            for (int i = 0; i < GoalCosts.Count; i++) sb.Append(i == 0 ? "" : ",").Append(GoalCosts[i].ToString(c));
            return sb.ToString();
        }
    }
}
