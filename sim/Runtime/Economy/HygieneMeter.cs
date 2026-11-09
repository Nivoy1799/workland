namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 035, immutable: the hygiene score at game minute <see cref="Minute"/>, kept in tenths of a percent (spec 036 tuning: the
    /// rates are a tenth of a percent a minute). <see cref="Advance"/> moves it to a later minute with the sources seen now
    /// (HygieneRules); the clock comes from the caller (the economy's game minute), never from the system.
    /// </summary>
    public sealed record HygieneMeter(int Tenths, long Minute)
    {
        /// <summary>The whole percent shown (0-100, rounded down).</summary>
        public int Percent => HygieneRules.PercentOf(Tenths);

        /// <summary>A new shop: <see cref="HygieneRules.StartPercent"/> at <paramref name="minute"/>.</summary>
        public static HygieneMeter Start(long minute) =>
            new HygieneMeter(HygieneRules.StartPercent * HygieneRules.TenthsPerPercent, minute);

        /// <summary>
        /// The meter at <paramref name="nowMinute"/>: every whole minute since <see cref="Minute"/> applies <paramref name="sources"/>
        /// once (a jump counts at most <see cref="HygieneRules.MaxCatchUpMinutes"/>). A clock that went back only moves the minute.
        /// </summary>
        public HygieneMeter Advance(long nowMinute, HygieneSources sources)
        {
            if (nowMinute <= Minute) return nowMinute == Minute ? this : this with { Minute = nowMinute };
            return new HygieneMeter(HygieneRules.Advance(Tenths, sources, nowMinute - Minute), nowMinute);
        }
    }
}
