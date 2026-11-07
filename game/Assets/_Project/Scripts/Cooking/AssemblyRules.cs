using System.Linq;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Pure rules for stations that combine items (spec 013): plating table (sim <c>plate</c>, up to
    /// <see cref="CookingLimits.MaxPlateInputs"/>) and bowl (sim <c>mix</c>, up to
    /// <see cref="CookingLimits.MaxMixInputs"/>).
    /// </summary>
    public static class AssemblyRules
    {
        public const string PlatedTag = "plated";

        public static bool IsAssembly(StationKind kind)
        {
            return kind == StationKind.PlatingTable || kind == StationKind.Bowl;
        }

        public static int MaxItems(StationKind kind)
        {
            switch (kind)
            {
                case StationKind.PlatingTable: return CookingLimits.MaxPlateInputs;
                case StationKind.Bowl: return CookingLimits.MaxMixInputs;
                default: return 0;
            }
        }

        /// <summary>Sim operation used when taking the station's contents.</summary>
        public static string OperationFor(StationKind kind)
        {
            return kind == StationKind.Bowl ? "mix" : "plate";
        }

        public static bool IsPlated(Item item)
        {
            return item != null && item.Tags.Contains(PlatedTag);
        }

        /// <summary>Whether this item may be added to a station that already holds <paramref name="count"/> items.</summary>
        public static CookOutcome CanAdd(StationKind kind, int count, Item item)
        {
            if (!IsAssembly(kind)) return CookOutcome.InvalidOption;
            if (item == null) return CookOutcome.NotFood;
            if (IsPlated(item)) return CookOutcome.AlreadyPlated;
            if (count >= MaxItems(kind)) return CookOutcome.StationFull;
            return CookOutcome.Ok;
        }
    }
}
