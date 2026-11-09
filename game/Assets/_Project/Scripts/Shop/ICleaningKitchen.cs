using Worktown.Game.Cooking;
using Worktown.Game.Net;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 035: what the cleaning service needs from the kitchen (KitchenService implements it): the spills and the smell for the score,
    /// a discard of what the player holds, and the cloth and the spill for a wipe. Server only.
    /// </summary>
    public interface ICleaningKitchen
    {
        /// <summary>Open spills (the drink dispensers' spill flags).</summary>
        int SpillCount { get; }

        /// <summary>Something burns on the heat right now (a pan, pot, basket or griddle spot in the Burnt zone).</summary>
        bool Burning { get; }

        /// <summary>Whether what <paramref name="player"/> holds may go into the trash (food, an empty cup, cookware with food). No change.</summary>
        bool CanDiscardHeld(PickupInteractor player);

        /// <summary>The held food goes (despawned), or the held cookware's contents go (the cookware stays in the hands). Never moves money or stock.</summary>
        CookOutcome DiscardHeld(PickupInteractor player, out string what);

        bool HoldsCloth(PickupInteractor player);

        bool HasSpill(int station);

        /// <summary>The spill at dispenser <paramref name="station"/> is wiped (its byte refreshed); false when there was none.</summary>
        bool ClearSpill(int station);

        /// <summary>The dispenser's byte goes out again (its spill and wiping bits changed).</summary>
        void RefreshDispenser(int station);
    }
}
