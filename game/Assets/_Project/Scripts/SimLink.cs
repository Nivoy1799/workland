using System;
using Worktown.Sim.Cooking;

namespace Worktown.Game
{
    /// <summary>
    /// Proves the /sim package is linked into the game assembly (spec 003 AC6).
    /// Builds a raw potato with the sim's ItemFactory and returns its identity hash.
    /// </summary>
    public static class SimLink
    {
        public static string PotatoHash()
        {
            var potato = new BaseItemDef("potato", "potato",
                new ItemStats(0.1f, 0f, 0f, 0.1f, 0.1f, 0f, 0.5f, 0.6f, 0f, 0.8f),
                new DonenessWindow(0.55f, 0.85f), Array.Empty<string>());
            var factory = new ItemFactory(new ItemCatalog(new[] { potato }), OperationCatalog.CreateDefault());
            OperationResult result = factory.CreateBase("potato");
            if (!result.Ok || result.Item == null)
                throw new InvalidOperationException("sim refused potato: " + result.Error);
            return result.Item.Hash;
        }
    }
}
