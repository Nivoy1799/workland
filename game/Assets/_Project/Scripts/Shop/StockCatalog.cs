using System;
using System.Collections.Generic;
using Worktown.Game.Cooking;
using Worktown.Sim.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 034, pure data and rules: every good the shop stocks, in a fixed order (the order of the synced counts, one byte each, and
    /// of the terminal's list), where it stands and how much fits.
    /// - The fridge's and the dry shelf's slots (StockLayout), one good each: vegetables, fruit, seitan, falafel mix 12, bread (flatbread,
    ///   wraps) 8, sauce bottles 6.
    /// - Packaging on the assembly board's rack (wrap paper, box, bag) and the cups of the drink dispenser: 20 each.
    /// - The drinks: kegs at the taps, 20 cups each, 2 per tap.
    /// How the stations draw it (spec 034 decision): a slot or the rack gives one sealed unit per take; a sauce dispenser draws its
    /// squeeze's portion (10 to 30 % of a bottle) from the opened bottle of that sauce, opening the next bottle from the dry shelf when
    /// it runs out; a tap draws its pour (a percent of a cup per 4 ml) from the opened keg; the cup stack gives one cup (its lid
    /// included, the lid stack is free). Money moves only when ordering at the terminal.
    /// </summary>
    public static class StockCatalog
    {
        public const int VegetableCapacity = 12;
        public const int BreadCapacity = 8;
        public const int SauceCapacity = 6;
        public const int PackagingCapacity = 20;
        public const int KegCapacity = 2;
        /// <summary>% of one grocer item in a unit: 100 (one item), a keg 20 cups.</summary>
        public const int ItemPercent = 100;
        public const int KegPercent = Supplier.CupsPerKeg * ItemPercent;

        /// <summary>One stocked good.</summary>
        public readonly struct Good
        {
            public Good(string id, string name, int capacity, int grocerItemsPerUnit)
            {
                Id = id;
                Name = name;
                Capacity = capacity;
                GrocerItemsPerUnit = grocerItemsPerUnit;
            }

            public string Id { get; }
            public string Name { get; }
            public int Capacity { get; }
            /// <summary>1, or <see cref="Supplier.CupsPerKeg"/> for a drink's keg.</summary>
            public int GrocerItemsPerUnit { get; }
            public int PercentPerUnit => GrocerItemsPerUnit * ItemPercent;
            public bool IsKeg => GrocerItemsPerUnit > 1;
        }

        public static readonly IReadOnlyList<Good> Goods = Array.AsReadOnly(BuildGoods());

        public static int Count => Goods.Count;

        private static Good[] BuildGoods()
        {
            var list = new List<Good>();
            foreach (string id in StockLayout.Fridge) list.Add(new Good(id, IngredientName(id), CapacityOf(id), 1));
            foreach (string id in StockLayout.Shelf) list.Add(new Good(id, IngredientName(id), CapacityOf(id), 1));
            for (int p = 0; p < BuildRules.PackagingCount; p++)
                list.Add(new Good(BuildRules.PackagingId(p), PackagingName(p), PackagingCapacity, 1));
            list.Add(new Good(Drinks.CupId, "cups", PackagingCapacity, 1));
            foreach (BaseItemDef drink in Drinks.All) list.Add(new Good(drink.Id, drink.DisplayName, KegCapacity, Supplier.CupsPerKeg));
            return list.ToArray();
        }

        /// <summary>Capacity of a fridge or shelf good (spec 034 defaults): sauces 6, bread 8, everything else 12.</summary>
        public static int CapacityOf(string id)
        {
            if (id == "garlic_herb_sauce" || id == "chili_sauce") return SauceCapacity;
            if (id == "flatbread" || id == "wrap") return BreadCapacity;
            return VegetableCapacity;
        }

        private static string IngredientName(string id)
        {
            int i = StockLayout.IngredientIndexOf(id);
            return i >= 0 ? DemoIngredients.All[i].DisplayName : id.Replace('_', ' ');
        }

        private static string PackagingName(int p) => p == BuildRules.Paper ? "wrap paper" : p == BuildRules.Box ? "boxes" : "bags";

        /// <summary>The good's position in <see cref="Goods"/>, or -1.</summary>
        public static int IndexOf(string id)
        {
            for (int i = 0; i < Goods.Count; i++)
                if (Goods[i].Id == id) return i;
            return -1;
        }

        public static bool IsValid(int line) => line >= 0 && line < Goods.Count;

        /// <summary>The shop's stock at the start of a new game: every line at half its capacity (sealed).</summary>
        public static ShopStock Starting()
        {
            var lines = new List<StockLine>(Goods.Count);
            foreach (Good g in Goods) lines.Add(new StockLine(g.Id, ShopStock.StartingCount(g.Capacity), g.Capacity, g.PercentPerUnit, 0));
            return ShopStock.Create(lines);
        }

        /// <summary>The supplier's offer for line <paramref name="line"/> (false for an unpriced good).</summary>
        public static bool TryOffer(EconomyCatalog catalog, int line, out SupplierOffer offer)
        {
            offer = null;
            return IsValid(line) && Supplier.TryOffer(catalog, Goods[line].Id, Goods[line].GrocerItemsPerUnit, out offer);
        }

        /// <summary>
        /// The line a stock slot shows and a delivery box is unpacked into: a fridge or shelf slot's good; the rack's packaging; the cup
        /// stack's cups; a tap's kegs. -1 for every other tool (a sauce dispenser draws from the shelf's bottles, the lid stack is free).
        /// </summary>
        public static int LineOfSlot(StationKind kind, int slot)
        {
            switch (kind)
            {
                case StationKind.Fridge:
                case StationKind.Shelf:
                    IReadOnlyList<string> ids = StockLayout.SlotsOf(kind);
                    return slot >= 0 && slot < ids.Count ? IndexOf(ids[slot]) : -1;
                case StationKind.Assembly:
                    return BuildRules.IsPackaging(slot) ? IndexOf(BuildRules.PackagingId(BuildRules.PackagingOf(slot))) : -1;
                case StationKind.DrinkDispenser:
                    if (DrinkRules.IsTap(slot)) return IndexOf(DrinkRules.DrinkOf(slot));
                    return slot == DrinkRules.CupTool ? IndexOf(Drinks.CupId) : -1;
                default:
                    return -1;
            }
        }

        /// <summary>
        /// The slot (tool) a RequestStation option of a station of <paramref name="kind"/> unpacks into: a fridge or shelf option is its
        /// slot; the rack's packaging option, a tap's press and the cup stack's option are their tool. -1 for anything else.
        /// </summary>
        public static int UnpackSlotOf(StationKind kind, int option)
        {
            switch (kind)
            {
                case StationKind.Fridge:
                case StationKind.Shelf:
                    return LineOfSlot(kind, option) >= 0 ? option : -1;
                case StationKind.Assembly:
                    return BuildRules.TryParsePackaging(option, out int p) ? BuildRules.DispenserCount + p : -1;
                case StationKind.DrinkDispenser:
                    if (DrinkRules.TryParsePour(option, out int tap, out bool start) && start) return tap;
                    return option == DrinkRules.CupOption ? DrinkRules.CupTool : -1;
                default:
                    return -1;
            }
        }

        /// <summary>The option that unpacks into slot <paramref name="slot"/> of a station of <paramref name="kind"/> (the inverse of <see cref="UnpackSlotOf"/>), or -1.</summary>
        public static int UnpackOptionOf(StationKind kind, int slot)
        {
            switch (kind)
            {
                case StationKind.Fridge:
                case StationKind.Shelf:
                    return LineOfSlot(kind, slot) >= 0 ? slot : -1;
                case StationKind.Assembly:
                    return BuildRules.IsPackaging(slot) ? BuildRules.PackagingOption(BuildRules.PackagingOf(slot)) : -1;
                case StationKind.DrinkDispenser:
                    if (DrinkRules.IsTap(slot)) return DrinkRules.PourStartOption(slot);
                    return slot == DrinkRules.CupTool ? DrinkRules.CupOption : -1;
                default:
                    return -1;
            }
        }

        /// <summary>The line a sauce dispenser draws from (the dry shelf's bottles of that sauce), or -1.</summary>
        public static int SauceLine(int dispenser) => IndexOf(BuildRules.SauceOf(dispenser));

        /// <summary>
        /// The synced byte of a line: sealed units for a good taken whole (a sauce's opened bottle is in its dispenser, not on the
        /// shelf); for a keg the cups left (sealed kegs and the opened one). At most 255.
        /// </summary>
        public static byte Presented(StockLine line)
        {
            if (line == null) return 0;
            long shown = line.PercentPerUnit > ItemPercent ? line.AvailablePercent / ItemPercent : line.Count;
            return (byte)Math.Max(0L, Math.Min(byte.MaxValue, shown));
        }

        /// <summary>The capacity of line <paramref name="line"/> in the unit of its synced byte (cups for a keg).</summary>
        public static int PresentedCapacity(int line)
        {
            if (!IsValid(line)) return 0;
            Good g = Goods[line];
            return g.IsKeg ? g.Capacity * g.GrocerItemsPerUnit : g.Capacity;
        }

        /// <summary>Client side: below a quarter of the capacity by the synced byte (spec 034: "Low stock: X").</summary>
        public static bool IsLow(int line, byte presented) => IsValid(line) && presented * 4 < PresentedCapacity(line);

        /// <summary>The good's name ("tomato", "wrap paper", "lemonade"), or "stock".</summary>
        public static string NameOf(int line) => IsValid(line) ? Goods[line].Name : "stock";

        /// <summary>
        /// Client side: how many sealed units still fit into line <paramref name="line"/> by its synced count (a keg's count is cups: the
        /// opened keg counts as one); -1 while the count is unknown.
        /// </summary>
        public static int RoomFor(int line, int presented)
        {
            if (!IsValid(line) || presented < 0) return -1;
            Good g = Goods[line];
            int units = g.IsKeg ? presented / g.GrocerItemsPerUnit : presented; // the server counts sealed kegs only
            return Math.Max(0, g.Capacity - units);
        }

        /// <summary>"6 lettuce", "1 keg of lemonade (20 cups)", for a box and the feed.</summary>
        public static string Describe(int line, int units)
        {
            if (!IsValid(line)) return units + " items";
            Good g = Goods[line];
            if (g.IsKeg) return units == 1 ? "a keg of " + g.Name : units + " kegs of " + g.Name;
            return units + " " + g.Name;
        }
    }
}
