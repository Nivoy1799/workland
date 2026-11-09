namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 031b: how the assembly board charges a player (the server's KitchenService implements it with the economy; tests
    /// count the calls). <paramref name="percent"/> of one item's grocer price: 100 for packaging, a portion for a squeeze.
    /// </summary>
    public interface IAssemblyWallet
    {
        /// <summary>Charges <paramref name="percent"/> % of <paramref name="grocerId"/>'s grocer price to <paramref name="owner"/>;
        /// Ok, NotEnoughMoney or SimRefused, nothing charged on a refusal.</summary>
        CookOutcome Charge(int owner, string grocerId, int percent, out long cost);

        /// <summary>031b review: whether <paramref name="owner"/> could pay <paramref name="percent"/> % of
        /// <paramref name="grocerId"/>'s grocer price now (a dispenser press checks the smallest portion). Charges nothing.</summary>
        bool CanAfford(int owner, string grocerId, int percent);

        /// <summary>Spec 034: what a press answers when <see cref="CanAfford"/> says no: NotEnoughMoney (money) or OutOfStock (stock).</summary>
        CookOutcome Short { get; }

        /// <summary>
        /// Spec 034, the refund backstop: a <see cref="Charge"/> of <paramref name="cost"/> whose step could not be committed afterwards
        /// is given back (the money, or the stock drawn). Called at most once per successful charge, never after a refusal.
        /// </summary>
        void Refund(int owner, string grocerId, int percent, long cost);
    }
}
