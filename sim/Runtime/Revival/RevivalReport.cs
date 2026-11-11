using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Worktown.Sim.Climb;

namespace Worktown.Sim.Revival
{
    /// <summary>
    /// Spec 065: the revival report text (invariant, no timings). The catalog line, then per crew size a table of districts x
    /// median hours to each level (and median meals eaten there), the shop district against the stage entry hours, then the
    /// target lines (<c>PASS</c>/<c>FAIL</c>), the info lines (<c>INFO</c>, not counted) and <c>RESULT: PASS (k/k)</c> or
    /// <c>RESULT: FAIL (k/n)</c>.
    /// </summary>
    public static class RevivalReport
    {
        public static string Format(RevivalCatalog c, IReadOnlyList<RevivalTrace> traces, IReadOnlyList<TargetCheck> checks,
            IReadOnlyList<TargetCheck>? info = null, RevivalTargets? targets = null)
        {
            if (c == null) throw new ArgumentException("A catalog is required.", nameof(c));
            if (traces == null) throw new ArgumentException("traces are required.", nameof(traces));
            if (checks == null) throw new ArgumentException("checks are required.", nameof(checks));
            RevivalTargets t = targets ?? RevivalTargets.Default;
            CultureInfo inv = CultureInfo.InvariantCulture;
            string N(long v) => v.ToString(inv);
            var sb = new StringBuilder(4096);
            void Line(string s) => sb.Append(s).Append('\n');

            Line("REVIVAL REPORT (spec 065)");
            Line("goals " + string.Join("/", c.LevelGoals.Select(g => N(g))) + " points; points by stars " + string.Join("/", c.PointsByStars.Select(g => N(g)))
                + "; spill " + N(c.SpillPermille) + " permille; stage bonus " + N(c.StageBonusPpm) + " ppm");
            Line("crew " + string.Join(" ", c.Crew.Select(r => "<=" + N(r.MaxPlayers) + ":" + N(r.GoalPermille))) + "; footfall "
                + string.Join("/", c.FootfallPermille.Select(g => N(g))) + "; ceiling " + string.Join("/", c.StageIngredientCeiling.Select(g => N(g)))
                + " + greenhouse " + string.Join("/", c.GreenhouseCeilingBonus.Select(g => N(g))));

            foreach (int p in traces.Select(x => x.Players).Distinct().OrderBy(x => x))
            {
                List<RevivalTrace> runs = traces.Where(x => x.Players == p).ToList();
                Line("");
                Line("== " + N(p) + " players, " + N(runs.Count) + " seeds, horizon " + RevivalEvaluation.H(runs.Min(x => x.HorizonSeconds))
                    + " h (median hours to each level) ==");
                Line(Row("district", 18, "goal", 6, "start", 8, "Patched", 9, "Lively", 9, "Thriving", 9, "meals", 7));
                foreach (DistrictRevivalDef d in c.Districts)
                {
                    if (!runs[0].DistrictIds.Contains(d.DistrictId)) continue;
                    string L(RevivalLevel l) => RevivalEvaluation.H(RevivalEvaluation.Median(runs.Select(x => Dnf(x.LevelSeconds(d.DistrictId, l)))));
                    long meals = RevivalEvaluation.Median(runs.Select(x => (long)x.MealsIn(d.DistrictId)));
                    Line(Row(d.DistrictId, 18, N(d.GoalPermille), 6, N(d.StartProgressPpm / 10000) + "%", 8, L(RevivalLevel.Patched), 9,
                        L(RevivalLevel.Lively), 9, L(RevivalLevel.Thriving), 9, N(meals), 7));
                }
                if (runs[0].DistrictIds.Contains(t.ShopDistrict))
                {
                    string S(int st) => RevivalEvaluation.H(RevivalEvaluation.Median(runs.Select(x => Dnf(x.StageEnteredSeconds(st)))));
                    string L(RevivalLevel l) => RevivalEvaluation.H(RevivalEvaluation.Median(runs.Select(x => Dnf(x.LevelSeconds(t.ShopDistrict, l)))));
                    Line("shop " + t.ShopDistrict + ": Patched " + L(RevivalLevel.Patched) + " h, Lively " + L(RevivalLevel.Lively) + " h, Thriving "
                        + L(RevivalLevel.Thriving) + " h; stages S2 " + S(2) + " h, S3 " + S(3) + " h, S4 " + S(4) + " h, S5 " + S(5) + " h");
                }
            }

            Line("");
            Line("== targets ==");
            foreach (TargetCheck ch in checks) Line(ch.Line);
            if (info != null) foreach (TargetCheck ch in info) Line("INFO " + ch.Id + " " + ch.Detail);
            int passed = checks.Count(x => x.Passed);
            Line(passed == checks.Count ? "RESULT: PASS (" + N(passed) + "/" + N(checks.Count) + ")" : "RESULT: FAIL (" + N(passed) + "/" + N(checks.Count) + ")");
            return sb.ToString();
        }

        private static long Dnf(long seconds) => seconds < 0 ? ClimbEvaluation.Dnf : seconds;

        private static string Row(params object[] cells)
        {
            var sb = new StringBuilder();
            for (int i = 0; i + 1 < cells.Length; i += 2)
            {
                string s = (string)cells[i];
                int w = (int)cells[i + 1];
                sb.Append(s.Length >= w ? s + " " : s.PadRight(w));
            }
            return sb.ToString().TrimEnd();
        }
    }
}
