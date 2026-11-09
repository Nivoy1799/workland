using System;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 027c: one sale at the counter as clients see it for the reward moment: which counter, the stars, what was paid
    /// and the tip in it (whole dollars, as the feed shows them). Nothing beyond what the serve feed already shows (D21);
    /// <see cref="Serial"/> counts sales so a client can tell a new one. Pure value, synced on <see cref="ServeFeed"/>.
    /// </summary>
    public struct ServeReward : IEquatable<ServeReward>
    {
        /// <summary>Counts sales from 1 (0 = none yet); wraps around.</summary>
        public int Serial;
        /// <summary>Station index of the counter.</summary>
        public int Counter;
        /// <summary>1-5.</summary>
        public byte Stars;
        public int Paid;
        public int Tip;

        /// <summary>The next sale after <paramref name="previous"/>; stars clamped to 0-5, money to 0..int.MaxValue.</summary>
        public static ServeReward Next(ServeReward previous, int counter, int stars, long paid, long tip)
        {
            int serial = previous.Serial == int.MaxValue ? 1 : previous.Serial + 1;
            return new ServeReward
            {
                Serial = serial,
                Counter = counter,
                Stars = (byte)Math.Max(0, Math.Min(StarRating.MaxStars, stars)),
                Paid = Clamp(paid),
                Tip = Math.Min(Clamp(tip), Clamp(paid)),
            };
        }

        private static int Clamp(long money) => (int)Math.Max(0L, Math.Min(int.MaxValue, money));

        public bool Equals(ServeReward other)
        {
            return Serial == other.Serial && Counter == other.Counter && Stars == other.Stars && Paid == other.Paid && Tip == other.Tip;
        }

        public override bool Equals(object obj) => obj is ServeReward other && Equals(other);

        public override int GetHashCode() => Serial;

        public override string ToString() => "ServeReward(#" + Serial + ", counter " + Counter + ", " + Stars + " stars, $" + Paid + ", tip $" + Tip + ")";
    }
}
