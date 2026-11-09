using System;
using Worktown.Sim.Economy;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 022: the numbers of one shift: customers served, money they paid, their stars and the customers who left
    /// unserved. Immutable; the With methods return a new value.
    /// Spec 032: plus the tips in what they paid, their reviews (count, star total, the last three packed with
    /// <see cref="GuestReview.Packed"/>, newest first) and the missed guests by reason (<see cref="MissReason"/>).
    /// </summary>
    public readonly struct ShiftSummary
    {
        public static readonly ShiftSummary Empty = default;

        public ShiftSummary(int served, long earned, int starsTotal, int missed)
            : this(served, earned, starsTotal, missed, 0, 0, 0, 0, 0, 0, 0, 0, 0)
        {
        }

        public ShiftSummary(int served, long earned, int starsTotal, int missed, long tips, int reviews, int reviewStarsTotal,
            int missedPatience, int missedGone, int missedClosed, byte review1, byte review2, byte review3)
        {
            Served = served;
            Earned = earned;
            StarsTotal = starsTotal;
            Missed = missed;
            Tips = tips;
            Reviews = reviews;
            ReviewStarsTotal = reviewStarsTotal;
            MissedPatience = missedPatience;
            MissedGone = missedGone;
            MissedClosed = missedClosed;
            Review1 = review1;
            Review2 = review2;
            Review3 = review3;
        }

        public int Served { get; }
        public long Earned { get; }
        public int StarsTotal { get; }
        public int Missed { get; }
        /// <summary>Spec 032: the tips in <see cref="Earned"/>.</summary>
        public long Tips { get; }
        public int Reviews { get; }
        public int ReviewStarsTotal { get; }
        public int MissedPatience { get; }
        public int MissedGone { get; }
        public int MissedClosed { get; }
        /// <summary>The newest review, packed (0 = none), then the two before it.</summary>
        public byte Review1 { get; }
        public byte Review2 { get; }
        public byte Review3 { get; }

        /// <summary>Average stars of the served dishes in tenths (42 = 4.2), rounded half up; 0 when none.</summary>
        public int AverageStarsTenths => Served <= 0 ? 0 : (int)((StarsTotal * 10L + Served / 2) / Served);

        /// <summary>Spec 032: average review stars in tenths, rounded half up; 0 when none.</summary>
        public int AverageReviewTenths => Reviews <= 0 ? 0 : (int)((ReviewStarsTotal * 10L + Reviews / 2) / Reviews);

        public ShiftSummary WithServed(int stars, long pay)
        {
            return new ShiftSummary(Served + 1, Earned + Math.Max(0L, pay), StarsTotal + Math.Max(0, stars), Missed, Tips, Reviews,
                ReviewStarsTotal, MissedPatience, MissedGone, MissedClosed, Review1, Review2, Review3);
        }

        /// <summary>Spec 032: a guest paid <paramref name="paid"/> (with <paramref name="tip"/> in it) for a dish of
        /// <paramref name="stars"/> and left <paramref name="review"/>.</summary>
        public ShiftSummary WithSale(int stars, long paid, long tip, GuestReview review)
        {
            ShiftSummary s = WithServed(stars, paid);
            int reviews = s.Reviews, total = s.ReviewStarsTotal;
            byte r1 = s.Review1, r2 = s.Review2, r3 = s.Review3;
            if (review != null)
            {
                reviews++;
                total += review.Stars;
                r3 = r2;
                r2 = r1;
                r1 = review.Packed;
            }
            return new ShiftSummary(s.Served, s.Earned, s.StarsTotal, s.Missed, s.Tips + Math.Max(0L, tip), reviews, total,
                s.MissedPatience, s.MissedGone, s.MissedClosed, r1, r2, r3);
        }

        /// <summary>Guests who left unserved (spec 022; spec 032: counted as <see cref="MissReason.Gone"/>).</summary>
        public ShiftSummary WithMissed(int count) => WithMissed(count, MissReason.Gone);

        /// <summary>Spec 032: <paramref name="count"/> guests left unserved for <paramref name="reason"/>.</summary>
        public ShiftSummary WithMissed(int count, MissReason reason)
        {
            int n = Math.Max(0, count);
            return new ShiftSummary(Served, Earned, StarsTotal, Missed + n, Tips, Reviews, ReviewStarsTotal,
                MissedPatience + (reason == MissReason.Patience ? n : 0), MissedGone + (reason == MissReason.Gone ? n : 0),
                MissedClosed + (reason == MissReason.Closed ? n : 0), Review1, Review2, Review3);
        }

        /// <summary>"4.2" style text of the average.</summary>
        public static string StarsText(int tenths)
        {
            if (tenths < 0) tenths = 0;
            return (tenths / 10) + "." + (tenths % 10);
        }
    }
}
