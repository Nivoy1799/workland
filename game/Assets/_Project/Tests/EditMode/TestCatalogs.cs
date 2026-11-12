using System.Collections.Generic;
using System.Linq;
using Worktown.Sim.Climb;

namespace Worktown.Game.Tests
{
    /// <summary>
    /// Spec 057: the one place the tests build their own spec 052 climb catalogs (so a change of the catalog's constructor is a one-line fix
    /// here). <see cref="Restaurant"/> is the default catalog with stage 2 changed.
    /// </summary>
    public static class TestCatalogs
    {
        /// <summary>
        /// The default catalog (cents) with stage 2's rent (whole dollars), gate window and gate stars, and optionally one purchase's cost
        /// (whole dollars) changed.
        /// </summary>
        public static ClimbCatalog Restaurant(long rentDollars = 50, int gateWindow = 5, int gateStarsTenths = 35, string changedPurchase = null,
            long changedCostDollars = 0, IReadOnlyList<CrewScaleRow> crew = null)
        {
            ClimbCatalog d = ClimbCatalog.CreateDefault(1, crew ?? ClimbCatalog.DefaultCrewTable); // whole dollars, scaled to cents below
            IEnumerable<StageDef> stages = d.Stages.Select(s => s.Stage == ClimbStage.FirstRestaurant
                ? s with { Upkeep = rentDollars, GateWindow = gateWindow, GateStarsTenths = gateStarsTenths } : s);
            IEnumerable<PurchaseDef> purchases = d.Purchases.Select(p => p.Id == changedPurchase ? p with { Cost = changedCostDollars } : p);
            return new ClimbCatalog(d.Profiles, d.Dishes, d.Channels, d.Rivals, purchases, stages, d.BigJobs, d.Weights, d.Stage1DishId,
                d.StartingMoney, ClimbCatalog.CentsPerDollar, d.CrewTable);
        }
    }
}
