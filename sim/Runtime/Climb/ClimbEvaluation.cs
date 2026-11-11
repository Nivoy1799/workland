using System;
using System.Collections.Generic;
using System.Linq;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: checks runs against T1-T8. Medians are over seeds (a DNF counts as never). Groups: player count (ascending),
    /// profile (first-seen order), strategy (<see cref="ClimbStrategy.All"/> order). A target whose runs are missing (e.g. no
    /// novice runs for T3) gives no line. T1 uses the reference profile; T3, T5-T8 the reference strategy; T2 every run.
    /// </summary>
    public static class ClimbEvaluation
    {
        /// <summary>The value of a stage never entered.</summary>
        public const long Dnf = long.MaxValue;

        /// <summary>Median (odd: the middle; even: the mean of the two middles, DNF when either is DNF); <see cref="Dnf"/> when empty.</summary>
        public static long Median(IReadOnlyList<long> values)
        {
            if (values.Count == 0) return Dnf;
            var sorted = values.OrderBy(v => v).ToList();
            int n = sorted.Count;
            if (n % 2 == 1) return sorted[n / 2];
            long a = sorted[n / 2 - 1], b = sorted[n / 2];
            return a == Dnf || b == Dnf ? Dnf : a + (b - a) / 2;
        }

        /// <summary>Entered seconds of <paramref name="stage"/>, <see cref="Dnf"/> when never entered.</summary>
        public static long EnteredOrDnf(ClimbRun run, ClimbStage stage)
        {
            long s = run.EnteredSeconds(stage);
            return s < 0 ? Dnf : s;
        }

        public static IReadOnlyList<TargetCheck> Evaluate(IReadOnlyList<ClimbRun> runs, ClimbTargets targets)
        {
            var checks = new List<TargetCheck>();
            List<int> players = runs.Select(r => r.Setup.Players).Distinct().OrderBy(p => p).ToList();
            List<string> profiles = runs.Select(r => r.Setup.ProfileId).Distinct().ToList();
            ClimbStrategy reference = ClimbStrategy.Reference;
            List<ClimbRun> Of(int p, string profile, ClimbStrategy? strategy) =>
                runs.Where(r => r.Setup.Players == p && r.Setup.ProfileId == profile && (strategy == null || r.Setup.Strategy == strategy)).ToList();

            foreach (int p in players)
            {
                // T1 pacing.
                List<ClimbRun> refRuns = Of(p, targets.ReferenceProfileId, reference);
                if (refRuns.Count > 0)
                    for (ClimbStage st = ClimbStage.FirstRestaurant; st <= ClimbStage.BeyondKitchen; st++)
                    {
                        long m = Median(refRuns.Select(r => EnteredOrDnf(r, st)).ToList());
                        (long from, long to) = targets.BandOf(st);
                        checks.Add(new TargetCheck("T1", m >= from && m <= to,
                            $"{p}p {targets.ReferenceProfileId} S{(int)st} {H(m)} h in {ClimbReport.FormatBand(from)}-{ClimbReport.FormatBand(to)}"));
                    }

                // T2 real effort.
                foreach (string profile in profiles)
                {
                    List<ClimbRun> all = Of(p, profile, null);
                    int min = int.MaxValue;
                    string where = "";
                    foreach (ClimbRun r in all)
                        for (ClimbStage st = ClimbStage.FoodCorner; st < ClimbStage.BeyondKitchen; st++)
                        {
                            if (!r.Completed(st)) continue;
                            int n = r.ServicesIn(st);
                            if (n < min) { min = n; where = $"S{(int)st} {r.Setup.Strategy.Id} seed {r.Setup.Seed}"; }
                        }
                    checks.Add(min == int.MaxValue
                        ? new TargetCheck("T2", true, $"{p}p {profile} no stage completed")
                        : new TargetCheck("T2", min >= targets.MinServicesPerStage,
                            $"{p}p {profile} min {min} services per stage ({where}) >= {targets.MinServicesPerStage}"));
                }

                // T3 skill matters.
                List<ClimbRun> experts = Of(p, targets.ExpertProfileId, reference);
                List<ClimbRun> novices = Of(p, targets.NoviceProfileId, reference);
                if (experts.Count > 0 && novices.Count > 0)
                {
                    long e = Median(experts.Select(r => EnteredOrDnf(r, ClimbStage.BeyondKitchen)).ToList());
                    long nv = Median(novices.Select(r => EnteredOrDnf(r, ClimbStage.BeyondKitchen)).ToList());
                    long horizon = novices.Min(r => r.HorizonSeconds);
                    bool novFinishes = nv != Dnf && nv < horizon;
                    bool ratio = e != Dnf && nv != Dnf && e * 1000 <= (long)targets.SkillRatioPermille * nv;
                    checks.Add(new TargetCheck("T3", novFinishes && ratio,
                        $"{p}p expert S5 {H(e)} h <= {Ratio(targets.SkillRatioPermille)} x novice {H(nv)} h; novice within {H(horizon)} h"));
                }

                foreach (string profile in profiles)
                {
                    // T4 no shortcut.
                    var medians = new List<(ClimbStrategy Strategy, long Median)>();
                    foreach (ClimbStrategy s in ClimbStrategy.All)
                    {
                        List<ClimbRun> rs = Of(p, profile, s);
                        if (rs.Count > 0) medians.Add((s, Median(rs.Select(r => EnteredOrDnf(r, ClimbStage.BeyondKitchen)).ToList())));
                    }
                    if (medians.Count > 0)
                    {
                        var finish = medians.Where(m => m.Median != Dnf).ToList();
                        if (finish.Count == 0)
                            checks.Add(new TargetCheck("T4", false, $"{p}p {profile} no strategy reaches S5 (0/{medians.Count} finish)"));
                        else
                        {
                            var fastest = finish.OrderBy(m => m.Median).First();
                            long med = Median(finish.Select(m => m.Median).ToList());
                            bool ok = fastest.Median * 1000 >= (long)targets.ShortcutPermille * med;
                            checks.Add(new TargetCheck("T4", ok,
                                $"{p}p {profile} fastest {fastest.Strategy.Id} {H(fastest.Median)} h >= {Ratio(targets.ShortcutPermille)} x median {H(med)} h ({finish.Count}/{medians.Count} finish)"));
                        }
                    }

                    List<ClimbRun> rr = Of(p, profile, reference);
                    if (rr.Count == 0) continue;

                    // T5 no runaway: share and snowball.
                    int maxShare = -1;
                    string shareWhere = "no rival channel";
                    for (ClimbStage st = ClimbStage.FoodCorner; st <= ClimbStage.BeyondKitchen; st++)
                    {
                        foreach (string ch in rr.SelectMany(r => r.RivalChannels(st)).Distinct())
                        {
                            var shares = rr.Select(r => (long)r.SharePermille(st, ch, targets.ShareWindow)).Where(v => v >= 0).ToList();
                            if (shares.Count == 0) continue;
                            int m = (int)Median(shares);
                            if (m > maxShare) { maxShare = m; shareWhere = $"S{(int)st} {ch}"; }
                        }
                    }
                    checks.Add(new TargetCheck("T5", maxShare <= targets.MaxSharePermille,
                        $"{p}p {profile} max share {Pct(Math.Max(0, maxShare))} ({shareWhere}) <= {Pct(targets.MaxSharePermille)}"));
                    int worst = int.MaxValue;
                    string snowWhere = "no two completed stages";
                    for (ClimbStage st = ClimbStage.FirstRestaurant; st < ClimbStage.BeyondKitchen; st++)
                    {
                        var both = rr.Where(r => r.Completed(st) && r.Completed(st - 1)).ToList();
                        if (both.Count == 0) continue;
                        long cur = Median(both.Select(r => (long)r.ServicesIn(st)).ToList());
                        long prev = Median(both.Select(r => (long)r.ServicesIn(st - 1)).ToList());
                        int ratio = prev == 0 ? 9999 : (int)(cur * 1000 / prev);
                        if (ratio < worst) { worst = ratio; snowWhere = $"S{(int)st}/S{(int)st - 1}"; }
                    }
                    checks.Add(new TargetCheck("T5", worst == int.MaxValue || worst >= targets.SnowballPermille,
                        $"{p}p {profile} min stage length ratio {Ratio(worst == int.MaxValue ? 0 : worst)}x ({snowWhere}) >= {Ratio(targets.SnowballPermille)}x"));

                    // T6 upkeep rises.
                    var up = new int[6];
                    bool t6 = true;
                    var parts = new List<string>();
                    for (ClimbStage st = ClimbStage.FoodCorner; st <= ClimbStage.BeyondKitchen; st++)
                    {
                        var played = rr.Where(r => r.ServicesIn(st) > 0).ToList();
                        if (played.Count == 0)
                        {
                            up[(int)st] = -1;
                            if (st < ClimbStage.BeyondKitchen) t6 = false;
                            parts.Add($"S{(int)st} -");
                            continue;
                        }
                        up[(int)st] = (int)Median(played.Select(r => (long)r.UpkeepPermille(st)).ToList());
                        if (up[(int)st] > targets.MaxUpkeepPermille) t6 = false;
                        if (st > ClimbStage.FoodCorner && st < ClimbStage.BeyondKitchen && up[(int)st] <= up[(int)st - 1]) t6 = false;
                        parts.Add($"S{(int)st} {Pct(up[(int)st])}");
                    }
                    checks.Add(new TargetCheck("T6", t6,
                        $"{p}p {profile} upkeep {string.Join(", ", parts)}; S1<S2<S3<S4, all <= {Pct(targets.MaxUpkeepPermille)}"));

                    // T7 money has a use.
                    int idle = 0;
                    var idleStages = new SortedSet<int>();
                    foreach (ClimbRun r in rr)
                        for (ClimbStage st = ClimbStage.FoodCorner; st <= ClimbStage.BeyondKitchen; st++)
                        {
                            int n = r.IdleMoneyServices(targets.GoalMinTimes, targets.GoalMaxTimes, targets.GoalWindow, st);
                            idle += n;
                            if (n > 0) idleStages.Add((int)st);
                        }
                    checks.Add(new TargetCheck("T7", idle == 0,
                        $"{p}p {profile} {idle} idle-money services{(idle > 0 ? " (in S" + string.Join(",S", idleStages) + ")" : "")} (goal {targets.GoalMinTimes}-{targets.GoalMaxTimes}x mean net of last {targets.GoalWindow})"));

                    // T8 no stuck debt.
                    int streak = rr.Max(r => r.LongestDebtStreak());
                    checks.Add(new TargetCheck("T8", streak <= targets.MaxDebtServices,
                        $"{p}p {profile} longest debt {streak} services <= {targets.MaxDebtServices}"));
                }
            }
            return checks.AsReadOnly();
        }

        private static string H(long seconds) => seconds == Dnf ? "DNF" : ClimbReport.FormatHours(seconds);
        private static string Pct(int permille) => (permille / 10) + "%";
        private static string Ratio(int permille) => ClimbReport.FormatRatio(permille);
    }
}
