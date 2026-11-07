using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>Server-side pickup checks. Pure functions, no networking (spec 003 AC4).</summary>
    public static class PickupRules
    {
        /// <summary>Maximum distance in metres between player and item for a pickup.</summary>
        public const float MaxReach = 2.5f;

        /// <summary>True when the item is within <see cref="MaxReach"/> of the player (inclusive).</summary>
        public static bool IsInReach(Vector3 playerPosition, Vector3 itemPosition)
        {
            return (itemPosition - playerPosition).sqrMagnitude <= MaxReach * MaxReach;
        }
    }
}
