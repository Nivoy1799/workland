using UnityEngine;

namespace Worktown.Game.Net.Rules
{
    /// <summary>Server-side pickup checks. Pure functions, no networking (spec 003 AC4).</summary>
    public static class PickupRules
    {
        /// <summary>Maximum distance in metres between player and item for a pickup.</summary>
        public const float MaxReach = 2.5f;

        /// <summary>s: the server ignores a player's requests that come closer together than this (PickupInteractor).</summary>
        public const float ServerRequestInterval = 0.2f;
        /// <summary>s: extra margin on top of one tick for the client's own spacing.</summary>
        public const float ClientIntervalMargin = 0.01f;

        /// <summary>
        /// Spec 027b review: how far apart the client sends its requests (clicks, keys, knife strokes). Requests leave on the
        /// next tick and are read by the server on one, so two of them can arrive up to a tick closer together than they were
        /// sent; the client keeps <see cref="ServerRequestInterval"/> plus one tick plus a margin, so a queued click never
        /// lands inside the server's limit. A bad tick length counts as 30 Hz.
        /// </summary>
        public static float ClientRequestInterval(float tickDelta)
        {
            if (float.IsNaN(tickDelta) || float.IsInfinity(tickDelta) || tickDelta <= 0f) tickDelta = 1f / 30f;
            return ServerRequestInterval + tickDelta + ClientIntervalMargin;
        }

        /// <summary>True when the item is within <see cref="MaxReach"/> of the player (inclusive).</summary>
        public static bool IsInReach(Vector3 playerPosition, Vector3 itemPosition)
        {
            return (itemPosition - playerPosition).sqrMagnitude <= MaxReach * MaxReach;
        }
    }
}
