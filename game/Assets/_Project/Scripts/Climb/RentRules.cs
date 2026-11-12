using System;
using Worktown.Game.Economy;

namespace Worktown.Game.Climb
{
    /// <summary>
    /// Spec 057, pure: the rent and upkeep at each stage 2 service end (open question 4). What is due and what was owed are paid together,
    /// owed first, from the balance above the reserve (the reserve is never taken, so stock can always be ordered); what stays unpaid is owed,
    /// up to the cap; the rest is forgiven. No bankruptcy: the balance never goes below 0.
    /// </summary>
    public static class RentRules
    {
        /// <summary>One service end's charge: what was <see cref="Paid"/>, what is owed after it and what was forgiven over the cap.</summary>
        public readonly struct Result : IEquatable<Result>
        {
            public Result(long paid, long owedAfter, long forgiven)
            {
                Paid = paid;
                OwedAfter = owedAfter;
                Forgiven = forgiven;
            }

            public long Paid { get; }
            public long OwedAfter { get; }
            public long Forgiven { get; }

            public bool Equals(Result other) => Paid == other.Paid && OwedAfter == other.OwedAfter && Forgiven == other.Forgiven;
            public override bool Equals(object obj) => obj is Result r && Equals(r);
            public override int GetHashCode() => HashCode.Combine(Paid, OwedAfter, Forgiven);
            public override string ToString() => "paid " + Paid + ", owed " + OwedAfter + ", forgiven " + Forgiven;
        }

        /// <summary>
        /// total = due + owed; paid = min(total, max(0, balance - reserve)); owedAfter = min(cap, total - paid); forgiven = the rest.
        /// Negative inputs count as 0.
        /// </summary>
        public static Result Charge(long balance, long due, long owed, long reserve, long cap)
        {
            long total = Math.Max(0L, due) + Math.Max(0L, owed);
            long free = Math.Max(0L, Math.Max(0L, balance) - Math.Max(0L, reserve));
            long paid = Math.Min(total, free);
            long owedAfter = Math.Min(Math.Max(0L, cap), total - paid);
            return new Result(paid, owedAfter, total - paid - owedAfter);
        }

        /// <summary>The feed line of a charge: "Rent and upkeep: -$50.00".</summary>
        public static string PaidText(long paid) => "Rent and upkeep: " + EconomyRules.Money(-Math.Max(0L, paid));

        /// <summary>The feed line while rent is owed: "Rent owed: $30.50".</summary>
        public static string OwedText(long owed) => "Rent owed: " + EconomyRules.Money(owed);

        /// <summary>Why a purchase is refused while rent is owed.</summary>
        public const string PayFirstText = "Pay the rent first";
    }
}
