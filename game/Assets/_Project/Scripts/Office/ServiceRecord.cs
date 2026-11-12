using System;

namespace Worktown.Game.Office
{
    /// <summary>
    /// Spec 059: one service as the Results app reads it, recorded at each service end (synced in OfficeTerminal.History, the last 8 saved):
    /// the day, the guests that came (chose this shop), were served and missed, what they paid and tipped (cents), the service's stars in
    /// tenths (GateRules.ServiceStarsTenths, -1 when nobody was served or missed), the mean wait of the served guests (whole seconds, -1
    /// when none), what was sold per result line (<see cref="ResultsRules.SoldLines"/>: six counts in MenuPrices order) and the town share
    /// the rivals left us (permille, -1 while unknown: spec 058 fills it). Whole numbers in small fields; a plain struct, like ShiftReport.
    /// </summary>
    public struct ServiceRecord : IEquatable<ServiceRecord>
    {
        public const short Unknown = -1;

        public int Day;
        public short Came;
        public short Served;
        public short Missed;
        public int Earned;
        public int Tips;
        public short StarsTenths;
        public short WaitSeconds;
        public byte Sold0;
        public byte Sold1;
        public byte Sold2;
        public byte Sold3;
        public byte Sold4;
        public byte Sold5;
        public short RivalPermille;

        /// <summary>A record, every value clamped to its field (sold: up to 6 counts, each 0 to 255).</summary>
        public static ServiceRecord Of(int day, int came, int served, int missed, long earned, long tips, int starsTenths, int waitSeconds,
            int[] sold, int rivalPermille = Unknown)
        {
            var r = new ServiceRecord
            {
                Day = Math.Max(1, day),
                Came = Short(came, 0),
                Served = Short(served, 0),
                Missed = Short(missed, 0),
                Earned = (int)Math.Max(0L, Math.Min(int.MaxValue, earned)),
                Tips = (int)Math.Max(0L, Math.Min(int.MaxValue, tips)),
                StarsTenths = starsTenths < 0 ? Unknown : Short(starsTenths, 0),
                WaitSeconds = waitSeconds < 0 ? Unknown : Short(waitSeconds, 0),
                RivalPermille = rivalPermille < 0 ? Unknown : Short(Math.Min(1000, rivalPermille), 0),
            };
            for (int i = 0; i < ResultsRules.SoldLines; i++) r = r.WithSold(i, sold != null && i < sold.Length ? sold[i] : 0);
            return r;
        }

        /// <summary>The sold count of result line <paramref name="i"/> (0 out of range).</summary>
        public int SoldAt(int i)
        {
            switch (i)
            {
                case 0: return Sold0;
                case 1: return Sold1;
                case 2: return Sold2;
                case 3: return Sold3;
                case 4: return Sold4;
                case 5: return Sold5;
                default: return 0;
            }
        }

        /// <summary>This record with result line <paramref name="i"/>'s count set (clamped 0 to 255).</summary>
        public ServiceRecord WithSold(int i, int count)
        {
            byte b = (byte)Math.Max(0, Math.Min(byte.MaxValue, count));
            ServiceRecord r = this;
            switch (i)
            {
                case 0: r.Sold0 = b; break;
                case 1: r.Sold1 = b; break;
                case 2: r.Sold2 = b; break;
                case 3: r.Sold3 = b; break;
                case 4: r.Sold4 = b; break;
                case 5: r.Sold5 = b; break;
            }
            return r;
        }

        private static short Short(int v, int min) => (short)Math.Max(min, Math.Min(short.MaxValue, v));

        public bool Equals(ServiceRecord o) =>
            Day == o.Day && Came == o.Came && Served == o.Served && Missed == o.Missed && Earned == o.Earned && Tips == o.Tips
            && StarsTenths == o.StarsTenths && WaitSeconds == o.WaitSeconds && Sold0 == o.Sold0 && Sold1 == o.Sold1 && Sold2 == o.Sold2
            && Sold3 == o.Sold3 && Sold4 == o.Sold4 && Sold5 == o.Sold5 && RivalPermille == o.RivalPermille;

        public override bool Equals(object obj) => obj is ServiceRecord other && Equals(other);

        public override int GetHashCode() => Day ^ (Served << 8) ^ (Earned << 16);
    }
}
