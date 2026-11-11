using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Worktown.Sim.Climb;

namespace Worktown.ClimbReport
{
    /// <summary>
    /// Spec 052: runs the strategy grid for the given player counts, profiles and seeds, prints the report and exits
    /// 0 when every target passes, 2 when one fails, 1 on bad arguments.
    /// Spec 052b: also runs the C1 calibration (1, 4 and 16 players, the 053 stage 1 setup) and the GAME NUMBERS rows
    /// (T1g); <c>--calibration</c> runs only C1; <c>--crew-scaling off</c> uses one neutral crew row; <c>--game-scale</c>
    /// multiplies the game amounts (spec 037: the game's money is the catalog's cents, so the default is 1).
    /// Usage: climb-report [--players 1,2,4,8,16] [--profiles novice,average,expert] [--seeds 5] [--hours 40]
    ///        [--service-seconds 360] [--between-seconds 60] [--out file] [--calibration] [--crew-scaling on|off]
    ///        [--game-scale 1] [--trace players,profile,strategy,seed]
    /// </summary>
    public static class Program
    {
        private static readonly int[] CalibrationPlayers = { 1, 4, 16 };

        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            var watch = Stopwatch.StartNew();
            int[] players = { 1, 2, 4, 8, 16 };
            string[] profiles = { "novice", "average", "expert" };
            int seeds = 5, hours = 40, serviceSeconds = 360, betweenSeconds = 60, gameScale = 1;
            string? outFile = null;
            string? trace = null;
            bool calibrationOnly = false, crewScaling = true;
            ClimbCatalog probe = ClimbCatalog.CreateDefault();
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + args[i]);
                    switch (args[i])
                    {
                        case "--players": players = Next().Split(',').Select(ParsePositive).ToArray(); break;
                        case "--profiles":
                            profiles = Next().Split(',');
                            foreach (string p in profiles) probe.Profile(p);
                            break;
                        case "--seeds": seeds = ParsePositive(Next()); break;
                        case "--hours": hours = ParsePositive(Next()); break;
                        case "--service-seconds": serviceSeconds = ParsePositive(Next()); if (serviceSeconds < 60) throw new ArgumentException("--service-seconds must be >= 60"); break;
                        case "--between-seconds": betweenSeconds = int.Parse(Next(), CultureInfo.InvariantCulture); if (betweenSeconds < 0) throw new ArgumentException("--between-seconds must be >= 0"); break;
                        case "--out": outFile = Next(); break;
                        case "--trace": trace = Next(); break;
                        case "--calibration": calibrationOnly = true; break;
                        case "--crew-scaling":
                            string v = Next();
                            if (v == "on") crewScaling = true;
                            else if (v == "off") crewScaling = false;
                            else throw new ArgumentException("--crew-scaling must be on or off");
                            break;
                        case "--game-scale": gameScale = ParsePositive(Next()); break;
                        default: throw new ArgumentException("Unknown argument " + args[i]);
                    }
                }
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException)
            {
                Console.Error.WriteLine("climb-report: " + e.Message);
                Console.Error.WriteLine("usage: climb-report [--players 1,2,4,8,16] [--profiles novice,average,expert] [--seeds 5] [--hours 40] [--service-seconds 360] [--between-seconds 60] [--out file] [--calibration] [--crew-scaling on|off] [--game-scale 1] [--trace players,profile,strategy,seed]  (--trace prints one run and always exits 0)");
                return 1;
            }

            IReadOnlyList<CrewScaleRow> table = crewScaling ? ClimbCatalog.DefaultCrewTable : ClimbCatalog.NeutralCrewTable;
            ClimbCatalog catalog = ClimbCatalog.CreateDefault(crewTable: table);

            if (trace != null)
            {
                // One run's canonical text (players,profile,strategy,seed), for tuning.
                string[] t = trace.Split(',');
                if (t.Length != 4) { Console.Error.WriteLine("climb-report: --trace players,profile,strategy,seed"); return 1; }
                CrewSetup one;
                try { one = new CrewSetup(ParsePositive(t[0]), catalog.Profile(t[1]).Id, ClimbStrategy.Parse(t[2]), (ulong)ParsePositive(t[3]), serviceSeconds, betweenSeconds); }
                catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException) { Console.Error.WriteLine("climb-report: " + e.Message); return 1; }
                Console.Out.Write(new ClimbEngine(catalog).Run(one, hours * 3600L).Canonical);
                return 0;
            }

            ClimbTargets targets = ClimbTargets.Default;
            // C1: the 053 stage 1 setup (no crew scaling of demand or goal, the 12-potato slot, the 15 s summary).
            var calCatalog = ClimbCatalog.CreateDefault(crewTable: ClimbCatalog.CalibrationCrewTable(table), stage1Extras: false);
            // GAME NUMBERS: the corner as built (no stage 1 extras), with this crew table.
            var gameCatalog = ClimbCatalog.CreateDefault(crewTable: table, stage1Extras: false);
            var calSetups = new List<CrewSetup>();
            foreach (int p in CalibrationPlayers)
                for (int seed = 1; seed <= seeds; seed++)
                    calSetups.Add(new CrewSetup(p, targets.ReferenceProfileId, ClimbStrategy.Reference, (ulong)seed, serviceSeconds, GameNumbers.GameBetweenSeconds));
            var calEngine = new ClimbEngine(calCatalog);
            var calRuns = new ClimbRun[calSetups.Count];
            long calHorizon = Math.Min(hours, 8) * 3600L;
            Parallel.For(0, calSetups.Count, i => calRuns[i] = calEngine.Run(calSetups[i], calHorizon));
            IReadOnlyList<TargetCheck> c1 = ClimbEvaluation.Calibration(calRuns, targets);

            string report;
            bool pass;
            if (calibrationOnly)
            {
                report = Worktown.Sim.Climb.ClimbReport.Format(calRuns, c1);
                pass = c1.All(c => c.Passed);
            }
            else
            {
                var setups = new List<CrewSetup>();
                foreach (int p in players)
                foreach (string profile in profiles)
                foreach (ClimbStrategy s in ClimbStrategy.All)
                    for (int seed = 1; seed <= seeds; seed++)
                        setups.Add(new CrewSetup(p, profile, s, (ulong)seed, serviceSeconds, betweenSeconds));

                var engine = new ClimbEngine(catalog);
                var runs = new ClimbRun[setups.Count];
                long horizon = hours * 3600L;
                Parallel.For(0, setups.Count, i => runs[i] = engine.Run(setups[i], horizon));
                var rows = new GameNumbersRow[GameNumbers.ReportPlayers.Count];
                Parallel.For(0, rows.Length, i => rows[i] = GameNumbers.For(gameCatalog, GameNumbers.ReportPlayers[i], gameScale, seeds, serviceSeconds,
                    targets.ReferenceProfileId));
                var checks = new List<TargetCheck>(ClimbEvaluation.Evaluate(runs, targets));
                checks.AddRange(c1);
                checks.AddRange(ClimbEvaluation.GameNumberChecks(rows, targets));
                report = Worktown.Sim.Climb.ClimbReport.Format(runs, checks, ClimbEvaluation.Info(runs, targets), rows);
                pass = checks.All(c => c.Passed);
            }
            if (outFile != null) File.WriteAllText(outFile, report);
            Console.Out.Write(report);
            Console.Error.WriteLine($"climb-report: done in {watch.ElapsedMilliseconds} ms");
            return pass ? 0 : 2;
        }

        private static int ParsePositive(string s)
        {
            int v = int.Parse(s, CultureInfo.InvariantCulture);
            if (v < 1) throw new ArgumentException("Values must be at least 1: " + s);
            return v;
        }
    }
}
