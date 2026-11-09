using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>
    /// Spec 027d: marks the small flat sign on a station's front (a TextMesh named "Label" on the station root). It is read
    /// only from the cook side: it faces the station's local -z, and WorldLabelFade hides it from behind, from far behind
    /// geometry (it is depth tested) and across the view centre in first person. Display only.
    /// </summary>
    public sealed class StationSign : MonoBehaviour
    {
        /// <summary>m below the top of the station body the sign hangs (under the worktop edge, clear of the knobs).</summary>
        public const float BelowTop = 0.26f;
        /// <summary>m the sign stands off the station's front face.</summary>
        public const float Standoff = 0.04f;
        /// <summary>The label's scale at 1 m: with the builder's font this gives letters about 6 cm tall.</summary>
        public const float BaseScale = 0.28f;

        /// <summary>
        /// Local position of a sign on a station body of this height (the body is centred on the root). <paramref name="frontZ"/>
        /// is the local z of the front-most surface of the body and its model (negative: the front is local -z).
        /// </summary>
        public static Vector3 LocalPosition(float height, float frontZ) =>
            new Vector3(0f, height * 0.5f - BelowTop - 0.12f, frontZ - Standoff);
    }
}
