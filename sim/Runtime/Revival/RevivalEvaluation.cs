using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Worktown.Sim.Climb;

namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065: checks replays against V1-V7 and gives spec 052's <see cref="TargetCheck"/> lines. Medians are over the traces
    /// of one crew size (a level never reached counts as never, <see cref="ClimbEvaluation.Dnf"/>); crews in ascending order.
    /// V1, V2 and V5 give a line per crew size, V3 per other crew and level, V4 for the reference crew, V7 one line.
    /// V6 runs climbs, so it is separate: <see cref="FootfallCheck"/> on runs of a catalog from <see cref="WithFootfall"/>.
    /// </summary>
    public static class RevivalEvaluation
    {
        public static IReadOnlyList<TargetCheck> Evaluate(IReadOnlyList<RevivalTrace> traces, RevivalTargets targets)
        {
            if (traces == null) throw new ArgumentException("traces are required.", nameof(traces));
            if (targets == null) throw new ArgumentException("targets are required.", nameof(targets));
            var checks = new List<TargetCheck>();
            List<int> crews = traces.Select(t => t.Players).Distinct().OrderBy(p => p).ToList();
            List<RevivalTrace> Of(int p) => traces.Where(t => t.Players == p).ToList();
            string shop = targets.ShopDistrict;
            var reference = Of(targets.ReferencePlayers);

            foreach (int p in crews)
            {
                List<RevivalTrace> runs = Of(p);
                long horizon = runs.Min(t => t.HorizonSeconds);
                long Stage(int st) => Median(runs.Select(t => Dnf(t.StageEnteredSeconds(st))));
                long Level(string d, RevivalLevel l) => Median(runs.Select(t => Dnf(t.LevelSeconds(d, l))));
                if (!runs[0].DistrictIds.Contains(shop)) continue;

                // V1: Patched near the first restaurant.
                long s2 = Stage(2), patched = Level(shop, RevivalLevel.Patched);
                bool v1 = s2 != ClimbEvaluation.Dnf && patched != ClimbEvaluation.Dnf
                    && patched >= s2 + targets.PatchedFromS2Seconds && patched <= s2 + targets.PatchedToS2Seconds;
                checks.Add(new TargetCheck("V1", v1, $"{p}p {shop} Patched {Hh(patched)} in S2 {Hh(s2)} {Signed(targets.PatchedFromS2Seconds)}..{Signed(targets.PatchedToS2Seconds)} h"));

                // V2: Lively during the service mix, Thriving from automation to a little after stage 5.
                long s3 = Stage(3), s4 = Stage(4), s5 = Stage(5);
                long lively = Level(shop, RevivalLevel.Lively), thriving = Level(shop, RevivalLevel.Thriving);
                bool v2a = lively != ClimbEvaluation.Dnf && s3 != ClimbEvaluation.Dnf && lively >= s3 && lively <= Math.Min(s4, horizon);
                checks.Add(new TargetCheck("V2", v2a, $"{p}p {shop} Lively {Hh(lively)} in S3 {Hh(s3)} .. S4 {Hh(s4)}"));
                long thrivingTo = s5 == ClimbEvaluation.Dnf ? horizon : s5 + targets.ThrivingAfterS5Seconds;
                bool v2b = thriving != ClimbEvaluation.Dnf && s4 != ClimbEvaluation.Dnf && thriving >= s4 && thriving <= thrivingTo;
                checks.Add(new TargetCheck("V2", v2b, $"{p}p {shop} Thriving {Hh(thriving)} in S4 {Hh(s4)} .. S5 {Hh(s5)} {Signed(targets.ThrivingAfterS5Seconds)} h"));

                // V3: fair crews against the reference crew. 065 review: like T1 (D40), the lower edge is times the crew's h(n),
                // so a crew whose climb is h times faster may revive h times faster; the upper edge stays.
                int h = Math.Max(0, Math.Min(1000, runs[0].DifficultyPermille));
                int fairMin = (int)((long)targets.FairMinPermille * h / 1000);
                if (p != targets.ReferencePlayers && reference.Count > 0)
                    foreach (RevivalLevel l in new[] { RevivalLevel.Patched, RevivalLevel.Lively })
                    {
                        long mine = Level(shop, l);
                        long baseline = Median(reference.Select(t => Dnf(t.LevelSeconds(shop, l))));
                        bool ok = mine != ClimbEvaluation.Dnf && baseline != ClimbEvaluation.Dnf && baseline > 0
                            && mine * 1000 >= baseline * fairMin && mine * 1000 <= baseline * targets.FairMaxPermille;
                        string ratio = mine == ClimbEvaluation.Dnf || baseline == ClimbEvaluation.Dnf || baseline == 0 ? "-" : Ratio((int)Math.Min(99999, mine * 1000 / baseline));
                        checks.Add(new TargetCheck("V3", ok,
                            $"{p}p {shop} {l} {Hh(mine)} = {ratio}x of {targets.ReferencePlayers}p {Hh(baseline)} in {Ratio(fairMin)}-{Ratio(targets.FairMaxPermille)}x"));
                    }

                // V4: every district comes back to Patched (reference crew).
                if (p == targets.ReferencePlayers)
                {
                    string worst = "";
                    long worstAt = -1;
                    int reached = 0;
                    foreach (string d in runs[0].DistrictIds)
                    {
                        long at = Level(d, RevivalLevel.Patched);
                        if (at != ClimbEvaluation.Dnf && at <= horizon) reached++;
                        if (at > worstAt) { worstAt = at; worst = d; }
                    }
                    int all = runs[0].DistrictIds.Count;
                    checks.Add(new TargetCheck("V4", reached == all,
                        $"{p}p {reached}/{all} districts Patched within {Hh(horizon)} (last {worst} {Hh(worstAt)})"));
                }

                // V5: the Low Ring comes back last.
                if (runs[0].DistrictIds.Contains(targets.LastDistrict))
                {
                    var problems = new List<string>();
                    var seen = new List<string>();
                    foreach (RevivalLevel l in new[] { RevivalLevel.Patched, RevivalLevel.Lively, RevivalLevel.Thriving })
                    {
                        long last = Level(targets.LastDistrict, l);
                        if (last == ClimbEvaluation.Dnf) continue;
                        seen.Add($"{l} {Hh(last)}");
                        foreach (string d in runs[0].DistrictIds.Where(d => d != targets.LastDistrict))
                        {
                            long at = Level(d, l);
                            if (at > last) problems.Add($"{d} {l} {Hh(at)}");
                        }
                    }
                    string where = seen.Count == 0 ? "reaches no level" : string.Join(", ", seen);
                    checks.Add(new TargetCheck("V5", problems.Count == 0, problems.Count == 0
                        ? $"{p}p {targets.LastDistrict} last ({where})"
                        : $"{p}p {targets.LastDistrict} ({where}) is not last: {string.Join(", ", problems.Take(4))}{(problems.Count > 4 ? ", ..." : "")}"));
                }
            }

            // V7: never back.
            int drops = traces.Count(t => t.LevelDropped);
            checks.Add(new TargetCheck("V7", drops == 0, $"{drops} of {traces.Count} replays dropped a level"));
            return checks.AsReadOnly();
        }

        /// <summary>
        /// V6: the 052 T1 bands of the <paramref name="players"/> crew for climbs whose footfall was multiplied by
        /// <paramref name="permille"/> (<paramref name="runs"/> from a <see cref="WithFootfall"/> catalog, reference profile and
        /// strategy). Passes when every T1 line of that crew passes; no T1 line fails.
        /// </summary>
        public static TargetCheck FootfallCheck(int permille, IReadOnlyList<ClimbRun> runs, ClimbTargets climbTargets, int players)
        {
            if (runs == null) throw new ArgumentException("runs are required.", nameof(runs));
            if (climbTargets == null) throw new ArgumentException("climbTargets are required.", nameof(climbTargets));
            List<ClimbRun> crew = runs.Where(r => r.Setup.Players == players).ToList();
            List<TargetCheck> t1 = ClimbEvaluation.Evaluate(crew, climbTargets).Where(c => c.Id == "T1").ToList();
            if (t1.Count == 0) return new TargetCheck("V6", false, $"footfall {Ratio(permille)}x: no {players}p reference runs");
            var failed = t1.Where(c => !c.Passed).Select(c => c.Detail).ToList();
            string stages = string.Join(", ", t1.Select(c => c.Detail.Replace(players + "p " + climbTargets.ReferenceProfileId + " ", "")));
            return new TargetCheck("V6", failed.Count == 0, $"footfall {Ratio(permille)}x {players}p T1 {(failed.Count == 0 ? "pass" : "FAIL")}: {stages}");
        }

        /// <summary>
        /// A copy of <paramref name="c"/> with every channel's <c>GuestsPerService</c> times <paramref name="permille"/> / 1000
        /// (floored), everything else equal (1000 gives an equal catalog). V6's worst case: the bonus from the first service.
        /// </summary>
        public static ClimbCatalog WithFootfall(ClimbCatalog c, int permille)
        {
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            if (permille < 0) throw new ArgumentOutOfRangeException(nameof(permille), "permille must be >= 0.");
            long s = c.MoneyScale; // the constructor multiplies money by the scale again; every amount is a whole multiple of it
            return new ClimbCatalog(
                c.Profiles,
                c.Dishes.Select(d => d with { IngredientCost = d.IngredientCost / s, ReferencePrice = d.ReferencePrice / s }),
                c.Channels.Select(ch => ch with { GuestsPerService = (int)((long)ch.GuestsPerService * permille / 1000), Upkeep = ch.Upkeep / s }),
                c.Rivals.Select(r => r with { PriceOffset = (int)(r.PriceOffset / s) }),
                c.Purchases.Select(p => p with { Cost = p.Cost / s, UpkeepPerService = p.UpkeepPerService / s }),
                c.Stages.Select(st => st with { Upkeep = st.Upkeep / s }),
                c.BigJobs.Select(j => j with { Deposit = j.Deposit / s, Prize = j.Prize / s }),
                c.Weights, c.Stage1DishId, c.StartingMoney / s, s, c.CrewTable);
        }

        /// <summary>Median of play seconds, <see cref="ClimbEvaluation.Dnf"/> for never (spec 052's rule).</summary>
        public static long Median(IEnumerable<long> values) => ClimbEvaluation.Median((values ?? Enumerable.Empty<long>()).ToList());

        /// <summary>Hours with one decimal ("1.4"), "never" for <see cref="ClimbEvaluation.Dnf"/>.</summary>
        public static string H(long seconds)
        {
            if (seconds == ClimbEvaluation.Dnf || seconds < 0) return "never";
            long tenths = (seconds + 180) / 360;
            return (tenths / 10).ToString(CultureInfo.InvariantCulture) + "." + (tenths % 10).ToString(CultureInfo.InvariantCulture);
        }

        private static long Dnf(long seconds) => seconds < 0 ? ClimbEvaluation.Dnf : seconds;

        private static string Hh(long seconds) => seconds == ClimbEvaluation.Dnf || seconds < 0 ? "never" : H(seconds) + " h";

        private static string Signed(long seconds)
        {
            string h = H(Math.Abs(seconds));
            return (seconds < 0 ? "-" : "+") + h;
        }

        private static string Ratio(int permille) =>
            (permille / 1000).ToString(CultureInfo.InvariantCulture) + "." + (permille % 1000 / 10).ToString("00", CultureInfo.InvariantCulture);
    }
}
