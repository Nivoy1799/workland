namespace Worktown.Sim.Economy
{
    public static class EconomyLimits
    {
        public const long MaxBalance = 1_000_000_000_000_000L;
        public const long MaxPrice = 1_000_000_000L;
        public const int MaxGoodUnits = 1_000_000;
        public const int MaxLot = 10_000;
        public const int MaxPopulation = 100_000;
        public const int MaxDemandPerTick = 1_000_000;
        public const int DedupWindowTicks = 24;
    }
}
