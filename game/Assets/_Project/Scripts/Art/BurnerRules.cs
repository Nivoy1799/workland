using UnityEngine;

namespace Worktown.Game.Art
{
    /// <summary>Spec 023: pure test whether a vessel stands on a burner (client side, for steam and sound).</summary>
    public static class BurnerRules
    {
        /// <summary>How far outside the burner top a vessel pivot may be and still count as standing on it.</summary>
        public const float Margin = 0.1f;
        /// <summary>How far above or below the burner top the vessel pivot may be.</summary>
        public const float HeightTolerance = 0.15f;

        /// <summary>
        /// <paramref name="local"/> is the vessel pivot in the burner's local space, <paramref name="size"/> the burner
        /// top (x, z) and <paramref name="topY"/> its local height.
        /// </summary>
        public static bool IsOver(Vector3 local, Vector2 size, float topY)
        {
            return Mathf.Abs(local.x) <= size.x * 0.5f + Margin
                && Mathf.Abs(local.z) <= size.y * 0.5f + Margin
                && Mathf.Abs(local.y - topY) <= HeightTolerance;
        }
    }
}
