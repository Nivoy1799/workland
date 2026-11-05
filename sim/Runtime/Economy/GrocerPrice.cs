namespace Worktown.Sim.Economy
{
    /// <summary>Fixed NPC grocer price in whole dollars for a spec 005 base item id.</summary>
    public sealed record GrocerPrice(string BaseId, long Price);
}
