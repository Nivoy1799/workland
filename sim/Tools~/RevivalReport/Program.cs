using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Worktown.Sim.Climb;
using Worktown.Sim.Revival;
using Worktown.Sim.Town;

namespace Worktown.RevivalReport
{
    /// <summary>
    /// Spec 065: replays the 052b climb (average profile, reference strategy) into the revival for the given crews and seeds,
    /// checks V1-V7 and prints the report. V6 runs the 4-player climb again with every channel's guests times the top footfall
    /// level (counted), and as INFO the lower levels and the town plan's 1100, 1200 and 1300. Exit 0 when every target passes, 2 when one fails, 1 on bad arguments.
    /// Usage: revival-report [--players 1,4,16] [--seeds 5] [--hours 40] [--out file]
    /// </summary>
    public static class Program
    {
        private const string Usage = "usage: revival-report [--players 1,4,16] [--seeds 5] [--hours 40] [--out file]";

        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            var watch = Stopwatch.StartNew();
            int[] players = { 1, 4, 16 };
            int seeds = 5, hours = 40;
            string? outFile = null;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + args[i]);
                    switch (args[i])
                    {
                        case "--players":
                            players = Next().Split(',').Select(ParsePositive).Distinct().OrderBy(p => p).ToArray();
                            if (players.Any(p => p > 16)) throw new ArgumentException("--players must be 1..16");
                            break;
                        case "--seeds": seeds = ParsePositive(Next()); break;
                        case "--hours": hours = ParsePositive(Next()); break;
                        case "--out": outFile = Next(); break;
                        default: throw new ArgumentException("Unknown argument " + args[i]);
                    }
                }
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException)
            {
                Console.Error.WriteLine("revival-report: " + e.Message);
                Console.Error.WriteLine(Usage);
                return 1;
            }

            TownCatalog town = TownCatalog.CreateDefault();
            RevivalCatalog revival = RevivalCatalog.CreateDefault(town);
            RevivalPlan plan = RevivalPlan.Default;
            RevivalTargets targets = RevivalTargets.Default;
            ClimbTargets climbTargets = ClimbTargets.Default;
            ClimbCatalog climb = ClimbCatalog.CreateDefault();
            long horizon = hours * 3600L;

            var setups = new List<CrewSetup>();
            foreach (int p in players)
                for (int seed = 1; seed <= seeds; seed++)
                    setups.Add(new CrewSetup(p, climbTargets.ReferenceProfileId, ClimbStrategy.Reference, (ulong)seed));
            var engine = new ClimbEngine(climb);
            var traces = new RevivalTrace[setups.Count];
            Parallel.For(0, setups.Count, i => traces[i] = RevivalReplay.Replay(engine.Run(setups[i], horizon), revival, plan));
            var checks = new List<TargetCheck>(RevivalEvaluation.Evaluate(traces, targets));

            // V6: the footfall bonus at its worst (every channel from the first service), counted at the top level.
            int top = revival.FootfallPermille[RevivalCatalog.Levels - 1];
            var info = new List<TargetCheck>();
            // INFO: the lower levels and the town plan's +10/20/30 % (the spec's start), not counted.
            var levels = new List<int> { top };
            levels.AddRange(new[] { revival.FootfallPermille[1], revival.FootfallPermille[2], 1100, 1200, 1300 }
                .Where(v => v != top && v > 1000).Distinct().OrderBy(v => v));
            foreach (int permille in levels)
            {
                var scaled = new ClimbEngine(RevivalEvaluation.WithFootfall(climb, permille));
                var runs = new ClimbRun[seeds];
                Parallel.For(0, seeds, i => runs[i] = scaled.Run(new CrewSetup(targets.ReferencePlayers, climbTargets.ReferenceProfileId,
                    ClimbStrategy.Reference, (ulong)(i + 1)), horizon));
                TargetCheck v6 = RevivalEvaluation.FootfallCheck(permille, runs, climbTargets, targets.ReferencePlayers);
                if (permille == top && checks.All(c => c.Id != "V6")) checks.Add(v6);
                else info.Add(v6);
            }

            string report = Worktown.Sim.Revival.RevivalReport.Format(revival, traces, checks, info, targets);
            if (outFile != null) File.WriteAllText(outFile, report);
            Console.Out.Write(report);
            Console.Error.WriteLine($"revival-report: done in {watch.ElapsedMilliseconds} ms");
            return checks.All(c => c.Passed) ? 0 : 2;
        }

        private static int ParsePositive(string s)
        {
            int v = int.Parse(s, CultureInfo.InvariantCulture);
            if (v < 1) throw new ArgumentException("Values must be at least 1: " + s);
            return v;
        }
    }
}
