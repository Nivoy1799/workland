using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 031: marks the build spot <see cref="Vessel"/> (<see cref="VesselKind.Assembly"/>) of the assembly board
    /// <see cref="Board"/>. Like a griddle spot it is a fixed scene NetworkObject without a Pickup: food goes on as the next layer,
    /// empty hands fold a wrap or take the build (RequestUse). Data only (set by the scene builder); the server keys the build by
    /// the vessel's object id (KitchenState). The spot never moves: its layers are drawn by Art/BuildStackView.
    /// </summary>
    public sealed class AssemblySpot : MonoBehaviour
    {
        [SerializeField] private Station _board;

        public Station Board => _board;
    }
}
