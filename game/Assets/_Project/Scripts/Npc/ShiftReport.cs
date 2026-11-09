using System;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 032: the shift summary's new numbers as clients see them (synced on the ticket rail, one value per shift: the open one
    /// and the last one): the tips, the reviews (count, average in tenths, the last three packed as stars + text key), the missed
    /// guests by reason. Whole numbers and keys only (D21), small fields. A plain struct, like 027c's ServeReward.
    /// </summary>
    public struct ShiftReport : IEquatable<ShiftReport>
    {
        public int Tips;
        public short Reviews;
        public short ReviewTenths;
        public short MissedPatience;
        public short MissedGone;
        public short MissedClosed;
        public byte Review1;
        public byte Review2;
        public byte Review3;

        public static ShiftReport From(ShiftSummary s)
        {
            return new ShiftReport
            {
                Tips = (int)Math.Max(0L, Math.Min(int.MaxValue, s.Tips)),
                Reviews = Short(s.Reviews),
                ReviewTenths = Short(s.AverageReviewTenths),
                MissedPatience = Short(s.MissedPatience),
                MissedGone = Short(s.MissedGone),
                MissedClosed = Short(s.MissedClosed),
                Review1 = s.Review1,
                Review2 = s.Review2,
                Review3 = s.Review3,
            };
        }

        private static short Short(int v) => (short)Math.Max(0, Math.Min(short.MaxValue, v));

        public bool Equals(ShiftReport o) =>
            Tips == o.Tips && Reviews == o.Reviews && ReviewTenths == o.ReviewTenths && MissedPatience == o.MissedPatience
            && MissedGone == o.MissedGone && MissedClosed == o.MissedClosed && Review1 == o.Review1 && Review2 == o.Review2
            && Review3 == o.Review3;

        public override bool Equals(object obj) => obj is ShiftReport other && Equals(other);

        public override int GetHashCode() => Tips ^ (Reviews << 16) ^ Review1;
    }
}
