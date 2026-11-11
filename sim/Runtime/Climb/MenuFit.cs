using System;
using System.Collections.Generic;
using System.Linq;

namespace Worktown.Sim.Climb
{
    /// <summary>
    /// Spec 052b: the crew chooses its menu (pure, no draws; runs before a service, after buying). The active menu is a subset
    /// of the owned menu: the stage 1 dish always first and always active, never empty, never above the stage's MaxDishes.
    /// Drop: the last normal service missed more than <see cref="DropMissedPermille"/> and more than one dish is active: the
    /// active dish with the most actions goes (tie: id ordinal, last wins), never the stage 1 dish. Re-add: the last
    /// <see cref="AddWindow"/> normal services each missed less than <see cref="AddMissedPermille"/>: the owned inactive dish
    /// with the fewest actions comes back (tie: id ordinal, first wins). At most one change per service.
    /// </summary>
    public static class MenuFit
    {
        public const int DropMissedPermille = 100;
        public const int AddMissedPermille = 30;
        public const int AddWindow = 3;
        /// <summary>Missed shares kept in <see cref="ClimbState.RecentMissed"/>.</summary>
        public const int Kept = 3;

        /// <summary>The dishes guests may order now: the owned menu filtered by <see cref="ClimbState.ActiveMenu"/> (stage 1 dish always).</summary>
        public static IReadOnlyList<DishDef> Active(ClimbCatalog catalog, ClimbState state)
        {
            var list = new List<DishDef>();
            foreach (DishDef d in catalog.Menu(state.Stage, state.Owned))
                if (d.Id == catalog.Stage1DishId || state.ActiveMenu.Contains(d.Id)) list.Add(d);
            return list;
        }

        /// <summary>
        /// The menu change before the next service: the new active ids (buying order) and the dish dropped or re-added (at most
        /// one of them; both null when nothing changes).
        /// </summary>
        public static (IReadOnlyList<string> Active, string? Dropped, string? Added) Next(ClimbCatalog catalog, ClimbState state)
        {
            IReadOnlyList<DishDef> owned = catalog.Menu(state.Stage, state.Owned);
            List<DishDef> active = Active(catalog, state).ToList();
            IReadOnlyList<int> missed = state.RecentMissed;
            if (missed.Count > 0 && missed[missed.Count - 1] > DropMissedPermille && active.Count > 1)
            {
                DishDef? drop = null;
                foreach (DishDef d in active)
                {
                    if (d.Id == catalog.Stage1DishId) continue;
                    if (drop == null || d.Actions > drop.Actions || (d.Actions == drop.Actions && string.CompareOrdinal(d.Id, drop.Id) > 0)) drop = d;
                }
                if (drop != null)
                {
                    active.Remove(drop);
                    return (Ids(active), drop.Id, null);
                }
            }
            if (missed.Count >= AddWindow && missed.Skip(missed.Count - AddWindow).All(m => m < AddMissedPermille))
            {
                DishDef? add = null;
                foreach (DishDef d in owned)
                {
                    if (active.Contains(d)) continue;
                    if (add == null || d.Actions < add.Actions || (d.Actions == add.Actions && string.CompareOrdinal(d.Id, add.Id) < 0)) add = d;
                }
                if (add != null)
                {
                    var ids = new HashSet<string>(Ids(active), StringComparer.Ordinal) { add.Id };
                    return (owned.Where(d => ids.Contains(d.Id)).Select(d => d.Id).ToList().AsReadOnly(), null, add.Id);
                }
            }
            return (Ids(active), null, null);
        }

        /// <summary>Applies <see cref="Next"/> to the state.</summary>
        public static (ClimbState State, string? Dropped, string? Added) Apply(ClimbCatalog catalog, ClimbState state)
        {
            (IReadOnlyList<string> active, string? dropped, string? added) = Next(catalog, state);
            return (state with { ActiveMenu = active }, dropped, added);
        }

        /// <summary>The missed list after a normal service that missed <paramref name="missedPermille"/> (newest last, at most <see cref="Kept"/>).</summary>
        public static IReadOnlyList<int> Record(IReadOnlyList<int> recent, int missedPermille)
        {
            var list = new List<int>(recent) { missedPermille };
            if (list.Count > Kept) list.RemoveRange(0, list.Count - Kept);
            return list.AsReadOnly();
        }

        private static IReadOnlyList<string> Ids(List<DishDef> dishes) => dishes.Select(d => d.Id).ToList().AsReadOnly();
    }
}
