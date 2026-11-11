using System;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: how far a rival has reacted (price cuts, quality steps) and its streaks: services in a row you beat its share
    /// (<see cref="AheadStreak"/>) and services in a row its share was at least twice yours (<see cref="BehindStreak"/>).
    /// </summary>
    public sealed record RivalState(string RivalId, int PriceCuts, int QualitySteps, int AheadStreak, int BehindStreak)
    {
        /// <summary>Services in a row that trigger a reaction or an undo.</summary>
        public const int StreakLength = 3;
        /// <summary>Quality added per quality step (permille).</summary>
        public const int QualityStep = 25;

        public static RivalState Start(string rivalId) => new RivalState(rivalId, 0, 0, 0, 0);

        /// <summary>The rival's quality now (base plus its steps, at most 1000).</summary>
        public int QualityOf(RivalDef def) => Math.Min(1000, def.QualityPermille + QualitySteps * QualityStep);

        /// <summary>The rival's price offset from the reference now, in money (its offset minus its cuts, one unit each).</summary>
        public long OffsetOf(RivalDef def, long unit) => def.PriceOffset - PriceCuts * unit;

        /// <summary>
        /// After one service on its channel: you beat its guests 3 times in a row, it cuts its price by one unit or, when cuts
        /// are used up, raises quality one step; its guests were at least twice yours 3 times in a row, it undoes one step
        /// (a quality step first). Bounded by the def's maximums, no draws.
        /// </summary>
        public RivalState React(RivalDef def, int yourGuests, int rivalGuests)
        {
            bool ahead = yourGuests > rivalGuests;
            bool behind = rivalGuests > 0 && rivalGuests >= 2 * yourGuests;
            int aheadStreak = ahead ? AheadStreak + 1 : 0;
            int behindStreak = behind ? BehindStreak + 1 : 0;
            int cuts = PriceCuts, steps = QualitySteps;
            if (aheadStreak >= StreakLength)
            {
                aheadStreak = 0;
                if (cuts < def.MaxPriceCuts) cuts++;
                else if (steps < def.MaxQualitySteps) steps++;
            }
            if (behindStreak >= StreakLength)
            {
                behindStreak = 0;
                if (steps > 0) steps--;
                else if (cuts > 0) cuts--;
            }
            return new RivalState(RivalId, cuts, steps, aheadStreak, behindStreak);
        }
    }
}
