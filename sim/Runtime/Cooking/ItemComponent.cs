namespace Worktown.Sim.Cooking
{
    /// <summary>One base-item part of an item, with its own doneness and ideal window.</summary>
    public sealed record ItemComponent(string BaseId, float Doneness, DonenessWindow Ideal);
}
