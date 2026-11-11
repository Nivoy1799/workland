using System;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052b, D40: the one place where crew size changes the climb. <see cref="For"/> turns the catalog's crew table and a
    /// player count into <see cref="CrewFactors"/>; the engine and the policy read crew size only through it, so the rule can be
    /// swapped without touching them. The rule (relative to the 4-player reference crew, <see cref="ReferencePlayers"/>):
    /// guest demand (and with it work spots, staff, events and running costs) is n / 4; goals and one-time costs are
    /// G(n) = n x h(n) / 4, with h(n) the per-player difficulty of the table (data). Prices, stock unit costs and ingredients never
    /// change with the crew. Money progress is kept in reference money: each service adds <c>net / G(n)</c>
    /// (<see cref="ToReference"/>), so a player who joins or leaves changes only later services.
    /// </summary>
    public static class CrewScaling
    {
        /// <summary>The reference crew (D40): its factors are all 1000.</summary>
        public const int ReferencePlayers = 4;

        /// <summary>The factors for a crew of <paramref name="players"/> (measured at service start).</summary>
        public static CrewFactors For(ClimbCatalog catalog, int players)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (players < 1) throw new ArgumentOutOfRangeException(nameof(players), "players must be at least 1");
            CrewScaleRow row = catalog.RowFor(players);
            int demand = row.FixedDemandPermille > 0 ? row.FixedDemandPermille : (int)Math.Min(1_000_000L, players * 1000L / ReferencePlayers);
            int goal = row.FixedGoalPermille > 0 ? row.FixedGoalPermille
                : (int)Math.Max(1L, Math.Min(1_000_000L, (long)players * row.DifficultyPermille / ReferencePlayers));
            int baseStock = catalog.StageOf(ClimbStage.FoodCorner).StockPerService;
            int stock = row.FixedStock > 0 ? row.FixedStock : baseStock <= 0 ? 0 : (int)Math.Max(1L, ((long)baseStock * demand + 999) / 1000);
            return new CrewFactors(players, row.DifficultyPermille, demand, goal, row.PatienceSeconds, stock);
        }

        /// <summary>A count (guests, work spots) scaled by <paramref name="permille"/>: floored, at least 1 when the count is above 0.</summary>
        public static int ScaleCount(int value, int permille)
        {
            if (value <= 0) return 0;
            return (int)Math.Max(1L, Math.Min(int.MaxValue, (long)value * permille / 1000));
        }

        /// <summary>Real money scaled by <paramref name="permille"/>, floored to whole <paramref name="unit"/>s (running costs).</summary>
        public static long ScaleMoney(long money, int permille, long unit = 1)
        {
            long u = Math.Max(1L, unit);
            return GuestChoice.FloorDiv(money * permille, 1000 * u) * u;
        }

        /// <summary>Real money of this crew as reference (4-player) money: <c>money * 1000 / G</c>, floored to whole units.</summary>
        public static long ToReference(long money, int goalPermille, long unit = 1)
        {
            if (goalPermille < 1) throw new ArgumentOutOfRangeException(nameof(goalPermille), "goalPermille must be at least 1");
            long u = Math.Max(1L, unit);
            return GuestChoice.FloorDiv(money * 1000, goalPermille * u) * u;
        }

        /// <summary>Reference money as this crew's real money: <c>money * G / 1000</c>, floored to whole units (goals shown to the crew).</summary>
        public static long ToReal(long referenceMoney, int goalPermille, long unit = 1) => ScaleMoney(referenceMoney, goalPermille, unit);
    }
}
