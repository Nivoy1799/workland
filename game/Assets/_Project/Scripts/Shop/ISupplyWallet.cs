using Worktown.Game.Cooking;
using Worktown.Sim.Economy;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 034: how the ordering terminal charges a player (the server's SupplyService implements it with the economy; tests count the
    /// calls). The 031b money-step pattern: <see cref="CanAfford"/> before anything changes, <see cref="Charge"/> once, and the refund
    /// backstop <see cref="Refund"/> when the order could not be booked after it was paid.
    /// </summary>
    public interface ISupplyWallet
    {
        /// <summary>Whether the player could pay <paramref name="cost"/> now. Charges nothing.</summary>
        bool CanAfford(long cost);

        /// <summary>Charges <paramref name="packs"/> packs of <paramref name="offer"/> (the bulk price), once: Ok, NotEnoughMoney or
        /// SimRefused, nothing charged on a refusal.</summary>
        CookOutcome Charge(SupplierOffer offer, int packs, out long cost);

        /// <summary>Gives <paramref name="cost"/> back (a paid order that could not be booked).</summary>
        void Refund(long cost);
    }
}
