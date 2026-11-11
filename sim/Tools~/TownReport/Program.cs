using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Worktown.Sim.Town;
using Report = Worktown.Sim.Town.TownReport;

namespace Worktown.TownReport
{
    /// <summary>
    /// Spec 064: prints the town report of <see cref="TownCatalog.CreateDefault"/> and exits 0 when every check passes,
    /// 2 when one fails, 1 on bad arguments. Usage: town-report [--out file]
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            string? outFile = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--out" && i + 1 < args.Length) outFile = args[++i];
                else
                {
                    Console.Error.WriteLine("town-report: " + (args[i] == "--out" ? "missing value for --out" : "unknown argument " + args[i]));
                    Console.Error.WriteLine("usage: town-report [--out file]");
                    return 1;
                }
            }

            TownCatalog catalog = TownCatalog.CreateDefault();
            IReadOnlyList<TownProblem> problems = TownCheck.Run(catalog);
            string text = Report.Format(catalog, problems);
            Console.Write(text);
            if (outFile != null) File.WriteAllText(outFile, text);
            return problems.Count == 0 ? 0 : 2;
        }
    }
}
