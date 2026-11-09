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
    }
}
