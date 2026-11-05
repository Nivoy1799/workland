namespace Worktown.Sim.Cooking
{
    /// <summary>Synchronous name lookup (e.g. over a gateway cache). /sim never waits for the network.</summary>
    public interface IItemNameSource
    {
        bool TryGetName(string itemHash, out string name);
    }
}
