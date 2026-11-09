using Worktown.Game.Cooking;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 032: how the register pays a player (the server's CustomerService implements it with the economy; tests count the calls).
    /// The same pattern as 031b's <see cref="IAssemblyWallet"/>: the desk decides, the wallet only moves the money.
    /// </summary>
    public interface IRegisterWallet
    {
        /// <summary>Pays <paramref name="amount"/> to <paramref name="payee"/>'s account; Ok, or a refusal and nothing paid.</summary>
        CookOutcome Pay(int payee, long amount, out long paid);
    }
}
