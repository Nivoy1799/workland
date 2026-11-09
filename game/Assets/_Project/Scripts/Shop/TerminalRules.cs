using Worktown.Game.Economy;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 034, pure: the ordering terminal on the back wall. Its screen is a small paged list: <see cref="Rows"/> rows of goods
    /// (StockCatalog order), a page back and a page on, and the pack count (1 to Supplier.MaxPacksPerOrder). Those are the terminal's
    /// slots (station options of its StockSlots). The page and the pack count are the client's own view; only a row's click is sent,
    /// as RequestStation(terminal, <see cref="OrderOption"/>(line, packs)) — two ints, no new RPC — and the server checks and charges it.
    /// </summary>
    public static class TerminalRules
    {
        public const int Rows = 6;
        public const int PrevSlot = Rows;
        public const int NextSlot = Rows + 1;
        public const int PacksSlot = Rows + 2;
        public const int SlotCount = Rows + 3;
        /// <summary>Order options start here (above every slot number, so a slot's own number is never an order).</summary>
        public const int OrderBase = 16;
        private const int PacksStride = 4;

        public static int Pages => (StockCatalog.Count + Rows - 1) / Rows;

        public static bool IsRow(int slot) => slot >= 0 && slot < Rows;

        /// <summary>The line shown in row <paramref name="row"/> of page <paramref name="page"/>, or -1 (past the list's end).</summary>
        public static int LineAt(int page, int row)
        {
            if (!IsRow(row) || page < 0 || page >= Pages) return -1;
            int line = page * Rows + row;
            return StockCatalog.IsValid(line) ? line : -1;
        }

        /// <summary>The page that shows line <paramref name="line"/>.</summary>
        public static int PageOf(int line) => StockCatalog.IsValid(line) ? line / Rows : 0;

        public static int NextPage(int page) => Pages <= 0 ? 0 : (page + 1) % Pages;
        public static int PrevPage(int page) => Pages <= 0 ? 0 : (page + Pages - 1) % Pages;

        /// <summary>1, 2, 3, then 1 again.</summary>
        public static int NextPacks(int packs) => packs >= Supplier.MaxPacksPerOrder || packs < 1 ? 1 : packs + 1;

        /// <summary>The option of an order of <paramref name="packs"/> packs of line <paramref name="line"/>.</summary>
        public static int OrderOption(int line, int packs) => OrderBase + line * PacksStride + packs;

        /// <summary>The line and pack count of an order option; false for anything else (a slot's own number, a bad line or count).</summary>
        public static bool TryParseOrder(int option, out int line, out int packs)
        {
            line = -1;
            packs = 0;
            if (option < OrderBase) return false;
            int rest = option - OrderBase;
            line = rest / PacksStride;
            packs = rest % PacksStride;
            return StockCatalog.IsValid(line) && packs >= 1 && packs <= Supplier.MaxPacksPerOrder;
        }

        /// <summary>The delivery time as the terminal shows it ("1 h"; minutes below an hour).</summary>
        public static string DelayText(int minutes) => minutes >= 60 && minutes % 60 == 0 ? minutes / 60 + " h" : minutes + " min";

        /// <summary>A game minute as the clock shows it: "14:20".</summary>
        public static string ClockOf(long gameMinute)
        {
            if (gameMinute < 0) gameMinute = 0;
            return (gameMinute / 60 % 24).ToString("00") + ":" + (gameMinute % 60).ToString("00");
        }

        /// <summary>"1 pack" / "3 packs".</summary>
        public static string PacksText(int packs) => packs == 1 ? "1 pack" : packs + " packs";

        /// <summary>
        /// One row of the list: "tomato     4/12   1 pack $336" (the stock as clients see it, low in the HUD's sense marked "!").
        /// </summary>
        public static string RowText(int line, byte count, int packs, long price)
        {
            if (!StockCatalog.IsValid(line)) return string.Empty;
            StockCatalog.Good g = StockCatalog.Goods[line];
            string stock = count + "/" + StockCatalog.PresentedCapacity(line) + (g.IsKeg ? " cups" : string.Empty);
            string low = StockCatalog.IsLow(line, count) ? " !" : string.Empty;
            return g.Name + "  " + stock + low + "  · " + EconomyRules.Money(price);
        }

        /// <summary>"Click · order 1 pack of tomato ($336, 1 h)"-style words (without the key).</summary>
        public static string OrderWords(int line, int packs, long price, int delayMinutes)
        {
            if (!StockCatalog.IsValid(line)) return "order";
            StockCatalog.Good g = StockCatalog.Goods[line];
            string what = g.IsKeg ? (packs == 1 ? "a keg of " : packs + " kegs of ") + g.Name : PacksText(packs) + " of " + g.Name;
            return "order " + what + " (" + EconomyRules.Money(price) + ", " + DelayText(delayMinutes) + ")";
        }

        /// <summary>The words of the page and pack buttons.</summary>
        public static string ButtonWords(int slot, int page, int packs)
        {
            switch (slot)
            {
                case PrevSlot: return "page back (" + (PrevPage(page) + 1) + "/" + Pages + ")";
                case NextSlot: return "next page (" + (NextPage(page) + 1) + "/" + Pages + ")";
                case PacksSlot: return "order " + PacksText(NextPacks(packs)) + " at a time";
                default: return null;
            }
        }

        /// <summary>The price of an order as the terminal shows it (the bulk price; the server charges exactly this).</summary>
        public static long PriceOf(EconomyCatalog catalog, int line, int packs) =>
            catalog != null && StockCatalog.TryOffer(catalog, line, out SupplierOffer offer) ? Supplier.OrderCost(catalog, offer, packs) : -1;
    }
}
