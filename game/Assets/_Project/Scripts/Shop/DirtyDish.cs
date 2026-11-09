using UnityEngine;

namespace Worktown.Game.Shop
{
    /// <summary>
    /// Spec 035: marks a pickup as a dirty dish (a plate an eat-in guest brought back, smudged; carried like any pickup). The server's
    /// DishRoom owns it (since when it is dirty); it goes into the sink, never into the trash and never onto food. Marker only.
    /// </summary>
    public sealed class DirtyDish : MonoBehaviour
    {
    }
}
