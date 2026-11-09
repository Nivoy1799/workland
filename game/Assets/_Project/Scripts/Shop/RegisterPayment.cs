using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 032, server only: one guest waiting at the register to pay for what it was handed. The bill and the tip were decided at
    /// the hand-over (the bill from the sim's sale, the tip from <see cref="TipRules"/>), the way of paying and the note when the
    /// guest ordered (seeded); <see cref="Tray"/> is the change built so far. The review inputs ride along. Changed only by
    /// <see cref="RegisterDesk"/>.
    /// </summary>
    public sealed class RegisterPayment
    {
        public int GuestId;
        /// <summary>The owner id of the player who handed the food over: the money is theirs, only they work this payment.</summary>
        public int Payee;
        public byte Dish;
        public long Bill;
        public long Tip;
        public bool Card;
        /// <summary>Cash: what the guest handed (the due rounded up to its notes).</summary>
        public long Handed;
        public int Tray;
        public int OrderStars;
        public int Misses;
        public int WaitedTenths;
        public int PatienceTenths;

        /// <summary>What the guest pays: the bill and the tip.</summary>
        public long Due => Bill + Tip;

        /// <summary>The right change (0 for a card).</summary>
        public long ChangeDue => Card ? 0 : CashRules.ChangeDue(Handed, Due);
    }
}
