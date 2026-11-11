namespace Worktown.Sim.Economy
{
    /// <summary>Fixed NPC grocer price in cents (spec 037, D39) for a spec 005 base item id.</summary>
    public sealed record GrocerPrice(string BaseId, long Price);
}
