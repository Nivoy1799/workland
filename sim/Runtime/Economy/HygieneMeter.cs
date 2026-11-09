namespace Worktown.Sim.Economy
{
    /// <summary>
    /// Spec 035, immutable: the hygiene score at game minute <see cref="Minute"/>. <see cref="Advance"/> moves it to a later minute with
    /// the sources seen now (HygieneRules); the clock comes from the caller (the economy's game minute), never from the system.
    /// </summary>
    public sealed record HygieneMeter(int Percent, long Minute)
    {
        /// <summary>A new shop: <see cref="HygieneRules.StartPercent"/> at <paramref name="minute"/>.</summary>
        public static HygieneMeter Start(long minute) => new HygieneMeter(HygieneRules.StartPercent, minute);

        /// <summary>
        /// The meter at <paramref name="nowMinute"/>: every whole minute since <see cref="Minute"/> applies <paramref name="sources"/>
        /// once (a jump counts at most <see cref="HygieneRules.MaxCatchUpMinutes"/>). A clock that went back only moves the minute.
        /// </summary>
        public HygieneMeter Advance(long nowMinute, HygieneSources sources)
        {
            if (nowMinute <= Minute) return nowMinute == Minute ? this : this with { Minute = nowMinute };
            return new HygieneMeter(HygieneRules.Advance(Percent, sources, nowMinute - Minute), nowMinute);
        }
    }
}
