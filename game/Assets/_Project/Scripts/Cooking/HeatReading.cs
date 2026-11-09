namespace Worktown.Game.Cooking
{
    /// <summary>Spec 027c: what one vessel on the heat shows: zone, progress (0-255) and the warning. Pure value.</summary>
    public readonly struct HeatReading
    {
        public HeatReading(HeatZone zone, byte progress, bool warn)
        {
            Zone = zone;
            Progress = progress;
            Warn = warn;
        }

        /// <summary>Nothing on the heat.</summary>
        public static HeatReading Empty => new HeatReading(HeatZone.None, 0, false);

        public HeatZone Zone { get; }
        public byte Progress { get; }
        public bool Warn { get; }

        public override string ToString() => Zone + " " + Progress + (Warn ? " warn" : string.Empty);
    }
}
