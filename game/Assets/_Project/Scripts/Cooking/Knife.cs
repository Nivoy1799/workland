using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 019: marks a pickup as a kitchen knife. Carried like food (Pickup); while held it lets its holder
    /// cut food lying on a board. Marker only, no rules: KitchenService checks for it on the server.
    /// </summary>
    public sealed class Knife : MonoBehaviour
    {
        [Tooltip("Station index of the board this knife belongs to (where a lost knife comes back).")]
        [SerializeField] private int _boardIndex;

        public int BoardIndex
        {
            get => _boardIndex;
            internal set => _boardIndex = value;
        }
    }
}
