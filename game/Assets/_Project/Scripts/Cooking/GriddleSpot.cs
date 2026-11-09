using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 030: marks a griddle-spot <see cref="Vessel"/> (<see cref="VesselKind.Griddle"/>) as spot <see cref="Spot"/> of the
    /// griddle station <see cref="Griddle"/>. Like a fryer basket it is a fixed scene NetworkObject without a Pickup: food goes
    /// on it, empty hands take it off, the spatula flips it (RequestUse). Data only (set by the scene builder); the server keys
    /// the spot's state by the vessel's object id (KitchenState). The spot object never moves: its contents are tweened
    /// (Art/GriddleSpotView).
    /// </summary>
    public sealed class GriddleSpot : MonoBehaviour
    {
        [SerializeField] private Station _griddle;
        [SerializeField] private int _spot;

        public Station Griddle => _griddle;
        public int Spot => _spot;
    }
}
