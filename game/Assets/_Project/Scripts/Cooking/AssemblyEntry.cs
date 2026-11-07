using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>One item on a plating table or in a bowl (spec 013), with the color of the food it came from.</summary>
    public readonly struct AssemblyEntry
    {
        public AssemblyEntry(Item item, byte color)
        {
            Item = item;
            Color = color;
        }

        public Item Item { get; }
        public byte Color { get; }
    }
}
