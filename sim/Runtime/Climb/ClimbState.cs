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
        int Trophies)
    {
        /// <summary>True when <paramref name="purchaseId"/> is owned.</summary>
        public bool Owns(string purchaseId)
        {
            foreach (string id in Owned)
                if (string.Equals(id, purchaseId, StringComparison.Ordinal)) return true;
            return false;
        }

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
