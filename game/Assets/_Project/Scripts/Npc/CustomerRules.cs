using System;
using System.Collections.Generic;
using UnityEngine;

namespace Worktown.Game.Npc
{
    /// <summary>
    /// Pure rules for visible NPC customers (spec 014). Customers only show the sim's open demand;
    /// they never decide prices or payouts.
    /// </summary>
    public static class CustomerRules
    {
        /// <summary>Most customers in the queue at once (spec 014 open question 1).</summary>
        public const int MaxVisible = 6;
        public const float WalkSpeed = 2f;
        public const float SlotSpacing = 1f;

        /// <summary>Most valuable first, so the queue shows what matters.</summary>
        public static readonly IReadOnlyList<string> WishOrder = new[] { "meal", "snack", "drink" };

        /// <summary>
        /// The wishes to show, in queue order: meal orders first, then snack, then drink, each up to
        /// its open demand, all together capped at <see cref="MaxVisible"/>.
        /// </summary>
        public static IReadOnlyList<string> DesiredWishes(Func<string, int> openDemand)
        {
            var wishes = new List<string>(MaxVisible);
            foreach (string market in WishOrder)
            {
                int demand = Math.Max(0, openDemand(market));
                for (int i = 0; i < demand && wishes.Count < MaxVisible; i++) wishes.Add(market);
                if (wishes.Count >= MaxVisible) break;
            }
            return wishes;
        }

        /// <summary>Queue position <paramref name="index"/>: from the head, along the queue direction.</summary>
        public static Vector3 QueueSlot(Vector3 head, Vector3 direction, int index)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.right;
            return head + direction.normalized * (SlotSpacing * Mathf.Max(0, index));
        }

        /// <summary>Bubble text for a wish, e.g. "Meal?".</summary>
        public static string WishText(string market)
        {
            if (string.IsNullOrEmpty(market)) return "?";
            return char.ToUpperInvariant(market[0]) + market.Substring(1) + "?";
        }
    }
}
