namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052: a repeatable stage 5 big job that replaces one normal service. Spec 052b: stakes grow with success: at tier t
    /// (0 = start, raised by an event or a won competition, at most <see cref="MaxTier"/>) deposit, prize and the event price are
    /// each multiplied by <see cref="StakeGrowthPermille"/>/1000 t times, floored each step. Event guests do not grow (052b
    /// review): the engine caps them at what the crew can serve, so a well-run event at a higher tier pays more.
    /// </summary>
    public sealed record BigJobDef(string Id, string Name, BigJobKind Kind, long Deposit, int Guests, int PricePermille,
        int RivalScorePermille, long Prize, string? RequiresPurchase, int CooldownServices, int StakeGrowthPermille = 1000,
        int MaxTier = 0)
    {
        /// <summary>The deposit at <paramref name="tier"/> (floored each step to whole <paramref name="unit"/>s).</summary>
        public long DepositAt(int tier, long unit = 1) => Grow(Deposit, tier, unit);

        /// <summary>The prize at <paramref name="tier"/> (floored each step to whole <paramref name="unit"/>s).</summary>
        public long PrizeAt(int tier, long unit = 1) => Grow(Prize, tier, unit);

        /// <summary>The event price factor (permille of the menu price) at <paramref name="tier"/> (floored each step).</summary>
        public int PricePermilleAt(int tier) => (int)System.Math.Min(int.MaxValue, Grow(PricePermille, tier, 1));

        /// <summary>The tier after a job at <paramref name="tier"/>: +1 (at most <see cref="MaxTier"/>) when it was an event or a won competition.</summary>
        public int NextTier(int tier, bool won) => Kind == BigJobKind.Event || won ? System.Math.Min(MaxTier, tier + 1) : tier;

        private long Grow(long value, int tier, long unit)
        {
            long u = System.Math.Max(1L, unit);
            long v = value;
            for (int k = 0; k < System.Math.Min(tier, MaxTier); k++) v = v * StakeGrowthPermille / (1000 * u) * u;
            return v;
        }
    }
}
