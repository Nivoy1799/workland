using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Worktown.Sim.Cooking;

namespace Worktown.ItemList
{
    /// <summary>
    /// Spec 056a: enumerates the items the shipped menu can produce (<see cref="ReachableItems"/> over <see cref="MenuRoutes.Default"/>
    /// and <see cref="KitchenItems"/>) and writes the reachable-items file. Exit 0 ok, 1 bad arguments or a failed write, 2 a
    /// wrong route or too many items.
    /// Usage: item-list [--out file]   (no --out: the file text goes to stdout)
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
                    Console.Error.WriteLine("Usage: item-list [--out file]");
                    return 1;
                }
            }

            IReadOnlyList<ReachableItem> items;
            try
            {
                var factory = new ItemFactory(KitchenItems.CreateItemCatalog(), OperationCatalog.CreateDefault());
                items = ReachableItems.Enumerate(factory, MenuRoutes.Default, ReachLimits.Default);
            }
            catch (InvalidOperationException e)
            {
                Console.Error.WriteLine(e.Message);
                return 2;
            }

            string text = ReachableItemsText.Format(items);
            if (outFile == null)
            {
                Console.Out.Write(text);
                return 0;
            }
            try
            {
                string full = Path.GetFullPath(outFile);
                string? dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = full + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                File.Move(tmp, full, true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Console.Error.WriteLine("Could not write " + outFile + ": " + e.Message);
                return 1;
            }
            int targets = 0;
            foreach (ReachableItem item in items) if (item.IsTarget) targets++;
            Console.WriteLine($"reachable items: {items.Count} ({targets} targets), set hash {ReachableItemsText.SetHash(items)}, written to {outFile}");
            return 0;
        }
    }
}
