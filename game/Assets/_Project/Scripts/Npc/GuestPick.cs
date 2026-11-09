namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 032, server: who gets a dish at the counter (<see cref="CustomerService.TryPick"/>, a pure read): the guest, its dish,
    /// how the dish fits its order (<see cref="OrderFit"/>), the bill's share of the sim's payout and how long it waited (tenths of
    /// a second) of its patience. Spec 033: a dish or (<see cref="IsDrink"/>) the order's drink line; GuestTab combines the lines of an
    /// order with a drink into one pick for the register.
    /// </summary>
    public readonly struct GuestPick
    {
        public GuestPick(int customerId, byte dish, OrderFit fit, int billPermille, int waitedTenths, int patienceTenths, bool drink = false)
        {
            IsDrink = drink;
            CustomerId = customerId;
            Dish = dish;
            Fit = fit;
            BillPermille = billPermille;
            WaitedTenths = waitedTenths;
            PatienceTenths = patienceTenths;
        }

        /// <summary>Spec 033: this pick is the order's drink line (a lidded cup), not its dish.</summary>
        public bool IsDrink { get; }
        public int CustomerId { get; }
        public byte Dish { get; }
        public OrderFit Fit { get; }
        public int BillPermille { get; }
        public int WaitedTenths { get; }
        public int PatienceTenths { get; }
    }
}
