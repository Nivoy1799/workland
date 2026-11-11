using System;
using System.Collections.Generic;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: the crew's climb between two services. Time is <see cref="PlaySeconds"/> (the injected clock, advanced only by
    /// the engine). <see cref="StageEnteredSeconds"/> is indexed by stage (1..5), -1 while not entered.
    /// <see cref="Reputation"/> is the earned reputation; owned bonuses are added on top when a guest chooses.
    /// <see cref="LastQuality"/>, <see cref="LastWait"/>, <see cref="LastChosen"/> and <see cref="LastDemandActions"/> are
    /// from the last normal service. <see cref="RecentStars"/> holds the stars (tenths) of this stage's services, newest last.
    /// <see cref="JobReadyAt"/> is the first service index a big job may run again.
    /// Spec 052b: <see cref="Balance"/> is reference (4-player) money, the crew's progress (D40, see <see cref="CrewScaling"/>).
    /// <see cref="Owned"/> may hold an id several times (copies). <see cref="ActiveMenu"/> is the dish ids guests may order
    /// (buying order; the stage 1 dish first), <see cref="JobTier"/> the stake tier per big job, <see cref="RecentMissed"/> the
    /// missed share (permille) of the last normal services, newest last (cleared on a menu change and a new stage), <see cref="LastNet"/>
    /// the real net of the last normal service (the bar a big job must clear, 052b review).
    /// </summary>
    public sealed record ClimbState(
        int ServiceIndex,
        long PlaySeconds,
        ClimbStage Stage,
        long Balance,
        int Reputation,
        int LastQuality,
        int LastWait,
        int LastChosen,
        int LastDemandActions,
        IReadOnlyList<string> Owned,
        IReadOnlyDictionary<string, long> Prices,
        IReadOnlyList<RivalState> Rivals,
        IReadOnlyList<long> StageEnteredSeconds,
        IReadOnlyList<int> RecentStars,
        IReadOnlyDictionary<string, int> JobReadyAt,
        int Trophies,
        IReadOnlyList<string> ActiveMenu,
        IReadOnlyDictionary<string, int> JobTier,
        IReadOnlyList<int> RecentMissed,
        long LastNet = 0)
    {
        /// <summary>True when <paramref name="purchaseId"/> is owned (at least one copy).</summary>
        public bool Owns(string purchaseId) => Count(purchaseId) > 0;

        /// <summary>Owned copies of <paramref name="purchaseId"/>.</summary>
        public int Count(string purchaseId)
        {
            int n = 0;
            foreach (string id in Owned)
                if (string.Equals(id, purchaseId, StringComparison.Ordinal)) n++;
            return n;
        }

        /// <summary>The stake tier of big job <paramref name="jobId"/> (0 at the start).</summary>
        public int TierOf(string jobId) => JobTier.TryGetValue(jobId, out int t) ? t : 0;

        /// <summary>The state of rival <paramref name="rivalId"/>.</summary>
        public RivalState RivalOf(string rivalId)
        {
            foreach (RivalState r in Rivals)
                if (string.Equals(r.RivalId, rivalId, StringComparison.Ordinal)) return r;
            throw new ArgumentException("Unknown rival: " + rivalId, nameof(rivalId));
        }

        /// <summary>When <paramref name="stage"/> was entered (play seconds), or -1.</summary>
        public long EnteredSeconds(ClimbStage stage) => StageEnteredSeconds[(int)stage];
    }
}
