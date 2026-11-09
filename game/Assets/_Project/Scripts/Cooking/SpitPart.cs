using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 036: marks a fixed spit vessel (<see cref="VesselKind.SpitCone"/> or <see cref="VesselKind.SpitTray"/>) as part of the spit
    /// station <see cref="Spit"/>. Data only (set by the scene builder), like a griddle spot's marker; the server keys the spit's state by
    /// the station's index (SpitDesk) and publishes onto these vessels.
    /// </summary>
    public sealed class SpitPart : MonoBehaviour
    {
        [SerializeField] private Station _spit;

        public Station Spit => _spit;
    }
}
