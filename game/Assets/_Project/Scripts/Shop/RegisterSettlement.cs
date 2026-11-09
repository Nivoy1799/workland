namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 032: a payment that went through, once: what the payee was paid (<see cref="Paid"/>: the due, less the difference of a
    /// wrong change), the change given and due, and whether the register settled it by itself (the kitchen closed, the payee left).
    /// </summary>
    public readonly struct RegisterSettlement
    {
        public RegisterSettlement(RegisterPayment payment, long paid, long changeGiven, bool automatic)
        {
            Payment = payment;
            Paid = paid;
            ChangeGiven = changeGiven;
            Automatic = automatic;
        }

        public RegisterPayment Payment { get; }
        public long Paid { get; }
        public long ChangeGiven { get; }
        public bool Automatic { get; }

        /// <summary>
        /// 032 review: the tip actually paid: none when the register settled by itself; else what was paid above the bill (a wrong
        /// change takes it from the tip first), at most the tip.
        /// </summary>
        public long TipPaid => Payment == null || Automatic ? 0 : System.Math.Max(0L, System.Math.Min(Payment.Tip, Paid - Payment.Bill));

        /// <summary>Cash with change other than the right one (the review and the pay both notice).</summary>
        public bool WrongChange => Payment != null && !Payment.Card && !Automatic && ChangeGiven != Payment.ChangeDue;
    }
}
