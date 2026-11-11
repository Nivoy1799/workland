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
    /// Usage: climb-report [--players 1,4,16] [--profiles novice,average,expert] [--seeds 5] [--hours 40]
    ///        [--service-seconds 360] [--between-seconds 60] [--out file]
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            var watch = Stopwatch.StartNew();
            int[] players = { 1, 4, 16 };
            string[] profiles = { "novice", "average", "expert" };
            int seeds = 5, hours = 40, serviceSeconds = 360, betweenSeconds = 60;
            string? outFile = null;
            string? trace = null;
            ClimbCatalog catalog = ClimbCatalog.CreateDefault();
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
                            foreach (string p in profiles) catalog.Profile(p);
                            break;
                        case "--seeds": seeds = ParsePositive(Next()); break;
                        case "--hours": hours = ParsePositive(Next()); break;
                        case "--service-seconds": serviceSeconds = ParsePositive(Next()); if (serviceSeconds < 60) throw new ArgumentException("--service-seconds must be >= 60"); break;
                        case "--between-seconds": betweenSeconds = int.Parse(Next(), CultureInfo.InvariantCulture); if (betweenSeconds < 0) throw new ArgumentException("--between-seconds must be >= 0"); break;
                        case "--out": outFile = Next(); break;
                        case "--trace": trace = Next(); break;
                        default: throw new ArgumentException("Unknown argument " + args[i]);
                    }
                }
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException)
            {
                Console.Error.WriteLine("climb-report: " + e.Message);
                Console.Error.WriteLine("usage: climb-report [--players 1,4,16] [--profiles novice,average,expert] [--seeds 5] [--hours 40] [--service-seconds 360] [--between-seconds 60] [--out file] [--trace players,profile,strategy,seed]  (--trace prints one run and always exits 0)");
                return 1;
            }

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
            IReadOnlyList<TargetCheck> checks = ClimbEvaluation.Evaluate(runs, ClimbTargets.Default);
            string report = Worktown.Sim.Climb.ClimbReport.Format(runs, checks);
            if (outFile != null) File.WriteAllText(outFile, report);
            Console.Out.Write(report);
            Console.Error.WriteLine($"climb-report: {runs.Length} runs in {watch.ElapsedMilliseconds} ms");
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
