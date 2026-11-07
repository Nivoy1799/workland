using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>One item on a plating table or in a bowl (spec 013). Spec 017: its look comes from the item.</summary>
    public readonly struct AssemblyEntry
    {
        public AssemblyEntry(Item item)
        {
            Item = item;
        }

        public Item Item { get; }
    }
}
