using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: the climb report text. One section per player count (rows profile x strategy: hours to S2..S5 as median
    /// [min-max] over seeds and the S5 DNF count; then per stage for the reference strategy: services, net $/h, upkeep %,
    /// share %, missed %, stars), then one line per target and a last <c>RESULT: PASS (k/k)</c> or <c>RESULT: FAIL (k/n)</c>.
    /// Integers only; the same runs give the same text.
    /// </summary>
    public static class ClimbReport
    {
        /// <summary>The report; net $/h is printed in whole dollars (<paramref name="moneyScale"/> money per dollar: 100 for cents, D39).</summary>
        public static string Format(IReadOnlyList<ClimbRun> runs, IReadOnlyList<TargetCheck> checks, long moneyScale = 1)
        {
            long unit = Math.Max(1L, moneyScale);
            var sb = new StringBuilder();
            sb.Append("Climb report (spec 052): ").Append(runs.Count).Append(" runs\n");
            List<string> profiles = runs.Select(r => r.Setup.ProfileId).Distinct().ToList();
            foreach (int p in runs.Select(r => r.Setup.Players).Distinct().OrderBy(x => x))
            {
                sb.Append('\n').Append("== ").Append(p).Append(p == 1 ? " player" : " players").Append(" ==\n");
                sb.Append(Pad("profile", 9)).Append(Pad("strategy", 19));
                for (int st = 2; st <= 5; st++) sb.Append(Pad("S" + st + " h", 17));
                sb.Append("DNF\n");
                foreach (string profile in profiles)
                foreach (ClimbStrategy s in ClimbStrategy.All)
                {
                    var rs = runs.Where(r => r.Setup.Players == p && r.Setup.ProfileId == profile && r.Setup.Strategy == s).ToList();
                    if (rs.Count == 0) continue;
                    sb.Append(Pad(profile, 9)).Append(Pad(s.Id, 19));
                    for (ClimbStage st = ClimbStage.FirstRestaurant; st <= ClimbStage.BeyondKitchen; st++)
                    {
                        var v = rs.Select(r => ClimbEvaluation.EnteredOrDnf(r, st)).ToList();
                        sb.Append(Pad(H(ClimbEvaluation.Median(v)) + " [" + H(v.Min()) + "-" + H(v.Max()) + "]", 17));
                    }
                    sb.Append(rs.Count(r => r.EnteredSeconds(ClimbStage.BeyondKitchen) < 0)).Append('\n');
                }
                sb.Append("reference ").Append(ClimbStrategy.Reference.Id).Append(" per stage (medians over seeds):\n");
                sb.Append(Pad("profile", 9)).Append(Pad("stage", 7)).Append(Pad("services", 10)).Append(Pad("net$/h", 9))
                    .Append(Pad("upkeep", 8)).Append(Pad("share", 7)).Append(Pad("missed", 8)).Append("stars\n");
                foreach (string profile in profiles)
                {
                    var rr = runs.Where(r => r.Setup.Players == p && r.Setup.ProfileId == profile && r.Setup.Strategy == ClimbStrategy.Reference).ToList();
                    if (rr.Count == 0) continue;
                    for (ClimbStage st = ClimbStage.FoodCorner; st <= ClimbStage.BeyondKitchen; st++)
                    {
                        var played = rr.Where(r => r.ServicesIn(st) > 0).ToList();
                        if (played.Count == 0) continue;
                        long M(Func<ClimbRun, long> f) => ClimbEvaluation.Median(played.Select(f).ToList());
                        sb.Append(Pad(profile, 9)).Append(Pad("S" + (int)st, 7))
                            .Append(Pad(M(r => r.ServicesIn(st)).ToString(), 10))
                            .Append(Pad(GuestChoice.FloorDiv(M(r => r.NetPerHour(st)), unit).ToString(), 9))
                            .Append(Pad(Pct(M(r => r.UpkeepPermille(st))), 8))
                            .Append(Pad(Pct(M(r => ShareOfRivals(r, st))), 7))
                            .Append(Pad(Pct(M(r => r.MissedPermille(st))), 8))
                            .Append(Tenths(M(r => r.StarsTenths(st)))).Append('\n');
                    }
                }
            }
            sb.Append("\n== targets ==\n");
            foreach (TargetCheck c in checks) sb.Append(c.Line).Append('\n');
            int passed = checks.Count(c => c.Passed);
            sb.Append(passed == checks.Count ? "RESULT: PASS (" + passed + "/" + checks.Count + ")" : "RESULT: FAIL (" + passed + "/" + checks.Count + ")");
            sb.Append('\n');
            return sb.ToString();
        }

        /// <summary>Your share of every rival channel together in <paramref name="stage"/>: yours / (yours + rivals') permille.</summary>
        public static long ShareOfRivals(ClimbRun run, ClimbStage stage)
        {
            long you = 0, all = 0;
            foreach (ServiceResult r in run.Services)
            {
                if (r.Stage != stage) continue;
                for (int i = 0; i < r.ChannelIds.Count; i++)
                    if (r.RivalGuests[i] >= 0) { you += r.YourGuests[i]; all += r.YourGuests[i] + r.RivalGuests[i]; }
            }
            return all == 0 ? 0 : you * 1000 / all;
        }

        /// <summary>Seconds as hours with one decimal, rounded half up ("1.1").</summary>
        public static string FormatHours(long seconds)
        {
            long tenths = (seconds * 10 + 1800) / 3600;
            return (tenths / 10) + "." + (tenths % 10);
        }

        /// <summary>Seconds as hours with up to two decimals, trailing zeros dropped ("0.75", "1.5", "3").</summary>
        public static string FormatBand(long seconds)
        {
            long hundredths = (seconds * 100 + 1800) / 3600;
            return Trim(hundredths);
        }

        /// <summary>Permille as a ratio with up to two decimals ("0.8", "0.85", "1").</summary>
        public static string FormatRatio(int permille) => Trim((permille + 5) / 10);

        private static string Trim(long hundredths)
        {
            long whole = hundredths / 100, frac = hundredths % 100;
            if (frac == 0) return whole.ToString();
            return frac % 10 == 0 ? whole + "." + frac / 10 : whole + "." + frac.ToString("00");
        }

        private static string H(long seconds) => seconds == ClimbEvaluation.Dnf ? "DNF" : FormatHours(seconds);
        private static string Pct(long permille) => (permille / 10) + "%";
        private static string Tenths(long tenths) => (tenths / 10) + "." + (tenths % 10);
        private static string Pad(string s, int width) => s.Length >= width ? s + " " : s.PadRight(width);
    }
}
