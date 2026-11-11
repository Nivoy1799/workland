using UnityEngine;
using Worktown.Game.Cooking;
using Worktown.Game.Net;

namespace Worktown.Game.Climb
{
    /// <summary>
    /// Spec 053: the one test of a closed station, slot, place or cookware at a stage, shared by the server's refusal (KitchenService)
    /// and the client's prompt (PickupInteractor), so the two never disagree. The rules themselves are <see cref="ShopStageRules"/>.
    /// </summary>
    public static class ClosedTargets
    {
        /// <summary>
        /// Whether <paramref name="target"/> (a station, one of its stock slots, a place surface or cookware) is closed at
        /// <paramref name="stage"/>. <paramref name="option"/> is the station option for a station (-1: the station as a whole).
        /// </summary>
        public static bool IsClosed(byte stage, Component target, int option = -1)
        {
            if (stage == ShopStageRules.OpenKitchen || target == null) return false;
            if (target is StockSlot slot && slot.Station != null) return !ShopStageRules.IsOpen(stage, slot.Station.Kind, slot.Option);
            if (target.TryGetComponent(out Vessel vessel) && !ShopStageRules.IsOpenVessel(stage, vessel.Kind)) return true;
            if (target is PlaceSurface surface)
            {
                if (surface.IsBurner) return !ShopStageRules.IsStationOpen(stage, StationKind.Stove);
                if (surface.IsCupSpot) return !ShopStageRules.IsStationOpen(stage, StationKind.DrinkDispenser);
            }
            Station station = target as Station ?? target.GetComponentInParent<Station>();
            if (station == null) return false;
            return option >= 0 ? !ShopStageRules.IsOpen(stage, station.Kind, option) : !ShopStageRules.IsStationOpen(stage, station.Kind);
        }
    }
}
