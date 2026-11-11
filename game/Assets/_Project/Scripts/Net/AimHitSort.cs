using UnityEngine;
using Worktown.Game.Cooking;

namespace Worktown.Game.Net
{
    /// <summary>
    /// 041 review: how a hit of the camera-centre ray is sorted, shared by <see cref="PickupInteractor"/> (FindExactTarget) and the
    /// built-scene aim tests, so a test cannot drift from the game. Stateless; it only looks up components on the hit collider. What
    /// the interactor does next with a hit (free spots, tools near the ray, the drink spots, predictions) stays the interactor's.
    /// </summary>
    public static class AimHitSort
    {
        /// <summary>
        /// The class of a hit on <paramref name="collider"/> and the component it means (<paramref name="source"/>: the
        /// <see cref="StationButton"/>, <see cref="PlaceSurface"/>, fixed <see cref="Vessel"/>, <see cref="Station"/> or
        /// <see cref="Pickup"/>; null for <see cref="AimHitClass.Other"/>). Checked in this order: a button on the collider itself
        /// (with its station), a place surface on the collider itself (checked before its station), a fixed vessel above it, a station
        /// above it, an item above it.
        /// </summary>
        public static AimHitClass Classify(Collider collider, out Component source)
        {
            source = null;
            if (collider == null) return AimHitClass.Other;
            if (collider.TryGetComponent(out StationButton button) && button.Station != null)
            {
                source = button;
                return AimHitClass.Button;
            }
            if (collider.TryGetComponent(out PlaceSurface surface))
            {
                source = surface;
                return AimHitClass.Surface;
            }
            Vessel vessel = collider.GetComponentInParent<Vessel>();
            if (vessel != null && VesselRules.IsFixed(vessel.Kind))
            {
                source = vessel;
                return AimHitClass.FixedVessel;
            }
            Station station = collider.GetComponentInParent<Station>();
            if (station != null)
            {
                source = station;
                return AimHitClass.Station;
            }
            Pickup pickup = collider.GetComponentInParent<Pickup>();
            if (pickup != null)
            {
                source = pickup;
                return AimHitClass.Item;
            }
            return AimHitClass.Other;
        }

        /// <summary>Spec 028b: a hit on a station with slots (the fridge, a shelf) means the slot nearest the hit point; null without slots.</summary>
        public static StockSlot SlotAt(Station station, Vector3 point) =>
            station != null && station.SlotCount > 0 ? station.NearestSlot(point) : null;

        /// <summary>
        /// Spec 031, 032, 033: on these stations a hit means a tool only near the ray (the interactor's ToolOnRay), not the slot nearest the
        /// hit: the assembly board, the register and the drink dispenser (spec 037: and the menu board's buttons).
        /// </summary>
        public static bool SlotFollowsTheRay(StationKind kind) =>
            kind == StationKind.Assembly || kind == StationKind.Register || kind == StationKind.DrinkDispenser || kind == StationKind.MenuBoard;
    }
}
