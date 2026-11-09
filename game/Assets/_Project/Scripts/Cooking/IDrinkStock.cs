namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 038b: a drink wallet that can run short in the middle of a pour (the shop's stock: the keg's last part). The dispenser draws
    /// at most what is left and fills only that much (<see cref="DrinkDesk.End"/>); a money wallet does not implement it.
    /// </summary>
    public interface IDrinkStock
    {
        /// <summary>% of one cup of <paramref name="drinkId"/> that can still be drawn now (0: none). Changes nothing.</summary>
        long AvailablePercent(string drinkId);
    }
}
