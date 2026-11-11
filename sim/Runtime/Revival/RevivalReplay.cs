using System;
using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Climb;

namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065: replays a spec 052 climb into the revival. Per service in order: a higher stage than the service before gives
    /// the stage bonus (once per stage passed); a normal service splits <c>Served</c> over its channels in proportion to
    /// <c>YourGuests</c> and each channel's meals over the plan's districts (largest remainder both times); an event big job's
    /// meals go to the plan's event district; a competition adds nothing. Stars are <c>1 + min(4, AvgQuality / 200)</c> (052
    /// step 5); the crew is the run's player count. Meals of one service are added per district in catalog order.
    /// </summary>
    public static class RevivalReplay
    {
        /// <summary>Stars of a service: <c>1 + min(4, quality / 200)</c>, quality below 0 counted as 0.</summary>
        public static int StarsOf(int avgQuality) => 1 + Math.Min(4, Math.Max(0, avgQuality) / 200);

        /// <summary>
        /// Splits <paramref name="total"/> by <paramref name="weights"/> (largest remainder; ties go to the earlier entry). All
        /// zero weights give all zeros. Weights must be &gt;= 0 and the total &gt;= 0.
        /// </summary>
        public static int[] Split(int total, IReadOnlyList<int> weights)
        {
            if (weights == null) throw new ArgumentException("weights are required.", nameof(weights));
            if (total < 0) throw new ArgumentOutOfRangeException(nameof(total), "total must be >= 0.");
            if (weights.Any(w => w < 0)) throw new ArgumentOutOfRangeException(nameof(weights), "weights must be >= 0.");
            var parts = new int[weights.Count];
            long sum = weights.Sum(w => (long)w);
            if (sum == 0 || total == 0) return parts;
            var rest = new long[weights.Count];
            long given = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                long share = total * (long)weights[i];
                parts[i] = (int)(share / sum);
                rest[i] = share % sum;
                given += parts[i];
            }
            foreach (int i in Enumerable.Range(0, weights.Count).OrderByDescending(i => rest[i]).ThenBy(i => i).Take((int)(total - given)))
                parts[i]++;
            return parts;
        }

        /// <summary>
        /// Replays <paramref name="run"/> into a fresh revival (<see cref="RevivalEngine.Start"/>) with the rules of the class
        /// summary and returns the trace: level-up hours per district (service end), meals per district, the climb's stage entry
        /// seconds, its difficulty h(n), the number of stage bonuses, whether a level dropped and the final digest. Throws
        /// <see cref="ArgumentException"/> for a missing input or a plan district the catalog does not know.
        /// </summary>
        public static RevivalTrace Replay(ClimbRun run, RevivalCatalog c, RevivalPlan plan)
        {
            if (run == null) throw new ArgumentException("A run is required.", nameof(run));
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            if (plan == null) throw new ArgumentException("A plan is required.", nameof(plan));
            plan.Validate(c);

            int players = run.Setup.Players;
            int n = c.Districts.Count;
            var levels = new long[n][];
            for (int i = 0; i < n; i++) levels[i] = new long[] { -1, -1, -1 };
            var meals = new int[n];
            RevivalState s = RevivalEngine.Start(c);
            bool dropped = false;
            int entries = 0;
            ClimbStage? previous = null;

            foreach (ServiceResult r in run.Services)
            {
                long end = r.StartSeconds + r.Seconds;
                RevivalState before = s;
                if (previous != null)
                    for (int st = (int)previous.Value + 1; st <= (int)r.Stage; st++)
                    {
                        (s, _) = RevivalEngine.EnterStage(s, c, st);
                        entries++;
                    }
                previous = previous == null || r.Stage > previous.Value ? r.Stage : previous;

                var perDistrict = new int[n];
                if (r.BigJobId == null)
                {
                    int[] byChannel = Split(Math.Max(0, r.Served), r.ChannelIds.Select((id, i) => Math.Max(0, r.YourGuests[i])).ToList());
                    // Ties of the channel split go to the earlier channel; ChannelIds are in id order (spec 052).
                    for (int k = 0; k < r.ChannelIds.Count; k++)
                    {
                        if (byChannel[k] == 0) continue;
                        IReadOnlyList<(string DistrictId, int Weight)> shares = plan.SharesOf(r.ChannelIds[k]);
                        int[] split = Split(byChannel[k], shares.Select(x => x.Weight).ToList());
                        for (int j = 0; j < shares.Count; j++) perDistrict[c.IndexOf(shares[j].DistrictId)] += split[j];
                    }
                }
                else if (!plan.IsCompetition(r.BigJobId))
                    perDistrict[c.IndexOf(plan.EventDistrict)] += Math.Max(0, r.Served);

                int stars = StarsOf(r.AvgQuality);
                for (int i = 0; i < n; i++)
                {
                    if (perDistrict[i] == 0) continue;
                    meals[i] += perDistrict[i];
                    (s, _) = RevivalEngine.AddMeals(s, c, c.Districts[i].DistrictId, stars, perDistrict[i], players);
                }

                for (int i = 0; i < n; i++)
                {
                    RevivalLevel was = before.Districts[i].Level, now = s.Districts[i].Level;
                    if (now < was) dropped = true;
                    for (int L = (int)was + 1; L <= (int)now; L++)
                        if (levels[i][L - 1] < 0) levels[i][L - 1] = end;
                }
            }

            var stages = new long[5];
            for (int st = 1; st <= 5; st++) stages[st - 1] = run.EnteredSeconds((ClimbStage)st);
            return new RevivalTrace(players, run.Setup.Seed, run.HorizonSeconds, c.Districts.Select(d => d.DistrictId).ToList(),
                levels.Select(l => (IReadOnlyList<long>)l).ToList(), stages, meals, entries, dropped, s.Digest, run.Crew.DifficultyPermille);
        }
    }
}
