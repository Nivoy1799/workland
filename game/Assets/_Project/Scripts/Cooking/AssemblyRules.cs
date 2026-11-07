using System.Linq;
using Worktown.Sim.Cooking;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Pure rules for cookware that combines items (spec 013, spec 021: the plate and the bowl are carried):
    /// plate (sim <c>plate</c>, up to <see cref="CookingLimits.MaxPlateInputs"/>) and bowl (sim <c>mix</c>, up to
    /// <see cref="CookingLimits.MaxMixInputs"/>).
    /// </summary>
    public static class AssemblyRules
    {
        public const string PlatedTag = "plated";

        public static bool IsAssembly(VesselKind kind) => VesselRules.IsAssembly(kind);

        public static int MaxItems(VesselKind kind) => IsAssembly(kind) ? VesselRules.Capacity(kind) : 0;

        /// <summary>Sim operation used when taking, pouring or selling the contents.</summary>
        public static string OperationFor(VesselKind kind)
        {
            return kind == VesselKind.Bowl ? "mix" : "plate";
        }

        public static bool IsPlated(Item item)
        {
            return item != null && item.Tags.Contains(PlatedTag);
        }

        /// <summary>Whether this item may be added to a plate or bowl that already holds <paramref name="count"/> items.</summary>
        public static CookOutcome CanAdd(VesselKind kind, int count, Item item)
        {
            if (!IsAssembly(kind)) return CookOutcome.InvalidOption;
            if (item == null) return CookOutcome.NotFood;
            return VesselRules.CanPutIn(kind, count, IsPlated(item));
        }
    }
}
