namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 033: how the drink dispenser charges a player (the server's KitchenService implements it with the economy; tests count the
    /// calls). One pour is one charge: <paramref name="percent"/> % of the drink's grocer price, plus the cup's when
    /// <paramref name="withCup"/> (it was empty: a top-up pays no second cup).
    /// </summary>
    public interface IDrinkWallet
    {
        /// <summary>Charges the pour to <paramref name="owner"/>; Ok, NotEnoughMoney or SimRefused, nothing charged on a refusal.</summary>
        CookOutcome Charge(int owner, string drinkId, int percent, bool withCup, out long cost);

        /// <summary>Whether <paramref name="owner"/> could pay such a pour now (a press checks the smallest one). Charges nothing.</summary>
        bool CanAfford(int owner, string drinkId, int percent, bool withCup);
    }
}
