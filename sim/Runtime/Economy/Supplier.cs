using System;

namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 034, pure: the one supplier (the wholesaler's delivery service). It sells every grocer good in packs at a bulk discount
    /// and delivers after a delay. Integer math, whole dollars.
    /// - Order cost = grocer price × grocer items × 80 %, rounded up (<see cref="OrderCost"/>).
    /// - Anti-faucet rule: a unit never costs less than the byproduct ceiling B + 1 (a good dearer than that never drops below it with
    ///   the discount; at most its grocer price), so no component bought in bulk can be worth less than the byproducts it may make.
    ///   With the default catalog (B = 31) only the $35 goods (wrap paper, the paper bag, the cup) are affected: $32 a unit, not $28.
    /// - The wholesaler's cap counts each component at <see cref="BulkUnitPrice"/>, which is never above what one unit cost in any
    ///   order (PriceRules.StockCost, SurplusCap).
    /// </summary>
    public static class Supplier
    {
        /// <summary>The bulk price is this share of the grocer price (‰).</summary>
        public const int BulkPermille = 800;
        /// <summary>Stock units in one pack (one delivery box), for every good but a drink's keg.</summary>
        public const int DefaultPackSize = 6;
        /// <summary>A drink comes as one keg per box.</summary>
        public const int KegPackSize = 1;
        /// <summary>Cups of a drink in one keg (grocer items per stock unit).</summary>
        public const int CupsPerKeg = 20;
        /// <summary>Game minutes from the order to the boxes at the back door (one game hour, about a real minute).</summary>
        public const int DelayMinutes = 60;
        /// <summary>The most packs one order may have.</summary>
        public const int MaxPacksPerOrder = 3;

        /// <summary>The least a unit may cost after the discount: B + 1, at most the grocer price itself.</summary>
        public static long FaucetFloor(EconomyCatalog c, long grocerPrice)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            return Math.Min(Math.Max(0L, grocerPrice), c.ByproductMaxPerComponent + 1);
        }

        /// <summary>
        /// What one grocer item counts at in the wholesaler's cap: the bulk price rounded down, but never below
        /// <see cref="FaucetFloor"/>. Never above what one item cost in any order (<see cref="OrderCost"/>).
        /// </summary>
        public static long BulkUnitPrice(EconomyCatalog c, long grocerPrice)
        {
            long p = Math.Max(0L, grocerPrice);
            return Math.Max(p * BulkPermille / 1000, FaucetFloor(c, p));
        }

        /// <summary>
        /// The price of <paramref name="grocerItems"/> grocer items at <paramref name="grocerPrice"/>: 80 % rounded up, never below
        /// <paramref name="grocerItems"/> × <see cref="FaucetFloor"/>. 0 for no items.
        /// </summary>
        public static long OrderCost(EconomyCatalog c, long grocerPrice, long grocerItems)
        {
            if (grocerItems <= 0 || grocerPrice <= 0) return 0;
            long bulk = (grocerPrice * grocerItems * BulkPermille + 999) / 1000;
            return Math.Max(bulk, grocerItems * FaucetFloor(c, grocerPrice));
        }

        /// <summary>The price of <paramref name="packs"/> packs of <paramref name="offer"/>.</summary>
        public static long OrderCost(EconomyCatalog c, SupplierOffer offer, int packs)
        {
            if (offer == null) throw new ArgumentNullException(nameof(offer));
            return OrderCost(c, offer.GrocerPrice, offer.GrocerItems(packs));
        }

        /// <summary>
        /// The offer for <paramref name="goodId"/> from the catalog's grocer price (false for an unpriced good): a keg of
        /// <see cref="CupsPerKeg"/> cups per box for a drink sold by the cup (<paramref name="grocerItemsPerUnit"/> &gt; 1), else
        /// <see cref="DefaultPackSize"/> units per box; delivered after <see cref="DelayMinutes"/>.
        /// </summary>
        public static bool TryOffer(EconomyCatalog c, string goodId, int grocerItemsPerUnit, out SupplierOffer offer)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            offer = null!;
            if (grocerItemsPerUnit < 1 || !c.TryGetGrocerPrice(goodId, out long price)) return false;
            int pack = grocerItemsPerUnit > 1 ? KegPackSize : DefaultPackSize;
            offer = new SupplierOffer(goodId, price, grocerItemsPerUnit, pack, DelayMinutes);
            return true;
        }
    }
}
