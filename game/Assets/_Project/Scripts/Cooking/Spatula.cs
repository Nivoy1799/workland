using UnityEngine;

namespace Worktown.Game.Cooking
{
    /// <summary>
    /// Spec 030: marks a pickup as the griddle's spatula. Carried like the knife (a free item with a Pickup, placeable);
    /// while held it lets its holder flip food on a griddle spot (RequestUse(spot, VesselRules.ActionFlip)). Marker only, no
    /// rules: KitchenService checks for it on the server. A lost spatula comes back at its home spot like the knife.
    /// </summary>
    public sealed class Spatula : MonoBehaviour
    {
    }
}
