namespace Worktown.Game.Npc
{
    /// <summary>
    /// Spec 032, server: who gets a dish at the counter (<see cref="CustomerService.TryPick"/>, a pure read): the guest, its dish,
    /// how the dish fits its order (<see cref="OrderFit"/>), the bill's share of the sim's payout and how long it waited (tenths of
    /// a second) of its patience.
    /// </summary>
    public readonly struct GuestPick
    {
        public GuestPick(int customerId, byte dish, OrderFit fit, int billPermille, int waitedTenths, int patienceTenths)
        {
            CustomerId = customerId;
            Dish = dish;
            Fit = fit;
            BillPermille = billPermille;
            WaitedTenths = waitedTenths;
            PatienceTenths = patienceTenths;
        }

        public int CustomerId { get; }
        public byte Dish { get; }
        public OrderFit Fit { get; }
        public int BillPermille { get; }
        public int WaitedTenths { get; }
        public int PatienceTenths { get; }
    }
}
