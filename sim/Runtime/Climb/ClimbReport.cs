using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: the climb report text. One section per player count (rows profile x strategy: hours to S2..S5 as median
    /// [min-max] over seeds and the S5 DNF count; then per stage for the reference strategy: services, hours, net $/h, upkeep %,
    /// share %, missed %, stars, and (spec 052b) menu drops, re-adds and repeat buys; then the section's INFO lines), a crew-size
    /// table (D40: h(n) and hours per stage), the GAME NUMBERS table, then one line per target and a last
    /// <c>RESULT: PASS (k/k)</c> or <c>RESULT: FAIL (k/n)</c>. INFO lines are not counted. Integers only; the same runs give the
    /// same text. Money per whole dollar is read from the runs (<see cref="ClimbRun.MoneyScale"/>).
    /// </summary>
    public static class ClimbReport
    {
        public static string Format(IReadOnlyList<ClimbRun> runs, IReadOnlyList<TargetCheck> checks,
            IReadOnlyList<TargetCheck>? info = null, IReadOnlyList<GameNumbersRow>? gameNumbers = null)
        {
            long unit = runs.Count > 0 ? Math.Max(1L, runs[0].MoneyScale) : 1L;
            var sb = new StringBuilder();
            sb.Append("Climb report (specs 052, 052b): ").Append(runs.Count).Append(" runs\n");
            List<string> profiles = runs.Select(r => r.Setup.ProfileId).Distinct().ToList();
            List<int> players = runs.Select(r => r.Setup.Players).Distinct().OrderBy(x => x).ToList();
            foreach (int p in players)
            {
                sb.Append('\n').Append("== ").Append(p).Append(p == 1 ? " player" : " players").Append(" ==\n");
                CrewFactors crew = runs.First(r => r.Setup.Players == p).Crew;
                sb.Append("crew (D40): h ").Append(Thousandths(crew.DifficultyPermille)).Append(", demand ")
                    .Append(Thousandths(crew.DemandPermille)).Append("x, goal ").Append(Thousandths(crew.GoalPermille))
                    .Append("x, stock ").Append(crew.StockPerService).Append(", patience +").Append(crew.PatienceSeconds).Append(" s\n");
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
                sb.Append(Pad("profile", 9)).Append(Pad("stage", 7)).Append(Pad("services", 10)).Append(Pad("hours", 7)).Append(Pad("net$/h", 9))
                    .Append(Pad("upkeep", 8)).Append(Pad("share", 7)).Append(Pad("missed", 8)).Append(Pad("stars", 7))
                    .Append(Pad("drops", 7)).Append(Pad("adds", 6)).Append("repeats\n");
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
                            .Append(Pad(H(M(r => ClimbEvaluation.SecondsOrDnf(r, st))), 7))
                            .Append(Pad(GuestChoice.FloorDiv(M(r => r.NetPerHour(st)), unit).ToString(), 9))
                            .Append(Pad(Pct(M(r => r.UpkeepPermille(st))), 8))
                            .Append(Pad(Pct(M(r => ShareOfRivals(r, st))), 7))
                            .Append(Pad(Pct(M(r => r.MissedPermille(st))), 8))
                            .Append(Pad(Tenths(M(r => r.StarsTenths(st))), 7))
                            .Append(Pad(M(r => r.MenuDrops(st)).ToString(), 7))
                            .Append(Pad(M(r => r.MenuAdds(st)).ToString(), 6))
                            .Append(M(r => r.RepeatBuys(st))).Append('\n');
                    }
                }
                if (info != null)
                    foreach (TargetCheck i in info.Where(i => i.Detail.StartsWith(p + "p ", StringComparison.Ordinal)))
                        sb.Append("INFO ").Append(i.Id).Append(' ').Append(i.Detail).Append(i.Passed ? " (would pass)" : " (would fail)").Append('\n');
            }
            if (players.Count > 0) AppendCrewTable(sb, runs, players);
            if (gameNumbers != null && gameNumbers.Count > 0) AppendGameNumbers(sb, gameNumbers);
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

        /// <summary>Spec 052b: game money (cents, D39) as dollars with cents ("$41.10").</summary>
        public static string FormatMoney(long cents)
        {
            long c = Math.Abs(cents);
            return (cents < 0 ? "-$" : "$") + (c / ClimbCatalog.CentsPerDollar) + "." + (c % ClimbCatalog.CentsPerDollar).ToString("00");
        }

        private static void AppendCrewTable(StringBuilder sb, IReadOnlyList<ClimbRun> runs, List<int> players)
        {
            sb.Append("\n== crew sizes (D40; reference ").Append(ClimbStrategy.Reference.Id).Append(", hours in each stage, medians) ==\n");
            sb.Append(Pad("players", 9)).Append(Pad("h(n)", 7)).Append(Pad("profile", 9));
            for (int st = 1; st <= 4; st++) sb.Append(Pad("S" + st + " h", 7));
            sb.Append("to S5 h\n");
            foreach (int p in players)
            foreach (string profile in runs.Select(r => r.Setup.ProfileId).Distinct())
            {
                var rr = runs.Where(r => r.Setup.Players == p && r.Setup.ProfileId == profile && r.Setup.Strategy == ClimbStrategy.Reference).ToList();
                if (rr.Count == 0) continue;
                sb.Append(Pad(p.ToString(), 9)).Append(Pad(Thousandths(rr[0].Crew.DifficultyPermille), 7)).Append(Pad(profile, 9));
                for (ClimbStage st = ClimbStage.FoodCorner; st < ClimbStage.BeyondKitchen; st++)
                    sb.Append(Pad(H(ClimbEvaluation.Median(rr.Select(r => ClimbEvaluation.SecondsOrDnf(r, st)).ToList())), 7));
                sb.Append(H(ClimbEvaluation.Median(rr.Select(r => ClimbEvaluation.EnteredOrDnf(r, ClimbStage.BeyondKitchen)).ToList()))).Append('\n');
            }
        }

        private static void AppendGameNumbers(StringBuilder sb, IReadOnlyList<GameNumbersRow> rows)
        {
            sb.Append("\n== GAME NUMBERS (reference crew at the game's pace; amounts this crew's, game money) ==\n");
            sb.Append(Pad("players", 9)).Append(Pad("h(n)", 7)).Append(Pad("demand", 8)).Append(Pad("goalx", 7)).Append(Pad("S1 goal", 10))
                .Append(Pad("drop", 6)).Append(Pad("pitch", 8)).Append(Pad("start", 9)).Append(Pad("S2 rent", 9)).Append(Pad("licence", 10))
                .Append(Pad("services", 10)).Append("hours\n");
            foreach (GameNumbersRow r in rows)
                sb.Append(Pad(r.Players.ToString(), 9)).Append(Pad(Thousandths(r.DifficultyPermille), 7))
                    .Append(Pad(Thousandths(r.DemandPermille) + "x", 8)).Append(Pad(Thousandths(r.GoalPermille) + "x", 7))
                    .Append(Pad(FormatMoney(r.GoalGame), 10)).Append(Pad(r.StockPerService.ToString(), 6))
                    .Append(Pad(FormatMoney(r.PitchFeeGame), 8)).Append(Pad(FormatMoney(r.StartGame), 9))
                    .Append(Pad(FormatMoney(r.RentGame), 9)).Append(Pad(FormatMoney(r.LicenseGame), 10))
                    .Append(Pad(r.MedianServices < 0 ? "DNF" : r.MedianServices.ToString(), 10))
                    .Append(r.TenthsOfHours < 0 ? "DNF" : Tenths(r.TenthsOfHours)).Append('\n');
        }

        private static string Trim(long hundredths)
        {
            long whole = hundredths / 100, frac = hundredths % 100;
            if (frac == 0) return whole.ToString();
            return frac % 10 == 0 ? whole + "." + frac / 10 : whole + "." + frac.ToString("00");
        }

        private static string Thousandths(int permille)
        {
            int whole = permille / 1000, frac = permille % 1000;
            if (frac == 0) return whole.ToString();
            string f = frac.ToString("000").TrimEnd('0');
            return whole + "." + f;
        }

        private static string H(long seconds) => seconds == ClimbEvaluation.Dnf ? "DNF" : FormatHours(seconds);
        private static string Pct(long permille) => (permille / 10) + "%";
        private static string Tenths(long tenths) => (tenths / 10) + "." + (tenths % 10);
        private static string Pad(string s, int width) => s.Length >= width ? s + " " : s.PadRight(width);
    }
}
